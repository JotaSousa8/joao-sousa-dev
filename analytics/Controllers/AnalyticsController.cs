using AnalyticsApi.Contracts;
using AnalyticsApi.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AnalyticsApi.Controllers;

[ApiController]
[Route("api/analytics")]
[EnableCors("Site")]
public sealed class AnalyticsController(
    PageViewIngestService ingest,
    AnalyticsSummaryService summary,
    ApiKeyAuthenticator apiKey) : ControllerBase
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
}
