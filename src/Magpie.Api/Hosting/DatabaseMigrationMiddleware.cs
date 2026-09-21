using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Magpie.Models;

using Microsoft.AspNetCore.Http;

namespace Magpie.Api.Hosting;

internal sealed class DatabaseMigrationMiddleware(RequestDelegate next)
{
    public const string MigratingStatus = "Migrating";

    public const string StartupFailedStatus = "StartupFailed";

    private const string HealthPath = "/api/system/health";

    private const string ApiPath = "/api";

    public async Task InvokeAsync(HttpContext context, DatabaseMigrationState migrationState)
    {
        if (migrationState.IsComplete)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var failed = migrationState.IsFailed;
        var title = failed ? "The database work at start failed." : "The database is not ready.";
        var detail = failed
            ? "The database work at start failed. The host stops. Read the host log."
            : "The host is applying database migrations.";

        var path = context.Request.Path.Value ?? string.Empty;
        IResult result;
        if (context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase))
        {
            result = Results.Ok(new SystemHealth { Status = failed ? StartupFailedStatus : MigratingStatus });
        }
        else if (IsPageRequest(context.Request, path))
        {
            result = Results.Content(
                BuildPage(title, detail, failed),
                "text/html",
                Encoding.UTF8,
                StatusCodes.Status503ServiceUnavailable);
        }
        else
        {
            result = Results.Problem(
                detail: detail,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: title);
        }

        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    private static bool IsPageRequest(HttpRequest request, string path)
    {
        return !path.StartsWith(ApiPath, StringComparison.OrdinalIgnoreCase) &&
            request.Headers.Accept.Any(value =>
                value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string BuildPage(string title, string detail, bool failed)
    {
        var refresh = failed ? string.Empty : "<meta http-equiv=\"refresh\" content=\"5\">";

        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
            $"{refresh}<title>Magpie</title></head>" +
            $"<body><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(detail)}</p></body></html>";
    }
}
