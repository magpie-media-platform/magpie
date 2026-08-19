using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Events;

internal sealed class ExtensionActivationBackgroundService(
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    MagpieHostEnvironment hostEnvironment,
    TimeProvider timeProvider,
    ILogger<ExtensionActivationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var listener = eventBus is EventBus bus
                ? bus.CreateListener("extension.activation.changed", localOnly: false, callSite: "extension-activation")
                : eventBus.CreateListener("extension.activation.changed");
            await foreach (var magpieEvent in listener.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!TryGetExtensionActivationGeneration(magpieEvent, out var generation))
                {
                    continue;
                }

                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using var scopeHandle = scope.ConfigureAwait(false);
                    var repository = scope.ServiceProvider.GetRequiredService<IExtensionNodeStatusRepository>();
                    var existing = await repository
                        .GetByNodeIdAsync(hostEnvironment.NodeId, stoppingToken)
                        .ConfigureAwait(false);
                    if (existing is not null &&
                        existing.ExtensionHealth is ExtensionNodeHealth.CurrentHealthy &&
                        existing.AppliedGeneration < generation)
                    {
                        await repository.UpsertAsync(
                            existing with
                            {
                                ExtensionHealth = ExtensionNodeHealth.StaleRequiresRestart,
                                SchedulerEligible = !hostEnvironment.IsClustered,
                                LastSeenAt = timeProvider.GetUtcNow().UtcDateTime,
                                Message = "Extension changes require a restart.",
                            },
                            stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "The extension activation service failed to apply generation {Generation}",
                        generation);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static bool TryGetExtensionActivationGeneration(MagpieEvent magpieEvent, out long generation)
    {
        generation = default;
        return magpieEvent is not null &&
            string.Equals(magpieEvent.Name, "extension.activation.changed", StringComparison.Ordinal) &&
            magpieEvent.Context.TryGetValue("extension.generation", out var raw) &&
            long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out generation);
    }
}
