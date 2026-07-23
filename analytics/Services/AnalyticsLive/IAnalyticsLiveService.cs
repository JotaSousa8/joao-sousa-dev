namespace AnalyticsApi.Services.AnalyticsLive;

public interface IAnalyticsLiveService
{
    Task<object> GetLiveAsync(CancellationToken cancellationToken = default);
}
