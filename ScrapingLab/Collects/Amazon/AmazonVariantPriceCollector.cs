using ScrapingLab.Collects.Amazon.Models;
using ScrapingLab.Models;

namespace ScrapingLab.Collects.Amazon;

/// <summary>자식 페이지 대신 ASIN 벌크 AJAX 응답으로 각 옵션 조합 가격을 채웁니다.</summary>
public sealed class AmazonVariantPriceCollector(AmazonPageClient client)
{
    public async Task<VariantPriceCollectionReport> CollectAsync(
        AmazonProductMappingResult mapping, string html, Uri pageUrl, CancellationToken cancellationToken = default)
    {
        var report = new VariantPriceCollectionReport
        {
            AdditionalRequestsEnabled = true,
            BatchSize = AmazonVariantBulkRequestBuilder.BatchSize
        };
        var pending = mapping.VariantTargets.Where(x => !x.IsSelected || !HasVerifiedPrice(x.Independency.Price))
            .GroupBy(x => x.Asin, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pending.Count == 0) return report;
            var requests = new AmazonVariantBulkRequestBuilder().CreateBatches(html, pending.Keys, pageUrl);
            if (requests.Count == 0)
            {
                report.Warnings.Add("Bulk request metadata was missing. Unverified option prices remain null.");
                return report;
            }
            foreach (var request in requests)
            {
                if (report.BulkRequests.Count > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
                var bulk = new VariantBulkRequestObservation { Url = request.Url.AbsoluteUri, Asins = request.Asins.ToList() };
                report.BulkRequests.Add(bulk);
                try
                {
                    var page = await client.FetchBulkAsync(request, cancellationToken).ConfigureAwait(false);
                    bulk.FetchedAtUtc = page.FetchedAtUtc;
                    bulk.HttpStatusCode = page.HttpStatusCode;
                    bulk.HttpElapsedMilliseconds = page.HttpElapsedMilliseconds;
                    if (page.HttpStatusCode is < 200 or >= 300)
                    {
                        report.StoppedOnChallengeOrRateLimit = page.HttpStatusCode is 429 or 503;
                        throw new HttpRequestException($"Bulk request returned HTTP {page.HttpStatusCode}.");
                    }
                    var parsed = new AmazonVariantBulkResponseParser().Parse(page.Html);
                    report.Warnings.AddRange(parsed.Warnings);
                    var received = parsed.Observations.GroupBy(x => x.Asin, StringComparer.Ordinal)
                        .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
                    bulk.ResponseAsinCount = received.Keys.Count(request.Asins.Contains);
                    foreach (var unexpected in received.Keys.Where(x => !request.Asins.Contains(x)))
                        report.Warnings.Add($"Ignored unrequested ASIN {unexpected} in the bulk response.");
                    foreach (var asin in request.Asins)
                    {
                        VariantPriceObservation observation;
                        if (!received.TryGetValue(asin, out var records))
                            observation = new VariantPriceObservation { Asin = asin, Error = "No slot was returned for the requested ASIN." };
                        else if (records.Select(x => (x.Price?.Amount, x.Price?.Currency, x.IsAvailable, x.Error)).Distinct().Count() > 1)
                            observation = new VariantPriceObservation { Asin = asin, Error = "Conflicting slots were returned for the same ASIN." };
                        else observation = records[0];
                        observation.FetchedAtUtc = page.FetchedAtUtc;
                        observation.HttpStatusCode = page.HttpStatusCode;
                        observation.FinalUrl = page.FinalUrl.AbsoluteUri;
                        report.Observations.Add(observation);
                        if (observation.ResponseAsin != asin || observation.Price is not { } price || !HasVerifiedPrice(price)) continue;
                        if (mapping.Product.Price?.Currency is { } pageCurrency && price.Currency != pageCurrency)
                        {
                            observation.Error = $"Bulk currency {price.Currency} differs from page currency {pageCurrency}.";
                            observation.Price = null;
                            continue;
                        }
                        foreach (var target in pending[asin])
                        {
                            target.Independency.Price = CopyPrice(price);
                            if (target.IsSelected) mapping.Product.Price = CopyPrice(price);
                        }
                    }
                    if (bulk.ResponseAsinCount == 0)
                    {
                        bulk.Error = "The bulk response did not contain any requested ASIN slots.";
                        report.Warnings.Add(bulk.Error);
                        break;
                    }
                }
                catch (AmazonProductParsingException exception)
                {
                    bulk.Error = exception.Message;
                    report.StoppedOnChallengeOrRateLimit = exception.IsChallenge;
                }
                catch (HttpRequestException exception) { bulk.Error = exception.Message; }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { bulk.Error = "Bulk HTTP request timed out."; }
                if (bulk.Error is not null)
                {
                    report.Warnings.Add(bulk.Error + " Remaining bulk requests stopped.");
                    break;
                }
            }
            return report;
        }
        finally
        {
            report.MappedCombinationCount = mapping.VariantTargets.Count;
            report.CombinationsWithVerifiedPrice = mapping.VariantTargets.Count(x => HasVerifiedPrice(x.Independency.Price));
            report.AdditionalRequestCount = report.BulkRequests.Count;
            report.WasCancelled = cancellationToken.IsCancellationRequested;
            if (report.CombinationsWithVerifiedPrice < report.MappedCombinationCount)
                report.Warnings.Add($"Verified prices: {report.CombinationsWithVerifiedPrice}/{report.MappedCombinationCount}. Unverified prices remain null or have an unknown currency.");
        }
    }

    private static bool HasVerifiedPrice(Price? price) => price is not null && !string.IsNullOrWhiteSpace(price.Currency);
    private static Price CopyPrice(Price price) => new() { Amount = price.Amount, Currency = price.Currency };
}
