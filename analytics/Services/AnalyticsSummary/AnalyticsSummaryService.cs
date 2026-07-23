namespace AnalyticsApi.Services.AnalyticsSummary;

using AnalyticsApi.Infrastructure.Persistence;
using AnalyticsApi.Services.Shared;
using AnalyticsApi.Services.Utm;
using Microsoft.EntityFrameworkCore;

public sealed class AnalyticsSummaryService(
    AnalyticsDbContext db,
    IConfiguration config,
    IUtmAttributionService utmAttribution) : IAnalyticsSummaryService
{
    public async Task<object> GetSummaryAsync(
        string? fromRaw,
        string? toRaw,
        string? limitRaw,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var last7 = now.AddDays(-7);
        var last30 = now.AddDays(-30);
        var lisbon = AnalyticsText.ResolveLisbonTimeZone();

        var fromUtc = AnalyticsText.ParseQueryUtc(fromRaw, lisbon) ?? last7;
        var toUtc = AnalyticsText.ParseQueryUtc(toRaw, lisbon) ?? now.AddMinutes(1);
        if (toUtc < fromUtc)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }

        var limit = 100;
        if (int.TryParse(limitRaw, out var parsedLimit))
        {
            limit = Math.Clamp(parsedLimit, 1, 500);
        }

        var excludedIps = ExcludedIpOptions.Resolve(config);
        var excludedList = excludedIps.ToList();
        var visitors = excludedList.Count == 0
            ? db.PageViews
            : db.PageViews.Where(x => x.ClientIp == null || !excludedList.Contains(x.ClientIp));
        var ownViews = excludedList.Count == 0
            ? db.PageViews.Where(_ => false)
            : db.PageViews.Where(x => x.ClientIp != null && excludedList.Contains(x.ClientIp));

        var total = await visitors.CountAsync(cancellationToken);
        var last7Count = await visitors.CountAsync(x => x.OccurredAtUtc >= last7, cancellationToken);
        var last30Count = await visitors.CountAsync(x => x.OccurredAtUtc >= last30, cancellationToken);
        var uniqueVisitors30 = await visitors
            .Where(x => x.OccurredAtUtc >= last30 && x.ClientIp != null)
            .Select(x => x.ClientIp)
            .Distinct()
            .CountAsync(cancellationToken);
        if (uniqueVisitors30 == 0)
        {
            uniqueVisitors30 = await visitors
                .Where(x => x.OccurredAtUtc >= last30 && x.VisitorHash != null)
                .Select(x => x.VisitorHash)
                .Distinct()
                .CountAsync(cancellationToken);
        }

        var byPath = await visitors
            .GroupBy(x => x.Path)
            .Select(g => new { path = g.Key, views = g.Count() })
            .OrderByDescending(x => x.views)
            .Take(20)
            .ToListAsync(cancellationToken);

        var byCountry = await visitors
            .Where(x => x.Country != null)
            .GroupBy(x => x.Country!)
            .Select(g => new { country = g.Key, views = g.Count() })
            .OrderByDescending(x => x.views)
            .Take(20)
            .ToListAsync(cancellationToken);

        var byCity = await visitors
            .Where(x => x.City != null)
            .GroupBy(x => new { x.City, x.Country })
            .Select(g => new
            {
                city = g.Key.City!,
                country = g.Key.Country,
                views = g.Count()
            })
            .OrderByDescending(x => x.views)
            .Take(20)
            .ToListAsync(cancellationToken);

        var byBrowser = await visitors
            .Where(x => x.Browser != null)
            .GroupBy(x => x.Browser!)
            .Select(g => new { browser = g.Key, views = g.Count() })
            .OrderByDescending(x => x.views)
            .Take(20)
            .ToListAsync(cancellationToken);

        var byOs = await visitors
            .Where(x => x.Os != null)
            .GroupBy(x => x.Os!)
            .Select(g => new { os = g.Key, views = g.Count() })
            .OrderByDescending(x => x.views)
            .Take(20)
            .ToListAsync(cancellationToken);

        var byLanguage = await visitors
            .Where(x => x.Language != null)
            .GroupBy(x => x.Language!)
            .Select(g => new { language = g.Key, views = g.Count() })
            .OrderByDescending(x => x.views)
            .Take(20)
            .ToListAsync(cancellationToken);

        var utmSources = await visitors
            .Where(x => x.UtmSource != null)
            .Select(x => x.UtmSource!)
            .ToListAsync(cancellationToken);
        var byUtmSource = utmSources
            .GroupBy(s => utmAttribution.NormalizeSource(s) ?? s)
            .Select(g => new { source = g.Key, views = g.Count() })
            .OrderByDescending(x => x.views)
            .Take(20)
            .ToList();

        var byIsp = await visitors
            .Where(x => x.Isp != null)
            .GroupBy(x => x.Isp!)
            .Select(g => new { isp = g.Key, views = g.Count() })
            .OrderByDescending(x => x.views)
            .Take(20)
            .ToListAsync(cancellationToken);

        var dayWindowStart = now.AddDays(-30);
        var dayRows = await visitors
            .Where(x => x.OccurredAtUtc >= dayWindowStart)
            .Select(x => new { x.OccurredAtUtc, x.ClientIp, x.Country, x.City })
            .ToListAsync(cancellationToken);
        var byDay = dayRows
            .GroupBy(x => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), lisbon).Date)
            .Select(g => new
            {
                day = g.Key.ToString("yyyy-MM-dd"),
                views = g.Count(),
                uniqueIps = g.Select(x => x.ClientIp).Where(ip => ip != null).Distinct().Count()
            })
            .OrderBy(x => x.day)
            .ToList();

        var byDayIp = dayRows
            .Where(x => x.ClientIp != null)
            .GroupBy(x => new
            {
                Day = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), lisbon).Date,
                Ip = x.ClientIp!
            })
            .Select(g => new
            {
                day = g.Key.Day.ToString("yyyy-MM-dd"),
                ip = g.Key.Ip,
                views = g.Count(),
                country = g.Select(x => x.Country).FirstOrDefault(c => c != null),
                city = g.Select(x => x.City).FirstOrDefault(c => c != null),
            })
            .OrderByDescending(x => x.day)
            .ThenByDescending(x => x.views)
            .ThenBy(x => x.ip)
            .Take(300)
            .ToList();

        var ipRows = await visitors
            .Where(x => x.ClientIp != null && x.OccurredAtUtc >= last30)
            .Select(x => new { x.ClientIp, x.OccurredAtUtc, x.Country, x.Region, x.City, x.Isp, x.Org, x.Asn })
            .ToListAsync(cancellationToken);
        var byIp = ipRows
            .GroupBy(x => x.ClientIp!)
            .Select(g =>
            {
                var days = g
                    .Select(x => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), lisbon).Date)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .Select(d => d.ToString("yyyy-MM-dd"))
                    .ToList();
                return new
                {
                    ip = g.Key,
                    views = g.Count(),
                    daysActive = days.Count,
                    days,
                    country = g.Select(x => x.Country).FirstOrDefault(c => c != null),
                    region = g.Select(x => x.Region).FirstOrDefault(c => c != null),
                    city = g.Select(x => x.City).FirstOrDefault(c => c != null),
                    isp = g.Select(x => x.Isp).FirstOrDefault(c => c != null),
                    org = g.Select(x => x.Org).FirstOrDefault(c => c != null),
                    asn = g.Select(x => x.Asn).FirstOrDefault(c => c != null),
                    firstSeenUtc = g.Min(x => x.OccurredAtUtc),
                    lastSeenUtc = g.Max(x => x.OccurredAtUtc),
                };
            })
            .OrderByDescending(x => x.views)
            .Take(30)
            .ToList();

        var ownIpRows = await ownViews
            .Select(x => new
            {
                x.ClientIp,
                x.OccurredAtUtc,
                x.Country,
                x.City,
                x.Isp,
                x.Org,
                x.UtmSource,
                x.UtmMedium,
                x.UtmCampaign,
                x.UtmContent,
                x.UtmTerm,
                x.Path
            })
            .ToListAsync(cancellationToken);
        var ownByIp = ownIpRows
            .Where(x => x.ClientIp != null)
            .GroupBy(x => x.ClientIp!)
            .Select(g =>
            {
                var latestWithUtm = g
                    .OrderByDescending(x => x.OccurredAtUtc)
                    .FirstOrDefault(x => x.UtmSource != null || x.UtmMedium != null || x.UtmCampaign != null);
                var latest = g.OrderByDescending(x => x.OccurredAtUtc).First();
                var utmRow = latestWithUtm ?? latest;
                return new
                {
                    ip = g.Key,
                    label = ExcludedIpOptions.LabelFor(g.Key),
                    views = g.Count(),
                    lastSeenUtc = g.Max(x => x.OccurredAtUtc),
                    country = g.Select(x => x.Country).FirstOrDefault(c => c != null),
                    city = g.Select(x => x.City).FirstOrDefault(c => c != null),
                    isp = g.Select(x => x.Isp).FirstOrDefault(c => c != null)
                        ?? g.Select(x => x.Org).FirstOrDefault(c => c != null),
                    utmSource = utmRow.UtmSource,
                    utmMedium = utmRow.UtmMedium,
                    utmCampaign = utmRow.UtmCampaign,
                    utmContent = utmRow.UtmContent,
                    utmTerm = utmRow.UtmTerm,
                };
            })
            .OrderByDescending(x => x.views)
            .ToList();

        var rangeCount = await visitors.CountAsync(
            x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc,
            cancellationToken);

        var recent = await visitors
            .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(limit)
            .Select(x => new
            {
                x.OccurredAtUtc,
                x.Path,
                x.Referrer,
                x.Country,
                x.Region,
                x.City,
                x.PostalCode,
                x.Latitude,
                x.Longitude,
                x.Asn,
                x.Org,
                x.Isp,
                x.Language,
                x.Screen,
                x.Browser,
                x.Os,
                x.UtmSource,
                x.UtmMedium,
                x.UtmCampaign,
                x.UtmContent,
                x.UtmTerm,
                ip = x.ClientIp,
                userAgent = x.UserAgent,
                visitor = x.VisitorHash
            })
            .ToListAsync(cancellationToken);

        return new
        {
            totalViews = total,
            viewsLast7Days = last7Count,
            viewsLast30Days = last30Count,
            uniqueVisitorsLast30Days = uniqueVisitors30,
            excludedIps = excludedList,
            ownTraffic = new
            {
                totalViews = ownIpRows.Count,
                byIp = ownByIp
            },
            timezoneNote = "Europe/Lisbon (Portugal)",
            range = new
            {
                fromUtc,
                toUtc,
                limit,
                matched = rangeCount,
                returned = recent.Count
            },
            byPath,
            byCountry,
            byCity,
            byIsp,
            byBrowser,
            byOs,
            byLanguage,
            byUtmSource,
            byDay,
            byDayIp,
            byIp,
            recent
        };
    }
}
