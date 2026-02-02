using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions;

/// <summary>
/// Manages operations within transactions.
/// </summary>
public interface ITransactionManager
{
    /// <summary>
    /// Executes an operation using a transaction.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    Task ExecuteWithTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an operation using a transaction.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    Task<TResult> ExecuteWithTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
