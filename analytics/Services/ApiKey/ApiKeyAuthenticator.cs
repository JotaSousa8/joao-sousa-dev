namespace AnalyticsApi.Services.ApiKey;

using AnalyticsApi.Services.Shared;
using Microsoft.AspNetCore.Mvc;

public sealed class ApiKeyAuthenticator(IConfiguration config) : IApiKeyAuthenticator
{
    public IActionResult? UnauthorizedIfInvalid(HttpRequest request)
    {
        var expectedKey = config["Analytics:ApiKey"];
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            return new ObjectResult(new { title = "Analytics:ApiKey is not configured." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        if (!request.Headers.TryGetValue("X-Api-Key", out var provided)
            || !AnalyticsText.FixedTimeEquals(provided.ToString(), expectedKey))
        {
            return new UnauthorizedResult();
        }

        return null;
    }
}
