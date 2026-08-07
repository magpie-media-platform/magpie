using System;

namespace Magpie.Models.Data;

/// <summary>
/// A root folder default for one hierarchy level.
/// </summary>
public sealed record RootFolderDefault
{
    /// <summary>
    /// The id of the default quality tier list.
    /// </summary>
    public Guid? QualityTierListId { get; set; }
}
