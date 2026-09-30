using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using ScrapingLab.Collects.Amazon.Models;

namespace ScrapingLab.Collects.Amazon;

public sealed class AmazonVariantBulkRequestBuilder
{
    // 현재 상품 페이지의 twister-slots-dimsum.getBatchSize()가 사용하는 묶음 크기입니다.
    public const int BatchSize = 8;

    public IReadOnlyList<AmazonVariantBulkRequest> CreateBatches(string html, IEnumerable<string> codes, Uri pageUrl)
    {
        var asins = codes.Select(NormalizeAsin).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (asins.Count == 0) return [];
        var template = Create(html, asins.Take(BatchSize), pageUrl);
        if (template is null) return [];
        return asins.Chunk(BatchSize).Select(batch => new AmazonVariantBulkRequest
        {
            Url = new UriBuilder(template.Url)
            {
                Query = Regex.Replace(template.Url.Query, @"(?<=asinList=)[^&]*", Uri.EscapeDataString(string.Join(",", batch)))
            }.Uri,
            Asins = batch,
            Referer = template.Referer,
            Headers = template.Headers
        }).ToList();
    }

    public AmazonVariantBulkRequest? Create(string html, IEnumerable<string> codes, Uri pageUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(pageUrl);
        if (AmazonUrl.GetAsin(pageUrl) is null) throw new ArgumentException("Amazon US 상품 URL이 필요합니다.", nameof(pageUrl));
        var asins = codes.Select(NormalizeAsin).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (asins.Count == 0) return null;
        if (asins.Count > BatchSize) throw new ArgumentException($"한 벌크 요청에는 최대 {BatchSize}개 ASIN을 지정합니다.", nameof(codes));

        var document = new HtmlParser().ParseDocument(html);
        var twister = document.QuerySelectorAll("script")
            .FirstOrDefault(x => x.TextContent.Contains("twister-js-init-dpx-data", StringComparison.Ordinal))?.TextContent;
        JsonElement? immutable = null;
        if (twister is not null)
        {
            foreach (var key in new[] { "twisterUpdateURLInfo", "twisterUpdateURLAppend" })
            {
                var update = AmazonEmbeddedJson.ReadProperty(twister, key);
                if (update is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("immutableParams", out var parameters))
                { immutable = parameters; break; }
            }
        }

        var productScript = document.QuerySelectorAll("script")
            .FirstOrDefault(x => Regex.IsMatch(x.TextContent, "\"productGroupID\"\\s*:")
                && Regex.IsMatch(x.TextContent, "\"parentAsin\"\\s*:"))?.TextContent;
        var parent = NormalizeAsin(Text(immutable, "parentAsin")
            ?? (twister is null ? null : AmazonEmbeddedJson.ReadString(twister, "parentAsin"))
            ?? (productScript is null ? null : AmazonEmbeddedJson.ReadString(productScript, "parentAsin")));
        if (parent is null) return null;
        var ptd = Text(immutable, "ptd");
        var pgid = Text(immutable, "pgid")
            ?? (productScript is null ? null : AmazonEmbeddedJson.ReadString(productScript, "productGroupID"));
        var landing = NormalizeAsin(twister is null ? null : AmazonEmbeddedJson.ReadString(twister, "landingAsin"))
            ?? AmazonUrl.GetAsin(pageUrl)!;
        var deviceType = twister is null ? "web" : AmazonEmbeddedJson.ReadString(twister, "deviceType") ?? "web";
        var isMobile = deviceType is "mobile" or "mobileApp";
        var parametersByName = new Dictionary<string, string>
        {
            ["isDimensionSlotsAjax"] = "1",
            ["asinList"] = string.Join(",", asins),
            ["vs"] = "1",
            ["asin"] = landing,
            ["parentAsin"] = parent,
            ["isPrime"] = "0",
            ["deviceOs"] = Text(immutable, "deviceOs") ?? (isMobile ? "android" : "unrecognized"),
            ["landingAsin"] = landing,
            ["deviceType"] = deviceType,
            ["showFancyPrice"] = "false",
            ["twisterFlavor"] = isMobile ? "twisterPlusInlineTwister2D" : "twisterPlusDesktopConfigurator"
        };
        if (!string.IsNullOrWhiteSpace(ptd)) parametersByName["productTypeDefinition"] = ptd;
        if (!string.IsNullOrWhiteSpace(pgid)) parametersByName["productGroupId"] = pgid;
        var query = string.Join("&", parametersByName.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        return new AmazonVariantBulkRequest
        {
            Url = new UriBuilder(pageUrl) { Path = "/gp/product/ajax/twisterDimensionSlotsDefault", Query = query, Fragment = "" }.Uri,
            Asins = asins,
            Referer = pageUrl,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Accept"] = "text/html,*/*",
                ["X-Requested-With"] = "XMLHttpRequest"
            }
        };
    }

    private static string? Text(JsonElement? json, string key) => json is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    public static string? NormalizeAsin(string? code)
    {
        var asin = code?.Split('ㅡ').Last().Trim().ToUpperInvariant();
        return asin is not null && Regex.IsMatch(asin, "^[A-Z0-9]{10}$") ? asin : null;
    }
}
