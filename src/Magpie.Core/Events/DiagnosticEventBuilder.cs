using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class DiagnosticEventBuilder
{
    public static MagpieEvent BuildDiagnosticFindingRaisedEvent(Guid findingId, string category, string severity)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.DiagnosticFindingId] = findingId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.DiagnosticCategory] = category ?? string.Empty,
            [MagpieEvents.Keys.DiagnosticSeverity] = severity ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.DiagnosticFindingRaised, context, default, default, null);
    }

    public static MagpieEvent BuildDiagnosticFindingUpdatedEvent(Guid findingId, string category, string severity)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.DiagnosticFindingId] = findingId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.DiagnosticCategory] = category ?? string.Empty,
            [MagpieEvents.Keys.DiagnosticSeverity] = severity ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.DiagnosticFindingUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildDiagnosticScanCompletedEvent(int findingCount)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.DiagnosticFindingCount] = findingCount.ToString(CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.DiagnosticScanCompleted, context, default, default, null);
    }
}
