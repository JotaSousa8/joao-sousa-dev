namespace AnalyticsApi.Services.AnalyticsSummary;

public interface IAnalyticsSummaryService
{
    Task<object> GetSummaryAsync(
        string? fromRaw,
        string? toRaw,
        string? limitRaw,
        CancellationToken cancellationToken = default);
}
