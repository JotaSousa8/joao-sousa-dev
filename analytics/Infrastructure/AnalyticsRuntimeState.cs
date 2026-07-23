namespace AnalyticsApi.Infrastructure;

public sealed record AnalyticsRuntimeState(
    bool HasConnectionString,
    string Source,
    int PlainEnvLen,
    int B64EnvLen,
    int AnalyticsEnvLen);
