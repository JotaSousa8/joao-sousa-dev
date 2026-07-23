using System.Text.Json.Serialization;

namespace AnalyticsApi.Services.GeoIp.Models;

internal sealed record IpWhoResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("country_code")] string? CountryCode,
    [property: JsonPropertyName("region")] string? Region,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("postal")] string? Postal,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("connection")] IpWhoConnection? Connection);

internal sealed record IpWhoConnection(
    [property: JsonPropertyName("asn")] int? Asn,
    [property: JsonPropertyName("org")] string? Org,
    [property: JsonPropertyName("isp")] string? Isp);
