using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
    string? DefaultValue)
{
    /// <summary>
    /// Checks a supplied or default schedule value without external access and returns an error or null.
    /// </summary>
    [JsonIgnore]
    public Func<string?, string?>? ValidateValue { get; set; }

    /// <summary>
    /// Uses parameters and UTC time to check a schedule value, replacing its type check, and returns an error or null.
    /// </summary>
    [JsonIgnore]
    public Func<string?, IReadOnlyDictionary<string, string>, DateTime, string?>? ValidateScheduleValue { get; set; }
}
