using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Database;

using Microsoft.EntityFrameworkCore;

namespace Magpie.Data.PostgreSQL;

internal sealed class PostgreSqlDatabaseManagementProvider(MagpieDbContext context) : IDatabaseManagementProvider
{
    public string Provider => "PostgreSQL";

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        return [.. applied];
    }
}
