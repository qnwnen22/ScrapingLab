namespace ScrapingLab.Models;

public sealed record PageFetchResult(
    Uri RequestedUrl,
    Uri FinalUrl,
    DateTimeOffset FetchedAtUtc,
    int? HttpStatusCode,
    string? ContentType,
    string Html,
    string SourceKind,
    double? HttpElapsedMilliseconds);
