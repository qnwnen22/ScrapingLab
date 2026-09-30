namespace ScrapingLab.Scraping;

public sealed class Scraper(HttpClient httpClient)
{
    public async Task<string> FetchHtmlAsync(Uri targetUrl, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"요청: {targetUrl}");

        // 이 줄에 중단점을 걸고 요청과 응답을 단계별로 확인합니다.
        using var response = await httpClient.GetAsync(targetUrl, cancellationToken);
        Console.WriteLine($"응답: {(int)response.StatusCode} {response.StatusCode}");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return html;
    }
}
