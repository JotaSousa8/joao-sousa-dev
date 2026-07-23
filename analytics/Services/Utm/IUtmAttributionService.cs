namespace AnalyticsApi.Services.Utm;

using AnalyticsApi.Contracts;
using AnalyticsApi.Contracts.Responses;

public interface IUtmAttributionService
{
    string? NormalizeSource(string? value);

    UtmResolution Resolve(PageViewRequest request, string? userAgent);
}
