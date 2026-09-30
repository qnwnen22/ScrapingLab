namespace ScrapingLab.Collects.Amazon.Models;

public sealed class VariantPriceCollectionReport
{
    public bool AdditionalRequestsEnabled { get; set; }
    public int MappedCombinationCount { get; set; }
    public int CombinationsWithVerifiedPrice { get; set; }
    public int AdditionalRequestCount { get; set; }
    public bool StoppedOnChallengeOrRateLimit { get; set; }
    public bool WasCancelled { get; set; }
    public List<VariantPriceObservation> Observations { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
