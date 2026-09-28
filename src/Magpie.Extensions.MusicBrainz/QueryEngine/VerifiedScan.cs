using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.MusicBrainz.Mapping;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed class VerifiedScan
{
    private readonly object _gate = new();
    private readonly List<SearchHit> _hits = [];
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<SearchHit> _pending = [];
    private readonly HashSet<string> _warnings = [];

    internal VerifiedScan(int offset = 0)
    {
        NextOffset = offset;
    }

    private int NextOffset { get; set; }

    private bool SourceExhausted { get; set; }

    private bool Truncated { get; set; }

    internal (
        int HitCount,
        int PendingCount,
        int NextOffset,
        bool SourceExhausted,
        bool Exhausted,
        bool Truncated,
        IReadOnlyList<string> Warnings) Read()
    {
        lock (_gate)
        {
            return (
                _hits.Count,
                _pending.Count,
                NextOffset,
                SourceExhausted,
                SourceExhausted && _pending.Count == 0,
                Truncated,
                _warnings.ToArray());
        }
    }

    internal SearchHit HitAt(int index)
    {
        lock (_gate)
        {
            return _hits[index];
        }
    }

    internal void AppendAccepted(IEnumerable<SearchHit> hits, int nextOffset, bool sourceExhausted, bool truncated)
    {
        lock (_gate)
        {
            foreach (var hit in hits)
            {
                if (_seen.Add(hit.PrimaryMbid))
                {
                    _hits.Add(hit);
                }
            }

            NextOffset = nextOffset;
            SourceExhausted = sourceExhausted;
            Truncated |= truncated;
        }
    }

    internal void Enqueue(IEnumerable<SearchHit> hits, int nextOffset, bool sourceExhausted, bool truncated)
    {
        lock (_gate)
        {
            foreach (var hit in hits)
            {
                if (_seen.Add(hit.PrimaryMbid))
                {
                    _pending.Enqueue(hit);
                }
            }

            NextOffset = nextOffset;
            SourceExhausted = sourceExhausted;
            Truncated |= truncated;
        }
    }

    internal bool TryPeekPending(out SearchHit? hit)
    {
        lock (_gate)
        {
            hit = _pending.Count > 0 ? _pending.Peek() : null;
            return hit is not null;
        }
    }

    internal void CompletePending(
        bool accepted,
        SearchHit? acceptedHit,
        IEnumerable<string> warnings)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                throw new InvalidOperationException("No pending MusicBrainz candidate exists.");
            }

            var hit = _pending.Dequeue();
            if (accepted)
            {
                _hits.Add(acceptedHit ?? hit);
            }

            foreach (var warning in warnings)
            {
                _warnings.Add(warning);
            }
        }
    }

    internal void AddWarnings(IEnumerable<string> warnings)
    {
        lock (_gate)
        {
            foreach (var warning in warnings)
            {
                _warnings.Add(warning);
            }
        }
    }
}
