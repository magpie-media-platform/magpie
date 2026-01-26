namespace Magpie.Models.Result;

/// <summary>
/// Represents a successful result with a value.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
/// <param name="Value">The value of the result.</param>
public sealed record SuccessResult<T>(T Value) : Result<T>(true);
