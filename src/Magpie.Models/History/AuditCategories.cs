namespace Magpie.Models.History;

/// <summary>
/// Constants for audit/history entry categories.
/// </summary>
public static class AuditCategories
{
    /// <summary>
    /// Security-related operations (users, API keys, authentication).
    /// </summary>
    public const string Security = "Security";

    /// <summary>
    /// System configuration operations (settings, settings groups).
    /// </summary>
    public const string System = "System";

    /// <summary>
    /// Integration operations (media servers, download clients, indexers).
    /// </summary>
    public const string Integrations = "Integrations";
}
