using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed record CloudflareClearance
{
    public required IReadOnlyDictionary<string, string> Cookies { get; init; }

    public string? UserAgent { get; init; }

    public string? Body { get; init; }

    public int Status { get; init; }

    public bool Reused { get; init; }
}
