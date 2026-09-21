using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Observability;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Runtime;

internal sealed class DefaultJobRetryPolicy(
    ISettingsRepository settingsRepository,
    TimeProvider timeProvider,
    ILogger<DefaultJobRetryPolicy> logger) : IJobRetryPolicy
{
    private static readonly TimeSpan DefaultBase = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultCap = TimeSpan.FromMinutes(30);
    private const int DefaultMaxAttempts = 5;
    private const double DefaultFactor = 2.0;

    private const string SettingsGroup = "job-retry";
    private const string BaseKey = "base-seconds";
    private const string FactorKey = "factor";
    private const string CapKey = "cap-seconds";
    private const string MaxAttemptsKey = "max-attempts";

    private static JobExecution BuildRetry(JobExecution source, Guid originalId, int attempt, DateTime notBeforeAt)
    {
        var retry = BuildRequeue(source, originalId);
        retry.Attempt = attempt;
        retry.NotBeforeAt = notBeforeAt;
        return retry;
    }

    private static JobExecution BuildRequeue(JobExecution source, Guid originalId)
    {
        return new JobExecution
        {
            JobType = source.JobType,
            ExtensionId = source.ExtensionId,
            ScheduleTriggerId = source.ScheduleTriggerId,
            EventTriggerId = source.EventTriggerId,
            Scope = source.Scope,
            Parameters = new(source.Parameters),
            TriggerContext = new(source.TriggerContext),
            Status = JobExecutionStatus.Queued,
            OriginalExecutionId = originalId,
            ParentExecutionId = source.ParentExecutionId,
            CorrelationId = source.CorrelationId,
            LockPartition = source.LockPartition,
        };
    }

    private static void InjectCurrentTrace(IDictionary<string, string> triggerContext)
    {
        if (Activity.Current is not { } activity ||
            DistributedContextPropagator.Current.Fields.Any(triggerContext.ContainsKey))
        {
            return;
        }

        DistributedContextPropagator.Current.Inject(
            activity,
            triggerContext,
            static (carrier, key, value) => ((IDictionary<string, string>)carrier!)[key] = value);
    }

    private static DateTime ComputeNotBeforeAt(JobRetryPolicy policy, int nextAttempt, DateTime nowUtc)
    {
        var multiplier = Math.Pow(policy.Factor, Math.Max(0, nextAttempt - 1));
        var delaySeconds = Math.Min(policy.BaseDelay.TotalSeconds * multiplier, policy.Cap.TotalSeconds);
        if (double.IsNaN(delaySeconds) || delaySeconds < 0)
        {
            delaySeconds = policy.BaseDelay.TotalSeconds;
        }

        return nowUtc.AddSeconds(delaySeconds);
    }

    private static JobRetryPolicy ResolvePolicy(IReadOnlyDictionary<string, string> settings)
    {
        var baseDelay = ReadPositiveSeconds(settings, BaseKey) ?? DefaultBase;
        var factor = ReadPositiveDouble(settings, FactorKey) ?? DefaultFactor;
        var cap = ReadPositiveSeconds(settings, CapKey) ?? DefaultCap;
        var maxAttempts = ReadPositiveInt(settings, MaxAttemptsKey) ?? DefaultMaxAttempts;

        return new JobRetryPolicy(baseDelay, factor, cap, maxAttempts);
    }

    private static int? ReadPositiveInt(IReadOnlyDictionary<string, string> settings, string key)
    {
        if (settings.TryGetValue(key, out var raw) && int.TryParse(raw, out var value) && value > 0)
        {
            return value;
        }

        return null;
    }

    private static double? ReadPositiveDouble(IReadOnlyDictionary<string, string> settings, string key)
    {
        if (settings.TryGetValue(key, out var raw) && double.TryParse(raw, out var value) && value > 0)
        {
            return value;
        }

        return null;
    }

    private static TimeSpan? ReadPositiveSeconds(IReadOnlyDictionary<string, string> settings, string key)
    {
        if (settings.TryGetValue(key, out var raw) && double.TryParse(raw, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    public async Task<JobExecution?> BuildRetryAsync(JobExecution failed, CancellationToken cancellationToken = default)
    {
        var resolvedPolicy = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        var nextAttempt = (failed.Attempt ?? 1) + 1;
        if (nextAttempt > resolvedPolicy.MaxAttempts)
        {
            JobsTelemetry.RetryAttemptsExhaustedTotal.Add(
                1,
                new KeyValuePair<string, object?>("job.type", failed.JobType));
            return null;
        }

        var originalId = failed.OriginalExecutionId ?? failed.Id;
        var notBeforeAt = ComputeNotBeforeAt(resolvedPolicy, nextAttempt, timeProvider.GetUtcNow().UtcDateTime);
        var retry = BuildRetry(failed, originalId, nextAttempt, notBeforeAt);
        InjectCurrentTrace(retry.TriggerContext);

        return retry;
    }

    private async Task<JobRetryPolicy> ResolveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var group = await settingsRepository
                .GetByIdAsync(new SettingsGroupLookupKey(null, SettingsGroup), cancellationToken)
                .ConfigureAwait(false);

            var settings = group?.Settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal)

                ?? new Dictionary<string, string>();
            return ResolvePolicy(settings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The policy failed to read the job-retry settings, so it will use the compiled defaults");
            return new JobRetryPolicy(DefaultBase, DefaultFactor, DefaultCap, DefaultMaxAttempts);
        }
    }
}
