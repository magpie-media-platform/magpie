using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Core.Config;
using Magpie.Core.Observability;
using Magpie.Models;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Runtime.Scheduling;

internal sealed class MlfqJobConcurrencyScheduler : IJobConcurrencyScheduler
{
    private enum ReservationState
    {
        Active,
        Released,
    }

    private readonly record struct Config(
        int[] Widths,
        TimeSpan[] Quanta,
        int General,
        int FastLane,
        SemaphoreSlim FastLaneSemaphore);

    private readonly record struct TrackedJob(string JobType, int Level, DateTime AdmittedAt, DateTime LevelEnteredAt);

    private sealed class ReservedSlot(MlfqJobConcurrencyScheduler scheduler, int level) : IDisposable
    {
        private readonly StateManager<ReservationState> _state = new(ReservationState.Active);

        public void Dispose()
        {
            if (_state.TryTransition(ReservationState.Released, ReservationState.Active))
            {
                scheduler.ReleaseReservedSlot(level);
            }
        }
    }

    private sealed class Reservation(SemaphoreSlim? semaphore) : IDisposable
    {
        private readonly StateManager<ReservationState> _state = new(ReservationState.Active);

        public static Reservation Noop() => new(null);

        public void Dispose()
        {
            if (semaphore is null)
            {
                return;
            }

            if (_state.TryTransition(ReservationState.Released, ReservationState.Active))
            {
                semaphore.Release();
            }
        }
    }

    private const int LevelCount = 4;

    private const int MinimumTotalConcurrency = LevelCount + 1;

    private static readonly TimeSpan[] DefaultQuanta =
    [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan];

    private static readonly int[] TaperWeights = [8, 4, 2, 1];

    private readonly object _lock = new();
    private readonly Dictionary<Guid, TrackedJob> _jobs = [];
    private readonly Dictionary<string, Queue<JobClaimCandidate>> _pageByType = new(StringComparer.Ordinal);
    private readonly List<string> _typeOrder = [];
    private readonly int[] _occupancy = new int[LevelCount];
    private readonly TimeProvider _time;
    private readonly Config _config;

    private IReadOnlyList<JobClaimCandidate>? _pageSource;
    private int _pageSourceCount;
    private int _cursor;

    public MlfqJobConcurrencyScheduler(
        JobRuntimeOptions options,
        TimeProvider time,
        ILogger<MlfqJobConcurrencyScheduler> logger)
    {
        _time = time;
        _config = Create(options, logger);

        _ = JobsTelemetry.Meter.CreateObservableGauge(
            "magpie.jobs.mlfq_level_occupancy",
            ObserveLevelOccupancy);
        _ = JobsTelemetry.Meter.CreateObservableGauge(
            "magpie.jobs.mlfq_level_capacity",
            ObserveLevelCapacity);
        _ = JobsTelemetry.Meter.CreateObservableGauge(
            "magpie.jobs.fast_lane_in_use",
            ObserveFastLaneInUse);
    }

    public int Total => _config.General + _config.FastLane;

    public int GeneralCapacity => _config.General;

    public int FastLaneCapacity => _config.FastLane;

    public int InFlight
    {
        get
        {
            lock (_lock)
            {
                return _occupancy.Sum() + (_config.FastLane - _config.FastLaneSemaphore.CurrentCount);
            }
        }
    }

    private static int[] DeriveWidths(int total)
    {
        total = Math.Max(total, LevelCount);
        var weightSum = TaperWeights.Sum();
        var widths = new int[LevelCount];
        var assigned = 0;

        for (var level = 1; level < LevelCount; level++)
        {
            var width = Math.Max(1, total * TaperWeights[level] / weightSum);
            widths[level] = width;
            assigned += width;
        }

        widths[0] = Math.Max(1, total - assigned);
        return widths;
    }

    public bool TryReserveSlot(out IDisposable reservation)
    {
        lock (_lock)
        {
            for (var level = 0; level < _occupancy.Length; level++)
            {
                if (_occupancy[level] < _config.Widths[level])
                {
                    _occupancy[level]++;
                    reservation = new ReservedSlot(this, level);
                    return true;
                }
            }
        }

        reservation = Reservation.Noop();
        return false;
    }

    public bool HasAdmissionCapacity()
    {
        lock (_lock)
        {
            return _occupancy[0] < _config.Widths[0];
        }
    }

    public bool TrySelectAdmission(
        IReadOnlyList<JobClaimCandidate> candidates,
        ISet<Guid> excluded,
        out JobClaimCandidate selected)
    {
        selected = default;
        lock (_lock)
        {
            if (_occupancy[0] >= _config.Widths[0] || candidates.Count == 0)
            {
                return false;
            }

            GroupPage(candidates);

            for (var step = 0; step < _typeOrder.Count; step++)
            {
                var type = _typeOrder[(_cursor + step) % _typeOrder.Count];
                if (!_pageByType.TryGetValue(type, out var queue))
                {
                    continue;
                }

                while (queue.Count > 0 &&
                    (excluded.Contains(queue.Peek().Id) || _jobs.ContainsKey(queue.Peek().Id)))
                {
                    queue.Dequeue();
                }

                if (queue.Count > 0)
                {
                    selected = queue.Peek();
                    return true;
                }

                _pageByType.Remove(type);
            }

            return false;
        }
    }

