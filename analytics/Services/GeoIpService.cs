using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    // v2: prefer ip-api (often better city match for PT ISP ranges than ipwho.is).
    private static readonly Regex AsnRegex = new(@"^AS(?<n>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        var key = $"geo-loc-v2:{ip}";
        if (cache.TryGetValue(key, out GeoLocation? cached))
        {
            return cached;
        }

        var location = await TryIpApiAsync(ip, cancellationToken)
            ?? await TryIpWhoAsync(ip, cancellationToken);

        if (location is not null)
        {
            cache.Set(key, location, TimeSpan.FromHours(12));
        }

        return location;
    }

    private async Task<GeoLocation?> TryIpApiAsync(IPAddress ip, CancellationToken cancellationToken)
    {
        try
        {
            // Free tier is HTTP-only (no key). Fine for server-side lookups.
            var url =
                $"http://ip-api.com/json/{ip}?fields=status,message,countryCode,regionName,city,zip,lat,lon,isp,org,as";
            using var response = await http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<IpApiResponse>(cancellationToken);
            if (payload is not { Status: "success" })
            {
                return null;
            }

            return new GeoLocation(
                NormalizeCountry(payload.CountryCode),
                TruncateOrNull(payload.RegionName, 80),
                TruncateOrNull(payload.City, 80),
                TruncateOrNull(payload.Zip, 24),
                payload.Lat,
                payload.Lon,
                ParseAsn(payload.As),
                TruncateOrNull(payload.Org, 120),
                TruncateOrNull(payload.Isp, 120));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogDebug(ex, "ip-api GeoIP lookup failed for {Ip}", ip);
            return null;
        }
    }

    private async Task<GeoLocation?> TryIpWhoAsync(IPAddress ip, CancellationToken cancellationToken)
    {
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

            return new GeoLocation(
                NormalizeCountry(payload.CountryCode),
                TruncateOrNull(payload.Region, 80),
                TruncateOrNull(payload.City, 80),
                TruncateOrNull(payload.Postal, 24),
                payload.Latitude,
                payload.Longitude,
                payload.Connection?.Asn,
                TruncateOrNull(payload.Connection?.Org, 120),
                TruncateOrNull(payload.Connection?.Isp, 120));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogDebug(ex, "ipwho.is GeoIP lookup failed for {Ip}", ip);
            return null;
        }
    }

    private static string? NormalizeCountry(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    private static string? TruncateOrNull(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    private static int? ParseAsn(string? asField)
    {
        if (string.IsNullOrWhiteSpace(asField))
        {
            return null;
        }

        var match = AsnRegex.Match(asField.Trim());
        return match.Success && int.TryParse(match.Groups["n"].Value, out var n) ? n : null;
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

    private sealed record IpApiResponse(
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
