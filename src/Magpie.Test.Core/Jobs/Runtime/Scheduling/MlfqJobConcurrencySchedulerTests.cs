using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Core.Config;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Jobs.Runtime.Scheduling;
using Magpie.Models;
using Magpie.Models.Enums;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime.Scheduling;

[TestFixture]
public sealed class MlfqJobConcurrencySchedulerTests
{
    private readonly IJobRegistry _registry = new JobRegistry([], new(), NullLogger<JobRegistry>.Instance);

    [Test]
    public void TrySelectAdmission_SkipsTheJobType_WhileItRunsTheMaxOfItsNodeConcurrencyLimit()
    {
        // Arrange
        var registry = new JobRegistry(
            [
                new(
                    new()
                    {
                        JobType = "Encode",
                        DisplayName = "Encode",
                        Category = "Test",
                        Scope = JobScope.System,
                        SupportsImmediate = false,
                        ConcurrencyLimit = new() { Max = 1, PerNode = true },
                    },
                    typeof(object))
            ],
            new(),
            NullLogger<JobRegistry>.Instance);
        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions { PoolSize = 15 },
            TimeProvider.System,
            registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);
        var first = new JobClaimCandidate(new("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d01"), "Encode", null);
        var second = new JobClaimCandidate(new("6a1f3a52-0d8e-4c55-9e1c-0b4c2b1f1d02"), "Encode", null);
        scheduler.CommitAdmission(first);

