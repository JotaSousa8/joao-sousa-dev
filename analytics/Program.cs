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

// Never throw during connection-string parsing — a bad Supabase URI must not
// prevent Kestrel from binding /health (ACA otherwise keeps the old SQLite revision).
const string MissingDb =
    "Host=127.0.0.1;Port=5432;Database=missing;Username=missing;Password=missing;SSL Mode=Disable";
string connectionString;
var hasConnectionString = false;
var resolveSource = "none";
try
{
    var resolved = ResolveConnectionStringDetailed(builder.Configuration);
    connectionString = resolved.Value;
    resolveSource = resolved.Source;
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("WARNING: Missing ConnectionStrings:Analytics. Set ANALYTICS_CONNECTION_STRING.");
        connectionString = MissingDb;
        resolveSource = "missing";
    }
    else
    {
        // Validate early; invalid keywords / mangled secrets throw here instead of in DI.
        _ = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        hasConnectionString = true;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"WARNING: Invalid ConnectionStrings:Analytics ({ex.GetType().Name}: {ex.Message}). Starting degraded.");
    connectionString = MissingDb;
    hasConnectionString = false;
    resolveSource = "invalid";
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
    options.SerializerOptions.Converters.Add(new UtcNullableDateTimeJsonConverter());
});

builder.Services.AddSingleton(new AnalyticsRuntimeState(
    hasConnectionString,
    resolveSource,
    Environment.GetEnvironmentVariable("ConnectionStrings__Analytics")?.Length ?? 0,
    Environment.GetEnvironmentVariable("ConnectionStrings__AnalyticsB64")?.Length ?? 0,
    Environment.GetEnvironmentVariable("ANALYTICS_CONNECTION_STRING")?.Length ?? 0));

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
app.UseRateLimiter();
app.UseCors("Site");

// Bind health first — no DB work before Kestrel can accept probes/traffic.
app.MapGet("/health", async (AnalyticsDbContext db, AnalyticsRuntimeState runtime) =>
{
    const string database = "postgresql";
    var canConnect = false;
    if (runtime.HasConnectionString)
    {
        try
        {
            canConnect = await db.Database.CanConnectAsync();
        }
        catch
        {
            canConnect = false;
        }
    }

    return Results.Ok(new
    {
        status = canConnect ? "ok" : "degraded",
        database,
        canConnect,
        connectionStringConfigured = runtime.HasConnectionString,
        connectionStringSource = runtime.Source,
        env = new
        {
            connectionStringsAnalyticsLen = runtime.PlainEnvLen,
            connectionStringsAnalyticsB64Len = runtime.B64EnvLen,
            analyticsConnectionStringLen = runtime.AnalyticsEnvLen
        },
        build = Environment.GetEnvironmentVariable("BUILD_SHA") ?? "unknown"
    });
});

