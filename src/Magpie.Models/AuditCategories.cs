namespace Magpie.Models;

/// <summary>
/// The category constants for audit and history entries.
/// </summary>
public static class AuditCategories
{
    /// <summary>
    /// The category for content operations.
    /// </summary>
    public const string Content = "Content";

    /// <summary>
    /// The category for security operations.
    /// </summary>
    public const string Security = "Security";

    /// <summary>
    /// The category for system-level operations.
    /// </summary>
    public const string System = "System";

    /// <summary>
    /// The category for external integration operations.
    /// </summary>
    public const string Integrations = "Integrations";

    /// <summary>
    /// The category for job processing operations.
    /// </summary>
    public const string Jobs = "Jobs";
}
