namespace Magpie.Models.Data;

/// <summary>
/// The cluster-wide activation state of the extensions.
/// </summary>
public record ExtensionActivationState
{
    /// <summary>
    /// The id of the singleton row.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The generation number that increases with each activation change.
    /// </summary>
    public long Generation { get; set; }
}
