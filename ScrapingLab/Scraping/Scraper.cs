using System.Diagnostics;
using ScrapingLab.Models;

namespace ScrapingLab.Scraping;

public sealed class Scraper(HttpClient httpClient)
{
    public async Task<PageFetchResult> FetchPageAsync(Uri targetUrl, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"요청: {targetUrl}");

        var stopwatch = Stopwatch.StartNew();
        // 이 줄에 중단점을 걸고 요청과 응답을 단계별로 확인합니다.
        using var response = await httpClient.GetAsync(targetUrl, cancellationToken);
        Console.WriteLine($"응답: {(int)response.StatusCode} {response.StatusCode}");

        // 차단 응답도 먼저 원본을 보존하여 정상 상품 데이터와 구분합니다.
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return new PageFetchResult(targetUrl, response.RequestMessage?.RequestUri ?? targetUrl,
            DateTimeOffset.UtcNow, (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString(), html, "http", stopwatch.Elapsed.TotalMilliseconds);
    }
}
