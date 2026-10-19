# Cluster backplanes

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

Nodes in a [cluster](../../administration/clustering.md) need a way to tell each other that something happened, such as a job being cancelled or a setting changing. That channel is the backplane. A cluster requires one. The PostgreSQL and SQL Server providers each ship a backplane built on the database, and an extension can supply another by implementing `IClusterBackplane`, which [replaces the transport](README.md#replacement-components). SQLite supplies none.

The backplane carries events only. The database stays authoritative for application state and [job records](../../administration/jobs-and-automation.md#jobs-and-executions), and an event just tells a node that state changed. The host sends locally raised events through the backplane and publishes events from peers to the [local event bus](../using-built-in-services.md#publishing-and-observing-events). It does this only when `Magpie:Cluster:Mode` is not `SingleNode`, and a clustered host refuses to start without a registered backplane.

Register one [singleton](../using-built-in-services.md#service-lifetime-and-ownership) in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point), before the host supplies its fallback:

```csharp
services.AddSingleton<IClusterBackplane, ExampleBackplane>();
```

## Transport contract

`BroadcastEventAsync` sends a locally raised `MagpieEvent` to peers, and `CreateListener` supplies an `IEventListener` for incoming ones. Delivery is best effort. The backplane stores nothing, and the host does not resend a missed event.

Preserve the event exactly: identity, origin, timestamps, context, and job and [trace relationships](../../administration/observability.md#tracing-an-operation). The host uses the origin `NodeId` to tell local events from peer events, so rewriting it on receipt turns a peer event into an outbound one and starts a relay loop. Serialize the received event itself rather than building a new one:

```csharp
private static byte[] EncodeEvent(MagpieEvent magpieEvent)
{
    return JsonSerializer.SerializeToUtf8Bytes(magpieEvent);
}

private static MagpieEvent DecodeEvent(byte[] payload)
{
    return JsonSerializer.Deserialize<MagpieEvent>(payload)
        ?? throw new JsonException("The event payload is null.");
}
```

This only shows the payload boundary. Sending, framing, subscriptions, reconnects, and disposal depend on your transport, and all connected providers need a compatible wire format. Validate the payload and confirm the sender is trusted before the listener yields an event.

## Lifetime and failure

The host creates one listener at startup and disposes it when receiving stops, so release the subscription in `Dispose` and unblock any waiting readers. Reconnect after a temporary disconnect inside the listener's `ReadAllAsync` sequence. If `ReadAllAsync` throws, the host logs it and stops receiving peer events. If the sequence simply ends, it stops without logging. In both cases the host does not create a new listener, so the node hears nothing from peers until it restarts. If `BroadcastEventAsync` throws, the host logs a warning and drops that event. Honor cancellation in sends and reads.

## Limits after a missed signal

The host does not recover missed signals from database state:

- A node that misses a job cancellation lets the running execution finish, and the record then becomes Cancelled.
- A node that misses a change to event triggers or notification rules keeps the old rules until the next change or a restart.
- A node that misses an extension disable or uninstall event does not call that hook.

Extension activation status is the exception, because each node refreshes it from the database every 30 seconds.

See [IClusterBackplane](../../../src/Magpie.Sdk/Extensions/IClusterBackplane.cs), [IEventListener](../../../src/Magpie.Sdk/Services/IEventListener.cs), and the [PostgreSQL](../../../src/Magpie.Data.PostgreSQL/PostgreSqlClusterBackplane.cs) and [SQL Server](../../../src/Magpie.Data.SqlServer/SqlServerClusterBackplane.cs) implementations.

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
