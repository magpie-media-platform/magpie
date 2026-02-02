namespace Magpie.Models.Enums;

/// <summary>
/// The availability of an indexer.
/// </summary>
public enum IndexerAvailability
{
    /// <summary>
    /// The availability is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The indexer is open to the public.
    /// </summary>
    Public,

    /// <summary>
    /// The indexer requires registration or an invite.
    /// </summary>
    Private,

    /// <summary>
    /// The indexer is partially open, with some restrictions.
    /// </summary>
    SemiPrivate
}
