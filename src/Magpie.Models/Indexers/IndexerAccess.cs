namespace Magpie.Models.Indexers;

/// <summary>
/// The access type for an indexer.
/// </summary>
public enum IndexerAccess
{
    /// <summary>
    /// Access type is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Open to anyone.
    /// </summary>
    Public,

    /// <summary>
    /// Requires registration or invite.
    /// </summary>
    Private,

    /// <summary>
    /// Open registration with some restrictions.
    /// </summary>
    SemiPrivate
}
