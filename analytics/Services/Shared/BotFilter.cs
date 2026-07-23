namespace AnalyticsApi.Services.Shared;

using AnalyticsApi.Infrastructure.Persistence;

/// <summary>Light User-Agent bot detection for crawlers that inflate uniques.</summary>
public static class BotFilter
{
    private static readonly string[] Tokens =
    [
        "googlebot",
        "bingbot",
        "slurp",
        "duckduckbot",
        "baiduspider",
        "yandexbot",
        "facebookexternalhit",
        "twitterbot",
        "linkedinbot",
        "semrushbot",
        "ahrefsbot",
        "mj12bot",
        "bytespider",
        "gptbot",
        "claudebot",
        "petalbot",
        "applebot",
        "dotbot",
        "rogerbot",
    ];

    public static bool IsBot(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return false;
        }

        var ua = userAgent.ToLowerInvariant();
        foreach (var token in Tokens)
        {
            if (ua.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>EF-translatable filter: keep null UA and non-bot UAs.</summary>
    public static IQueryable<PageView> ExcludeBots(this IQueryable<PageView> query) =>
        query.Where(x =>
            x.UserAgent == null
            || (
                !x.UserAgent.ToLower().Contains("googlebot")
                && !x.UserAgent.ToLower().Contains("bingbot")
                && !x.UserAgent.ToLower().Contains("slurp")
                && !x.UserAgent.ToLower().Contains("duckduckbot")
                && !x.UserAgent.ToLower().Contains("baiduspider")
                && !x.UserAgent.ToLower().Contains("yandexbot")
                && !x.UserAgent.ToLower().Contains("facebookexternalhit")
                && !x.UserAgent.ToLower().Contains("twitterbot")
                && !x.UserAgent.ToLower().Contains("linkedinbot")
                && !x.UserAgent.ToLower().Contains("semrushbot")
                && !x.UserAgent.ToLower().Contains("ahrefsbot")
                && !x.UserAgent.ToLower().Contains("mj12bot")
                && !x.UserAgent.ToLower().Contains("bytespider")
                && !x.UserAgent.ToLower().Contains("gptbot")
                && !x.UserAgent.ToLower().Contains("claudebot")
                && !x.UserAgent.ToLower().Contains("petalbot")
                && !x.UserAgent.ToLower().Contains("applebot")
                && !x.UserAgent.ToLower().Contains("dotbot")
                && !x.UserAgent.ToLower().Contains("rogerbot")
            ));
}
