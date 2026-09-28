using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// The definition of a relationship role that a subject kind can fill for content.
/// </summary>
public sealed record RelationshipRoleDefinition
{
    /// <summary>
    /// The relationship kind that metadata providers write for this role, such as director or studio.
    /// </summary>
    public required string Slug { get; set; }

    /// <summary>
    /// The display name of the role.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// The meaning of the role.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The example names of related subjects for this role.
    /// </summary>
    public IReadOnlyList<string> Examples { get; set; } = [];
}
