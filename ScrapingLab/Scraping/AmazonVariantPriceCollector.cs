using System.Text;
using System.Text.Json;
using ScrapingLab.Models;

namespace ScrapingLab.Scraping;

/// <summary>Fills each observed combination with a price verified on its own child-ASIN page.</summary>
public sealed class AmazonVariantPriceCollector(Scraper scraper)
{
    public async Task<VariantPriceCollectionReport> CollectAsync(
        AmazonProductMappingResult mapping, string outputDirectory, CancellationToken cancellationToken)
    {
        var report = new VariantPriceCollectionReport { AdditionalRequestsEnabled = true };
        var pending = mapping.VariantTargets.Where(x => !x.IsSelected || !HasVerifiedPrice(x.Independency.Price))
            .GroupBy(x => x.Asin, StringComparer.Ordinal).ToList();
        var parser = new AmazonProductParser();
        try
        {
            await SaveCheckpointAsync(mapping, report, outputDirectory);
            for (var index = 0; index < pending.Count; index++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                var group = pending[index];
                Console.WriteLine($"옵션 가격 확인 {index + 1}/{pending.Count}: {group.Key} / {group.First().Names}");
                var observation = new VariantPriceObservation { Asin = group.Key };
                report.Observations.Add(observation);
                var variantDirectory = Path.Combine(outputDirectory, "variants", group.Key);
                Directory.CreateDirectory(variantDirectory);
                var url = new Uri($"https://www.amazon.com/dp/{group.Key}?th=1&psc=1");

                try
                {
                    var page = await scraper.FetchPageAsync(url, cancellationToken);
                    observation.FetchedAtUtc = page.FetchedAtUtc;
                    observation.HttpStatusCode = page.HttpStatusCode;
                    observation.FinalUrl = page.FinalUrl.AbsoluteUri;
                    await File.WriteAllTextAsync(Path.Combine(variantDirectory, "page.html"),
                        page.Html, new UTF8Encoding(false), cancellationToken);
                    if (page.HttpStatusCode is < 200 or >= 300)
                    {
                        observation.Error = $"HTTP {page.HttpStatusCode}";
                        if (page.HttpStatusCode is 429 or 503)
                        {
                            report.StoppedOnChallengeOrRateLimit = true;
                            report.Warnings.Add($"Variant requests stopped after {observation.Error} at {group.Key}. Unverified prices remain null.");
                        }
                    }
                    else
                    {
                        var details = parser.Parse(page.Html, url, page.FinalUrl, page.FetchedAtUtc, page.HttpStatusCode);
                        observation.SelectedAsin = details.SelectedAsin;
                        if (details.SelectedAsin != group.Key)
                            observation.Error = $"Requested child {group.Key}, but response selected {details.SelectedAsin ?? "unknown"}.";
                        else if (details.CurrentPrice?.Amount is not { } amount || string.IsNullOrWhiteSpace(details.CurrentPrice.Currency))
                            observation.Error = "The child page did not expose an unambiguous numeric price and currency.";
                        else
                        {
                            observation.Price = new Price { Amount = (double)amount, Currency = details.CurrentPrice.Currency };
                            observation.PriceDisplayText = details.CurrentPrice.DisplayText;
                            if (details.Evidence.TryGetValue(nameof(details.CurrentPrice), out var evidence)) observation.Evidence = evidence;
                            foreach (var target in group)
                            {
                                target.Independency.Price = new Price
                                {
                                    Amount = observation.Price.Amount,
                                    Currency = observation.Price.Currency
                                };
                                if (target.IsSelected) mapping.Product.Price = new Price
                                {
                                    Amount = observation.Price.Amount,
                                    Currency = observation.Price.Currency
                                };
                            }
                        }
                    }
                }
                catch (AmazonProductParsingException exception)
                {
                    observation.Error = exception.Message;
                    if (exception.IsChallenge)
                    {
                        report.StoppedOnChallengeOrRateLimit = true;
                        report.Warnings.Add($"Variant requests stopped after a challenge at {group.Key}. Unverified prices remain null.");
                    }
                }
                catch (HttpRequestException exception) { observation.Error = exception.Message; }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { observation.Error = "HTTP request timed out."; }

                await WriteAtomicallyAsync(Path.Combine(variantDirectory, "price.json"), JsonSerializer.Serialize(observation, ReportJson));
                await SaveCheckpointAsync(mapping, report, outputDirectory);
                if (report.StoppedOnChallengeOrRateLimit) break;
            }
            return report;
        }
        finally
        {
            report.WasCancelled = cancellationToken.IsCancellationRequested;
            if (report.WasCancelled) report.Warnings.Add("Collection cancelled. Already observed prices and the partial Product are retained.");
            await SaveCheckpointAsync(mapping, report, outputDirectory);
        }
    }

    public static VariantPriceCollectionReport DescribeWithoutAdditionalRequests(AmazonProductMappingResult mapping)
    {
        var report = new VariantPriceCollectionReport();
        CompleteReport(mapping, report);
        return report;
    }

    private static bool HasVerifiedPrice(Price? price) => price is not null && !string.IsNullOrWhiteSpace(price.Currency);

    private static void CompleteReport(AmazonProductMappingResult mapping, VariantPriceCollectionReport report)
    {
        report.MappedCombinationCount = mapping.VariantTargets.Count;
        report.CombinationsWithVerifiedPrice = mapping.VariantTargets.Count(x => HasVerifiedPrice(x.Independency.Price));
        report.AdditionalRequestCount = report.Observations.Count;
        report.Warnings.RemoveAll(x => x.StartsWith("Verified prices:", StringComparison.Ordinal));
        if (report.CombinationsWithVerifiedPrice < report.MappedCombinationCount)
            report.Warnings.Add($"Verified prices: {report.CombinationsWithVerifiedPrice}/{report.MappedCombinationCount}. Unverified combinations have a null price or an unknown currency.");
    }

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static async Task SaveCheckpointAsync(AmazonProductMappingResult mapping, VariantPriceCollectionReport report, string outputDirectory)
    {
        CompleteReport(mapping, report);
        await WriteAtomicallyAsync(Path.Combine(outputDirectory, "product.json"),
            Newtonsoft.Json.JsonConvert.SerializeObject(mapping.Product, Newtonsoft.Json.Formatting.Indented));
        await WriteAtomicallyAsync(Path.Combine(outputDirectory, "variant-prices.json"), JsonSerializer.Serialize(report, ReportJson));
    }

    private static async Task WriteAtomicallyAsync(string path, string json)
    {
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), CancellationToken.None);
        File.Move(temporaryPath, path, overwrite: true);
    }
}

public sealed class VariantPriceCollectionReport
{
    public bool AdditionalRequestsEnabled { get; set; }
    public int MappedCombinationCount { get; set; }
    public int CombinationsWithVerifiedPrice { get; set; }
    public int AdditionalRequestCount { get; set; }
    public bool StoppedOnChallengeOrRateLimit { get; set; }
    public bool WasCancelled { get; set; }
    public List<VariantPriceObservation> Observations { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class VariantPriceObservation
{
    public string Asin { get; set; } = "";
    public string? SelectedAsin { get; set; }
    public string? FinalUrl { get; set; }
    public DateTimeOffset? FetchedAtUtc { get; set; }
    public int? HttpStatusCode { get; set; }
    public Price? Price { get; set; }
    public string? PriceDisplayText { get; set; }
    public AmazonFieldEvidence? Evidence { get; set; }
    public string? Error { get; set; }
}
