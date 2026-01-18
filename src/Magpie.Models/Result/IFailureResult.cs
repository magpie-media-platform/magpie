namespace Magpie.Models.Result;

/// <summary>
/// Represents a failed result.
/// </summary>
public interface IFailureResult : IResult
{
    /// <summary>
    /// The error reason or message.
    /// </summary>
    string Reason { get; }
    /// <summary>
    /// An optional error code.
    /// </summary>
    string? ErrorCode { get; }
    /// <summary>
    /// An optional additional, more specific error code.
    /// </summary>
    string? SubErrorCode { get; }
}

/// <summary>
/// Represents a typed failed result.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
public interface IFailureResult<T> : IFailureResult, IResult<T>
{
}
