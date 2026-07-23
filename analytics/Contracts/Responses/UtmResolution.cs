namespace AnalyticsApi.Contracts.Responses;

public sealed record UtmResolution(
    string? Source,
    string? Medium,
    string? Campaign,
    string? Content,
    string? Term);
