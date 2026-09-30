using Newtonsoft.Json;
using ScrapingLab.Models;
using ScrapingLab.Scraping;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace ScrapingLab;


public interface ICollect
{
    Product GetProduct(string url, string html);
}

public class Price
{
    public double Amount { get; set; } = 0;
    public string? Currency { get; set; }
}
public class Independency
{
    /// <summary>
    /// 상품 옵션 독립 조합 고유 식별자
    /// </summary>
    public string? Codes { get; set; }
    /// <summary>
    /// 상품 옵션 독립 조합 이름
    /// <example>빨강|S, 파랑|X</example>
    /// </summary>
    public string? Names { get; set; }
    /// <summary>
    /// 상품 가격
    /// </summary>
    public Price? Price { get; set; }
}
public class OptionValue
{
    /// <summary>
    /// 옵션값 고유 식별자
    /// </summary>
    public string? Code { get; set; }
    /// <summary>
    /// 옵션 값
    /// <example>
    /// 예: "빨간", "파랑", "초록", "S", "M", "L"
    /// </example>
    /// </summary>
    public string? Name { get; set; }
    /// <summary>
    /// 상품 이미지 URL
    /// </summary>
    public string? ImageUrl { get; set; }
}
public class Combination
{
    /// <summary>
    /// 옵션 조합 고유 식별자
    /// </summary>
    public string? Code { get; set; }
    /// <summary>
    /// 옵션명
    /// <example>
    /// 예: "색상", "크기"
    /// </example>
    /// </summary>
    public string? Name { get; set; }
    public List<OptionValue>? OptionValues { get; set; } = null;
}
public class Option
{
    public List<Combination>? Combinations { get; set; } = null;
    public List<Independency>? Independencies { get; set; } = null;
}

