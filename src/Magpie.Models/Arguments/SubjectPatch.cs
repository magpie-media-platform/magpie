using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The fields to change on a <see cref="Subject"/>.
/// </summary>
public sealed record SubjectPatch : TagsPatch
{
    /// <summary>
    /// The id of the image to make primary.
    /// </summary>
    public ScalarPatch<Guid>? PrimaryImageId { get; init; }
}
