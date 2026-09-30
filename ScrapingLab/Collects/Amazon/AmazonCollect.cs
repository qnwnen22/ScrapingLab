using System.Net;
using ScrapingLab.Collects.Amazon.Models;
using ScrapingLab.Models;

namespace ScrapingLab.Collects.Amazon;

/// <summary>Amazon 상품 요청, 파싱, 공통 Product 매핑과 옵션별 가격 확인을 담당합니다.</summary>
public sealed class AmazonCollect(bool collectVariantPrices = true) : ICollect
{
    /// <summary>URL로 수집합니다. HTML을 전달하면 네트워크 요청 없이 해당 응답만 파싱합니다.</summary>
    public Product GetProduct(string url, string? html = null) =>
        GetProductCoreAsync(url, html).GetAwaiter().GetResult();

    private async Task<Product> GetProductCoreAsync(string url, string? html)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            AmazonUrl.GetAsin(uri) is null)
            throw new ArgumentException("Amazon US 상품 URL이 필요합니다.", nameof(url));

        var targetUrl = AmazonUrl.Normalize(uri);
        using var httpClient = CreateHttpClient();
        var client = new AmazonPageClient(httpClient);
        var page = html is null
            ? await client.FetchPageAsync(targetUrl).ConfigureAwait(false)
            : new PageFetchResult(targetUrl, targetUrl, DateTimeOffset.UtcNow,
                null, "text/html", html, "provided-html", null);

        if (page.HttpStatusCode is < 200 or >= 300)
            throw new HttpRequestException($"Amazon 상품 요청 실패: HTTP {page.HttpStatusCode}");

        var details = new AmazonProductParser().Parse(page.Html, targetUrl, page.FinalUrl,
            page.FetchedAtUtc, page.HttpStatusCode);
        var mapping = new AmazonProductMapper().Map(details, page.Html);
        if (html is null && collectVariantPrices)
            await new AmazonVariantPriceCollector(client).CollectAsync(mapping).ConfigureAwait(false);

        return mapping.Product;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, UseCookies = true };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }
}
