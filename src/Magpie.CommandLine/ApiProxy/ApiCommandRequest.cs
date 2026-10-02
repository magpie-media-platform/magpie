using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;

using Microsoft.AspNetCore.Routing.Patterns;

namespace Magpie.CommandLine.ApiProxy;

internal sealed record ApiCommandRequest(
    HttpMethod Method,
    RoutePattern RoutePattern,
    IReadOnlyDictionary<string, string> RouteValues,
    IReadOnlyDictionary<string, IReadOnlyList<string>> QueryValues,
    string? BodyJson,
    string? FilePath = null,
    string? FileFieldName = null)
{
    public HttpRequestMessage ToHttpRequestMessage()
    {
        var message = new HttpRequestMessage(Method, BuildUri());
        if (BodyJson is not null)
        {
            message.Content = new StringContent(BodyJson, Encoding.UTF8, "application/json");
        }
        else if (FilePath is not null)
        {
            var multipart = new MultipartFormDataContent();
            multipart.Add(
                new StreamContent(File.OpenRead(FilePath)),
                FileFieldName ?? "file",
                Path.GetFileName(FilePath));
            message.Content = multipart;
        }

        return message;
    }

    private string BuildUri()
    {
        var builder = new StringBuilder();
        var firstSegment = true;
        foreach (var segment in RoutePattern.PathSegments)
        {
            if (firstSegment)
            {
                firstSegment = false;
            }
            else
            {
                builder.Append('/');
            }

            foreach (var part in segment.Parts)
            {
                AppendPart(builder, part);
            }
        }

        if (builder.Length == 0)
        {
            builder.Append('.');
        }

        AppendQuery(builder);

        return builder.ToString();
    }

    private void AppendPart(StringBuilder builder, RoutePatternPart part)
    {
        switch (part)
        {
            case RoutePatternLiteralPart literal:
                builder.Append(literal.Content);
                break;
            case RoutePatternSeparatorPart separator:
                builder.Append(separator.Content);
                break;
            case RoutePatternParameterPart parameter:
                AppendParameter(builder, parameter);
                break;
            default:
                throw new InvalidOperationException($"Unsupported route part {part.GetType().Name}.");
        }
    }

    private void AppendParameter(StringBuilder builder, RoutePatternParameterPart parameter)
    {
        var value = ResolveRouteValue(parameter.Name);
        if (parameter.IsCatchAll)
        {
            builder.Append(string.Join('/', value.Split('/').Select(Uri.EscapeDataString)));
        }
        else
        {
            builder.Append(Uri.EscapeDataString(value));
        }
    }

    private string ResolveRouteValue(string name)
    {
        return RouteValues.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Route value {name} was not provided.");
    }

    private void AppendQuery(StringBuilder builder)
    {
        var first = true;
        foreach (var (key, values) in QueryValues.OrderBy(q => q.Key, StringComparer.Ordinal))
        {
            foreach (var value in values)
            {
                builder.Append(first ? '?' : '&');
                first = false;
                builder.Append(Uri.EscapeDataString(key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(value));
            }
        }
    }
}
