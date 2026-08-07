using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Notifications;

namespace Magpie.Extensions.IntegrationTestShims.Notification;

internal sealed class MockNotificationProvider : INotificationProvider
{
    public const string ProviderTypeName = "mock-notification";
    public const string SendMethod = "send";
    public const string SettingTemplate = "template";
    public const string SettingTarget = "target";

    private readonly AuditWriter _audit = new(ProviderTypeName);
    private readonly LatencyInjector _latency = new(ProviderTypeName);

    public string ProviderType => ProviderTypeName;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
    [
        new()
        {
            Key = SettingTarget,
            Type = DataType.String,
            DisplayName = "Target",
            Description = "Free-form target address the mock records into the audit payload.",
            Placeholder = "mock://channel/integration-tests",
        },
        new()
        {
            Key = SettingTemplate,
            Type = DataType.String,
            DisplayName = "Template",
            Description = "Free-form template name the mock records into the audit payload.",
        },
    ];

    public async Task SendAsync(
        NotificationRule rule,
        MagpieEvent magpieEvent,
        CancellationToken cancellationToken = default)
    {
        await _latency.DelayAsync(SendMethod, cancellationToken).ConfigureAwait(false);

        var request = new SendRequest
        {
            Rule = new RecordedRule
            {
                Id = rule.Id,
                Name = rule.Name,
                ProviderType = rule.ProviderType,
                Enabled = rule.Enabled,
                OwnerUserId = rule.OwnerUserId,
                Settings = new Dictionary<string, string>(rule.Settings),
            },
            Event = new RecordedEvent
            {
                Id = magpieEvent.Id,
                Name = magpieEvent.Name,
                OccurredAt = magpieEvent.OccurredAt,
                PublishedAt = magpieEvent.PublishedAt,
                NodeId = magpieEvent.NodeId,
                Context = new Dictionary<string, string>(magpieEvent.Context),
            },
        };

        await _audit
            .WriteAsync(SendMethod, request, new SendResponse { Delivered = true }, cancellationToken)
            .ConfigureAwait(false);
    }

    internal sealed class SendRequest
    {
        public RecordedRule Rule { get; set; } = new();

        public RecordedEvent Event { get; set; } = new();
    }

    internal sealed class SendResponse
    {
        public bool Delivered { get; set; }
    }

    internal sealed class RecordedRule
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string ProviderType { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public Guid? OwnerUserId { get; set; }

        public Dictionary<string, string> Settings { get; set; } = [];
    }

    internal sealed class RecordedEvent
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTime OccurredAt { get; set; }

        public DateTime PublishedAt { get; set; }

        public string? NodeId { get; set; }

        public Dictionary<string, string> Context { get; set; } = [];
    }
}
