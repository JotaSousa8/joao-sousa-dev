using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace AnalyticsApi.Services;

public sealed class GeoIpService(HttpClient http, IMemoryCache cache, ILogger<GeoIpService> logger)
{
    public async Task<string?> ResolveCountryAsync(IPAddress? ip, CancellationToken cancellationToken = default)
    {
        if (ip is null || IPAddress.IsLoopback(ip) || IsPrivate(ip))
        {
            return null;
        }

        // Prefer IPv4-mapped form when present
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var key = $"geo:{ip}";
        if (cache.TryGetValue(key, out string? cached))
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
            var code = payload is { Success: true } && !string.IsNullOrWhiteSpace(payload.CountryCode)
                ? payload.CountryCode.Trim().ToUpperInvariant()
                : null;

            cache.Set(key, code, TimeSpan.FromHours(12));
            return code;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogDebug(ex, "GeoIP lookup failed for {Ip}", ip);
            return null;
        }
    }

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
        [property: JsonPropertyName("country_code")] string? CountryCode);
}
