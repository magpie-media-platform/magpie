using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions;

/// <summary>
/// The service contract for database transactions.
/// </summary>
public interface ITransactionManager
{
    /// <summary>
    /// Runs an operation in one transaction.
    /// </summary>
    /// <param name="operation">The work to run in the transaction</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task ExecuteWithTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs an operation in one transaction.
    /// </summary>
    /// <typeparam name="TResult">The type that the operation produces.</typeparam>
    /// <param name="operation">The work to run in the transaction</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The result of the operation</returns>
    Task<TResult> ExecuteWithTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// If a transaction is open, runs an action after it commits, otherwise runs the action now.
    /// </summary>
    /// <param name="action">The work to run after the commit</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task OnCommittedAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);
}
