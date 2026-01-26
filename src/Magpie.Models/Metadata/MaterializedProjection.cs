using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// A projection of materialized metadata suitable for IQueryable composition.
/// </summary>
/// <remarks>
/// This type is used for database-side materialization queries. Scalar fields
/// use the first non-null value by ordinal.
/// </remarks>
public sealed record MaterializedProjection
{
    /// <summary>
    /// The metadata container identifier.
    /// </summary>
    public Guid ContainerId { get; init; }

    /// <summary>
    /// The primary display title or name.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The original title for localized content.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>
    /// The title or name used for sorting.
    /// </summary>
    public string? SortTitle { get; init; }

    /// <summary>
    /// The primary language of the content.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// The primary synopsis, description, or biography.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// The original publication or release date.
    /// </summary>
    public DateTime? PublishDate { get; init; }

    /// <summary>
    /// The runtime duration for media content.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// The production status.
    /// </summary>
    public string? ProductionStatus { get; init; }

    /// <summary>
    /// The establishment date for organizations or groups.
    /// </summary>
    public DateTime? Established { get; init; }

    /// <summary>
    /// The dissolution date for organizations or groups.
    /// </summary>
    public DateTime? Dissolved { get; init; }

    /// <summary>
    /// The birth date for people.
    /// </summary>
    public DateTime? BirthDate { get; init; }

    /// <summary>
    /// The death date for people.
    /// </summary>
    public DateTime? DeathDate { get; init; }
}
