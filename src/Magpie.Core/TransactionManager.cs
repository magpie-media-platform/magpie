using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Core.Observability;
using Magpie.Data;

using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class TransactionManager(
    MagpieDbContext context,
    ILogger<TransactionManager> logger) : ITransactionManager
{
    private readonly List<Func<CancellationToken, Task>> _postCommitActions = [];

    public async Task ExecuteWithTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transactionHandle = transaction.ConfigureAwait(false);

        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _postCommitActions.Clear();
            await RollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            RecordOutcome(started, committed: false);
            throw;
        }

        RecordOutcome(started, committed: true);
        await DrainPostCommitActionsAsync().ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteWithTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        var started = Stopwatch.GetTimestamp();
        var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transactionHandle = transaction.ConfigureAwait(false);

        TResult result;
        try
        {
            result = await operation(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _postCommitActions.Clear();
            await RollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            RecordOutcome(started, committed: false);
            throw;
        }

        RecordOutcome(started, committed: true);
        await DrainPostCommitActionsAsync().ConfigureAwait(false);
        return result;
    }

    public async Task OnCommittedAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            await action(cancellationToken).ConfigureAwait(false);
            return;
        }

        _postCommitActions.Add(action);
    }

    private static void RecordOutcome(long started, bool committed)
    {
        DataTelemetry.TransactionOutcome.Add(
            1,
            new KeyValuePair<string, object?>("outcome", committed ? "committed" : "rolled_back"));
        DataTelemetry.TransactionDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private async Task DrainPostCommitActionsAsync()
    {
        if (_postCommitActions.Count == 0)
        {
            return;
        }

        var actions = _postCommitActions.ToArray();
        _postCommitActions.Clear();

        foreach (var action in actions)
        {
            try
            {
                await action(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The after-commit action failed after a committed transaction.");
            }
        }
    }

    private async Task RollbackAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The transaction manager failed to roll back after an operation failure.");
        }
    }
}
