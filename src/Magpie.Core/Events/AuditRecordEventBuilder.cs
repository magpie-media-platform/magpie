using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class AuditRecordEventBuilder
{
    public static MagpieEvent BuildAuditRecordCreatedEvent(Guid auditRecordId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.AuditRecordId] = auditRecordId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.AuditRecordCreated, context, default, default, null);
    }
}
