using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ScrapingLab.Models;
using ScrapingLab.Scraping;

namespace ScrapingLab;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CommandLineOptions options;
        try { options = CommandLineOptions.Parse(args); }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 2;
        }
        if (options.Help)
        {
            PrintUsage();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        var stopwatch = Stopwatch.StartNew();
        string? outputDirectory = null;

        try
        {
            PageFetchResult page;
            if (options.HtmlPath is { } savedHtmlPath)
            {
                var inputPath = Path.GetFullPath(savedHtmlPath);
                Console.WriteLine($"저장된 HTML 재파싱: {inputPath}");
                var html = await File.ReadAllTextAsync(inputPath, cancellation.Token);
                page = new PageFetchResult(options.Url!, options.Url!, File.GetLastWriteTimeUtc(inputPath),
                    null, "text/html", html, "saved-html", null);
            }
            else if (options.Url is { } targetUrl)
            {
                using var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                    UseCookies = true
                };
                using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
                httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
                page = await new Scraper(httpClient).FetchPageAsync(AmazonUrl.Normalize(targetUrl), cancellation.Token);
            }
            else
            {
                var samplePath = Path.Combine(AppContext.BaseDirectory, "Samples", "sample.html");
                Console.WriteLine($"로컬 샘플: {samplePath}");
                var html = await File.ReadAllTextAsync(samplePath, cancellation.Token);
                page = new PageFetchResult(new Uri(samplePath), new Uri(samplePath), DateTimeOffset.UtcNow,
                    null, "text/html", html, "sample", null);
            }

            var asin = options.Url is null ? null : AmazonUrl.GetAsin(options.Url);
            outputDirectory = Path.GetFullPath(Path.Combine(options.OutputRoot, asin ?? "page",
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff'Z'")));
            Directory.CreateDirectory(outputDirectory);
            var htmlPath = Path.Combine(outputDirectory, "page.html");
            await File.WriteAllTextAsync(htmlPath, page.Html, new UTF8Encoding(false), cancellation.Token);
            await WriteJsonAsync(Path.Combine(outputDirectory, "capture.json"), new
            {
                originalUrl = options.Url?.AbsoluteUri,
                requestedUrl = page.RequestedUrl.AbsoluteUri,
                finalUrl = page.FinalUrl.AbsoluteUri,
                page.FetchedAtUtc,
                recordedAtUtc = DateTimeOffset.UtcNow,
                page.HttpStatusCode,
                page.ContentType,
                page.SourceKind,
                page.HttpElapsedMilliseconds,
                htmlCharacters = page.Html.Length,
                htmlUtf8Bytes = Encoding.UTF8.GetByteCount(page.Html),
                htmlSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page.Html))).ToLowerInvariant(),
                sourceHtmlPath = options.HtmlPath is null ? null : Path.GetFullPath(options.HtmlPath)
            }, cancellation.Token);
            Console.WriteLine($"원본 HTML: {htmlPath}");

            if (page.HttpStatusCode is < 200 or >= 300)
                throw new HttpRequestException($"HTTP {page.HttpStatusCode}. 원본 응답은 저장되었습니다.");

            double? parseElapsedMilliseconds = null;
            if (asin is not null)
            {
                // 여기에 중단점을 걸어 원본 HTML과 추출된 상품 객체를 비교합니다.
                var parseStopwatch = Stopwatch.StartNew();
                var product = new AmazonProductParser().Parse(page.Html, options.Url!, page.FinalUrl,
                    page.FetchedAtUtc, page.HttpStatusCode);
                parseElapsedMilliseconds = parseStopwatch.Elapsed.TotalMilliseconds;
                var productPath = Path.Combine(outputDirectory, "product.json");
                await WriteJsonAsync(productPath, product, cancellation.Token);
                Console.WriteLine($"상품명: {product.FullTitle}");
                Console.WriteLine($"선택 ASIN: {product.SelectedAsin} / 부모 ASIN: {product.ParentAsin}");
                Console.WriteLine($"가격: {product.CurrentPrice?.DisplayText ?? "확인되지 않음"}");
                Console.WriteLine($"옵션: {product.SelectedColor} / {product.SelectedSize}");
                Console.WriteLine($"평점: {product.Rating} / 평가 수: {product.ReviewCount}");
                Console.WriteLine($"상품 JSON: {productPath}");
                foreach (var warning in product.Warnings) Console.WriteLine($"확인 필요: {warning}");
            }

            var totalElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            await WriteJsonAsync(Path.Combine(outputDirectory, "metrics.json"), new
            {
                page.HttpElapsedMilliseconds,
                parseElapsedMilliseconds,
                totalElapsedMilliseconds,
                note = "Single page observation. CLI startup and build time are excluded."
            }, cancellation.Token);
            Console.WriteLine($"전체 소요 시간: {totalElapsedMilliseconds:N0}ms");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("사용자가 작업을 중단했습니다.");
            return 130;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
                                         or UnauthorizedAccessException or OperationCanceledException
                                         or InvalidOperationException)
        {
            if (outputDirectory is not null)
            {
                await WriteJsonAsync(Path.Combine(outputDirectory, "failure.json"), new
                {
                    errorType = exception.GetType().Name,
                    exception.Message,
                    isChallenge = exception is AmazonProductParsingException { IsChallenge: true },
                    failedAtUtc = DateTimeOffset.UtcNow
                }, CancellationToken.None);
            }
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T data, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, JsonOptions),
            new UTF8Encoding(false), cancellationToken);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("dotnet run --project ScrapingLab -- [URL]");
        Console.WriteLine("dotnet run --project ScrapingLab -- --url URL --html 저장된_HTML_경로");
        Console.WriteLine("추가 옵션: --output 결과_상위_폴더, --help");
    }
}
