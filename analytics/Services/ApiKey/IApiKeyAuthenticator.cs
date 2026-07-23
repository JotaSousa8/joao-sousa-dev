namespace AnalyticsApi.Services.ApiKey;

using Microsoft.AspNetCore.Mvc;

public interface IApiKeyAuthenticator
{
    IActionResult? UnauthorizedIfInvalid(HttpRequest request);
}
