using System.Net;
using System.Text.RegularExpressions;
using AnalyticsApi.Contracts.Responses;

namespace AnalyticsApi.Services.GeoIp;

public static class GeoIpHelper
{
    private static readonly Regex AsnRegex = new(@"^AS(?<n>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Prefer a more specific PT city when one provider defaults to Lisbon (common for MEO/NOS ranges).
    /// </summary>
    public static GeoLocation? PickBest(GeoLocation? primary, GeoLocation? fallback)
    {
        if (primary is null) return fallback;
        if (fallback is null) return primary;

        var primaryCity = primary.City?.Trim();
        var fallbackCity = fallback.City?.Trim();
        if (string.IsNullOrWhiteSpace(primaryCity) && !string.IsNullOrWhiteSpace(fallbackCity))
        {
            return fallback.MergeWith(primary);
        }

        if (string.IsNullOrWhiteSpace(fallbackCity) || CitiesEqual(primaryCity, fallbackCity))
        {
            return primary.MergeWith(fallback);
        }

        if (IsPortugal(primary) || IsPortugal(fallback))
        {
            if (IsLisbonArea(primaryCity) && !IsLisbonArea(fallbackCity))
            {
                return fallback.MergeWith(primary);
            }

            if (!IsLisbonArea(primaryCity) && IsLisbonArea(fallbackCity))
            {
                return primary.MergeWith(fallback);
            }
        }

        return primary.MergeWith(fallback);
    }

    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
        }

        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    public static string? NormalizeCountry(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    public static string? TruncateOrNull(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    public static int? ParseAsn(string? asField)
    {
        if (string.IsNullOrWhiteSpace(asField))
        {
            return null;
        }

        var match = AsnRegex.Match(asField.Trim());
        return match.Success && int.TryParse(match.Groups["n"].Value, out var n) ? n : null;
    }

    private static bool IsPortugal(GeoLocation loc) =>
        string.Equals(loc.CountryCode, "PT", StringComparison.OrdinalIgnoreCase);

    private static bool IsLisbonArea(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return false;
        var c = city.Trim();
        return c.Equals("Lisbon", StringComparison.OrdinalIgnoreCase)
            || c.Equals("Lisboa", StringComparison.OrdinalIgnoreCase)
            || c.Equals("Lisbonne", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CitiesEqual(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
