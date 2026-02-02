using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// A parameter accepted by a job.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Description">A description of the parameter.</param>
/// <param name="Type">The parameter data type.</param>
/// <param name="Required">Whether the parameter is required.</param>
/// <param name="DefaultValue">The default value to use if no value is specified.</param>
public sealed record JobParameter(
    string Name,
    string? Description,
    DataType Type,
    bool Required,
    string? DefaultValue);
