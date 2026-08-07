using System;
using System.Collections.Generic;
using System.Linq;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class ExecutionTrace
{
    private readonly object _gate = new();
    private readonly List<RequestTrace> _requests = [];
    private readonly Dictionary<int, PlanNodeOutcome> _nodes = [];
    private readonly Dictionary<int, Dictionary<string, int>> _rejections = [];

    internal int DetailFanOut { get; private set; }

    internal bool? HasMore
    {
        get
        {
            lock (_gate)
            {
                return field;
            }
        }

        private set;
    }

    internal int BeginRequest(int? planNodeId, string role, string target)
    {
        lock (_gate)
        {
            var id = _requests.Count;
            _requests.Add(new RequestTrace
            {
                Id = id,
                PlanNodeId = planNodeId,
                Role = role,
                Target = target,
                Status = "running"
            });
            return id;
        }
    }

    internal void CompleteRequest(int id, string status, int? rowCount, int payloadBytes, long elapsedMilliseconds)
    {
        lock (_gate)
        {
            _requests[id] = _requests[id] with
            {
                Status = status,
                RowCount = rowCount,
                PayloadBytes = payloadBytes,
                ElapsedMilliseconds = elapsedMilliseconds
            };
        }
    }

    internal void MarkNode(int nodeId, string status, string? reason = null, int rowsIn = 0, int rowsOut = 0)
    {
        lock (_gate)
        {
            _nodes[nodeId] = new PlanNodeOutcome
            {
                NodeId = nodeId,
                Status = status,
                Reason = reason,
                RowsIn = rowsIn,
                RowsOut = rowsOut,
                Rejections = _rejections.TryGetValue(nodeId, out var rejections)
                    ? new Dictionary<string, int>(rejections, StringComparer.Ordinal)
                    : new Dictionary<string, int>(StringComparer.Ordinal)
            };
        }
    }

    internal bool TryMarkNodeSkipped(int nodeId, string reason)
    {
        lock (_gate)
        {
            if (_nodes.ContainsKey(nodeId))
            {
                return false;
            }

            _nodes[nodeId] = new PlanNodeOutcome
            {
                NodeId = nodeId,
                Status = "skipped",
                Reason = reason,
                Rejections = new Dictionary<string, int>(StringComparer.Ordinal)
            };
            return true;
        }
    }

    internal void Reject(int nodeId, string predicate)
    {
        lock (_gate)
        {
            if (!_rejections.TryGetValue(nodeId, out var rejections))
            {
                rejections = new Dictionary<string, int>(StringComparer.Ordinal);
                _rejections[nodeId] = rejections;
            }

            rejections[predicate] = rejections.GetValueOrDefault(predicate) + 1;
        }
    }

    internal void AddDetailFanOut(int count)
    {
        lock (_gate)
        {
            DetailFanOut += count;
        }
    }

    internal void CompleteExecution(bool hasMore)
    {
        lock (_gate)
        {
            HasMore = hasMore;
        }
    }

    internal IReadOnlyList<RequestTrace> Requests()
    {
        lock (_gate)
        {
            return [.. _requests];
        }
    }

    internal IReadOnlyList<PlanNodeOutcome> Nodes()
    {
        lock (_gate)
        {
            return [.. _nodes.Values.OrderBy(node => node.NodeId)];
        }
    }
}
