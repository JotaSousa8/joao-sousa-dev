using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace AnalyticsApi.Services;

public sealed record GeoLocation(string? CountryCode, string? City);

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
            var city = string.IsNullOrWhiteSpace(payload.City)
                ? null
                : Truncate(payload.City.Trim(), 80);

            var location = new GeoLocation(country, city);
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
        [property: JsonPropertyName("city")] string? City);
}
