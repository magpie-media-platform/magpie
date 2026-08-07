using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The fields to change on a subject.
/// </summary>
public sealed record SubjectPatch
{
    /// <summary>
    /// The patch for the tags.
    /// </summary>
    public SetPatch<string, string>? Tags { get; init; }

    /// <summary>
    /// The id of the image to make primary.
    /// </summary>
    public ScalarPatch<Guid>? PrimaryImageId { get; init; }
}
