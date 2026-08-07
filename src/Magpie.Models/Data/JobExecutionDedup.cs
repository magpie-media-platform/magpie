namespace Magpie.Models.Data;

/// <summary>
/// The deduplication constants for job executions.
/// </summary>
public static class JobExecutionDedup
{
    /// <summary>
    /// The parameter key that carries the deduplication key.
    /// </summary>
    public const string ParameterKey = "__magpie.dedupKey";
}
