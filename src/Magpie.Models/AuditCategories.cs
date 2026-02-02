namespace Magpie.Models;

/// <summary>
/// Constants for audit/history entry categories.
/// </summary>
public static class AuditCategories
{
    /// <summary>
    /// Security-related operations.
    /// </summary>
    public const string Security = "Security";

    /// <summary>
    /// System-level operations.
    /// </summary>
    public const string System = "System";

    /// <summary>
    /// Operations related to external integrations.
    /// </summary>
    public const string Integrations = "Integrations";

    /// <summary>
    /// Operations related to job processing.
    /// </summary>
    public const string Jobs = "Jobs";

    /// <summary>
    /// Operations related to content management.
    /// </summary>
    public static string Content = "Content";
}
