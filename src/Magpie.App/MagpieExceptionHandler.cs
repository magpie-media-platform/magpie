using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Models;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Magpie.App;

internal sealed class MagpieExceptionHandler(
    IProblemDetailsService problemDetailsService,
    TimeProvider timeProvider,
    ILogger<MagpieExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            BadHttpRequestException badRequestException => badRequestException.StatusCode,
            MagpieShutdownException => StatusCodes.Status409Conflict,
            UpstreamThrottledException => StatusCodes.Status503ServiceUnavailable,
            MagpieException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            DirectoryNotFoundException => StatusCodes.Status404NotFound,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            _ => (int?)null
        };

        if (statusCode is null)
        {
            return false;
        }

        var detail = exception switch
        {
            BadHttpRequestException { InnerException: JsonException jsonException }
                when jsonException.Message.Contains(
                    "could not be mapped to any .NET member",
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(jsonException.Path) =>
                $"The request body contains an unknown property at '{jsonException.Path}'.",
            BadHttpRequestException { InnerException: JsonException jsonException }
                when jsonException.Message.Contains(
                    "could not be mapped to any .NET member",
                    StringComparison.Ordinal) =>
                "The request body contains an unknown property.",
            BadHttpRequestException { InnerException: JsonException jsonException }
                when !string.IsNullOrWhiteSpace(jsonException.Path) =>
                $"The request body contains invalid JSON at '{jsonException.Path}'.",
            BadHttpRequestException { InnerException: JsonException } =>
                "The request body contains invalid JSON.",
            UpstreamThrottledException => "The provider is busy. Try again later.",
            _ => exception.Message
        };

        logger.LogWarning(exception, "The request failed with status {StatusCode}", statusCode.Value);

        httpContext.Response.StatusCode = statusCode.Value;

        if (exception is UpstreamThrottledException throttled)
        {
            var wait = throttled.RetryAt - timeProvider.GetUtcNow();
            httpContext.Response.Headers.RetryAfter = Math.Ceiling(Math.Max(0, wait.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = statusCode.Value,
                Detail = detail
            }
        }).ConfigureAwait(false);
    }
}
