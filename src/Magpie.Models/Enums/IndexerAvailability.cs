namespace Magpie.Models.Enums;

/// <summary>
/// The availability of an indexer.
/// </summary>
public enum IndexerAvailability
{
    /// <summary>
    /// The availability is not known.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The indexer is open to the public.
    /// </summary>
    Public = 1,

    /// <summary>
    /// The indexer needs a registration or an invite.
    /// </summary>
    Private = 2,

    /// <summary>
    /// The indexer is partly open, with some restrictions.
    /// </summary>
    SemiPrivate = 3
}
