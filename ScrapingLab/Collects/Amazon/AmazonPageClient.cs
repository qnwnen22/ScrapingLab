using System.Diagnostics;
using ScrapingLab.Collects.Amazon.Models;

namespace ScrapingLab.Collects.Amazon;

public sealed class AmazonPageClient(HttpClient httpClient)
{
    public async Task<PageFetchResult> FetchPageAsync(Uri targetUrl, CancellationToken cancellationToken = default)
        => await FetchAsync(new HttpRequestMessage(HttpMethod.Get, targetUrl), cancellationToken).ConfigureAwait(false);

    public async Task<PageFetchResult> FetchBulkAsync(AmazonVariantBulkRequest bulk, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, bulk.Url);
        request.Headers.Referrer = bulk.Referer;
        foreach (var header in bulk.Headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return await FetchAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PageFetchResult> FetchAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var ownedRequest = request;
        var targetUrl = request.RequestUri!;
        var stopwatch = Stopwatch.StartNew();
        // 이 줄에 중단점을 걸고 요청과 응답을 단계별로 확인합니다.
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // 차단 응답도 HTML을 읽어 정상 상품 응답과 구분합니다.
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new PageFetchResult(targetUrl, response.RequestMessage?.RequestUri ?? targetUrl,
            DateTimeOffset.UtcNow, (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString(), html, "http", stopwatch.Elapsed.TotalMilliseconds);
    }
}
