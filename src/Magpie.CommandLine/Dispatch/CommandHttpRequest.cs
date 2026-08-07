using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Magpie.CommandLine.Dispatch;

internal sealed record CommandHttpRequest
{
    private CommandHttpRequest(
        HttpMethod method,
        string uri,
        object? body,
        BodyKind bodyKind,
        bool allowsAnonymousRemote)
    {
        Method = method;
        Uri = uri;
        Body = body;
        Kind = bodyKind;
        AllowsAnonymousRemote = allowsAnonymousRemote;
    }
    private enum BodyKind
    {
        None,
        RawJson,
        File
    }

    public HttpMethod Method { get; }

    public string Uri { get; }

    public object? Body { get; }

    public bool AllowsAnonymousRemote { get; }

    private BodyKind Kind { get; }

    public static CommandHttpRequest Create(
        HttpMethod method,
        string uri,
        bool allowsAnonymousRemote = false)
    {
        return new CommandHttpRequest(method, uri, body: null, BodyKind.None, allowsAnonymousRemote);
    }

    public static CommandHttpRequest WithRawJsonBody(
        HttpMethod method,
        string uri,
        string body,
        bool allowsAnonymousRemote = false)
    {
        ArgumentNullException.ThrowIfNull(body);

        return new CommandHttpRequest(method, uri, body, BodyKind.RawJson, allowsAnonymousRemote);
    }

    public static CommandHttpRequest WithFileBody(
        HttpMethod method,
        string uri,
        string path,
        string fieldName,
        bool allowsAnonymousRemote = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        return new CommandHttpRequest(
            method,
            uri,
            new FileBody(path, fieldName),
            BodyKind.File,
            allowsAnonymousRemote);
    }

    public HttpRequestMessage Build(JsonSerializerOptions jsonOptions)
    {
        var message = new HttpRequestMessage(Method, Uri);
        switch (Kind)
        {
            case BodyKind.RawJson:
                message.Content = new StringContent((string)Body!, Encoding.UTF8, "application/json");
                break;
            case BodyKind.File:
                var file = (FileBody)Body!;
                var multipart = new MultipartFormDataContent();
                multipart.Add(new StreamContent(File.OpenRead(file.Path)), file.FieldName, Path.GetFileName(file.Path));
                message.Content = multipart;
                break;
        }

        return message;
    }

    private sealed record FileBody(string Path, string FieldName);
}
