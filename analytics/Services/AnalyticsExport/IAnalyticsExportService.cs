namespace AnalyticsApi.Services.AnalyticsExport;

public interface IAnalyticsExportService
{
    Task<(string FileName, string Csv)> BuildCsvAsync(
        string? fromRaw,
        string? toRaw,
        CancellationToken cancellationToken = default);
}
