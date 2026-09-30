namespace ScrapingLab.Models;

/// <summary>One observed product page, including the delivery and currency context of that response.</summary>
public sealed class AmazonProduct
{
    public string RequestedUrl { get; set; } = "";
    public string FinalUrl { get; set; } = "";
    public DateTimeOffset FetchedAtUtc { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? RequestedAsin { get; set; }
    public string? SelectedAsin { get; set; }
    public string? ParentAsin { get; set; }
    public string? CanonicalUrl { get; set; }
    public string Title { get; set; } = "";
    public string? TitleDifferentiators { get; set; }
    public string FullTitle { get; set; } = "";
    public string? Brand { get; set; }
    public string? BrandStoreUrl { get; set; }
    public AmazonPrice? CurrentPrice { get; set; }
    public AmazonPrice? ListPrice { get; set; }
    public Dictionary<string, AmazonPrice> OfferCharges { get; set; } = new(StringComparer.Ordinal);
    public string? SelectedColor { get; set; }
    public string? SelectedSize { get; set; }
    public List<AmazonVariantOption> Variants { get; set; } = [];
    public decimal? Rating { get; set; }
    public int? ReviewCount { get; set; }
    public string? RatingRawText { get; set; }
    public string? ReviewCountRawText { get; set; }
    public List<string> Features { get; set; } = [];
    public Dictionary<string, string> ProductDetails { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AmazonCategory> Categories { get; set; } = [];
    public string? MainImageUrl { get; set; }
    public List<string> ImageUrls { get; set; } = [];
    public string? Description { get; set; }
    public List<string> DescriptionImageUrls { get; set; } = [];
    public string? Availability { get; set; }
    public string? Seller { get; set; }
    public string? SellerUrl { get; set; }
    public string? ShipsFrom { get; set; }
    public string? DeliveryLocation { get; set; }
    public List<AmazonDeliveryMessage> DeliveryMessages { get; set; } = [];
    public Dictionary<string, AmazonFieldEvidence> Evidence { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
}

public sealed class AmazonPrice
{
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string DisplayText { get; set; } = "";
}

/// <summary>An option shown for one dimension; its ASIN is an observed link/default, not a full combination inventory.</summary>
public sealed class AmazonVariantOption
{
    public string Dimension { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Asin { get; set; }
    public bool IsSelected { get; set; }
    public string? State { get; set; }
    public string? Url { get; set; }
    public string? ImageUrl { get; set; }
}

public sealed class AmazonCategory
{
    public string Name { get; set; } = "";
    public string? Url { get; set; }
}

public sealed class AmazonDeliveryMessage
{
    public string Text { get; set; } = "";
    public string? EstimatedDateText { get; set; }
    public string? Destination { get; set; }
    public string? PriceText { get; set; }
    public string? Condition { get; set; }
    public string? CutoffText { get; set; }
}

public sealed class AmazonFieldEvidence
{
    public string Source { get; set; } = "";
    public string RawText { get; set; } = "";
}
