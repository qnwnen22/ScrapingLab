using ScrapingLab.Models;

namespace ScrapingLab.Collects.Amazon.Models;

public sealed class AmazonProductMappingResult
{
    public Product Product { get; set; } = new();
    public List<AmazonVariantTarget> VariantTargets { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
