using System;

namespace Magpie.Models.Content;

/// <summary>
/// Patch for <see cref="ContentHierarchyLevel"/>.
/// </summary>
public sealed record ContentHierarchyLevelPatch
{
    /// <inheritdoc cref="ContentHierarchyLevel.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="ContentHierarchyLevel.RelativePath" />
    public FieldPatch<string?>? RelativePath { get; init; }
    /// <inheritdoc cref="ContentHierarchyLevel.LanguagePreferences" />
    public CollectionPatch<LanguagePreference, Guid, LanguagePreferencePatch>? LanguagePreferences { get; init; }
    /// <inheritdoc cref="ContentHierarchyLevel.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
}
