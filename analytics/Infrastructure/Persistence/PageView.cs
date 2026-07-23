namespace AnalyticsApi.Infrastructure.Persistence;

public sealed class PageView
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Path { get; set; } = "/";
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public string? VisitorHash { get; set; }
    public string? ClientIp { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? Asn { get; set; }
    public string? Org { get; set; }
    public string? Isp { get; set; }
    public string? Language { get; set; }
    public string? Screen { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }
}