    public void CommitAdmission(JobClaimCandidate candidate)
    {
        lock (_lock)
        {
            if (_jobs.ContainsKey(candidate.Id))
            {
                return;
            }

            var now = _time.GetUtcNow().UtcDateTime;
            _jobs[candidate.Id] = new TrackedJob(candidate.JobType, Level: 0, AdmittedAt: now, LevelEnteredAt: now);
            _occupancy[0]++;

            EnsureType(candidate.JobType);
            var typeIndex = _typeOrder.IndexOf(candidate.JobType);
            if (typeIndex >= 0)
            {
                _cursor = (typeIndex + 1) % _typeOrder.Count;
            }
        }
    }

    public void Release(Guid id)
    {
        lock (_lock)
        {
            if (_jobs.Remove(id, out var job))
            {
                _occupancy[job.Level]--;
            }
        }
    }

    public IReadOnlyList<Guid> Rebalance()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        lock (_lock)
        {
            List<Guid>? demoted = null;

            foreach (var (id, job) in _jobs.OrderBy(kvp => kvp.Value.AdmittedAt).ToList())
            {
                var level = job.Level;
                if (level >= LevelCount - 1)
                {
                    continue;
                }

                var quantum = _config.Quanta[level];
                if (quantum == Timeout.InfiniteTimeSpan)
                {
                    continue;
                }

                if (now - job.LevelEnteredAt < quantum)
                {
                    continue;
                }

                var target = level + 1;
                if (_occupancy[target] >= _config.Widths[target])
                {
                    continue;
                }

                _occupancy[level]--;
                _occupancy[target]++;
                _jobs[id] = job with { Level = target, LevelEnteredAt = now };
                JobsTelemetry.MlfqDemotionsTotal.Add(
                    1,
                    new KeyValuePair<string, object?>("job.type", job.JobType),
                    new KeyValuePair<string, object?>("from_level", level),
                    new KeyValuePair<string, object?>("to_level", target));
                (demoted ??= []).Add(id);
            }

            return demoted ?? [];
        }
    }

    public async Task<IDisposable> AcquireImmediateSlotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_config.FastLaneSemaphore.Wait(0, cancellationToken))
        {
            return new Reservation(_config.FastLaneSemaphore);
        }

        if (TryReserveSlot(out var reservation))
        {
            return reservation;
        }

        var waitStartedTimestamp = Stopwatch.GetTimestamp();
        await _config.FastLaneSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        JobsTelemetry.FastLaneWaitTime.Record(Stopwatch.GetElapsedTime(waitStartedTimestamp).TotalMilliseconds);
        return new Reservation(_config.FastLaneSemaphore);
    }

    private static Config Create(JobRuntimeOptions options, ILogger<MlfqJobConcurrencyScheduler> logger)
    {
        if (options.PoolSize < MinimumTotalConcurrency)
        {
            logger.LogWarning(
                "Configured max jobs {Configured} is below {Minimum}, raising to fit general and fast-lane pools",
                options.PoolSize,
                MinimumTotalConcurrency);
        }

        var total = Math.Max(MinimumTotalConcurrency, options.PoolSize);
        var fastLane = Math.Max(1, total / 8);
        var general = total - fastLane;
        var widths = DeriveWidths(general);
        var quanta = new TimeSpan[LevelCount];
        for (var level = 0; level < LevelCount; level++)
        {
            var overridden = options.MlfqQuanta is not null && level < options.MlfqQuanta.Length
                ? options.MlfqQuanta[level]
                : null;
            quanta[level] = overridden ?? DefaultQuanta[level];
        }

        return new Config(widths, quanta, general, fastLane, new SemaphoreSlim(fastLane, fastLane));
    }

    private void GroupPage(IReadOnlyList<JobClaimCandidate> candidates)
    {
        if (ReferenceEquals(_pageSource, candidates) && _pageSourceCount == candidates.Count)
        {
            return;
        }

        _pageByType.Clear();
        foreach (var candidate in candidates)
        {
            if (!_pageByType.TryGetValue(candidate.JobType, out var queue))
            {
                queue = new Queue<JobClaimCandidate>();
                _pageByType[candidate.JobType] = queue;
                EnsureType(candidate.JobType);
            }

            queue.Enqueue(candidate);
        }

        _pageSource = candidates;
        _pageSourceCount = candidates.Count;
    }

    private void EnsureType(string type)
    {
        if (!_typeOrder.Contains(type))
        {
            _typeOrder.Add(type);
        }
    }

    private void ReleaseReservedSlot(int level)
    {
        lock (_lock)
        {
            _occupancy[level]--;
        }
    }

    private IEnumerable<Measurement<int>> ObserveLevelOccupancy()
    {
        var snapshot = new int[LevelCount];
        lock (_lock)
        {
            _occupancy.CopyTo(snapshot, 0);
        }

        var measurements = new Measurement<int>[LevelCount];
        for (var level = 0; level < LevelCount; level++)
        {
            measurements[level] = new(snapshot[level], new KeyValuePair<string, object?>("level", level));
        }

        return measurements;
    }

    private IEnumerable<Measurement<int>> ObserveLevelCapacity()
    {
        var measurements = new Measurement<int>[LevelCount];
        for (var level = 0; level < LevelCount; level++)
        {
            measurements[level] = new(_config.Widths[level], new KeyValuePair<string, object?>("level", level));
        }

        return measurements;
    }

    private int ObserveFastLaneInUse()
    {
        return _config.FastLane - _config.FastLaneSemaphore.CurrentCount;
    }
}
