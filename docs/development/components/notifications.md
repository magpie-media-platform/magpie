# Notification providers

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A notification provider turns a [Magpie event](../using-built-in-services.md#publishing-and-observing-events) into a delivery to an external service. [Notification rules](../../administration/notifications.md) decide which events qualify, who owns the rule, and which connection settings to use, and `INotificationProvider.SendAsync` receives the selected rule and event. You write a provider when a destination needs its own settings, payload, or delivery protocol. If the existing [Webhook provider](../../administration/notifications.md#configuring-a-webhook) fits, use that.

## From an event to delivery

A matching rule queues a [send-notification job](jobs.md). The job reads the current rule, selects the first registered provider whose `ProviderType` equals the rule's type, and calls `SendAsync`. The comparison is case-sensitive. Providers are [additive](README.md#additional-components), but a delivery uses one, so give yours a unique, stable type.

Extension providers register before the built-in Webhook provider, so an extension provider with type `webhook` replaces the built-in one for delivery and for rule tests.

A queued job does not keep the rule's old connection settings, so read the destination and credentials from `rule.Settings` on each call. If the rule was removed or its provider is no longer registered, the job logs it and skips delivery.

The job uses [retry behavior](job-retry-policy.md). A remote service can accept a message before the connection fails, so a retry can deliver the same event twice. Include the event identity if the destination can detect duplicates.

## Describing and registering the provider

Declare `ProviderType`, `DisplayName`, and `SettingsSchema`. The schema supplies the fields for notification-rule configuration, and credential fields should be marked secret. For validation beyond the schema, implement [`IValidatesIntegrationSettings`](../../reference/extensions.md#optional-capabilities) on the provider.

Register it in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddScoped<INotificationProvider, ExampleNotificationProvider>();
```

The [Webhook provider](../../../src/Magpie.Core/Notifications/WebhookNotificationProvider.cs) shows a settings schema, validation, payload construction, and HTTP dispatch. Its response policy is specific to webhooks, so choose one that suits your destination.

## Delivery outcome

An HTTP provider should get its client from `IHttpClientFactory` (see [shared facilities](../using-built-in-services.md#using-shared-facilities)). With `request` already holding the validated destination, authentication, and serialized event, the send looks like this:

```csharp
using var response = await client.SendAsync(request, cancellationToken)
    .ConfigureAwait(false);
response.EnsureSuccessStatusCode();
```

The owning method also disposes the request. This treats every non-success status as a failure, and a complete provider decides which responses to retry and how to report a permanent rejection. If the remote service rejects the request, throw. Return normally only if the provider records the response and drops the notification on purpose.

Forward cancellation, let failures that need a retry reach the runtime, and keep per-rule state out of shared fields. A completed call means the destination acknowledged the request, not that anyone read it.

## Rule tests

A rule test validates the settings against the schema and the provider's optional validator. If that fails, `SendAsync` isn't called. Otherwise it calls the provider with a synthetic test event, and it can test unsaved settings. A successful test means the provider accepted the delivery. It doesn't apply the rule's event pattern or filters.

See the [provider contract](../../../src/Magpie.Sdk/Notifications/INotificationProvider.cs), the [delivery job](../../../src/Magpie.Core/Jobs/SendNotificationJob.cs), and the [notification administration guide](../../administration/notifications.md).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
