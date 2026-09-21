using System;
using System.Collections.Generic;

using AngleSharp.Dom;

using Magpie.Models.Data;

namespace Magpie.Extensions.Cardigann.Engine;

internal interface IResultParser
{
    IReadOnlyList<Release> Parse(
        string html,
        CardigannDefinition definition,
        Uri baseUri,
        IReadOnlyDictionary<string, object?>? variables = null);

    IReadOnlyList<Release> ParseJson(
        string json,
        CardigannDefinition definition,
        Uri baseUri,
        IReadOnlyDictionary<string, object?> variables);

    string? ExtractDownloadTarget(
        string html,
        DownloadBlock download,
        IReadOnlyDictionary<string, object?>? variables = null);

    string? ExtractField(IElement? root, FieldDefinition field, string context);

    void ApplyResolvedDownload(Release release, Uri resolved);
}
