# Notifications

[Documentation](../README.md) / [Administration](README.md)

A notification rule picks out [events](jobs-and-automation.md#jobs-and-executions), such as a new collection item, and sends a message about them through a delivery provider. Metric thresholds and service availability belong in the [alert rules](observability.md#telemetry-signals) of your monitoring system.

**Admin > Notifications** requires [Admin access](access.md#assigning-a-role). Through the [API](../reference/api.md#notification-rules), Managers can create, change, and delete their own rules, and Requesters can read and test the rules they own. Administrators see and manage all rules.

## Rule scope

An enabled rule matches events from any account and from background work. Only the [event-name pattern](#creating-a-narrow-rule) and the [context filters](#matching-event-context) narrow the selection.

In a [cluster](clustering.md), each node matches the events that start on that node. A rule is skipped on a node where its provider is unavailable.

## Creating a narrow rule

On **Admin > Notifications**, **Add Rule** takes a name, a provider with its **Provider Settings**, an **Event Name Pattern**, optional **Context Filters**, and an **Enabled** switch.

The event-name pattern is a regular expression matched against the event name. For example, `^content\.hierarchyItem\.created$` matches the creation of a collection item, and for a [hierarchical content type](../using/collection.md#content-types-and-hierarchy) it also matches the title's child items. An empty pattern matches every event.

### Matching event context

Every context filter must match. **Add Filter** gives a **Key**, a **Pattern (regex)**, and **Negate**. For example, key `content.impact.contentType` with pattern `^movie$` restricts the rule above to movies.

To find the keys of another event, point a [webhook](#configuring-a-webhook) rule at the event with no filters, trigger the event for real, and read `event.context` in the JSON the destination receives. The key is the path after `event.context.`. **Test** sends a synthetic event with different context, so it is no help here.

## Testing the provider, then the rule

**Test** on a saved rule sends a synthetic event straight through the provider. It ignores the event pattern, the context filters, and **Enabled**, and **Test Sent** only means the provider call returned. Whether the message arrived shows only at the destination.

To test the rule itself, enable it and do something that produces the event, such as adding a title. If the test message arrives but a real action sends nothing, check the event pattern, **Enabled**, and the **Send notification** executions. In a cluster, repeat the action through each node you expect to deliver. See [Clustering](clustering.md#verifying-a-node-before-normal-use) for finding a node's ID.

## Configuring a webhook

The built-in **Webhook** provider has three settings: a **URL** that Magpie can reach, an **HTTP method** (POST by default, with GET and PUT also offered), and optional **Headers (JSON)**, such as `{"X-Notification-Token": "your-token"}`. Put credentials in the provider settings, not in rule names or patterns.

POST and PUT send a JSON payload with the event name, time, context, and the Magpie node ID and version. Invalid headers JSON is logged and the request goes out without those headers.

If the request fails to connect, times out, or gets HTTP 429 or a server error, the notification job fails and its [retry policy](jobs-and-automation.md#configuring-retries) applies. Any other unsuccessful status is logged and the message is dropped, so a test reports **Test Sent** even when the destination answers with HTTP 401.

## Diagnosing missing or repeated messages

Look for the **Send notification** executions in **Admin > System Jobs** around the time of the action. Magpie does not replay missed events. If the rule or provider was deleted while a delivery was queued, the execution completes without sending anything. [Observability](observability.md) explains how to connect application activity to the logs.

---

[Administration](README.md) | [Documentation](../README.md)
