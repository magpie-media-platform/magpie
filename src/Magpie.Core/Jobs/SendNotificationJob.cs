using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Notifications;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class SendNotificationJob(
    INotificationRuleRepository ruleRepository,
    IEnumerable<INotificationProvider> notificationProviders,
    ILogger<SendNotificationJob> logger) : IJob
{
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.SendNotification,
        DisplayName = "Send notification",
        Description = "Sends a notification through a notification provider.",
        Category = "System",
        Scope = JobScope.System,
        Enabled = true,
        OnDemand = false,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.Parallel,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.RuleId,
                "The id of the matched notification rule to deliver.",
                DataType.String,
                true,
                null),
            new(
                MagpieJobs.Parameters.Event,
                "The JSON-serialized event that matched the rule.",
                DataType.String,
                true,
                null)
        ],
    };

    private MagpieEvent DeserializeEvent(string rawEvent)
    {
        MagpieEvent? magpieEvent;
        try
        {
            magpieEvent = JsonSerializer.Deserialize<MagpieEvent>(rawEvent, _serializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The event data has an invalid format.", ex);
        }

        if (magpieEvent is null)
        {
            throw new ArgumentException("The event data could not be read.");
        }

        return magpieEvent;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(MagpieJobs.Parameters.RuleId, out var rawRuleId) ||
            string.IsNullOrWhiteSpace(rawRuleId))
        {
            throw new ArgumentException("A notification rule ID is required.");
        }

        if (!Guid.TryParse(rawRuleId, out var ruleId))
        {
            throw new ArgumentException("The notification rule ID has an invalid format.");
        }

        if (!context.Parameters.TryGetValue(MagpieJobs.Parameters.Event, out var rawEvent) ||
            string.IsNullOrWhiteSpace(rawEvent))
        {
            throw new ArgumentException("Event data is required.");
        }

        var magpieEvent = DeserializeEvent(rawEvent);

        var rule = await ruleRepository.GetByIdAsync(ruleId, cancellationToken).ConfigureAwait(false);
        if (rule is null)
        {
            logger.LogInformation(
                "Notification rule {RuleId} no longer exists, so the job skipped the delivery for event" +
                " {EventName}",
                ruleId,
                magpieEvent.Name);
            return;
        }

        var provider = string.IsNullOrWhiteSpace(rule.ProviderType)
            ? null
            : notificationProviders.FirstOrDefault(
                candidate => string.Equals(candidate.ProviderType, rule.ProviderType, StringComparison.Ordinal));
        if (provider is null)
        {
            logger.LogWarning(
                "Notification rule {RuleName} ({RuleId}) references unknown provider {ProviderType}, so the " +
                "job skipped the delivery for event {EventName}",
                rule.Name,
                rule.Id,
                rule.ProviderType,
                magpieEvent.Name);
            return;
        }

        await provider.SendAsync(rule, magpieEvent, cancellationToken).ConfigureAwait(false);
    }
}
