namespace Magpie.Models.Result;

/// <summary>
/// Represents a failed result.
/// </summary>
/// <param name="Reason">The error reason or message.</param>
/// <param name="ErrorCode">An optional error code.</param>
/// <param name="SubErrorCode">An optional additional, more specific error code.</param>
public sealed record FailureResult(
    string Reason,
    string? ErrorCode = default,
    string? SubErrorCode = default) : Result(false), IFailureResult;

/// <summary>
/// Represents a failed value result.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
/// <param name="Reason">The error reason or message.</param>
/// <param name="ErrorCode">An optional error code.</param>
/// <param name="SubErrorCode">An optional additional, more specific error code.</param>
public sealed record FailureResult<T>(
    string Reason,
    string? ErrorCode = default,
    string? SubErrorCode = default) : Result<T>(false), IFailureResult<T>;
