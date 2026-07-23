namespace AnalyticsApi.Services.Shared;

using System.Net;
using System.Security.Cryptography;
using System.Text;

public static class AnalyticsText
{
    public static string? SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        path = path.Trim();
        if (path.Length > 200 || path.Contains('\n') || path.Contains('\r'))
        {
            return null;
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path;
    }

    public static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    public static string? FormatScreen(int? width, int? height)
    {
        if (width is null or <= 0 or > 10000 || height is null or <= 0 or > 10000)
        {
            return null;
        }

        return $"{width}x{height}";
    }

    public static string? NormalizeIp(IPAddress? ip)
    {
        if (ip is null)
        {
            return null;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        return Truncate(ip.ToString(), 64);
    }

    public static string HashVisitor(string? ip, string? userAgent, string salt)
    {
        var material = $"{salt}|{ip}|{userAgent}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    public static TimeZoneInfo ResolveLisbonTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        }
    }

    public static DateTime? ParseQueryUtc(string? raw, TimeZoneInfo lisbon)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateTime.TryParse(
                raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return null;
        }

        return parsed.Kind switch
        {
            DateTimeKind.Utc => parsed,
            DateTimeKind.Local => parsed.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), lisbon)
        };
    }
}
