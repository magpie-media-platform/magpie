namespace Magpie.Models.Result;

/// <summary>
/// Represents a failed value result.
/// </summary>
/// <typeparam name="T">The type of value represented by the result.</typeparam>
/// <param name="Reason">The reason for the failure.</param>
/// <param name="ErrorCode">The error code for the failure.</param>
/// <param name="SubErrorCode">The sub-error code for the failure.</param>
public sealed record FailureResult<T>(
    string Reason,
    string? ErrorCode = default,
    string? SubErrorCode = default) : Result<T>(false)
{
    /// <summary>
    /// Converts the failure result to a different type.
    /// </summary>
    /// <typeparam name="TDestination">The type of the destination result.</typeparam>
    /// <returns>A copy of the failure result converted to <see cref="FailureResult{TDestination}"/>.</returns>
    public FailureResult<TDestination> As<TDestination>()
    {
        return new FailureResult<TDestination>(Reason, ErrorCode, SubErrorCode);
    }
}
