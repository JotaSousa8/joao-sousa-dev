using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AnalyticsApi.Persistence;
using AnalyticsApi.Serialization;
using AnalyticsApi.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = ResolveConnectionString(builder.Configuration);
var hasConnectionString = !string.IsNullOrWhiteSpace(connectionString);
if (!hasConnectionString)
{
    // Allow the process to start so /health can report misconfiguration instead of
    // Azure keeping an old SQLite revision forever.
    Console.Error.WriteLine("WARNING: Missing ConnectionStrings:Analytics. Set ANALYTICS_CONNECTION_STRING.");
    connectionString = "Host=127.0.0.1;Port=5432;Database=missing;Username=missing;Password=missing";
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
    options.SerializerOptions.Converters.Add(new UtcNullableDateTimeJsonConverter());
});

builder.Services.AddSingleton(new AnalyticsRuntimeState(hasConnectionString));

builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseNpgsql(connectionString));

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
    {
        if (builder.Environment.IsDevelopment())
        {
            // Any localhost / 127.0.0.1 port while developing (Live Server, Vite, preview, etc.)
            policy.SetIsOriginAllowed(static origin =>
                {
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        return false;
                    }

                    return uri.Host is "localhost" or "127.0.0.1";
                })
                .AllowAnyHeader()
                .WithMethods("GET", "POST", "OPTIONS");
            return;
        }

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .WithMethods("GET", "POST", "OPTIONS");
    });
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
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        await db.EnsureSchemaAsync();
        logger.LogInformation("Database schema ensured.");
    }
    catch (Exception ex)
    {
        // Do not crash the process — otherwise Azure keeps the previous (SQLite) revision.
        logger.LogError(ex, "Failed to ensure database schema. Check ConnectionStrings:Analytics / Supabase URI.");
    }
}

app.UseRateLimiter();
app.UseCors("Site");

