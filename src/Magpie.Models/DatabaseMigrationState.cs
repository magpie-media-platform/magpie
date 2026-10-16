using System.Collections.Generic;
using System.Threading.Tasks;

namespace Magpie.Models;

/// <summary>
/// The database migration state.
/// </summary>
public sealed class DatabaseMigrationState
{
    private readonly TaskCompletionSource<object?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<object?> _migrated =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile IReadOnlyCollection<string>? _unknownMigrations;

    /// <summary>
    /// Whether the database work is complete.
    /// </summary>
    public bool IsComplete => _completion.Task.IsCompletedSuccessfully;

    /// <summary>
    /// Whether the database work failed.
    /// </summary>
    public bool IsFailed { get; private set; }

    /// <summary>
    /// The task that completes when the database work is done.
    /// </summary>
    public Task Completion => _completion.Task;

    /// <summary>
    /// The pending migrations.
    /// </summary>
    public IReadOnlyCollection<string> PendingMigrations { get; private set; } = [];

    /// <summary>
    /// The task that completes when the migration is done.
    /// </summary>
    public Task Migrated => _migrated.Task;

    /// <summary>
    /// Whether the schema is known.
    /// </summary>
    public bool IsSchemaKnown => _unknownMigrations is { Count: 0 };

    /// <summary>
    /// The unknown migrations.
    /// </summary>
    public IReadOnlyCollection<string> UnknownMigrations => _unknownMigrations ?? [];

    /// <summary>
    /// Records the pending migrations.
    /// </summary>
    /// <param name="pendingMigrations">The pending migrations</param>
    public void RecordPendingMigrations(IReadOnlyCollection<string> pendingMigrations)
    {
        PendingMigrations = pendingMigrations;
    }

    /// <summary>
    /// Records that the database work failed.
    /// </summary>
    public void Fail()
    {
        IsFailed = true;
    }

    /// <summary>
    /// Records that the database work is complete.
    /// </summary>
    public void Complete()
    {
        _completion.TrySetResult(null);
    }

    /// <summary>
    /// Records that the migration is done.
    /// </summary>
    public void RecordMigrated()
    {
        _migrated.TrySetResult(null);
    }

    /// <summary>
    /// Records the result of a schema check.
    /// </summary>
    /// <param name="unknownMigrations">The unknown migrations</param>
    public void RecordSchemaCheck(IReadOnlyCollection<string> unknownMigrations)
    {
        _unknownMigrations = unknownMigrations;
    }
}
