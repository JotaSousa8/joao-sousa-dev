namespace AnalyticsApi.Controllers;

using AnalyticsApi.Infrastructure;
using AnalyticsApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
public sealed class HealthController(AnalyticsDbContext db, AnalyticsRuntimeState runtime) : ControllerBase
{
    [HttpGet("/health")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        const string database = "postgresql";
        var canConnect = false;
        if (runtime.HasConnectionString)
        {
            try
            {
                canConnect = await db.Database.CanConnectAsync(cancellationToken);
            }
            catch
            {
                canConnect = false;
            }
        }

        return Ok(new
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
    }
}
