using AnalyticsApi.Contracts;

namespace AnalyticsApi.Services;

public static class UtmAttribution
{
    /// <summary>Merge common short aliases into canonical UTM sources (e.g. ig → Instagram).</summary>
    public static string? NormalizeSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "ig" or "insta" => "instagram",
            "fb" => "facebook",
            "li" or "in" => "linkedin",
            var other => other
        };
    }

    /// <summary>
    /// Prefer explicit UTM; otherwise infer from Facebook/Instagram click IDs or referrer.
    /// </summary>
    public static (string? Source, string? Medium) Resolve(PageViewRequest request)
    {
        var medium = AnalyticsText.Truncate(request.UtmMedium, 120);
        var source = AnalyticsText.Truncate(NormalizeSource(request.UtmSource), 120);
        if (source is not null)
        {
            return (source, medium);
        }

        if (!string.IsNullOrWhiteSpace(request.Fbclid))
        {
            return ("facebook", medium ?? "social");
        }

        if (!string.IsNullOrWhiteSpace(request.Igshid) || !string.IsNullOrWhiteSpace(request.Igsh))
        {
            return ("instagram", medium ?? "social");
        }

        var fromReferrer = InferSourceFromReferrer(request.Referrer);
        return fromReferrer is null ? (null, medium) : (fromReferrer, medium ?? "social");
    }

    public static string? InferSourceFromReferrer(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer) || !Uri.TryCreate(referrer, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host is "facebook.com" or "www.facebook.com" or "m.facebook.com" or "lm.facebook.com"
            or "l.facebook.com" or "fb.com" or "www.fb.com")
        {
            return "facebook";
        }

        if (host is "instagram.com" or "www.instagram.com" or "l.instagram.com" or "m.instagram.com")
        {
            return "instagram";
        }

        if (host is "linkedin.com" or "www.linkedin.com" or "lnkd.in")
        {
            return "linkedin";
        }

        return null;
    }
}
