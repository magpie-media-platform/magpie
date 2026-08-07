using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Database;

using Microsoft.EntityFrameworkCore;

namespace Magpie.Data.SqlServer;

internal sealed class SqlServerDatabaseManagementProvider(MagpieDbContext context) : IDatabaseManagementProvider
{
    public string Provider => "MSSQL";

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
