using EtfInsight.Api.Attributes;
using EtfInsight.Core.Configuration;
using Microsoft.Extensions.Options;

namespace EtfInsight.Api.Middleware;

public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    private const string ApiKeyHeaderName = "X-API-Key";

    public async Task InvokeAsync(
		HttpContext context, 
		IOptions<AISettings> settings)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ApiKeyRequiredAttribute>() is null)
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey)
            || string.IsNullOrWhiteSpace(settings.Value.IngestAPIKey)
            || !string.Equals(providedKey, settings.Value.IngestAPIKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
            return;
        }

        await next(context);
    }
}