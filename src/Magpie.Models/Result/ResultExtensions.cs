using System;
using System.Threading.Tasks;

namespace Magpie.Models.Result;

/// <summary>
/// Monadic extension methods for Result types.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Represents a result that contains two zipped values.
    /// </summary>
    /// <typeparam name="T1">The type of the first value.</typeparam>
    /// <typeparam name="T2">The type of the second value.</typeparam>
    /// <param name="First">The first value.</param>
    /// <param name="Second">The second value.</param>
    public record struct ZipResult<T1, T2>(T1 First, T2 Second);

    /// <summary>
    /// Transforms the value if successful, otherwise forwards the error.
    /// </summary>
    public static Result<TResult> Map<TSource, TResult>(
        this Result<TSource> result,
        Func<TSource, TResult> selector)
    {
        return result switch
        {
            SuccessResult<TSource> success => Result.Success(selector(success.Value)),
            FailureResult<TSource> failure => failure.As<TResult>(),
            _ => Result.Fail<TResult>("Unexpected result")
        };
    }

    /// <summary>
    /// Transforms a zipped result using a 2-arg function.
    /// </summary>
    public static Result<TResult> Map<T1, T2, TResult>(
        this Result<ZipResult<T1, T2>> result,
        Func<T1, T2, TResult> selector)
    {
        return result.Map(t => selector(t.First, t.Second));
    }

    /// <summary>
    /// Transforms the value if successful, otherwise forwards the error. Async version.
    /// </summary>
    public static async Task<Result<TResult>> MapAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, TResult> selector)
    {
        return await resultTask.ConfigureAwait(false) switch
        {
            SuccessResult<TSource> success => Result.Success(selector(success.Value)),
            FailureResult<TSource> failure => failure.As<TResult>(),
            _ => Result.Fail<TResult>("Unexpected result")
        };
    }

    /// <summary>
    /// Transforms the value if successful using an async function, otherwise forwards the error.
    /// </summary>
    public static async Task<Result<TResult>> MapAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, Task<TResult>> selector)
    {
        return await resultTask.ConfigureAwait(false) switch
        {
            SuccessResult<TSource> success => Result.Success(await selector(success.Value).ConfigureAwait(false)),
            FailureResult<TSource> failure => failure.As<TResult>(),
            _ => Result.Fail<TResult>("Unexpected result")
        };
    }

    /// <summary>
    /// Transforms a zipped result using a 2-arg function. Async version.
    /// </summary>
    public static Task<Result<TResult>> MapAsync<T1, T2, TResult>(
        this Task<Result<ZipResult<T1, T2>>> resultTask,
        Func<T1, T2, TResult> selector)
    {
        return resultTask.MapAsync(t => selector(t.First, t.Second));
    }

    /// <summary>
    /// Transforms the value if successful, catching exceptions as failures.
    /// </summary>
    public static Result<TResult> TryMap<TSource, TResult>(
        this Result<TSource> result,
        Func<TSource, TResult> selector)
    {
        if (result is not SuccessResult<TSource> success)
        {
            return result is FailureResult<TSource> failure
                ? failure.As<TResult>()
                : Result.Fail<TResult>("Unexpected result");
        }

        try
        {
            return Result.Success(selector(success.Value));
        }
        catch (Exception ex)
        {
            return Result.Fail<TResult>($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Transforms the value if successful, catching exceptions as failures. Async version.
    /// </summary>
    public static async Task<Result<TResult>> TryMapAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, TResult> selector)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.TryMap(selector);
    }

    /// <summary>
    /// Transforms the value if successful using an async function, catching exceptions as failures.
    /// </summary>
    public static async Task<Result<TResult>> TryMapAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, Task<TResult>> selector)
    {
        var result = await resultTask.ConfigureAwait(false);
        if (result is not SuccessResult<TSource> success)
        {
            return result is FailureResult<TSource> failure
                ? failure.As<TResult>()
                : Result.Fail<TResult>("Unexpected result");
        }

        try
        {
            return Result.Success(await selector(success.Value).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return Result.Fail<TResult>($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Chains an operation that returns a Result, flattening the result.
    /// </summary>
    public static Result<TResult> Bind<TSource, TResult>(
        this Result<TSource> result,
        Func<TSource, Result<TResult>> selector)
    {
        return result switch
        {
            SuccessResult<TSource> success => selector(success.Value),
            FailureResult<TSource> failure => failure.As<TResult>(),
            _ => Result.Fail<TResult>("Unexpected result")
        };
    }

    /// <summary>
    /// Binds a zipped result using a 2-arg function.
    /// </summary>
    public static Result<TResult> Bind<T1, T2, TResult>(
        this Result<ZipResult<T1, T2>> result,
        Func<T1, T2, Result<TResult>> selector)
    {
        return result.Bind(t => selector(t.First, t.Second));
    }

    /// <summary>
    /// Chains an operation that returns a Result, flattening the result. Async version.
    /// </summary>
    public static async Task<Result<TResult>> BindAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, Result<TResult>> selector)
    {
        return await resultTask.ConfigureAwait(false) switch
        {
            SuccessResult<TSource> success => selector(success.Value),
            FailureResult<TSource> failure => failure.As<TResult>(),
            _ => Result.Fail<TResult>("Unexpected result")
        };
    }

    /// <summary>
    /// Chains an async operation that returns a Result, flattening the result.
    /// </summary>
    public static async Task<Result<TResult>> BindAsync<TSource, TResult>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, Task<Result<TResult>>> selector)
    {
        return await resultTask.ConfigureAwait(false) switch
        {
            SuccessResult<TSource> success => await selector(success.Value).ConfigureAwait(false),
            FailureResult<TSource> failure => failure.As<TResult>(),
            _ => Result.Fail<TResult>("Unexpected result")
        };
    }

    /// <summary>
    /// Binds a zipped result using a 2-arg async function.
    /// </summary>
    public static Task<Result<TResult>> BindAsync<T1, T2, TResult>(
        this Task<Result<ZipResult<T1, T2>>> resultTask,
        Func<T1, T2, Task<Result<TResult>>> selector)
    {
        return resultTask.BindAsync(t => selector(t.First, t.Second));
    }

    /// <summary>
    /// Combines two results into a zipped result. Fails if either fails.
    /// </summary>
    public static Result<ZipResult<T1, T2>> Zip<T1, T2>(this Result<T1> first, Result<T2> second)
    {
        return (first, second) switch
        {
            (SuccessResult<T1> s1, SuccessResult<T2> s2) => Result.Success(new ZipResult<T1, T2>(s1.Value, s2.Value)),
            (FailureResult<T1> f, _) => f.As<ZipResult<T1, T2>>(),
            (_, FailureResult<T2> f) => f.As<ZipResult<T1, T2>>(),
            _ => Result.Fail<ZipResult<T1, T2>>("Unexpected result")
        };
    }

    /// <summary>
    /// Combines two results and applies a selector. Fails if either fails.
    /// </summary>
    public static Result<TResult> Zip<T1, T2, TResult>(
        this Result<T1> first,
        Result<T2> second,
        Func<T1, T2, TResult> selector)
    {
        return (first, second) switch
        {
            (SuccessResult<T1> s1, SuccessResult<T2> s2) => Result.Success(selector(s1.Value, s2.Value)),
            (FailureResult<T1> f, _) => f.As<TResult>(),
            (_, FailureResult<T2> f) => f.As<TResult>(),
            _ => Result.Fail<TResult>("Unexpected result")
        };
    }

    /// <summary>
    /// Combines two async results into a zipped result. Fails if either fails.
    /// </summary>
    public static async Task<Result<ZipResult<T1, T2>>> ZipAsync<T1, T2>(
        this Task<Result<T1>> firstTask,
        Task<Result<T2>> secondTask)
    {
        var (first, second) = (await firstTask.ConfigureAwait(false), await secondTask.ConfigureAwait(false));
        return first.Zip(second);
    }

    /// <summary>
    /// Combines two async results and applies a selector. Fails if either fails.
    /// </summary>
    public static async Task<Result<TResult>> ZipAsync<T1, T2, TResult>(
        this Task<Result<T1>> firstTask,
        Task<Result<T2>> secondTask,
        Func<T1, T2, TResult> selector)
    {
        var (first, second) = (await firstTask.ConfigureAwait(false), await secondTask.ConfigureAwait(false));
        return first.Zip(second, selector);
    }

    /// <summary>
    /// Extracts the value from a successful result, or throws if failed.
    /// </summary>
    public static T Unwrap<T>(this Result<T> result)
    {
        return result switch
        {
            SuccessResult<T> success => success.Value,
            FailureResult<T> failure => throw new InvalidOperationException(failure.Reason),
            _ => throw new InvalidOperationException("Unexpected result")
        };
    }

    /// <summary>
    /// Extracts the value from a successful result, or throws if failed. Async version.
    /// </summary>
    public static async Task<T> UnwrapAsync<T>(this Task<Result<T>> resultTask)
    {
        return await resultTask.ConfigureAwait(false) switch
        {
            SuccessResult<T> success => success.Value,
            FailureResult<T> failure => throw new InvalidOperationException(failure.Reason),
            _ => throw new InvalidOperationException("Unexpected result")
        };
    }
}
