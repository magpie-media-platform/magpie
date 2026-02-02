using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Abstractions.Jobs;
using Magpie.Models;
using Magpie.Models.Enums;

namespace Magpie.Core.Jobs;

/// <summary>
/// Validates job parameters against a job's parameter definitions.
/// </summary>
public static class JobParameterValidator
{
    /// <summary>
    /// Validate and normalize parameters against the job's parameter definitions.
    /// </summary>
    /// <param name="job">The job containing parameter definitions.</param>
    /// <param name="parameters">The parameters to validate.</param>
    /// <returns>The validated and normalized parameters.</returns>
    public static IReadOnlyDictionary<string, string>? TryValidate(
        IJob job,
        IReadOnlyDictionary<string, string> parameters)
    {
        var caseInsensitiveParameters = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>();

        foreach (var parameter in job.Parameters)
        {
            if (caseInsensitiveParameters.TryGetValue(parameter.Name, out var value))
            {
                result[parameter.Name] = ValidateType(parameter, value);
            }
            else if (parameter.Required)
            {
                throw new ArgumentException($"Required parameter '{parameter.Name}' is missing");
            }
            else if (parameter.DefaultValue is not null)
            {
                result[parameter.Name] = parameter.DefaultValue;
            }
        }

        return result;
    }

    private static string ValidateType(JobParameter param, string value)
    {
        return param.Type switch
        {
            DataType.String => value,
            DataType.Number => ValidateNumber(param.Name, value),
            DataType.Boolean => ValidateBoolean(param.Name, value),
            DataType.Date => ValidateDate(param.Name, value),
            _ => throw new ArgumentException($"Parameter '{param.Name}' has unknown type '{param.Type}' parameters")
        };
    }

    private static string ValidateNumber(string name, string value)
    {
        if (!double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException($"Parameter '{name}' must be a number");
        }
        return value;
    }

    private static string ValidateBoolean(string name, string value)
    {
        if (string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, bool.FalseString, StringComparison.OrdinalIgnoreCase))
        {
            return value.ToLowerInvariant();
        }

        throw new ArgumentException($"Parameter '{name}' must be a boolean");
    }

    private static string ValidateDate(string name, string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return value;
        }

        throw new ArgumentException($"Parameter '{name}' must be a valid date");
    }
}
