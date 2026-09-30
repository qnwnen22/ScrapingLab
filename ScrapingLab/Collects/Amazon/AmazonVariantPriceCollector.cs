using ScrapingLab.Collects.Amazon.Models;
using ScrapingLab.Models;

namespace ScrapingLab.Collects.Amazon;

/// <summary>각 실제 자식 ASIN의 응답에서 확인한 가격을 해당 조합에 대입합니다.</summary>
public sealed class AmazonVariantPriceCollector(AmazonPageClient client)
{
    public async Task<VariantPriceCollectionReport> CollectAsync(
        AmazonProductMappingResult mapping, CancellationToken cancellationToken = default)
    {
        var report = new VariantPriceCollectionReport { AdditionalRequestsEnabled = true };
        var pending = mapping.VariantTargets.Where(x => !x.IsSelected || !HasVerifiedPrice(x.Independency.Price))
            .GroupBy(x => x.Asin, StringComparer.Ordinal).ToList();
        var parser = new AmazonProductParser();
        try
        {
            foreach (var group in pending)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                var observation = new VariantPriceObservation { Asin = group.Key };
                report.Observations.Add(observation);
                var url = new Uri($"https://www.amazon.com/dp/{group.Key}?th=1&psc=1");

                try
                {
                    var page = await client.FetchPageAsync(url, cancellationToken).ConfigureAwait(false);
                    observation.FetchedAtUtc = page.FetchedAtUtc;
                    observation.HttpStatusCode = page.HttpStatusCode;
                    observation.FinalUrl = page.FinalUrl.AbsoluteUri;
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
                                target.Independency.Price = CopyPrice(observation.Price);
                                if (target.IsSelected) mapping.Product.Price = CopyPrice(observation.Price);
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

                if (report.StoppedOnChallengeOrRateLimit) break;
            }
            return report;
        }
        finally
        {
            report.MappedCombinationCount = mapping.VariantTargets.Count;
            report.CombinationsWithVerifiedPrice = mapping.VariantTargets.Count(x => HasVerifiedPrice(x.Independency.Price));
            report.AdditionalRequestCount = report.Observations.Count;
            report.WasCancelled = cancellationToken.IsCancellationRequested;
            if (report.CombinationsWithVerifiedPrice < report.MappedCombinationCount)
                report.Warnings.Add($"Verified prices: {report.CombinationsWithVerifiedPrice}/{report.MappedCombinationCount}. Unverified prices remain null or have an unknown currency.");
        }
    }

    private static bool HasVerifiedPrice(Price? price) => price is not null && !string.IsNullOrWhiteSpace(price.Currency);

    private static Price CopyPrice(Price price) => new() { Amount = price.Amount, Currency = price.Currency };
}
