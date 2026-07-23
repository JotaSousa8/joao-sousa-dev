namespace AnalyticsApi.Services.GeoIp.Models;

using System.Text.Json.Serialization;

internal sealed record IpApiResponse(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("countryCode")] string? CountryCode,
    [property: JsonPropertyName("regionName")] string? RegionName,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("zip")] string? Zip,
    [property: JsonPropertyName("lat")] double? Lat,
    [property: JsonPropertyName("lon")] double? Lon,
    [property: JsonPropertyName("isp")] string? Isp,
    [property: JsonPropertyName("org")] string? Org,
    [property: JsonPropertyName("as")] string? As);
