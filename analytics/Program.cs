using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AnalyticsApi.Persistence;
using AnalyticsApi.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var dbPath = builder.Configuration["Analytics:DbPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "analytics.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<GeoIpService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(2);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("joao-sousa-analytics/1.0");
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Azure Container Apps / reverse proxies — trust forwarded headers from the platform.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var allowedOrigins = builder.Configuration.GetSection("Analytics:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:5174", "https://joaosousadev.me", "https://www.joaosousadev.me"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Site", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .WithMethods("GET", "POST", "OPTIONS"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("pageview", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
    await db.EnsureSchemaAsync();
}

app.UseRateLimiter();
app.UseCors("Site");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/analytics/pageview", async (
    PageViewRequest request,
    HttpContext http,
    AnalyticsDbContext db,
    IConfiguration config,
    GeoIpService geoIp,
    CancellationToken cancellationToken) =>
{
    var path = SanitizePath(request.Path);
    if (path is null)
    {
        return Results.BadRequest(new { error = "Invalid path." });
    }

    var referrer = Truncate(request.Referrer, 500);
    var userAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 400);
    var (browser, os) = UserAgentParser.Parse(userAgent);
    var language = Truncate(request.Language, 32);
    var screen = FormatScreen(request.ScreenWidth, request.ScreenHeight);
    var ip = http.Connection.RemoteIpAddress;
    var salt = config["Analytics:IpSalt"] ?? "change-me-in-production";
    var visitorHash = HashVisitor(ip?.ToString(), userAgent, salt);

    // Prefer Cloudflare header when present; otherwise resolve via GeoIP.
    var country = Truncate(http.Request.Headers["CF-IPCountry"].ToString(), 8);
    if (string.IsNullOrWhiteSpace(country) || country.Equals("XX", StringComparison.OrdinalIgnoreCase))
    {
        country = await geoIp.ResolveCountryAsync(ip, cancellationToken);
    }

    db.PageViews.Add(new PageView
    {
        OccurredAtUtc = DateTime.UtcNow,
        Path = path,
        Referrer = string.IsNullOrWhiteSpace(referrer) ? null : referrer,
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
        VisitorHash = visitorHash,
        Country = string.IsNullOrWhiteSpace(country) ? null : country,
        Language = language,
        Screen = screen,
        Browser = browser,
        Os = os,
        UtmSource = Truncate(request.UtmSource, 120),
        UtmMedium = Truncate(request.UtmMedium, 120),
        UtmCampaign = Truncate(request.UtmCampaign, 120),
        UtmContent = Truncate(request.UtmContent, 120),
        UtmTerm = Truncate(request.UtmTerm, 120),
    });

    await db.SaveChangesAsync(cancellationToken);
    return Results.Accepted();
})
.RequireRateLimiting("pageview")
.RequireCors("Site");

app.MapGet("/api/analytics/summary", async (
    HttpRequest httpRequest,
    AnalyticsDbContext db,
    IConfiguration config) =>
{
    var expectedKey = config["Analytics:ApiKey"];
    if (string.IsNullOrWhiteSpace(expectedKey))
    {
        return Results.Problem("Analytics:ApiKey is not configured.", statusCode: 500);
    }

    if (!httpRequest.Headers.TryGetValue("X-Api-Key", out var provided)
        || !FixedTimeEquals(provided.ToString(), expectedKey))
    {
        return Results.Unauthorized();
    }

    var now = DateTime.UtcNow;
    var last7 = now.AddDays(-7);
    var last30 = now.AddDays(-30);

    var total = await db.PageViews.CountAsync();
    var last7Count = await db.PageViews.CountAsync(x => x.OccurredAtUtc >= last7);
    var last30Count = await db.PageViews.CountAsync(x => x.OccurredAtUtc >= last30);
    var uniqueVisitors30 = await db.PageViews
        .Where(x => x.OccurredAtUtc >= last30 && x.VisitorHash != null)
        .Select(x => x.VisitorHash)
        .Distinct()
        .CountAsync();

    var byPath = await db.PageViews
        .GroupBy(x => x.Path)
        .Select(g => new { path = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byCountry = await db.PageViews
        .Where(x => x.Country != null)
        .GroupBy(x => x.Country!)
        .Select(g => new { country = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byBrowser = await db.PageViews
        .Where(x => x.Browser != null)
        .GroupBy(x => x.Browser!)
        .Select(g => new { browser = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byOs = await db.PageViews
        .Where(x => x.Os != null)
        .GroupBy(x => x.Os!)
        .Select(g => new { os = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byLanguage = await db.PageViews
        .Where(x => x.Language != null)
        .GroupBy(x => x.Language!)
        .Select(g => new { language = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byUtmSource = await db.PageViews
        .Where(x => x.UtmSource != null)
        .GroupBy(x => x.UtmSource!)
        .Select(g => new { source = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byDay = await db.PageViews
        .Where(x => x.OccurredAtUtc >= last30)
        .GroupBy(x => x.OccurredAtUtc.Date)
        .Select(g => new { day = g.Key, views = g.Count() })
        .OrderBy(x => x.day)
        .ToListAsync();

    var recent = await db.PageViews
        .OrderByDescending(x => x.OccurredAtUtc)
        .Take(50)
        .Select(x => new
        {
            x.OccurredAtUtc,
            x.Path,
            x.Referrer,
            x.Country,
            x.Language,
            x.Screen,
            x.Browser,
            x.Os,
            x.UtmSource,
            x.UtmMedium,
            x.UtmCampaign,
            x.UtmContent,
            x.UtmTerm,
            userAgent = x.UserAgent,
            visitor = x.VisitorHash
        })
        .ToListAsync();

    return Results.Ok(new
    {
        totalViews = total,
        viewsLast7Days = last7Count,
        viewsLast30Days = last30Count,
        uniqueVisitorsLast30Days = uniqueVisitors30,
        timezoneNote = "Europe/Lisbon (Portugal)",
        byPath,
        byCountry,
        byBrowser,
        byOs,
        byLanguage,
        byUtmSource,
        byDay,
        recent
    });
})
.RequireCors("Site");

app.Run();

static string? SanitizePath(string? path)
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

static string? Truncate(string? value, int max)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    value = value.Trim();
    return value.Length <= max ? value : value[..max];
}

static string? FormatScreen(int? width, int? height)
{
    if (width is null or <= 0 or > 10000 || height is null or <= 0 or > 10000)
    {
        return null;
    }

    return $"{width}x{height}";
}

static string HashVisitor(string? ip, string? userAgent, string salt)
{
    var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var material = $"{salt}|{day}|{ip}|{userAgent}";
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
    return Convert.ToHexString(hash)[..16].ToLowerInvariant();
}

static bool FixedTimeEquals(string a, string b)
{
    var ba = Encoding.UTF8.GetBytes(a);
    var bb = Encoding.UTF8.GetBytes(b);
    return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
}

internal sealed record PageViewRequest(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("referrer")] string? Referrer,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("screenWidth")] int? ScreenWidth,
    [property: JsonPropertyName("screenHeight")] int? ScreenHeight,
    [property: JsonPropertyName("utmSource")] string? UtmSource,
    [property: JsonPropertyName("utmMedium")] string? UtmMedium,
    [property: JsonPropertyName("utmCampaign")] string? UtmCampaign,
    [property: JsonPropertyName("utmContent")] string? UtmContent,
    [property: JsonPropertyName("utmTerm")] string? UtmTerm);
