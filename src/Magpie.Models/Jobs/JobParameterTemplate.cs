namespace Magpie.Models.Jobs;

/// <summary>
/// A parameter accepted by a job.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Description">A description of the parameter.</param>
/// <param name="Type">The parameter type (string, int, bool, etc.).</param>
/// <param name="Required">Whether the parameter is required.</param>
/// <param name="DefaultValue">The default value if not specified.</param>
public sealed record JobParameterTemplate(
    string Name,
    string? Description,
    string Type,
    bool Required,
    string? DefaultValue);
