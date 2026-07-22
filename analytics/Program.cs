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
        connectionStringConfigured = runtime.HasConnectionString
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
        ClientIp = clientIp,
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
        .Select(x => new { x.ClientIp, x.OccurredAtUtc, x.Country })
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

app.MapGet("/api/analytics/schema", async (
    HttpRequest httpRequest,
    AnalyticsDbContext db,
    IConfiguration config) =>
{
    if (!TryAuthorize(httpRequest, config, out var authError))
    {
        return authError!;
    }

    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();

    var tables = new List<object>();
    await using (var listCmd = connection.CreateCommand())
    {
        listCmd.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;
        await using var reader = await listCmd.ExecuteReaderAsync();
        var tableNames = new List<string>();
        while (await reader.ReadAsync())
        {
            tableNames.Add(reader.GetString(0));
        }

        await reader.CloseAsync();

        foreach (var tableName in tableNames)
        {
            var columns = new List<object>();
            await using (var colCmd = connection.CreateCommand())
            {
                colCmd.CommandText = """
                    SELECT column_name, data_type, is_nullable,
                           CASE WHEN EXISTS (
                             SELECT 1
                             FROM information_schema.table_constraints tc
                             JOIN information_schema.key_column_usage kcu
                               ON tc.constraint_name = kcu.constraint_name
                              AND tc.table_schema = kcu.table_schema
                             WHERE tc.table_schema = 'public'
                               AND tc.table_name = @table
                               AND tc.constraint_type = 'PRIMARY KEY'
                               AND kcu.column_name = c.column_name
                           ) THEN TRUE ELSE FALSE END AS is_pk
                    FROM information_schema.columns c
                    WHERE table_schema = 'public' AND table_name = @table
                    ORDER BY ordinal_position;
                    """;
                var p = colCmd.CreateParameter();
                p.ParameterName = "table";
                p.Value = tableName;
                colCmd.Parameters.Add(p);

                await using var colReader = await colCmd.ExecuteReaderAsync();
                while (await colReader.ReadAsync())
                {
                    columns.Add(new
                    {
                        name = colReader.GetString(0),
                        type = colReader.IsDBNull(1) ? "" : colReader.GetString(1),
                        notNull = string.Equals(colReader.GetString(2), "NO", StringComparison.OrdinalIgnoreCase),
                        primaryKey = !colReader.IsDBNull(3) && colReader.GetBoolean(3)
                    });
                }
            }

            tables.Add(new { name = tableName, columns });
        }
    }

    return Results.Ok(new { tables });
})
.RequireCors("Site");

app.MapPost("/api/analytics/query", async (
    SqlQueryRequest request,
    HttpRequest httpRequest,
    AnalyticsDbContext db,
    IConfiguration config,
    CancellationToken cancellationToken) =>
{
    if (!TryAuthorize(httpRequest, config, out var authError))
    {
        return authError!;
    }

    var sql = request.Sql?.Trim() ?? "";
    if (string.IsNullOrWhiteSpace(sql))
    {
        return Results.BadRequest(new { error = "SQL is required." });
    }

    if (!IsReadOnlySelect(sql))
    {
        return Results.BadRequest(new { error = "Only a single read-only SELECT (or WITH … SELECT) is allowed." });
    }

    var maxRows = 200;
    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync(cancellationToken);

    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 5;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= maxRows)
            {
                break;
            }

            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return Results.Ok(new
        {
            columns,
            rows,
            rowCount = rows.Count,
            truncated = rows.Count >= maxRows
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.RequireCors("Site");

app.Run();

static string ResolveConnectionString(IConfiguration config)
{
    var raw =
        config.GetConnectionString("Analytics")
        ?? config["Analytics:ConnectionString"]
        ?? config["DATABASE_URL"]
        ?? "";

    raw = raw.Trim();
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

static bool TryAuthorize(HttpRequest httpRequest, IConfiguration config, out IResult? error)
{
    error = null;
    var expectedKey = config["Analytics:ApiKey"];
    if (string.IsNullOrWhiteSpace(expectedKey))
    {
        error = Results.Problem("Analytics:ApiKey is not configured.", statusCode: 500);
        return false;
    }

    if (!httpRequest.Headers.TryGetValue("X-Api-Key", out var provided)
        || !FixedTimeEquals(provided.ToString(), expectedKey))
    {
        error = Results.Unauthorized();
        return false;
    }

    return true;
}

static bool IsReadOnlySelect(string sql)
{
    // Strip line comments for a light check; still reject multi-statement / writes.
    var withoutLineComments = string.Join(
        '\n',
        sql.Split('\n').Select(line =>
        {
            var idx = line.IndexOf("--", StringComparison.Ordinal);
            return idx >= 0 ? line[..idx] : line;
        }));

    var normalized = withoutLineComments.Trim().TrimEnd(';').Trim();
    if (normalized.Length == 0 || normalized.Contains(';'))
    {
        return false;
    }

    var upper = normalized.ToUpperInvariant();
    if (!(upper.StartsWith("SELECT", StringComparison.Ordinal) || upper.StartsWith("WITH", StringComparison.Ordinal)))
    {
        return false;
    }

    // Ignore string literals so values like 'into' do not trip keyword checks.
    var withoutStrings = System.Text.RegularExpressions.Regex.Replace(
        upper,
        @"'([^']|'')*'",
        "''",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    string[] banned =
    [
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "REPLACE",
        "ATTACH", "DETACH", "VACUUM", "REINDEX", "GRANT", "REVOKE", "TRUNCATE",
        "PRAGMA"
    ];

    foreach (var word in banned)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(
                withoutStrings,
                $@"\b{word}\b",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            return false;
        }
    }

    // Block SELECT … INTO / INSERT-like forms
    if (System.Text.RegularExpressions.Regex.IsMatch(
            withoutStrings,
            @"\bINTO\b",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant))
    {
        return false;
    }

    return true;
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

internal sealed record SqlQueryRequest(
    [property: JsonPropertyName("sql")] string? Sql);

internal sealed record AnalyticsRuntimeState(bool HasConnectionString);
