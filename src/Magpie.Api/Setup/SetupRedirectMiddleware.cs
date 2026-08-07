using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Magpie.Api.Setup;

internal sealed class SetupRedirectMiddleware(
    RequestDelegate next,
    ILogger<SetupRedirectMiddleware> logger)
{
    private static readonly string[] AlwaysAllowedApiPrefixes =
    [
        "/api/system/health",
        "/api/system/ready",
        "/api/system/info",
        "/api/system/setup",
        "/api/auth",
    ];

    public const string SetupRequiredHeader = "X-Magpie-Setup-Required";

    private const string SetupSettingsGroupName = "setup";

    private const string SetupCompleteSettingKey = "complete";

    private static bool IsApiRoute(string path)
    {
        return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlwaysAllowed(string path)
    {
        return AlwaysAllowedApiPrefixes.Any(prefix =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public async Task InvokeAsync(HttpContext context, ISettingsService settingsService)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path) || !IsApiRoute(path) || IsAlwaysAllowed(path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var setupGroup = await settingsService
            .GetSystemGroupAsync(SetupSettingsGroupName, context.RequestAborted)
            .ConfigureAwait(false);
        var completeValue = setupGroup?.Group.Settings
            .FirstOrDefault(s => s.Key == SetupCompleteSettingKey)?.Value;
        var isComplete = string.Equals(completeValue, "true", StringComparison.OrdinalIgnoreCase);
        if (isComplete)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        logger.LogDebug("The middleware blocked the API request to {Path}. The setup is not complete", path);

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers[SetupRequiredHeader] = "true";
        context.Response.Headers.Location = "/setup";

        await context.Response.WriteAsync("The Magpie setup is not complete.").ConfigureAwait(false);
    }
}
