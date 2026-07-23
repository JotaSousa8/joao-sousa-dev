namespace AnalyticsApi.Services.GeoIp;

using System.Net;
using AnalyticsApi.Contracts.Responses;
using AnalyticsApi.Services.GeoIp.Models;
using Microsoft.Extensions.Caching.Memory;

public sealed class GeoIpService(HttpClient http, IMemoryCache cache, ILogger<GeoIpService> logger)
    : IGeoIpService
{
    public async Task<GeoLocation?> ResolveAsync(IPAddress? ip, CancellationToken cancellationToken = default)
    {
        if (ip is null || IPAddress.IsLoopback(ip) || GeoIpHelper.IsPrivate(ip))
        {
            return null;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var key = $"geo-loc-v3:{ip}";
        if (cache.TryGetValue(key, out GeoLocation? cached))
        {
            return cached;
        }

        var ipApiTask = TryIpApiAsync(ip, cancellationToken);
        var ipWhoTask = TryIpWhoAsync(ip, cancellationToken);
        await Task.WhenAll(ipApiTask, ipWhoTask);

        var location = GeoIpHelper.PickBest(ipApiTask.Result, ipWhoTask.Result);
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
                GeoIpHelper.NormalizeCountry(payload.CountryCode),
                GeoIpHelper.TruncateOrNull(payload.RegionName, 80),
                GeoIpHelper.TruncateOrNull(payload.City, 80),
                GeoIpHelper.TruncateOrNull(payload.Zip, 24),
                payload.Lat,
                payload.Lon,
                GeoIpHelper.ParseAsn(payload.As),
                GeoIpHelper.TruncateOrNull(payload.Org, 120),
                GeoIpHelper.TruncateOrNull(payload.Isp, 120));
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
                GeoIpHelper.NormalizeCountry(payload.CountryCode),
                GeoIpHelper.TruncateOrNull(payload.Region, 80),
                GeoIpHelper.TruncateOrNull(payload.City, 80),
                GeoIpHelper.TruncateOrNull(payload.Postal, 24),
                payload.Latitude,
                payload.Longitude,
                payload.Connection?.Asn,
                GeoIpHelper.TruncateOrNull(payload.Connection?.Org, 120),
                GeoIpHelper.TruncateOrNull(payload.Connection?.Isp, 120));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogDebug(ex, "ipwho.is GeoIP lookup failed for {Ip}", ip);
            return null;
        }
    }
}
