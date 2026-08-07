using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Magpie.Data.SQLite;

internal sealed class SqliteModelCustomizer(ModelCustomizerDependencies dependencies) :
    RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ReleaseIdentityKey>()
            .HasIndex(e => new { e.Authority, e.Value })
            .IsUnique()
            .HasFilter("\"ReleaseId\" IS NOT NULL");
    }
}
