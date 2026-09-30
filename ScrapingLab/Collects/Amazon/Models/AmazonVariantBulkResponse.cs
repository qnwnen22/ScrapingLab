namespace ScrapingLab.Collects.Amazon.Models;

public sealed class AmazonVariantBulkResponse
{
    public List<VariantPriceObservation> Observations { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