app.MapGet("/health", async (AnalyticsDbContext db, AnalyticsRuntimeState runtime) =>
{
    string database;
    bool canConnect;
    try
    {
        var provider = db.Database.ProviderName ?? "unknown";
        database = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "postgresql"
            : provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "sqlite"
            : provider;
        canConnect = runtime.HasConnectionString && await db.Database.CanConnectAsync();
    }
    catch
    {
        database = "postgresql";
        canConnect = false;
    }

    return Results.Ok(new
    {
        status = canConnect ? "ok" : "degraded",
        database,
        canConnect,
        connectionStringConfigured = runtime.HasConnectionString,
        build = Environment.GetEnvironmentVariable("BUILD_SHA") ?? "unknown"
    });
});

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
    var clientIp = NormalizeIp(ip);
    var salt = config["Analytics:IpSalt"] ?? "change-me-in-production";
    // Stable across days: same IP + UA ≈ same visitor (NAT/VPN caveats apply).
    var visitorHash = HashVisitor(clientIp, userAgent, salt);

    // Prefer Cloudflare country header when present; resolve city (+ country fallback) via GeoIP.
    var country = Truncate(http.Request.Headers["CF-IPCountry"].ToString(), 8);
    string? city = null;
    var geo = await geoIp.ResolveAsync(ip, cancellationToken);
    if (geo is not null)
    {
        city = geo.City;
        if (string.IsNullOrWhiteSpace(country) || country.Equals("XX", StringComparison.OrdinalIgnoreCase))
        {
            country = geo.CountryCode;
        }
    }

    db.PageViews.Add(new PageView
    {
        OccurredAtUtc = DateTime.UtcNow,
        Path = path,
        Referrer = string.IsNullOrWhiteSpace(referrer) ? null : referrer,
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
        VisitorHash = visitorHash,
        ClientIp = clientIp,
        Country = string.IsNullOrWhiteSpace(country) || country.Equals("XX", StringComparison.OrdinalIgnoreCase) ? null : country,
        City = city,
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
    var lisbon = ResolveLisbonTimeZone();

    var fromUtc = ParseQueryUtc(httpRequest.Query["from"], lisbon) ?? last7;
    var toUtc = ParseQueryUtc(httpRequest.Query["to"], lisbon) ?? now.AddMinutes(1);
    if (toUtc < fromUtc)
    {
        (fromUtc, toUtc) = (toUtc, fromUtc);
    }

    var limit = 100;
    if (int.TryParse(httpRequest.Query["limit"], out var parsedLimit))
    {
        limit = Math.Clamp(parsedLimit, 1, 500);
    }

    var total = await db.PageViews.CountAsync();
    var last7Count = await db.PageViews.CountAsync(x => x.OccurredAtUtc >= last7);
    var last30Count = await db.PageViews.CountAsync(x => x.OccurredAtUtc >= last30);
    var uniqueVisitors30 = await db.PageViews
        .Where(x => x.OccurredAtUtc >= last30 && x.ClientIp != null)
        .Select(x => x.ClientIp)
        .Distinct()
        .CountAsync();
    if (uniqueVisitors30 == 0)
    {
        uniqueVisitors30 = await db.PageViews
            .Where(x => x.OccurredAtUtc >= last30 && x.VisitorHash != null)
            .Select(x => x.VisitorHash)
            .Distinct()
            .CountAsync();
    }

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

    var byCity = await db.PageViews
        .Where(x => x.City != null)
        .GroupBy(x => new { x.City, x.Country })
        .Select(g => new
        {
            city = g.Key.City!,
            country = g.Key.Country,
            views = g.Count()
        })
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

    var dayWindowStart = now.AddDays(-30);
    var dayStamps = await db.PageViews
        .Where(x => x.OccurredAtUtc >= dayWindowStart)
        .Select(x => x.OccurredAtUtc)
        .ToListAsync();
    var byDay = dayStamps
        .GroupBy(x => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x, DateTimeKind.Utc), lisbon).Date)
        .Select(g => new { day = g.Key.ToString("yyyy-MM-dd"), views = g.Count() })
        .OrderBy(x => x.day)
        .ToList();

    var ipRows = await db.PageViews
        .Where(x => x.ClientIp != null && x.OccurredAtUtc >= last30)
        .Select(x => new { x.ClientIp, x.OccurredAtUtc, x.Country, x.City })
        .ToListAsync();
    var byIp = ipRows
        .GroupBy(x => x.ClientIp!)
        .Select(g =>
        {
            var days = g
                .Select(x => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), lisbon).Date)
                .Distinct()
                .OrderByDescending(d => d)
                .Select(d => d.ToString("yyyy-MM-dd"))
                .ToList();
            return new
            {
                ip = g.Key,
                views = g.Count(),
                daysActive = days.Count,
                days,
                country = g.Select(x => x.Country).FirstOrDefault(c => c != null),
                city = g.Select(x => x.City).FirstOrDefault(c => c != null),
                firstSeenUtc = g.Min(x => x.OccurredAtUtc),
                lastSeenUtc = g.Max(x => x.OccurredAtUtc),
            };
        })
        .OrderByDescending(x => x.views)
        .Take(30)
        .ToList();

    var rangeCount = await db.PageViews.CountAsync(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc);

    var recent = await db.PageViews
        .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
        .OrderByDescending(x => x.OccurredAtUtc)
        .Take(limit)
        .Select(x => new
        {
            x.OccurredAtUtc,
            x.Path,
            x.Referrer,
            x.Country,
            x.City,
            x.Language,
            x.Screen,
            x.Browser,
            x.Os,
            x.UtmSource,
            x.UtmMedium,
            x.UtmCampaign,
            x.UtmContent,
            x.UtmTerm,
            ip = x.ClientIp,
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
        range = new
        {
            fromUtc,
            toUtc,
            limit,
            matched = rangeCount,
            returned = recent.Count
        },
        byPath,
        byCountry,
        byCity,
        byBrowser,
        byOs,
        byLanguage,
        byUtmSource,
        byDay,
        byIp,
        recent
    });
})
.RequireCors("Site");

app.Run();

static string ResolveConnectionString(IConfiguration config)
{
    static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    var raw =
        NullIfEmpty(config.GetConnectionString("Analytics"))
        ?? NullIfEmpty(config["Analytics:ConnectionString"])
        ?? NullIfEmpty(config["ANALYTICS_CONNECTION_STRING"])
        ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANALYTICS_CONNECTION_STRING"))
        ?? NullIfEmpty(config["DATABASE_URL"])
        ?? "";

    if (raw.Length == 0)
    {
        return raw;
    }

    // Supabase URI → Npgsql key=value form when needed
    if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(database))
        {
            database = "postgres";
        }

        return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }

    if (!raw.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase)
        && !raw.Contains("Ssl Mode", StringComparison.OrdinalIgnoreCase)
        && !raw.Contains("sslmode", StringComparison.OrdinalIgnoreCase))
    {
        raw += raw.EndsWith(';') ? "SSL Mode=Require;Trust Server Certificate=true" : ";SSL Mode=Require;Trust Server Certificate=true";
    }

    return raw;
}

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

static string? NormalizeIp(System.Net.IPAddress? ip)
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

static string HashVisitor(string? ip, string? userAgent, string salt)
{
    var material = $"{salt}|{ip}|{userAgent}";
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
    return Convert.ToHexString(hash)[..16].ToLowerInvariant();
}

static bool FixedTimeEquals(string a, string b)
{
    var ba = Encoding.UTF8.GetBytes(a);
    var bb = Encoding.UTF8.GetBytes(b);
    return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
}

static TimeZoneInfo ResolveLisbonTimeZone()
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

static DateTime? ParseQueryUtc(string? raw, TimeZoneInfo lisbon)
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

internal sealed record AnalyticsRuntimeState(bool HasConnectionString);
