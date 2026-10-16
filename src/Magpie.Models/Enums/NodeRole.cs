namespace Magpie.Models.Enums;

/// <summary>
/// A role that a Magpie node serves.
/// </summary>
public enum NodeRole
{
    /// <summary>
    /// The node serves the web interface.
    /// </summary>
    Ui,

    /// <summary>
    /// The node serves the API.
    /// </summary>
    Api,

    /// <summary>
    /// The node runs jobs.
    /// </summary>
    JobRunner,
}
