using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

namespace Magpie.Core.Diagnostics;

internal sealed class NodeStatusDiagnostic(
    IExtensionNodeStatusRepository nodeStatusRepository,
    TimeProvider timeProvider) : IDiagnosticCheck
{
    public const string Id = "core.nodes.status";

    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(2);

    public string DiagnosticCheckId => Id;

    public string Category => "Nodes";

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var seenAfter = timeProvider.GetUtcNow().UtcDateTime - HeartbeatTimeout;
        var statuses = await nodeStatusRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. statuses
                .Where(status => status.LastSeenAt >= seenAfter)
                .Where(status => status.ExtensionHealth == ExtensionNodeHealth.StaleRequiresRestart ||
                    !status.SchedulerEligible)
                .Select(status => new DiagnosticFinding
                {
                    DiagnosticCheckId = Id,
                    Severity = DiagnosticFindingSeverity.Warning,
                    Title = status.ExtensionHealth == ExtensionNodeHealth.StaleRequiresRestart
                        ? $"Node {status.NodeId} is pending restart"
                        : $"Node {status.NodeId} takes no jobs",
                    Detail = status.Message ?? string.Empty,
                    AffectedEntity = $"Node:{status.NodeId}",
                }),
        ];
    }
}
