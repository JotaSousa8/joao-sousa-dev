using System.Net;
using AnalyticsApi.Contracts;
using AnalyticsApi.Infrastructure.Persistence;

namespace AnalyticsApi.Services;

public sealed class PageViewIngestService(
    AnalyticsDbContext db,
    IConfiguration config,
    GeoIpService geoIp)
{
    public async Task<bool> TryIngestAsync(
        PageViewRequest request,
        string? userAgentHeader,
        string? cloudflareCountry,
        IPAddress? remoteIp,
        CancellationToken cancellationToken = default)
    {
        var path = AnalyticsText.SanitizePath(request.Path);
        if (path is null)
        {
            return false;
        }

        var referrer = AnalyticsText.Truncate(request.Referrer, 500);
        var userAgent = AnalyticsText.Truncate(userAgentHeader, 400);
        var (browser, os) = UserAgentParser.Parse(userAgent);
        var language = AnalyticsText.Truncate(request.Language, 32);
        var screen = AnalyticsText.FormatScreen(request.ScreenWidth, request.ScreenHeight);
        var (utmSource, utmMedium) = UtmAttribution.Resolve(request);
        var clientIp = AnalyticsText.NormalizeIp(remoteIp);
        var salt = config["Analytics:IpSalt"] ?? "change-me-in-production";
        var visitorHash = AnalyticsText.HashVisitor(clientIp, userAgent, salt);

        var country = AnalyticsText.Truncate(cloudflareCountry, 8);
        string? region = null;
        string? city = null;
        string? postalCode = null;
        double? latitude = null;
        double? longitude = null;
        int? asn = null;
        string? org = null;
        string? isp = null;

        var geo = await geoIp.ResolveAsync(remoteIp, cancellationToken);
        if (geo is not null)
        {
            region = geo.Region;
            city = geo.City;
            postalCode = geo.PostalCode;
            latitude = geo.Latitude;
            longitude = geo.Longitude;
            asn = geo.Asn;
            org = geo.Org;
            isp = geo.Isp;
            if (string.IsNullOrWhiteSpace(country) || country.Equals("XX", StringComparison.OrdinalIgnoreCase))
            {
                country = geo.CountryCode;
            }
        }

        db.PageViews.Add(new PageView
        {
            OccurredAtUtc = DateTime.UtcNow,
            Path = path,
            Referrer = string.IsNullOrWhiteSpace(referrer) ? null : referrer,
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            VisitorHash = visitorHash,
            ClientIp = clientIp,
            Country = string.IsNullOrWhiteSpace(country) || country.Equals("XX", StringComparison.OrdinalIgnoreCase)
                ? null
                : country,
            Region = region,
            City = city,
            PostalCode = postalCode,
            Latitude = latitude,
            Longitude = longitude,
            Asn = asn,
            Org = org,
            Isp = isp,
            Language = language,
            Screen = screen,
            Browser = browser,
            Os = os,
            UtmSource = utmSource,
            UtmMedium = utmMedium,
            UtmCampaign = AnalyticsText.Truncate(request.UtmCampaign, 120),
            UtmContent = AnalyticsText.Truncate(request.UtmContent, 120),
            UtmTerm = AnalyticsText.Truncate(request.UtmTerm, 120),
        });

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
