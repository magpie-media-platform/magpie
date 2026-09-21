namespace Magpie.Data.Configuration;

/// <summary>
/// The names of the job-execution reference columns that index the frozen audit lookup keys.
/// </summary>
public static class JobExecutionReferenceColumns
{
    /// <summary>
    /// The maximum length of a job-execution reference column.
    /// </summary>
    public const int MaxLength = 256;

    /// <summary>
    /// The column name for the content hierarchy item id of a job parameter.
    /// </summary>
    public const string ParameterContentHierarchyItemId = "ParameterContentHierarchyItemId";

    /// <summary>
    /// The column name for the content item id of a job parameter.
    /// </summary>
    public const string ParameterContentItemId = "ParameterContentItemId";

    /// <summary>
    /// The column name for the download id of a job parameter.
    /// </summary>
    public const string ParameterDownloadId = "ParameterDownloadId";

    /// <summary>
    /// The column name for the content hierarchy item id of a job trigger context.
    /// </summary>
    public const string TriggerContextContentHierarchyItemId = "TriggerContextContentHierarchyItemId";

    /// <summary>
    /// The column name for the content item id of a job trigger context.
    /// </summary>
    public const string TriggerContextContentItemId = "TriggerContextContentItemId";

    /// <summary>
    /// The column name for the download id of a job trigger context.
    /// </summary>
    public const string TriggerContextDownloadId = "TriggerContextDownloadId";

    internal static readonly (string Property, string SourceColumn, string JsonKey)[] All =
    [
        (ParameterContentHierarchyItemId, "Parameters", "contentHierarchyItemId"),
        (ParameterContentItemId, "Parameters", "contentItemId"),
        (ParameterDownloadId, "Parameters", "downloadId"),
        (TriggerContextContentHierarchyItemId, "TriggerContext", "event.context.content.hierarchyItemId"),
        (TriggerContextContentItemId, "TriggerContext", "event.context.content.itemId"),
        (TriggerContextDownloadId, "TriggerContext", "event.context.download.id"),
    ];
}
