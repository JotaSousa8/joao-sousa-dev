namespace AnalyticsApi.Contracts.Responses;

public sealed record GeoLocation(
    string? CountryCode,
    string? Region,
    string? City,
    string? PostalCode,
    double? Latitude,
    double? Longitude,
    int? Asn,
    string? Org,
    string? Isp);