        // Act
        var selectedWhileRunning = scheduler.TrySelectAdmission([second], new HashSet<Guid>(), out _);
        scheduler.Release(first.Id);
        var selectedAfterRelease = scheduler.TrySelectAdmission([second], new HashSet<Guid>(), out var selected);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(selectedWhileRunning, Is.False);
            Assert.That(selectedAfterRelease, Is.True);
            Assert.That(selected, Is.EqualTo(second));
        }
    }

    [Test]
    public void TrySelectAdmission_AdmitsTheMinorityTypeInTheFirstTwoSlots_WhenTheBacklogIsLarger()
    {
        // Arrange
        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions { PoolSize = 15 },
            TimeProvider.System,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        var candidates = new List<JobClaimCandidate>();
        for (var i = 0; i < 50; i++)
        {
            candidates.Add(new JobClaimCandidate(Guid.NewGuid(), "ReleaseSearchItem", $"rs:{i}"));
        }

        var minority = new JobClaimCandidate(Guid.NewGuid(), "ContentMetadataRefreshItem", "rm:1");
        candidates.Add(minority);

        var admitted = new List<JobClaimCandidate>();
        var excluded = new HashSet<Guid>();

        // Act
        while (scheduler.HasAdmissionCapacity() && scheduler.TrySelectAdmission(candidates, excluded, out var selected))
        {
            scheduler.CommitAdmission(selected);
            admitted.Add(selected);
            candidates.RemoveAll(c => c.Id == selected.Id);
        }

        // Assert
        Assert.That(admitted.Take(2).Select(candidate => candidate.Id), Does.Contain(minority.Id));
    }

    [Test]
    public void Demotion_MovesExpiredJobToNextLevel_WhenDownstreamHasRoom()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions
            {
                PoolSize = 15,
                MlfqQuanta = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), null, null],
            },
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        var job = new JobClaimCandidate(Guid.NewGuid(), "ReleaseSearchItem", "rs:1");
        scheduler.CommitAdmission(job);

        // Act
        time.Advance(TimeSpan.FromMilliseconds(500));
        var none = scheduler.Rebalance();

        // Assert
        Assert.That(none, Is.Empty);

        time.Advance(TimeSpan.FromMilliseconds(1500));
        var demotedToL1 = scheduler.Rebalance();
        Assert.That(demotedToL1, Does.Contain(job.Id));
    }

    [Test]
    public void Demotion_ReArmsQuantumPerLevel_WhenWorkEntersTheNextLevel()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions
            {
                PoolSize = 15,
                MlfqQuanta = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), null, null],
            },
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        var job = new JobClaimCandidate(Guid.NewGuid(), "ReleaseSearchItem", "rs:1");
        scheduler.CommitAdmission(job);

        // Act
        time.Advance(TimeSpan.FromSeconds(2));

        // Assert
        Assert.That(scheduler.Rebalance(), Does.Contain(job.Id));

        Assert.That(scheduler.Rebalance(), Is.Empty);

        time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.That(scheduler.Rebalance(), Does.Contain(job.Id));
    }

    [Test]
    public void Rebalance_OrdersDemotionsByAdmissionTime_WhenAdmissionTimesDiffer()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions
            {
                PoolSize = 5,
                MlfqQuanta = [TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), TimeSpan.FromHours(1), null],
            },
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);
        var jobOld = new JobClaimCandidate(Guid.NewGuid(), "Old", "p:old");
        var jobYoung = new JobClaimCandidate(Guid.NewGuid(), "Young", "p:young");
        scheduler.CommitAdmission(jobOld);
        time.Advance(TimeSpan.FromSeconds(1));
        scheduler.CommitAdmission(jobYoung);
        time.Advance(TimeSpan.FromSeconds(2));

        // Act
        var demoted = scheduler.Rebalance();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(demoted, Does.Contain(jobOld.Id));
            Assert.That(demoted, Does.Not.Contain(jobYoung.Id));
        }
    }

    [Test]
    public void Demotion_HoldsCurrentSlot_WhenDownstreamLevelIsFull()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions
            {
                PoolSize = 17,
                MlfqQuanta = [TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), TimeSpan.FromHours(1), null],
            },
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        for (var i = 0; i < 8; i++)
        {
            scheduler.CommitAdmission(new JobClaimCandidate(Guid.NewGuid(), "ReleaseSearchItem", $"rs:{i}"));
        }

        time.Advance(TimeSpan.FromSeconds(2));

        // Act
        var demoted = scheduler.Rebalance();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.GeneralCapacity, Is.EqualTo(15));
            Assert.That(demoted, Has.Count.EqualTo(4));
            Assert.That(scheduler.InFlight, Is.EqualTo(8));
            Assert.That(scheduler.Rebalance(), Is.Empty);
        }
    }

    [Test]
    public void Demotion_StallsL0Admission_WhenL0FillsWithHeldUndemotableJobs()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions
            {
                PoolSize = 15,
                MlfqQuanta =
                [
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(1),
                    null,
                ],
            },
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        var candidates = new List<JobClaimCandidate>();
        for (var i = 0; i < 100; i++)
        {
            candidates.Add(new JobClaimCandidate(Guid.NewGuid(), "ReleaseSearchItem", $"rs:{i}"));
        }

        // Act
        for (var tick = 0; tick < 50 && scheduler.InFlight < scheduler.GeneralCapacity; tick++)
        {
            time.Advance(TimeSpan.FromMilliseconds(2));
            scheduler.Rebalance();
            var excluded = new HashSet<Guid>();
            while (scheduler.HasAdmissionCapacity() &&
                scheduler.TrySelectAdmission(candidates, excluded, out var selected))
            {
                scheduler.CommitAdmission(selected);
                candidates.RemoveAll(c => c.Id == selected.Id);
            }
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.InFlight, Is.EqualTo(scheduler.GeneralCapacity));
            Assert.That(scheduler.HasAdmissionCapacity(), Is.False);
        }
    }

    [Test]
    public void TrySelectAdmission_LimitsTotalInFlight_WhenTheConfiguredCeilingIsReached()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions
            {
                PoolSize = 15,
                MlfqQuanta =
                [
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(1),
                    null,
                ],
            },
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        var candidates = new List<JobClaimCandidate>();
        for (var i = 0; i < 100; i++)
        {
            candidates.Add(new JobClaimCandidate(Guid.NewGuid(), $"Type{i % 5}", $"p:{i}"));
        }

        var maxInFlight = 0;
        for (var tick = 0; tick < 50; tick++)
        {
            time.Advance(TimeSpan.FromMilliseconds(2));
            scheduler.Rebalance();

            var excluded = new HashSet<Guid>();
            while (scheduler.HasAdmissionCapacity() &&
                scheduler.TrySelectAdmission(candidates, excluded, out var selected))
            {
                scheduler.CommitAdmission(selected);
                candidates.RemoveAll(c => c.Id == selected.Id);
            }

            // Act
            maxInFlight = Math.Max(maxInFlight, scheduler.InFlight);
        }

        // Assert
        Assert.That(maxInFlight, Is.EqualTo(14));
    }

    [Test]
    public void Release_AllowsFurtherAdmission_WhenAnOccupiedSlotIsFreed()
    {
        // Arrange
        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions { PoolSize = 15 },
            TimeProvider.System,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        // Act
        var admitted = new List<JobClaimCandidate>();
        var candidates = new List<JobClaimCandidate>();
        for (var i = 0; i < 64; i++)
        {
            candidates.Add(new JobClaimCandidate(Guid.NewGuid(), "ReleaseSearchItem", $"rs:{i}"));
        }

        var excluded = new HashSet<Guid>();
        while (scheduler.HasAdmissionCapacity() && scheduler.TrySelectAdmission(candidates, excluded, out var selected))
        {
            scheduler.CommitAdmission(selected);
            admitted.Add(selected);
            candidates.RemoveAll(c => c.Id == selected.Id);
        }

        // Assert
        Assert.That(scheduler.HasAdmissionCapacity(), Is.False);

        scheduler.Release(admitted[0].Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.HasAdmissionCapacity(), Is.True);
            Assert.That(scheduler.InFlight, Is.EqualTo(admitted.Count - 1));
        }
    }

    [Test]
    public async Task AcquireImmediateSlotAsync_ReleasesAdmissionSlot_WhenTheBorrowedSlotIsDisposed()
    {
        // Arrange
        var scheduler = new MlfqJobConcurrencyScheduler(
            new JobRuntimeOptions { PoolSize = 5 },
            TimeProvider.System,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);
        var candidate = new JobClaimCandidate(Guid.NewGuid(), "ReleaseSearchItem", "rs:1");
        var fast = await scheduler.AcquireImmediateSlotAsync(default).ConfigureAwait(false);

        // Act
        var borrowed = await scheduler.AcquireImmediateSlotAsync(default)
            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.InFlight, Is.EqualTo(2));
            Assert.That(scheduler.HasAdmissionCapacity(), Is.False);
            Assert.That(scheduler.TrySelectAdmission([candidate], new HashSet<Guid>(), out _), Is.False);
        }

        borrowed.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.InFlight, Is.EqualTo(1));
            Assert.That(scheduler.HasAdmissionCapacity(), Is.True);
            Assert.That(scheduler.TrySelectAdmission([candidate], new HashSet<Guid>(), out _), Is.True);
        }

        fast.Dispose();
    }

    [Test]
    public async Task AcquireImmediateSlotAsync_UsesFastLane_WhenGeneralPoolIsSaturated()
    {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = new JobRuntimeOptions { PoolSize = 5 };
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);
        Assert.That(scheduler.FastLaneCapacity, Is.EqualTo(1));

        var target = 4;
        var candidates = Enumerable
            .Range(0, 256)
            .Select(index => new JobClaimCandidate(Guid.NewGuid(), $"TestJob{index % 4}", null))
            .ToList();
        var excluded = new HashSet<Guid>();
        var admitted = new List<Guid>();
        for (var tick = 0; tick < 64 && scheduler.InFlight < target; tick++)
        {
            while (scheduler.HasAdmissionCapacity() &&
                scheduler.TrySelectAdmission(candidates, excluded, out var selected))
            {
                scheduler.CommitAdmission(selected);
                admitted.Add(selected.Id);
                excluded.Add(selected.Id);
                candidates.RemoveAll(candidate => candidate.Id == selected.Id);
                Assert.That(scheduler.InFlight, Is.LessThanOrEqualTo(scheduler.Total));
            }

            time.Advance(TimeSpan.FromDays(1));
            scheduler.Rebalance();
            Assert.That(scheduler.InFlight, Is.LessThanOrEqualTo(scheduler.Total));
        }

        Assert.That(scheduler.InFlight, Is.EqualTo(4));

        // Act
        var immediate = await scheduler.AcquireImmediateSlotAsync(default)
            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.InFlight, Is.EqualTo(5));
        }

        immediate.Dispose();
        foreach (var id in admitted)
        {
            scheduler.Release(id);
        }

        Assert.That(scheduler.InFlight, Is.EqualTo(0));
    }

    [Test]
    public async Task AcquireImmediateSlotAsync_BorrowsAGeneralSlot_WhenTheFastLaneIsSaturated()
    {
        // Arrange
        var options = new JobRuntimeOptions { PoolSize = 5 };
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            TimeProvider.System,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);
        Assert.That(scheduler.FastLaneCapacity, Is.EqualTo(1));

        // Act
        var fast = await scheduler.AcquireImmediateSlotAsync(default).ConfigureAwait(false);
        var borrowed = await scheduler.AcquireImmediateSlotAsync(default)
            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.InFlight, Is.EqualTo(2));
            Assert.That(scheduler.HasAdmissionCapacity(), Is.False);
        }

        borrowed.Dispose();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.InFlight, Is.EqualTo(1));
            Assert.That(scheduler.HasAdmissionCapacity(), Is.True);
        }

        fast.Dispose();
        Assert.That(scheduler.InFlight, Is.EqualTo(0));
    }

    [Test]
    public async Task AcquireImmediateSlotAsync_WaitsForFastLane_WhenEverythingIsSaturated()
    {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = new JobRuntimeOptions { PoolSize = 5 };
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);
        Assert.That(scheduler.FastLaneCapacity, Is.EqualTo(1));

        var fast = await scheduler.AcquireImmediateSlotAsync(default).ConfigureAwait(false);
        var target = scheduler.GeneralCapacity + 1;
        var candidates = Enumerable
            .Range(0, 256)
            .Select(index => new JobClaimCandidate(Guid.NewGuid(), $"TestJob{index % 4}", null))
            .ToList();
        var excluded = new HashSet<Guid>();
        var admitted = new List<Guid>();
        for (var tick = 0; tick < 64 && scheduler.InFlight < target; tick++)
        {
            while (scheduler.HasAdmissionCapacity() &&
                scheduler.TrySelectAdmission(candidates, excluded, out var selected))
            {
                scheduler.CommitAdmission(selected);
                admitted.Add(selected.Id);
                excluded.Add(selected.Id);
                candidates.RemoveAll(candidate => candidate.Id == selected.Id);
                Assert.That(scheduler.InFlight, Is.LessThanOrEqualTo(scheduler.Total));
            }

            time.Advance(TimeSpan.FromDays(1));
            scheduler.Rebalance();
            Assert.That(scheduler.InFlight, Is.LessThanOrEqualTo(scheduler.Total));
        }

        Assert.That(scheduler.InFlight, Is.EqualTo(target));
        Assert.That(scheduler.InFlight, Is.EqualTo(scheduler.Total));

        // Act
        var waiting = scheduler.AcquireImmediateSlotAsync(default);

        // Assert
        Assert.That(waiting.IsCompleted, Is.False);

        fast.Dispose();
        var granted = await waiting.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.InFlight, Is.EqualTo(scheduler.Total));
            Assert.That(scheduler.Total, Is.EqualTo(5));
        }

        granted.Dispose();
        foreach (var id in admitted)
        {
            scheduler.Release(id);
        }

        Assert.That(scheduler.InFlight, Is.EqualTo(0));
    }

    [Test]
    public async Task InFlight_CountsGeneralFastLaneAndBorrowedImmediateWork_WhenAllLanesHaveWork()
    {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = new JobRuntimeOptions { PoolSize = 16 };
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        var firstFast = await scheduler.AcquireImmediateSlotAsync(default).ConfigureAwait(false);
        var secondFast = await scheduler.AcquireImmediateSlotAsync(default).ConfigureAwait(false);
        var borrowed = await scheduler.AcquireImmediateSlotAsync(default)
            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Act
        var target = scheduler.GeneralCapacity + 2;
        var candidates = Enumerable
            .Range(0, 256)
            .Select(index => new JobClaimCandidate(Guid.NewGuid(), $"TestJob{index % 4}", null))
            .ToList();
        var excluded = new HashSet<Guid>();
        var admitted = new List<Guid>();
        for (var tick = 0; tick < 64 && scheduler.InFlight < target; tick++)
        {
            while (scheduler.HasAdmissionCapacity() &&
                scheduler.TrySelectAdmission(candidates, excluded, out var selected))
            {
                scheduler.CommitAdmission(selected);
                admitted.Add(selected.Id);
                excluded.Add(selected.Id);
                candidates.RemoveAll(candidate => candidate.Id == selected.Id);
                Assert.That(scheduler.InFlight, Is.LessThanOrEqualTo(scheduler.Total));
            }

            time.Advance(TimeSpan.FromDays(1));
            scheduler.Rebalance();
            Assert.That(scheduler.InFlight, Is.LessThanOrEqualTo(scheduler.Total));
        }

        Assert.That(scheduler.InFlight, Is.EqualTo(target));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admitted, Has.Count.EqualTo(scheduler.GeneralCapacity - 1));
            Assert.That(scheduler.InFlight, Is.EqualTo(scheduler.Total));
            Assert.That(scheduler.InFlight, Is.LessThanOrEqualTo(scheduler.Total));
        }

        borrowed.Dispose();
        firstFast.Dispose();
        secondFast.Dispose();
        foreach (var id in admitted)
        {
            scheduler.Release(id);
        }

        Assert.That(scheduler.InFlight, Is.EqualTo(0));
    }

    [Test]
    public void Admission_NeverDispatchesBeyondTheConfiguredCeiling_WhenDemandExceedsCapacity()
    {
        // Arrange
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = new JobRuntimeOptions { PoolSize = 8 };
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            time,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        var candidates = Enumerable
            .Range(0, 512)
            .Select(index => new JobClaimCandidate(Guid.NewGuid(), $"TestJob{index % 4}", null))
            .ToList();
        var excluded = new HashSet<Guid>();

        // Act
        for (var tick = 0; tick < 128; tick++)
        {
            while (scheduler.HasAdmissionCapacity() &&
                scheduler.TrySelectAdmission(candidates, excluded, out var selected))
            {
                scheduler.CommitAdmission(selected);
                excluded.Add(selected.Id);
                Assert.That(scheduler.InFlight, Is.LessThanOrEqualTo(scheduler.GeneralCapacity));
            }

            time.Advance(TimeSpan.FromDays(1));
            scheduler.Rebalance();
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.InFlight, Is.EqualTo(scheduler.GeneralCapacity));
            Assert.That(scheduler.HasAdmissionCapacity(), Is.False);
            Assert.That(
                scheduler.TrySelectAdmission(candidates, excluded, out _),
                Is.False,
                "Admission must refuse new work once the general pool is saturated.");
        }
    }

    [TestCase(16, 2, 14)]
    [TestCase(15, 1, 14)]
    [TestCase(30, 3, 27)]
    public void Constructor_SplitsCeilingIntoGeneralPoolAndFastLaneFloor_WhenCeilingIsSet(
        int poolSize,
        int fastLane,
        int general)
    {
        // Arrange
        var options = new JobRuntimeOptions { PoolSize = poolSize };

        // Act
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            TimeProvider.System,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.Total, Is.EqualTo(poolSize));
            Assert.That(scheduler.FastLaneCapacity, Is.EqualTo(fastLane));
            Assert.That(scheduler.GeneralCapacity, Is.EqualTo(general));
        }
    }

    [Test]
    public void Constructor_RaisesToMinimum_WhenConfiguredBelowFloor()
    {
        // Arrange
        var options = new JobRuntimeOptions { PoolSize = 2 };

        // Act
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            TimeProvider.System,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.Total, Is.EqualTo(5));
            Assert.That(scheduler.FastLaneCapacity, Is.EqualTo(1));
            Assert.That(scheduler.GeneralCapacity, Is.EqualTo(4));
        }
    }

    [Test]
    public void Constructor_UsesOptionsDefaultCeiling_WhenCeilingIsNotSpecified()
    {
        // Arrange
        var options = new JobRuntimeOptions();

        // Act
        var scheduler = new MlfqJobConcurrencyScheduler(
            options,
            TimeProvider.System,
            _registry,
            NullLogger<MlfqJobConcurrencyScheduler>.Instance);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.Total, Is.EqualTo(50));
            Assert.That(scheduler.FastLaneCapacity, Is.EqualTo(6));
            Assert.That(scheduler.GeneralCapacity, Is.EqualTo(44));
        }
    }
}
