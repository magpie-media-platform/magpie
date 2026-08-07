using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class ExtensionActivationGenerationRepository(
    MagpieDbContext context,
    ILogger<ExtensionActivationGenerationRepository> logger) : IExtensionActivationGenerationRepository
{
    private const int SingletonId = 1;

    public async Task<long> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var generation = await (
                from s in context.ExtensionActivationStates.AsNoTracking()
                where s.Id == SingletonId
                select (long?)s.Generation)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (generation is not null)
            {
                return generation.Value;
            }

            context.ExtensionActivationStates.Add(new() { Id = SingletonId, Generation = 0 });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The activation generation repository failed to read the activation generation.");
            throw;
        }
    }

    public async Task<long> BumpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await context.ExtensionActivationStates
                .Where(s => s.Id == SingletonId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Generation, e => e.Generation + 1), cancellationToken)
                .ConfigureAwait(false);

            if (updated == 0)
            {
                context.ExtensionActivationStates.Add(new() { Id = SingletonId, Generation = 1 });
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return 1;
            }

            return await GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The activation generation repository failed to increase the activation generation.");
            throw;
        }
    }
}
