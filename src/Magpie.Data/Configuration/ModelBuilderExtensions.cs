using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore;

namespace Magpie.Data.Configuration;

internal static class ModelBuilderExtensions
{
    private const string EntityName = "JobEnqueueCoalescingLock";
    private const string KeyProperty = "Key";

    internal static void ConfigureJobEnqueueCoalescingLock(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var builder = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(EntityName);
        builder.ToTable("job_enqueue_coalescing_lock");
        builder.IndexerProperty<string>(KeyProperty).HasMaxLength(64);
        builder.HasKey(KeyProperty);
    }
}