public class Product
{
    /// <summary>
    /// 상품 고유 식별자
    /// </summary>
    public string? Code { get; set; }
    /// <summary>
    /// 상품명
    /// </summary>
    public string? Title { get; set; }
    /// <summary>
    /// 브랜드 명
    /// </summary>
    public string? Brand { get; set; }
    /// <summary>
    /// 상품 URL
    /// </summary>
    public string ItemUrl { get; set; } = "";
    /// <summary>
    /// 상품 이미지 URL
    /// </summary>
    public List<string> ItemImages { get; set; } = new List<string>();
    /// <summary>
    /// 상품 설명
    /// </summary>
    public List<string>? Description { get; set; } = null;
    /// <summary>
    /// 상품 상세페이지 HTML
    /// </summary>
    public string? DetailHtml { get; set; } = null;
    /// <summary>
    /// 상품 가격
    /// </summary>
    public Price? Price { get; set; }
    public Option? Option { get; set; }
}

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

        #region MyRegion
        //var prod = new Product();
        //prod.Option = new Option()
        //{
        //    Combinations = new List<Combination>()
        //    {
        //        new Combination
        //        {
        //            Code = "color",
        //            Name = "색상",
        //            OptionValues = new List<OptionValue>()
        //            {
        //                new OptionValue
        //                {
        //                    Code = "red",
        //                    Name ="빨강"
        //                },
        //                new OptionValue
        //                {
        //                    Code = "blue",
        //                    Name ="파랑"
        //                }
        //            }
        //        },
        //        new Combination
        //        {
        //            Code = "size",
        //            Name = "사이즈",
        //            OptionValues = new List<OptionValue>()
        //            {
        //                new OptionValue
        //                {
        //                    Code = "s",
        //                    Name ="S"
        //                },
        //                new OptionValue
        //                {
        //                    Code = "x",
        //                    Name ="X"
        //                }
        //            }
        //        }
        //    },
        //    Independencies = new List<Independency>()
        //    {
        //        new Independency()
        //        {
        //            Codes = "red|s",
        //            Names = "빨강|S",
        //        },
        //        new Independency()
        //        {
        //            Codes = "red|x",
        //            Names = "빨강|X",
        //        },
        //        new Independency()
        //        {
        //            Codes = "blue|s",
        //            Names = "파랑|S",
        //        },
        //        new Independency()
        //        {
        //            Codes = "blue|x",
        //            Names = "파랑|X",
        //        }
        //    }
        //};
        //var json = JsonConvert.SerializeObject(prod, Formatting.Indented);
        #endregion

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
            using var httpClient = CreateHttpClient();
            var scraper = new Scraper(httpClient);
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
                page = await scraper.FetchPageAsync(AmazonUrl.Normalize(targetUrl), cancellation.Token);
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
            double? variantPriceElapsedMilliseconds = null;
            if (asin is not null)
            {
                // 여기에 중단점을 걸어 원본 HTML과 추출된 상품 객체를 비교합니다.
                var parseStopwatch = Stopwatch.StartNew();
                var details = new AmazonProductParser().Parse(page.Html, options.Url!, page.FinalUrl,
                    page.FetchedAtUtc, page.HttpStatusCode);
                var mapping = new AmazonProductMapper().Map(details, page.Html);
                var product = mapping.Product;
                parseElapsedMilliseconds = parseStopwatch.Elapsed.TotalMilliseconds;
                var productPath = Path.Combine(outputDirectory, "product.json");
                await File.WriteAllTextAsync(productPath, JsonConvert.SerializeObject(product, Formatting.Indented),
                    new UTF8Encoding(false), cancellation.Token);
                await WriteJsonAsync(Path.Combine(outputDirectory, "amazon-details.json"), details, cancellation.Token);
                await WriteJsonAsync(Path.Combine(outputDirectory, "option-mapping.json"), new
                {
                    mapping.Warnings,
                    targets = mapping.VariantTargets.Select(target => new
                    {
                        target.Asin, target.Codes, target.Names, target.IsSelected
                    })
                }, cancellation.Token);
                var variantStopwatch = Stopwatch.StartNew();
                var variantPrices = options.VariantPrices
                    ? await new AmazonVariantPriceCollector(scraper).CollectAsync(mapping, outputDirectory, cancellation.Token)
                    : AmazonVariantPriceCollector.DescribeWithoutAdditionalRequests(mapping);
                if (options.VariantPrices) variantPriceElapsedMilliseconds = variantStopwatch.Elapsed.TotalMilliseconds;
                await File.WriteAllTextAsync(productPath, JsonConvert.SerializeObject(product, Formatting.Indented),
                    new UTF8Encoding(false), cancellation.Token);
                await WriteJsonAsync(Path.Combine(outputDirectory, "variant-prices.json"), variantPrices, cancellation.Token);
                Console.WriteLine($"상품명: {product.Title}");
                Console.WriteLine($"선택 ASIN: {product.Code} / 부모 ASIN: {details.ParentAsin}");
                Console.WriteLine($"가격: {product.Price?.Currency} {product.Price?.Amount}");
                Console.WriteLine($"옵션 조합: {product.Option?.Independencies?.Count ?? 0}개 / 가격 확인: {variantPrices.CombinationsWithVerifiedPrice}개");
                Console.WriteLine($"상품 JSON: {productPath}");
                var detailWarnings = details.Warnings.Where(warning => !options.VariantPrices ||
                    !warning.StartsWith("Variants are the dimension options exposed", StringComparison.Ordinal));
                foreach (var warning in detailWarnings.Concat(mapping.Warnings).Concat(variantPrices.Warnings))
                    Console.WriteLine($"확인 필요: {warning}");
            }

            var totalElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            await WriteJsonAsync(Path.Combine(outputDirectory, "metrics.json"), new
            {
                page.HttpElapsedMilliseconds,
                parseElapsedMilliseconds,
                variantPriceElapsedMilliseconds,
                totalElapsedMilliseconds,
                note = options.VariantPrices
                    ? "Product page and additional child-page observations. CLI startup and build time are excluded."
                    : "Single page observation. CLI startup and build time are excluded."
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

    private static void PrintUsage()
    {
        Console.WriteLine("dotnet run --project ScrapingLab -- [URL]");
        Console.WriteLine("dotnet run --project ScrapingLab -- --url URL --html 저장된_HTML_경로");
        Console.WriteLine("추가 옵션: --output 결과_상위_폴더, --help");
        Console.WriteLine("--variant-prices: 노출된 옵션 조합의 상품 페이지를 순서대로 요청하여 각 가격을 확인합니다.");
    }
}
