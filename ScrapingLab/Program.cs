using System.Diagnostics;
using System.Text;
using ScrapingLab.Scraping;

namespace ScrapingLab;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Uri? targetUrl = null;
        if (args.Length > 1 || (args.Length == 1 &&
            (!Uri.TryCreate(args[0], UriKind.Absolute, out targetUrl) ||
             (targetUrl.Scheme != Uri.UriSchemeHttp && targetUrl.Scheme != Uri.UriSchemeHttps))))
        {
            Console.Error.WriteLine("사용법: dotnet run --project ScrapingLab -- [http 또는 https URL]");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ScrapingLab/0.1");
        var scraper = new Scraper(httpClient);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            string html;
            if (targetUrl is null)
            {
                var samplePath = Path.Combine(AppContext.BaseDirectory, "Samples", "sample.html");
                Console.WriteLine($"로컬 샘플: {samplePath}");
                html = await File.ReadAllTextAsync(samplePath, cancellation.Token);
            }
            else
            {
                html = await scraper.FetchHtmlAsync(targetUrl, cancellation.Token);
            }

            // 이 지점에 중단점을 걸면 수신한 HTML을 확인할 수 있습니다.
            // 대상 사이트가 정해지면 여기에 데이터 추출과 저장 코드를 연결합니다.
            var outputDirectory = Path.GetFullPath("artifacts");
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, "page.html");
            await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8, cancellation.Token);

            Console.WriteLine($"HTML 길이: {html.Length:N0}자");
            Console.WriteLine($"저장 경로: {outputPath}");
            Console.WriteLine($"소요 시간: {stopwatch.Elapsed.TotalMilliseconds:N0}ms");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("사용자가 작업을 중단했습니다.");
            return 130;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
                                         or UnauthorizedAccessException or OperationCanceledException)
        {
            // 디버거에서 예외 발생 위치를 확인하고, 콘솔에서도 호출 스택을 볼 수 있습니다.
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
