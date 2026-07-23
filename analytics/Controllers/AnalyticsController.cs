namespace AnalyticsApi.Controllers;

using System.Text;
using AnalyticsApi.Contracts;
using AnalyticsApi.Services.AnalyticsExport;
using AnalyticsApi.Services.AnalyticsLive;
using AnalyticsApi.Services.AnalyticsSummary;
using AnalyticsApi.Services.ApiKey;
using AnalyticsApi.Services.PageViewIngest;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/analytics")]
[EnableCors("Site")]
public sealed class AnalyticsController(
    IPageViewIngestService ingest,
    IAnalyticsSummaryService summary,
    IAnalyticsLiveService live,
    IAnalyticsExportService export,
    IApiKeyAuthenticator apiKey) : ControllerBase
{
    [HttpPost("pageview")]
    [EnableRateLimiting("pageview")]
    public async Task<IActionResult> TrackPageView(
        [FromBody] PageViewRequest request,
        CancellationToken cancellationToken)
    {
        var ok = await ingest.TryIngestAsync(
            request,
            Request.Headers.UserAgent.ToString(),
            Request.Headers["CF-IPCountry"].ToString(),
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        return ok ? Accepted() : BadRequest(new { error = "Invalid path." });
    }

    [HttpGet("live")]
    public async Task<IActionResult> GetLive(CancellationToken cancellationToken)
    {
        if (apiKey.UnauthorizedIfInvalid(Request) is { } failure)
        {
            return failure;
        }

        return Ok(await live.GetLiveAsync(cancellationToken));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        if (apiKey.UnauthorizedIfInvalid(Request) is { } failure)
        {
            return failure;
        }

        var payload = await summary.GetSummaryAsync(
            Request.Query["from"],
            Request.Query["to"],
            Request.Query["limit"],
            cancellationToken);

        return Ok(payload);
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv(CancellationToken cancellationToken)
    {
        if (apiKey.UnauthorizedIfInvalid(Request) is { } failure)
        {
            return failure;
        }

        var (fileName, csv) = await export.BuildCsvAsync(
            Request.Query["from"],
            Request.Query["to"],
            cancellationToken);

        var bytes = Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
