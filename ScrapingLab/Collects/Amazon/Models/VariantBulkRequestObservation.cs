namespace ScrapingLab.Collects.Amazon.Models;

public sealed class VariantBulkRequestObservation
{
    public string Url { get; set; } = "";
    public List<string> Asins { get; set; } = [];
    public DateTimeOffset? FetchedAtUtc { get; set; }
    public int? HttpStatusCode { get; set; }
    public double? HttpElapsedMilliseconds { get; set; }
    public int ResponseAsinCount { get; set; }
    public string? Error { get; set; }
}
