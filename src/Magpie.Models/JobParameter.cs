using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The declaration of one job parameter.
/// </summary>
/// <param name="Name">The name of the parameter</param>
/// <param name="Description">The description shown for the parameter</param>
/// <param name="Type">The data type of the parameter value</param>
/// <param name="Required">Whether the parameter is mandatory</param>
/// <param name="DefaultValue">The value that applies when the caller omits the parameter</param>
public sealed record JobParameter(
    string Name,
    string? Description,
    DataType Type,
    bool Required,
    string? DefaultValue);
