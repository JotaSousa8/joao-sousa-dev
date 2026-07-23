using System.Threading.RateLimiting;
using AnalyticsApi.Infrastructure;
using AnalyticsApi.Infrastructure.Persistence;
using AnalyticsApi.Serialization;
using AnalyticsApi.Services;
using AnalyticsApi.Services.GeoIp;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

namespace AnalyticsApi.Extensions;

public static class ServiceCollectionExtensions
{
    public const string MissingDb =
        "Host=127.0.0.1;Port=5432;Database=missing;Username=missing;Password=missing;SSL Mode=Disable";

    public static WebApplicationBuilder AddAnalyticsInfrastructure(this WebApplicationBuilder builder)
    {
        // Never throw during connection-string parsing — a bad Supabase URI must not
        // prevent Kestrel from binding /health (ACA otherwise keeps the old revision).
        string connectionString;
        var hasConnectionString = false;
        var resolveSource = "none";
        try
        {
            var resolved = ConnectionStringResolver.ResolveDetailed(builder.Configuration);
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

        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
                options.JsonSerializerOptions.Converters.Add(new UtcNullableDateTimeJsonConverter());
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

        builder.Services.AddScoped<PageViewIngestService>();
        builder.Services.AddScoped<AnalyticsSummaryService>();
        builder.Services.AddSingleton<ApiKeyAuthenticator>();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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

        return builder;
    }

    public static WebApplication UseAnalyticsPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseRateLimiter();
        app.UseCors("Site");
        app.MapControllers();

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

        return app;
    }
}
