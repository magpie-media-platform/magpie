using System;
using System.Collections.Generic;
using System.Linq;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed class ExecutionTrace
{
    private static readonly Func<int?, int?, int?> Add = (left, right) => left is null && right is null
        ? null
        : (left ?? 0) + (right ?? 0);

    private readonly object _gate = new();
    private readonly List<NodeOutcome> _nodes = [];
    private readonly List<RequestOutcome> _requests = [];
    private readonly List<StageOutcome> _stages = [];

    internal IReadOnlyList<NodeOutcome> Nodes
    {
        get
        {
            lock (_gate)
            {
                return [.. _nodes];
            }
        }
    }

    internal IReadOnlyList<RequestOutcome> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    internal IReadOnlyList<StageOutcome> Stages
    {
        get
        {
            lock (_gate)
            {
                return [.. _stages];
            }
        }
    }

    internal void RecordNode(PlanNode node, QueryResult result, long elapsedMilliseconds)
    {
        lock (_gate)
        {
            if (_nodes.Exists(outcome => outcome.NodeId == node.Id))
            {
                return;
            }

            _nodes.Add(new NodeOutcome(
                node.Id,
                node.NodeKind(),
                "executed",
                null,
                result.Hits.Count,
                result.PageInfo.HasMore,
                elapsedMilliseconds));
        }
    }

    internal void RecordNodeSkipped(PlanNode node, string reason)
    {
        lock (_gate)
        {
            if (_nodes.Exists(outcome => outcome.NodeId == node.Id))
            {
                return;
            }

            _nodes.Add(new NodeOutcome(node.Id, node.NodeKind(), "skipped", reason, 0, false, 0));
        }
    }

    internal void RecordRequestBuilt(int? nodeId, string role, string target)
    {
        lock (_gate)
        {
            _requests.Add(new RequestOutcome(nodeId, role, target));
        }
    }

    internal void RecordRequestExecuted(
        int? nodeId,
        string role,
        string target,
        int? statusCode,
        FetchOutcome outcome,
        long elapsedMilliseconds)
    {
        lock (_gate)
        {
            var request = _requests.LastOrDefault(candidate => candidate.NodeId == nodeId && candidate.Role == role &&
                candidate.Target == target &&
                candidate.Outcome is null);
            if (request is null)
            {
                request = new RequestOutcome(nodeId, role, target);
                _requests.Add(request);
            }

            request.StatusCode = statusCode;
            request.Outcome = outcome;
            request.ElapsedMilliseconds = elapsedMilliseconds;
        }
    }

    internal void RecordRemoteRows(int? nodeId, string role, string target, int count, int offset)
    {
        lock (_gate)
        {
            var request = _requests.LastOrDefault(candidate => candidate.NodeId == nodeId && candidate.Role == role &&
                candidate.Target == target);
            if (request is null)
            {
                request = new RequestOutcome(nodeId, role, target);
                _requests.Add(request);
            }

            request.RowCount = count;
            request.Offset = offset;
        }
    }

    internal void RecordStage(
        int? nodeId,
        string role,
        string outcome,
        string? reason,
        int? rowsIn = null,
        int? rowsOut = null,
        int? pages = null)
    {
        lock (_gate)
        {
            var index = _stages.FindIndex(stage => stage.NodeId == nodeId && stage.Role == role &&
                stage.Outcome == outcome &&
                stage.Reason == reason);
            if (index < 0)
            {
                _stages.Add(new StageOutcome(nodeId, role, outcome, reason, rowsIn, rowsOut, pages));
                return;
            }

            var existing = _stages[index];
            _stages[index] = existing with
            {
                RowsIn = Add(existing.RowsIn, rowsIn),
                RowsOut = Add(existing.RowsOut, rowsOut),
                Pages = pages is null ? existing.Pages : Math.Max(existing.Pages ?? 0, pages.Value)
            };
        }
    }

    internal sealed record NodeOutcome(
        int NodeId,
        string Kind,
        string Outcome,
        string? Reason,
        int RowCount,
        bool HasMore,
        long ElapsedMilliseconds);

    internal sealed class RequestOutcome(int? nodeId, string role, string target)
    {
        internal int? NodeId { get; } = nodeId;

        internal string Role { get; } = role;

        internal string Target { get; } = target;

        internal int? StatusCode { get; set; }

        internal FetchOutcome? Outcome { get; set; }

        internal long? ElapsedMilliseconds { get; set; }

        internal int? RowCount { get; set; }

        internal int? Offset { get; set; }
    }

    internal sealed record StageOutcome(
        int? NodeId,
        string Role,
        string Outcome,
        string? Reason,
        int? RowsIn,
        int? RowsOut,
        int? Pages);
}
