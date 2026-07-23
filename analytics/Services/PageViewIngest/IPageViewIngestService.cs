namespace AnalyticsApi.Services.PageViewIngest;

using System.Net;
using AnalyticsApi.Contracts;

public interface IPageViewIngestService
{
    Task<bool> TryIngestAsync(
        PageViewRequest request,
        string? userAgentHeader,
        string? cloudflareCountry,
        IPAddress? remoteIp,
        CancellationToken cancellationToken = default);
}
