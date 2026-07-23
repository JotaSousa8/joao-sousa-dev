using AnalyticsApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddAnalyticsInfrastructure();

var app = builder.Build();
app.UseAnalyticsPipeline();
app.Run();
