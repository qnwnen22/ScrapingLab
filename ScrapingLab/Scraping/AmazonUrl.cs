using System.Text.RegularExpressions;

namespace ScrapingLab.Scraping;

public static partial class AmazonUrl
{
    public static string? GetAsin(Uri url)
    {
        if (!url.Host.Equals("amazon.com", StringComparison.OrdinalIgnoreCase) &&
            !url.Host.EndsWith(".amazon.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = ProductPath().Match(url.AbsolutePath);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static Uri Normalize(Uri url) => GetAsin(url) is { } asin
        ? new Uri($"https://www.amazon.com/dp/{asin}?th=1&psc=1")
        : url;

    [GeneratedRegex(@"/(?:dp|gp/product)/([A-Z0-9]{10})(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ProductPath();
}
