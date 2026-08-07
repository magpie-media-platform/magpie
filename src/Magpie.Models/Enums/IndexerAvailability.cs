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
    Public,

    /// <summary>
    /// The indexer needs a registration or an invite.
    /// </summary>
    Private,

    /// <summary>
    /// The indexer is partly open, with some restrictions.
    /// </summary>
    SemiPrivate
}
