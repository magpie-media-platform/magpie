using System;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Magpie.Data;

internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
