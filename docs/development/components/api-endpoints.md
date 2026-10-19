# API endpoints

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

An API endpoint group adds HTTP routes to Magpie's API. You write one when an extension needs to expose an operation to its own UI, to scripts, or to other services. Implement `IApiEndpointGroup`. The host maps each registered group under `/api`, with the group's `Name` as the next path segment. Endpoints are minimal API endpoints, and Magpie's [authorization helpers](../../../src/Magpie.Sdk/Api/AuthorizationExtensions.cs) set the required role. The contract needs the [SDK's `net10.0` target](../../reference/extensions.md#framework-and-abi-compatibility), and extension endpoints are mapped only on nodes that serve the API role.

```csharp
using Magpie.Sdk.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

internal sealed class ExampleEndpoints : IApiEndpointGroup
{
    /// <inheritdoc />
    public string Name => "example";

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("ping", () => Results.Ok(new { status = "ready" }))
            .RequireViewer();
    }
}
```

Register it in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddSingleton<IApiEndpointGroup, ExampleEndpoints>();
```

After a restart, `GET /api/example/ping` returns `{"status":"ready"}` for a signed-in viewer. Groups are [additive](README.md#additional-components), and Magpie has no way to override a route, so pick a prefix that no other group uses.

## Keeping request work in the handler

The host calls `MapEndpoints` once during startup, and an exception there stops the host. Keep database and remote work out of it, and do not capture scoped services in the singleton group. Take them as handler parameters instead. A real handler calls the [built-in service](../using-built-in-services.md) that owns the data, passes the request cancellation token along, and turns the result into an HTTP response.

Every endpoint requires authentication. Use `.AllowAnonymous()` only for an operation that is meant to be public. The `Require...` helpers check the caller's [role](../../reference/roles.md): Viewer, Requester, Manager, or Admin. Use the lowest role that works. They do not check that the caller owns a record, so a mutation may also need an [ownership check](../../reference/roles.md#ownership-checks) in the service. A hidden button in the browser protects nothing.

See [IApiEndpointGroup](../../../src/Magpie.Sdk/Api/IApiEndpointGroup.cs). [API and CLI](../../administration/api-and-cli.md) covers calling the API from scripts.

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
