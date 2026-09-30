using ScrapingLab.Models;

namespace ScrapingLab.Collects.Amazon.Models;

public sealed class VariantPriceObservation
{
    public string Asin { get; set; } = "";
    public string? SelectedAsin { get; set; }
    public string? FinalUrl { get; set; }
    public DateTimeOffset? FetchedAtUtc { get; set; }
    public int? HttpStatusCode { get; set; }
    public Price? Price { get; set; }
    public string? PriceDisplayText { get; set; }
    public AmazonFieldEvidence? Evidence { get; set; }
    public string? Error { get; set; }
}
