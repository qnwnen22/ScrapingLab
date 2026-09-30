namespace ScrapingLab.Collects.Amazon.Models;

public sealed class AmazonVariantBulkRequest
{
    public required Uri Url { get; init; }
    public required IReadOnlyList<string> Asins { get; init; }
    public required Uri Referer { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
