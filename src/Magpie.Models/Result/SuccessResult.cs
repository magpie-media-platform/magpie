namespace Magpie.Models.Result;

/// <summary>
/// Represents a successful result.
/// </summary>
public sealed record SuccessResult() : Result(true), ISuccessResult;

/// <summary>
/// Represents a successful result with a value.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
/// <param name="Value"><inheritdoc cref="ISuccessResult{T}.Value" path="/summary"/></param>
public sealed record SuccessResult<T>(T Value) : Result<T>(true), ISuccessResult<T>;
