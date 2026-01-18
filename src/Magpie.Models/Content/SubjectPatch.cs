using System;

namespace Magpie.Models.Content;

/// <summary>
/// Patch for <see cref="Subject"/>.
/// </summary>
public sealed record SubjectPatch
{
    /// <inheritdoc cref="Subject.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="Subject.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
}
