using System;
using System.Threading.Tasks;

namespace Magpie.Models.Result;

/// <summary>
/// Monadic extension methods for Result types.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Transforms the value if successful, otherwise forwards the error.
    /// </summary>
    /// <typeparam name="TSource">The source value type.</typeparam>
    /// <typeparam name="TResult">The target value type.</typeparam>
    /// <param name="result">The result to transform.</param>
    /// <param name="selector">The transformation function.</param>
    public static Result<TResult> Map<TSource, TResult>(this Result<TSource> result, Func<TSource, TResult> selector)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }
        if (result is SuccessResult<TSource> success)
        {
            return Result.Success(selector(success.Value));
        }
        if (result is FailureResult<TSource> failure)
        {
            return new FailureResult<TResult>(failure.Reason, failure.ErrorCode, failure.SubErrorCode);
        }
        return Result.Fail<TResult>("Unexpected result");
    }

    /// <summary>
    /// Chains an operation that returns a Result, flattening the result.
    /// </summary>
    /// <typeparam name="TSource">The source value type.</typeparam>
    /// <typeparam name="TResult">The target value type.</typeparam>
    /// <param name="result">The result to chain from.</param>
    /// <param name="selector">The operation that returns a Result.</param>
    public static Result<TResult> Bind<TSource, TResult>(
        this Result<TSource> result,
        Func<TSource, Result<TResult>> selector)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }
        if (result is SuccessResult<TSource> success)
        {
            return selector(success.Value);
        }
        if (result is FailureResult<TSource> failure)
        {
            return new FailureResult<TResult>(failure.Reason, failure.ErrorCode, failure.SubErrorCode);
        }
        return Result.Fail<TResult>("Unexpected result");
    }

    /// <summary>
    /// Transforms the value if successful, otherwise forwards the error. Async version.
    /// </summary>
    /// <typeparam name="TSource">The source value type.</typeparam>
    /// <typeparam name="TResult">The target value type.</typeparam>
    /// <param name="resultTask">The result task to transform.</param>
    /// <param name="selector">The transformation function.</param>
    public static async Task<Result<TResult>> MapAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, TResult> selector)
    {
        if (resultTask is null)
        {
            throw new ArgumentNullException(nameof(resultTask));
        }
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        var result = await resultTask.ConfigureAwait(false);
        if (result is SuccessResult<TSource> success)
        {
            return Result.Success(selector(success.Value));
        }
        if (result is FailureResult<TSource> failure)
        {
            return new FailureResult<TResult>(failure.Reason, failure.ErrorCode, failure.SubErrorCode);
        }
        return Result.Fail<TResult>("Unexpected result");
    }

    /// <summary>
    /// Transforms the value if successful using an async function, otherwise forwards the error.
    /// </summary>
    /// <typeparam name="TSource">The source value type.</typeparam>
    /// <typeparam name="TResult">The target value type.</typeparam>
    /// <param name="resultTask">The result task to transform.</param>
    /// <param name="selector">The async transformation function.</param>
    public static async Task<Result<TResult>> MapAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, Task<TResult>> selector)
    {
        if (resultTask is null)
        {
            throw new ArgumentNullException(nameof(resultTask));
        }
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        var result = await resultTask.ConfigureAwait(false);
        if (result is SuccessResult<TSource> success)
        {
            return Result.Success(await selector(success.Value).ConfigureAwait(false));
        }
        if (result is FailureResult<TSource> failure)
        {
            return new FailureResult<TResult>(failure.Reason, failure.ErrorCode, failure.SubErrorCode);
        }
        return Result.Fail<TResult>("Unexpected result");
    }

    /// <summary>
    /// Chains an operation that returns a Result, flattening the result. Async version.
    /// </summary>
    /// <typeparam name="TSource">The source value type.</typeparam>
    /// <typeparam name="TResult">The target value type.</typeparam>
    /// <param name="resultTask">The result task to chain from.</param>
    /// <param name="selector">The operation that returns a Result.</param>
    public static async Task<Result<TResult>> BindAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, Result<TResult>> selector)
    {
        if (resultTask is null)
        {
            throw new ArgumentNullException(nameof(resultTask));
        }
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        var result = await resultTask.ConfigureAwait(false);
        if (result is SuccessResult<TSource> success)
        {
            return selector(success.Value);
        }
        if (result is FailureResult<TSource> failure)
        {
            return new FailureResult<TResult>(failure.Reason, failure.ErrorCode, failure.SubErrorCode);
        }
        return Result.Fail<TResult>("Unexpected result");
    }

    /// <summary>
    /// Chains an async operation that returns a Result, flattening the result.
    /// </summary>
    /// <typeparam name="TSource">The source value type.</typeparam>
    /// <typeparam name="TResult">The target value type.</typeparam>
    /// <param name="resultTask">The result task to chain from.</param>
    /// <param name="selector">The async operation that returns a Result.</param>
    public static async Task<Result<TResult>> BindAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, Task<Result<TResult>>> selector)
    {
        if (resultTask is null)
        {
            throw new ArgumentNullException(nameof(resultTask));
        }
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }
        var result = await resultTask.ConfigureAwait(false);

        if (result is SuccessResult<TSource> success)
        {
            return await selector(success.Value).ConfigureAwait(false);
        }
        if (result is FailureResult<TSource> failure)
        {
            return new FailureResult<TResult>(failure.Reason, failure.ErrorCode, failure.SubErrorCode);
        }
        return Result.Fail<TResult>("Unexpected result");
    }
}
