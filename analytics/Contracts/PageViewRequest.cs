using System.Text.Json.Serialization;

namespace AnalyticsApi.Contracts;

public sealed record PageViewRequest(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("referrer")] string? Referrer,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("screenWidth")] int? ScreenWidth,
    [property: JsonPropertyName("screenHeight")] int? ScreenHeight,
    /// <summary>Full landing href (query + hash). Backend parses UTMs / fbclid / igsh.</summary>
    [property: JsonPropertyName("url")] string? Url = null,
    // Optional legacy fields — ignored when present on url.
    [property: JsonPropertyName("utmSource")] string? UtmSource = null,
    [property: JsonPropertyName("utmMedium")] string? UtmMedium = null,
    [property: JsonPropertyName("utmCampaign")] string? UtmCampaign = null,
    [property: JsonPropertyName("utmContent")] string? UtmContent = null,
    [property: JsonPropertyName("utmTerm")] string? UtmTerm = null,
    [property: JsonPropertyName("fbclid")] string? Fbclid = null,
    [property: JsonPropertyName("igshid")] string? Igshid = null,
    [property: JsonPropertyName("igsh")] string? Igsh = null);
