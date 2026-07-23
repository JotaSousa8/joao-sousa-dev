namespace AnalyticsApi.Services.Shared;

using System.Text.RegularExpressions;

public static partial class UserAgentParser
{
    public static (string? Browser, string? Os) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return (null, null);
        }

        return (DetectBrowser(userAgent), DetectOs(userAgent));
    }

    private static string DetectBrowser(string ua)
    {
        if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
        {
            return "Edge";
        }

        if (ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) || ua.Contains("Opera", StringComparison.OrdinalIgnoreCase))
        {
            return "Opera";
        }

        if (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
        {
            return "Firefox";
        }

        if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) && !ua.Contains("Chromium", StringComparison.OrdinalIgnoreCase))
        {
            return "Chrome";
        }

        if (ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) && !ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
        {
            return "Safari";
        }

        if (ua.Contains("MSIE", StringComparison.OrdinalIgnoreCase) || ua.Contains("Trident/", StringComparison.OrdinalIgnoreCase))
        {
            return "IE";
        }

        return "Other";
    }

    private static string DetectOs(string ua)
    {
        if (IosRegex().IsMatch(ua))
        {
            return "iOS";
        }

        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            return "Android";
        }

        if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows";
        }

        if (ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) || ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase))
        {
            return "macOS";
        }

        if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) || ua.Contains("CrOS", StringComparison.OrdinalIgnoreCase))
        {
            return "Linux";
        }

        return "Other";
    }

    [GeneratedRegex(@"iPhone|iPad|iPod", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IosRegex();
}
