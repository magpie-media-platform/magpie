using System;

namespace Magpie.Models.RootFolders;

/// <summary>
/// Patch for <see cref="RootFolder"/>.
/// </summary>
public sealed record RootFolderPatch
{
    /// <inheritdoc cref="RootFolder.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="RootFolder.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="RootFolder.Path" />
    public FieldPatch<string>? Path { get; init; }
    /// <inheritdoc cref="RootFolder.ContentTypeId" />
    public FieldPatch<Guid>? ContentTypeId { get; init; }
}
