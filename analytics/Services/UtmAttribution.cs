using AnalyticsApi.Contracts;
using AnalyticsApi.Contracts.Responses;

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
    /// All URL / click-id / referrer / in-app browser attribution lives here (not in the browser).
    /// </summary>
    public static UtmResolution Resolve(PageViewRequest request, string? userAgent)
    {
        var q = ParseQueryFromUrl(request.Url);

        var source = FirstNonEmpty(
            NormalizeSource(q.Get("utm_source")),
            NormalizeSource(request.UtmSource));
        var medium = FirstNonEmpty(q.Get("utm_medium"), request.UtmMedium);
        var campaign = FirstNonEmpty(q.Get("utm_campaign"), request.UtmCampaign);
        var content = FirstNonEmpty(q.Get("utm_content"), request.UtmContent);
        var term = FirstNonEmpty(q.Get("utm_term"), request.UtmTerm);

        var fbclid = FirstNonEmpty(q.Get("fbclid"), request.Fbclid);
        var igshid = FirstNonEmpty(q.Get("igshid"), request.Igshid);
        var igsh = FirstNonEmpty(q.Get("igsh"), request.Igsh)
            ?? q.GetFirstMatching(static k => k.StartsWith("igsh", StringComparison.OrdinalIgnoreCase));

        medium = AnalyticsText.Truncate(medium, 120);
        campaign = AnalyticsText.Truncate(campaign, 120);
        content = AnalyticsText.Truncate(content, 120);
        term = AnalyticsText.Truncate(term, 120);
        source = AnalyticsText.Truncate(source, 120);

        if (source is not null)
        {
            return new UtmResolution(source, medium, campaign, content, term);
        }

        if (!string.IsNullOrWhiteSpace(fbclid))
        {
            return new UtmResolution("facebook", medium ?? "social", campaign, content, term);
        }

        if (!string.IsNullOrWhiteSpace(igshid) || !string.IsNullOrWhiteSpace(igsh))
        {
            return new UtmResolution("instagram", medium ?? "social", campaign, content, term);
        }

        var fromReferrer = InferSourceFromReferrer(request.Referrer);
        if (fromReferrer is not null)
        {
            return new UtmResolution(fromReferrer, medium ?? "social", campaign, content, term);
        }

        var fromUa = InferSourceFromUserAgent(userAgent);
        if (fromUa is not null)
        {
            return new UtmResolution(fromUa, medium ?? "social", campaign, content, term);
        }

        return new UtmResolution(null, medium, campaign, content, term);
    }

    public static string? InferSourceFromReferrer(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer) || !Uri.TryCreate(referrer, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.EndsWith("facebook.com", StringComparison.Ordinal)
            || host.EndsWith("fb.com", StringComparison.Ordinal)
            || host.EndsWith("facebook.net", StringComparison.Ordinal)
            || host is "fb.me" or "m.facebook.com" or "lm.facebook.com" or "l.facebook.com")
        {
            return "facebook";
        }

        if (host.EndsWith("instagram.com", StringComparison.Ordinal) || host is "l.instagram.com")
        {
            return "instagram";
        }

        if (host.EndsWith("linkedin.com", StringComparison.Ordinal) || host is "lnkd.in")
        {
            return "linkedin";
        }

        return null;
    }

    public static string? InferSourceFromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        // Facebook / Instagram in-app browsers.
        if (userAgent.Contains("FBAN", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("FBAV", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("FB_IAB", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("; FB4A", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("; FBIOS", StringComparison.OrdinalIgnoreCase))
        {
            return "facebook";
        }

        if (userAgent.Contains("Instagram", StringComparison.OrdinalIgnoreCase))
        {
            return "instagram";
        }

        return null;
    }

    /// <summary>Collect query params from ?search and from #hash?query.</summary>
    public static QueryBag ParseQueryFromUrl(string? url)
    {
        var bag = new QueryBag();
        if (string.IsNullOrWhiteSpace(url))
        {
            return bag;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && !Uri.TryCreate(url.Trim(), UriKind.RelativeOrAbsolute, out uri))
        {
            // Bare query or path+query
            ParseInto(bag, url);
            return bag;
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            ParseInto(bag, uri.Query);
        }

        var fragment = uri.Fragment;
        if (string.IsNullOrEmpty(fragment))
        {
            return bag;
        }

        var qIndex = fragment.IndexOf('?');
        if (qIndex >= 0)
        {
            ParseInto(bag, fragment[(qIndex + 1)..]);
        }

        return bag;
    }

    private static void ParseInto(QueryBag bag, string raw)
    {
        var text = raw.StartsWith('?') ? raw[1..] : raw;
        // Manual parse — HttpUtility may not be available on all TFMs without package.
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            string key;
            string value;
            if (eq < 0)
            {
                key = Uri.UnescapeDataString(part.Replace('+', ' '));
                value = "";
            }
            else
            {
                key = Uri.UnescapeDataString(part[..eq].Replace('+', ' '));
                value = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            bag.SetIfAbsent(key, value);
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    public sealed class QueryBag
    {
        private readonly Dictionary<string, string> _values =
            new(StringComparer.OrdinalIgnoreCase);

        public void SetIfAbsent(string key, string value)
        {
            if (!_values.ContainsKey(key))
            {
                _values[key] = value;
            }
        }

        public string? Get(string key) =>
            _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        public string? GetFirstMatching(Func<string, bool> predicate)
        {
            foreach (var (key, value) in _values)
            {
                if (predicate(key) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