// Schema ensure after listen so a bad DB never blocks process start.
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
            await db.EnsureSchemaAsync();
            logger.LogInformation("Database schema ensured.");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to ensure database schema. Check ConnectionStrings:Analytics / Supabase URI.");
        }
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

    // Prefer Cloudflare country header when present; enrich via free GeoIP (ipwho.is).
    var country = Truncate(http.Request.Headers["CF-IPCountry"].ToString(), 8);
    string? region = null;
    string? city = null;
    string? postalCode = null;
    double? latitude = null;
    double? longitude = null;
    int? asn = null;
    string? org = null;
    string? isp = null;
    var geo = await geoIp.ResolveAsync(ip, cancellationToken);
    if (geo is not null)
    {
        region = geo.Region;
        city = geo.City;
        postalCode = geo.PostalCode;
        latitude = geo.Latitude;
        longitude = geo.Longitude;
        asn = geo.Asn;
        org = geo.Org;
        isp = geo.Isp;
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
        Region = region,
        City = city,
        PostalCode = postalCode,
        Latitude = latitude,
        Longitude = longitude,
        Asn = asn,
        Org = org,
        Isp = isp,
        Language = language,
        Screen = screen,
        Browser = browser,
        Os = os,
        UtmSource = Truncate(NormalizeUtmSource(request.UtmSource), 120),
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

    var excludedIps = ResolveExcludedIps(config);
    var excludedList = excludedIps.ToList();
    // Main stats: everyone except your own IPs (still counted under ownTraffic).
    var visitors = excludedList.Count == 0
        ? db.PageViews
        : db.PageViews.Where(x => x.ClientIp == null || !excludedList.Contains(x.ClientIp));
    var ownViews = excludedList.Count == 0
        ? db.PageViews.Where(_ => false)
        : db.PageViews.Where(x => x.ClientIp != null && excludedList.Contains(x.ClientIp));

    var total = await visitors.CountAsync();
    var last7Count = await visitors.CountAsync(x => x.OccurredAtUtc >= last7);
    var last30Count = await visitors.CountAsync(x => x.OccurredAtUtc >= last30);
    var uniqueVisitors30 = await visitors
        .Where(x => x.OccurredAtUtc >= last30 && x.ClientIp != null)
        .Select(x => x.ClientIp)
        .Distinct()
        .CountAsync();
    if (uniqueVisitors30 == 0)
    {
        uniqueVisitors30 = await visitors
            .Where(x => x.OccurredAtUtc >= last30 && x.VisitorHash != null)
            .Select(x => x.VisitorHash)
            .Distinct()
            .CountAsync();
    }

    var byPath = await visitors
        .GroupBy(x => x.Path)
        .Select(g => new { path = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byCountry = await visitors
        .Where(x => x.Country != null)
        .GroupBy(x => x.Country!)
        .Select(g => new { country = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byCity = await visitors
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

    var byBrowser = await visitors
        .Where(x => x.Browser != null)
        .GroupBy(x => x.Browser!)
        .Select(g => new { browser = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byOs = await visitors
        .Where(x => x.Os != null)
        .GroupBy(x => x.Os!)
        .Select(g => new { os = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var byLanguage = await visitors
        .Where(x => x.Language != null)
        .GroupBy(x => x.Language!)
        .Select(g => new { language = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var utmSources = await visitors
        .Where(x => x.UtmSource != null)
        .Select(x => x.UtmSource!)
        .ToListAsync();
    var byUtmSource = utmSources
        .GroupBy(s => NormalizeUtmSource(s) ?? s)
        .Select(g => new { source = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToList();

    var byIsp = await visitors
        .Where(x => x.Isp != null)
        .GroupBy(x => x.Isp!)
        .Select(g => new { isp = g.Key, views = g.Count() })
        .OrderByDescending(x => x.views)
        .Take(20)
        .ToListAsync();

    var dayWindowStart = now.AddDays(-30);
    var dayRows = await visitors
        .Where(x => x.OccurredAtUtc >= dayWindowStart)
        .Select(x => new { x.OccurredAtUtc, x.ClientIp, x.Country, x.City })
        .ToListAsync();
    var byDay = dayRows
        .GroupBy(x => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), lisbon).Date)
        .Select(g => new
        {
            day = g.Key.ToString("yyyy-MM-dd"),
            views = g.Count(),
            uniqueIps = g.Select(x => x.ClientIp).Where(ip => ip != null).Distinct().Count()
        })
        .OrderBy(x => x.day)
        .ToList();

    var byDayIp = dayRows
        .Where(x => x.ClientIp != null)
        .GroupBy(x => new
        {
            Day = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), lisbon).Date,
            Ip = x.ClientIp!
        })
        .Select(g => new
        {
            day = g.Key.Day.ToString("yyyy-MM-dd"),
            ip = g.Key.Ip,
            views = g.Count(),
            country = g.Select(x => x.Country).FirstOrDefault(c => c != null),
            city = g.Select(x => x.City).FirstOrDefault(c => c != null),
        })
        .OrderByDescending(x => x.day)
        .ThenByDescending(x => x.views)
        .ThenBy(x => x.ip)
        .Take(300)
        .ToList();

    var ipRows = await visitors
        .Where(x => x.ClientIp != null && x.OccurredAtUtc >= last30)
        .Select(x => new { x.ClientIp, x.OccurredAtUtc, x.Country, x.Region, x.City, x.Isp, x.Org, x.Asn })
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
                region = g.Select(x => x.Region).FirstOrDefault(c => c != null),
                city = g.Select(x => x.City).FirstOrDefault(c => c != null),
                isp = g.Select(x => x.Isp).FirstOrDefault(c => c != null),
                org = g.Select(x => x.Org).FirstOrDefault(c => c != null),
                asn = g.Select(x => x.Asn).FirstOrDefault(c => c != null),
                firstSeenUtc = g.Min(x => x.OccurredAtUtc),
                lastSeenUtc = g.Max(x => x.OccurredAtUtc),
            };
        })
        .OrderByDescending(x => x.views)
        .Take(30)
        .ToList();

    var ownIpRows = await ownViews
        .Select(x => new { x.ClientIp, x.OccurredAtUtc, x.Country, x.City, x.Path })
        .ToListAsync();
    var ownByIp = ownIpRows
        .Where(x => x.ClientIp != null)
        .GroupBy(x => x.ClientIp!)
        .Select(g => new
        {
            ip = g.Key,
            label = OwnIpLabel(g.Key),
            views = g.Count(),
            lastSeenUtc = g.Max(x => x.OccurredAtUtc),
            country = g.Select(x => x.Country).FirstOrDefault(c => c != null),
            city = g.Select(x => x.City).FirstOrDefault(c => c != null),
        })
        .OrderByDescending(x => x.views)
        .ToList();

    var rangeCount = await visitors.CountAsync(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc);

    var recent = await visitors
        .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
        .OrderByDescending(x => x.OccurredAtUtc)
        .Take(limit)
        .Select(x => new
        {
            x.OccurredAtUtc,
            x.Path,
            x.Referrer,
            x.Country,
            x.Region,
            x.City,
            x.PostalCode,
            x.Latitude,
            x.Longitude,
            x.Asn,
            x.Org,
            x.Isp,
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
        excludedIps = excludedList,
        ownTraffic = new
        {
            totalViews = ownIpRows.Count,
            byIp = ownByIp
        },
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
        byIsp,
        byBrowser,
        byOs,
        byLanguage,
        byUtmSource,
        byDay,
        byDayIp,
        byIp,
        recent
    });
})
.RequireCors("Site");

app.Run();

static HashSet<string> ResolveExcludedIps(IConfiguration config)
{
    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (var ip in config.GetSection("Analytics:ExcludedIps").Get<string[]>() ?? [])
    {
        if (!string.IsNullOrWhiteSpace(ip))
        {
            set.Add(ip.Trim());
        }
    }

    var csv = config["ANALYTICS_EXCLUDED_IPS"]
        ?? Environment.GetEnvironmentVariable("ANALYTICS_EXCLUDED_IPS");
    if (!string.IsNullOrWhiteSpace(csv))
    {
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(part);
        }
    }

    return set;
}

static string OwnIpLabel(string ip) => ip switch
{
    "104.30.177.192" => "Work laptop",
    "176.79.88.124" => "Personal mobile",
    _ => "Own IP"
};

static (string Value, string Source) ResolveConnectionStringDetailed(IConfiguration config)
{
    static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Prefer direct process env (ACA secretref) before layered config.
    var b64 =
        NullIfEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__AnalyticsB64"))
        ?? NullIfEmpty(config["ConnectionStrings:AnalyticsB64"])
        ?? NullIfEmpty(config["ANALYTICS_CONNECTION_STRING_B64"])
        ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANALYTICS_CONNECTION_STRING_B64"));
    if (b64 is not null)
    {
        try
        {
            return (NormalizeConnectionString(Encoding.UTF8.GetString(Convert.FromBase64String(b64))), "b64");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("ANALYTICS_CONNECTION_STRING_B64 is not valid base64.", ex);
        }
    }

    var raw =
        NullIfEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Analytics"))
        ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANALYTICS_CONNECTION_STRING"))
        ?? NullIfEmpty(config.GetConnectionString("Analytics"))
        ?? NullIfEmpty(config["Analytics:ConnectionString"])
        ?? NullIfEmpty(config["ANALYTICS_CONNECTION_STRING"])
        ?? NullIfEmpty(config["DATABASE_URL"])
        ?? "";

    return raw.Length == 0 ? ("", "none") : (NormalizeConnectionString(raw), "plain");
}

static string NormalizeConnectionString(string raw)
{
    raw = raw.Trim().Trim('"').Trim('\'');

    // Supabase URI → Npgsql key=value (manual parse so passwords with '.' don't break ports)
    if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var schemeSplit = raw.Split(["://"], 2, StringSplitOptions.None);
        if (schemeSplit.Length != 2)
        {
            throw new InvalidOperationException("Invalid Postgres URI.");
        }

        var rest = schemeSplit[1];
        var slash = rest.IndexOf('/');
        var netloc = slash >= 0 ? rest[..slash] : rest;
        var database = slash >= 0 ? rest[(slash + 1)..] : "postgres";
        if (string.IsNullOrWhiteSpace(database))
        {
            database = "postgres";
        }

        var at = netloc.LastIndexOf('@');
        if (at < 0)
        {
            throw new InvalidOperationException(
                "Postgres URI missing USER:PASSWORD@HOST. Expected postgresql://postgres.PROJECT:PASSWORD@HOST:5432/postgres");
        }

        var userInfo = netloc[..at];
        var hostPort = netloc[(at + 1)..];
        var colon = userInfo.IndexOf(':');
        if (colon < 0)
        {
            throw new InvalidOperationException(
                "Postgres URI missing :PASSWORD before @HOST (password was probably put in the port field).");
        }

        var user = Uri.UnescapeDataString(userInfo[..colon]);
        var password = Uri.UnescapeDataString(userInfo[(colon + 1)..]);

        string host;
        var port = 5432;
        var portColon = hostPort.LastIndexOf(':');
        if (portColon > 0)
        {
            host = hostPort[..portColon];
            var portText = hostPort[(portColon + 1)..];
            if (!int.TryParse(portText, out port))
            {
                throw new InvalidOperationException(
                    $"Invalid port '{portText}' in Postgres URI — use :5432 after the host.");
            }
        }
        else
        {
            host = hostPort;
        }

        return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
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

/// <summary>Merge common short aliases into canonical UTM sources (e.g. ig → Instagram).</summary>
static string? NormalizeUtmSource(string? value)
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

internal sealed record AnalyticsRuntimeState(
    bool HasConnectionString,
    string Source,
    int PlainEnvLen,
    int B64EnvLen,
    int AnalyticsEnvLen);
