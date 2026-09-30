using ScrapingLab.Models;

namespace ScrapingLab.Collects.Amazon.Models;

/// <summary>Associates a real child ASIN with the existing user-model combination.</summary>
public sealed class AmazonVariantTarget
{
    public string Asin { get; set; } = "";
    public string Codes { get; set; } = "";
    public string Names { get; set; } = "";
    public bool IsSelected { get; set; }
    public Independency Independency { get; set; } = new();
}
