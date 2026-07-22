using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace AnalyticsApi.Services;

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

public sealed class GeoIpService(HttpClient http, IMemoryCache cache, ILogger<GeoIpService> logger)
{
    public async Task<GeoLocation?> ResolveAsync(IPAddress? ip, CancellationToken cancellationToken = default)
    {
        if (ip is null || IPAddress.IsLoopback(ip) || IsPrivate(ip))
        {
            return null;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var key = $"geo-loc:{ip}";
        if (cache.TryGetValue(key, out GeoLocation? cached))
        {
            return cached;
        }

        try
        {
            using var response = await http.GetAsync($"https://ipwho.is/{ip}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<IpWhoResponse>(cancellationToken);
            if (payload is not { Success: true })
            {
                return null;
            }

            var country = string.IsNullOrWhiteSpace(payload.CountryCode)
                ? null
                : payload.CountryCode.Trim().ToUpperInvariant();
            var region = string.IsNullOrWhiteSpace(payload.Region)
                ? null
                : Truncate(payload.Region.Trim(), 80);
            var city = string.IsNullOrWhiteSpace(payload.City)
                ? null
                : Truncate(payload.City.Trim(), 80);
            var postal = string.IsNullOrWhiteSpace(payload.Postal)
                ? null
                : Truncate(payload.Postal.Trim(), 24);
            var org = string.IsNullOrWhiteSpace(payload.Connection?.Org)
                ? null
                : Truncate(payload.Connection.Org.Trim(), 120);
            var isp = string.IsNullOrWhiteSpace(payload.Connection?.Isp)
                ? null
                : Truncate(payload.Connection.Isp.Trim(), 120);

            var location = new GeoLocation(
                country,
                region,
                city,
                postal,
                payload.Latitude,
                payload.Longitude,
                payload.Connection?.Asn,
                org,
                isp);
            cache.Set(key, location, TimeSpan.FromHours(12));
            return location;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogDebug(ex, "GeoIP lookup failed for {Ip}", ip);
            return null;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
        }

        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private sealed record IpWhoResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("country_code")] string? CountryCode,
        [property: JsonPropertyName("region")] string? Region,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("postal")] string? Postal,
        [property: JsonPropertyName("latitude")] double? Latitude,
        [property: JsonPropertyName("longitude")] double? Longitude,
        [property: JsonPropertyName("connection")] IpWhoConnection? Connection);

    private sealed record IpWhoConnection(
        [property: JsonPropertyName("asn")] int? Asn,
        [property: JsonPropertyName("org")] string? Org,
        [property: JsonPropertyName("isp")] string? Isp);
}
