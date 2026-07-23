namespace AnalyticsApi.Services.AnalyticsExport;

using System.Globalization;
using System.Text;
using AnalyticsApi.Infrastructure.Persistence;
using AnalyticsApi.Services.Shared;
using Microsoft.EntityFrameworkCore;

public sealed class AnalyticsExportService(AnalyticsDbContext db, IConfiguration config)
    : IAnalyticsExportService
{
    private const int MaxRows = 5000;

    public async Task<(string FileName, string Csv)> BuildCsvAsync(
        string? fromRaw,
        string? toRaw,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var lisbon = AnalyticsText.ResolveLisbonTimeZone();
        var fromUtc = AnalyticsText.ParseQueryUtc(fromRaw, lisbon) ?? now.AddDays(-1);
        var toUtc = AnalyticsText.ParseQueryUtc(toRaw, lisbon) ?? now.AddMinutes(1);
        if (toUtc < fromUtc)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }

        var excludedList = ExcludedIpOptions.Resolve(config).ToList();
        var visitors = db.PageViews.ExcludeBots();
        if (excludedList.Count > 0)
        {
            visitors = visitors.Where(x => x.ClientIp == null || !excludedList.Contains(x.ClientIp));
        }

        var rows = await visitors
            .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(MaxRows)
            .Select(x => new
            {
                x.OccurredAtUtc,
                x.Path,
                x.ClientIp,
                x.Country,
                x.Region,
                x.City,
                x.Isp,
                x.Browser,
                x.Os,
                x.UtmSource,
                x.UtmMedium,
                x.UtmCampaign,
                x.UtmContent,
                x.UtmTerm,
                x.Referrer
            })
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine(
            "occurred_at_utc,path,ip,country,region,city,isp,browser,os,utm_source,utm_medium,utm_campaign,utm_content,utm_term,referrer");

        foreach (var row in rows)
        {
            sb.Append(Csv(row.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            sb.Append(Csv(row.Path)).Append(',');
            sb.Append(Csv(row.ClientIp)).Append(',');
            sb.Append(Csv(row.Country)).Append(',');
            sb.Append(Csv(row.Region)).Append(',');
            sb.Append(Csv(row.City)).Append(',');
            sb.Append(Csv(row.Isp)).Append(',');
            sb.Append(Csv(row.Browser)).Append(',');
            sb.Append(Csv(row.Os)).Append(',');
            sb.Append(Csv(row.UtmSource)).Append(',');
            sb.Append(Csv(row.UtmMedium)).Append(',');
            sb.Append(Csv(row.UtmCampaign)).Append(',');
            sb.Append(Csv(row.UtmContent)).Append(',');
            sb.Append(Csv(row.UtmTerm)).Append(',');
            sb.Append(Csv(row.Referrer));
            sb.AppendLine();
        }

        var fromStamp = fromUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var toStamp = toUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var fileName = $"analytics-{fromStamp}-{toStamp}.csv";
        return (fileName, sb.ToString());
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuotes)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
