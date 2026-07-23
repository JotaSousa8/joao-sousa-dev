namespace AnalyticsApi.Services.GeoIp;

using System.Net;
using AnalyticsApi.Contracts.Responses;

public interface IGeoIpService
{
    Task<GeoLocation?> ResolveAsync(IPAddress? ip, CancellationToken cancellationToken = default);
}
