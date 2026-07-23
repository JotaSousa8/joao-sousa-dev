namespace AnalyticsApi.Services.AnalyticsLive;

using AnalyticsApi.Infrastructure.Persistence;
using AnalyticsApi.Services.Shared;
using Microsoft.EntityFrameworkCore;

public sealed class AnalyticsLiveService(AnalyticsDbContext db, IConfiguration config)
    : IAnalyticsLiveService
{
    public async Task<object> GetLiveAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var lastHour = now.AddHours(-1);
        var excludedList = ExcludedIpOptions.Resolve(config).ToList();

        var visitors = db.PageViews.ExcludeBots();
        if (excludedList.Count > 0)
        {
            visitors = visitors.Where(x => x.ClientIp == null || !excludedList.Contains(x.ClientIp));
        }

        var hourViews = visitors.Where(x => x.OccurredAtUtc >= lastHour);
        var viewsLastHour = await hourViews.CountAsync(cancellationToken);
        var uniqueLastHour = await hourViews
            .Where(x => x.ClientIp != null)
            .Select(x => x.ClientIp)
            .Distinct()
            .CountAsync(cancellationToken);
        if (uniqueLastHour == 0)
        {
            uniqueLastHour = await hourViews
                .Where(x => x.VisitorHash != null)
                .Select(x => x.VisitorHash)
                .Distinct()
                .CountAsync(cancellationToken);
        }

        return new
        {
            asOfUtc = now,
            viewsLastHour,
            uniqueLastHour
        };
    }
}
