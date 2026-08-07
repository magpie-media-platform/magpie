using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed class QueryExecution(string fingerprint)
{
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly List<Stage> _stages = [];
    private readonly List<SparqlTraceRecord> _sparqlTraceRecords = [];

    public string Fingerprint { get; } = fingerprint;

    public IReadOnlyList<Stage> Stages => _stages;

    public int Requests => _stages.Sum(value => value.HttpRequests);

    public int LogicalInvocations => _stages.Sum(value => value.Invocations);

    public int PrunedStages => _stages.Count(value => value.Status == "SKIPPED" || value.SkippedInvocations > 0);

    public int AvoidedLogicalInvocations => _stages.Sum(value => value.AvoidedLogicalInvocations);

    public int RowsFetched { get; private set; }

    public int ReturnedCount { get; private set; }

    public bool HasMore { get; private set; }

    public int WarningCount { get; private set; }

    public bool BoundHit { get; private set; }

    public string? TerminationReason { get; private set; }

    public long ElapsedMilliseconds { get; private set; }

    public bool IsFrozen { get; private set; }

    public StageInvocation Start(string name, string surface)
    {
        lock (_gate)
        {
            if (IsFrozen)
            {
                return new StageInvocation(this, name, ordinal: 0, active: false);
            }

            var stage = GetOrAdd(name, surface);
            stage.Status = "RAN";
            stage.Invocations++;
            return new StageInvocation(this, name, stage.Invocations, active: true);
        }
    }

    public void Skip(string name, string surface, string reason, int avoidedLogicalInvocations = 0)
    {
        Mutate(
            name,
            surface,
            stage =>
            {
                if (stage.Status != "RAN")
                {
                    stage.Status = "SKIPPED";
                }

                stage.SkippedInvocations++;
                stage.AvoidedLogicalInvocations += avoidedLogicalInvocations;
                AddReason(stage, reason);
            });
    }

    public void AddFetchedRows(int count)
    {
        lock (_gate)
        {
            if (!IsFrozen)
            {
                RowsFetched += count;
            }
        }
    }

    public void RecordReason(string name, string surface, string reason)
    {
        Mutate(name, surface, stage => AddReason(stage, reason));
    }

    public bool HasRun(string name)
    {
        lock (_gate)
        {
            return _stages.Any(value => string.Equals(value.Name, name, StringComparison.Ordinal) &&
                value.Status == "RAN");
        }
    }

    internal IReadOnlyList<SparqlTraceRecord> DrainSparqlTraceRecords()
    {
        lock (_gate)
        {
            var records = _sparqlTraceRecords.ToArray();
            _sparqlTraceRecords.Clear();
            return records;
        }
    }

    public void Freeze(
        int returnedCount,
        bool hasMore,
        bool boundHit,
        string terminationReason,
        int warningCount)
    {
        lock (_gate)
        {
            if (IsFrozen)
            {
                return;
            }

            ReturnedCount = returnedCount;
            HasMore = hasMore;
            BoundHit = boundHit;
            WarningCount = warningCount;
            TerminationReason = terminationReason;
            ElapsedMilliseconds = _stopwatch.ElapsedMilliseconds;
            IsFrozen = true;
        }
    }

    private static void AddReason(Stage stage, string reason)
    {
        if (!stage.Reasons.Contains(reason, StringComparer.Ordinal))
        {
            stage.Reasons.Add(reason);
        }
    }

    private Stage GetOrAdd(string name, string surface)
    {
        var stage = _stages.FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.Ordinal));
        if (stage is not null)
        {
            return stage;
        }

        stage = new Stage(name, surface);
        _stages.Add(stage);
        return stage;
    }

    private void Mutate(string name, string surface, Action<Stage> mutation)
    {
        lock (_gate)
        {
            if (!IsFrozen)
            {
                mutation(GetOrAdd(name, surface));
            }
        }
    }

    private void RecordSparqlRequest(
        string name,
        int ordinal,
        string queryText,
        bool captureTrace)
    {
        lock (_gate)
        {
            if (IsFrozen)
            {
                return;
            }

            GetOrAdd(name, "WDQS").HttpRequests++;
            if (captureTrace)
            {
                _sparqlTraceRecords.Add(new SparqlTraceRecord(name, ordinal, queryText));
            }
        }
    }

    internal sealed class StageInvocation(QueryExecution execution, string name, int ordinal, bool active) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private bool _disposed;

        public string Name { get; } = name;

        public int Ordinal { get; } = ordinal;

        public void RecordHttpRequest() => Mutate(stage => stage.HttpRequests++);

        public void RecordSparqlRequest(string queryText, bool captureTrace)
        {
            if (!active || _disposed)
            {
                return;
            }

            execution.RecordSparqlRequest(Name, Ordinal, queryText, captureTrace);
        }

        public void RecordCacheProvenance(SearchCacheProvenance provenance)
        {
            Mutate(stage =>
            {
                if (provenance == SearchCacheProvenance.Remote)
                {
                    stage.RemoteCacheFills++;
                }
                else if (provenance == SearchCacheProvenance.Cache)
                {
                    stage.CacheHits++;
                }
                else
                {
                    stage.InFlightJoins++;
                }
            });
        }

        public void RecordWindow() => Mutate(stage => stage.WindowCount++);

        public void RecordRowsProduced(int rows) => Mutate(stage => stage.RowsProduced += rows);

        public void RecordIdsProduced(int ids) => Mutate(stage => stage.IdsProduced += ids);

        public void RecordRows(int rowsIn, int rowsOut)
        {
            Mutate(stage =>
            {
                stage.RowsIn += rowsIn;
                stage.RowsOut += rowsOut;
            });
        }

        public void RecordSentinelsDropped(int count) => Mutate(stage => stage.SentinelsDropped += count);

        public void RecordLookaheadRows(int count) => Mutate(stage => stage.LookaheadRows += count);

        public void RecordDuplicatesDropped(int count) => Mutate(stage => stage.DuplicatesDropped += count);

        public void RecordOffsetRowsDropped(int count) => Mutate(stage => stage.OffsetRowsDropped += count);

        public void RecordRejection(string predicate, int count)
        {
            if (count <= 0)
            {
                return;
            }

            Mutate(stage =>
            {
                stage.Rejections.TryGetValue(predicate, out var previous);
                stage.Rejections[predicate] = previous + count;
            });
        }

        public void RecordReason(string reason) => Mutate(stage => AddReason(stage, reason));

        public void Dispose()
        {
            if (_disposed || !active)
            {
                return;
            }

            var elapsed = (long)((Stopwatch.GetTimestamp() - _started) * 1000d / Stopwatch.Frequency);
            Mutate(stage => stage.ElapsedMilliseconds += elapsed);
            _disposed = true;
        }

        private void Mutate(Action<Stage> mutation)
        {
            if (!active || _disposed)
            {
                return;
            }

            execution.Mutate(Name, "unknown", mutation);
        }
    }

    internal sealed class Stage(string name, string surface)
    {
        public string Name { get; } = name;

        public string Surface { get; } = surface;

        public string Status { get; set; } = "SKIPPED";

        public int Invocations { get; set; }

        public int SkippedInvocations { get; set; }

        public int AvoidedLogicalInvocations { get; set; }

        public int HttpRequests { get; set; }

        public int RemoteCacheFills { get; set; }

        public int CacheHits { get; set; }

        public int InFlightJoins { get; set; }

        public int WindowCount { get; set; }

        public int RowsProduced { get; set; }

        public int IdsProduced { get; set; }

        public int RowsIn { get; set; }

        public int RowsOut { get; set; }

        public int SentinelsDropped { get; set; }

        public int LookaheadRows { get; set; }

        public int DuplicatesDropped { get; set; }

        public int OffsetRowsDropped { get; set; }

        public Dictionary<string, int> Rejections { get; } = new(StringComparer.Ordinal);

        public List<string> Reasons { get; } = [];

        public long ElapsedMilliseconds { get; set; }
    }

    internal sealed record SparqlTraceRecord(string Stage, int Invocation, string QueryText);
}
