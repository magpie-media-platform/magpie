using System.Threading.Tasks;

namespace Magpie.Models;

/// <summary>
/// The completion state of the database work that the host runs at start.
/// </summary>
public sealed class DatabaseMigrationState
{
    private readonly TaskCompletionSource<object?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Whether the database work is complete.
    /// </summary>
    public bool IsComplete => _completion.Task.IsCompletedSuccessfully;

    /// <summary>
    /// Whether the database work failed and the host stops.
    /// </summary>
    public bool IsFailed { get; private set; }

    /// <summary>
    /// The task that completes when the database work is complete, and that stays pending when it failed.
    /// </summary>
    public Task Completion => _completion.Task;

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
}
