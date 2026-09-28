using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Cardigann.Engine.Session;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann.Engine.Session;

[TestFixture]
public sealed class SolverQueueTests
{
    [Test]
    public async Task EnterAsync_AdmitsInteractiveFirst_WhenBackgroundCallsAreQueued()
    {
        // Arrange
        var queue = new SolverQueue();
        await queue.EnterAsync(default).ConfigureAwait(false);
        var background = queue.EnterAsync(default);
        Task<bool> interactive;
        using (new SolverRequestScope(SolverRequestKind.Interactive))
        {
            interactive = queue.EnterAsync(default);
        }

        var laterBackground = queue.EnterAsync(default);

        // Act
        queue.Leave();
        var admitted = await interactive.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        var backgroundWaited = !background.IsCompleted;
        queue.Leave();
        var next = await background.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        var laterWaited = !laterBackground.IsCompleted;
        queue.Leave();
        var last = await laterBackground.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        queue.Leave();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admitted, Is.True);
            Assert.That(backgroundWaited, Is.True);
            Assert.That(next, Is.True);
            Assert.That(laterWaited, Is.True);
            Assert.That(last, Is.True);
            Assert.That(SolverRequestScope.Current, Is.EqualTo(SolverRequestKind.Background));
        }
    }
}
