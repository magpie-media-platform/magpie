using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Magpie.App;

internal sealed class MagpieExceptionHandler(
    IProblemDetailsService problemDetailsService,
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
            _ => exception.Message
        };

        logger.LogWarning("The request failed with status {StatusCode}: {Detail}", statusCode.Value, detail);

        httpContext.Response.StatusCode = statusCode.Value;

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
