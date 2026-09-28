using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("event-triggers", "event-triggers", "Manage event triggers")]
internal sealed class EventTriggersEndpointGroup : IApiEndpointGroup
{
    public string Name => "event-triggers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetEventTriggersAsync)
            .WithName(nameof(GetEventTriggersAsync))
            .Produces<IReadOnlyList<EventTrigger>>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetEventTriggerAsync)
            .WithName(nameof(GetEventTriggerAsync))
            .Produces<EventTrigger>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateEventTriggerAsync)
            .WithName(nameof(CreateEventTriggerAsync))
            .Produces<EventTrigger>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapPatch("{id:guid}", PatchEventTriggerAsync)
            .WithName(nameof(PatchEventTriggerAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapDelete("{id:guid}", DeleteEventTriggerAsync)
            .WithName(nameof(DeleteEventTriggerAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();
    }

    [MagpieCommand("list", "GET", "", "List event triggers")]
    internal async Task<IResult> GetEventTriggersAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        Guid? extensionId,
        bool? enabled,
        bool? mine,
        CancellationToken cancellationToken)
    {
        var triggers = await jobManager
            .ListEventTriggersAsync(extensionId, enabled, mine ?? false, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(triggers);
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show an event trigger")]
    internal async Task<IResult> GetEventTriggerAsync(
        [FromServices] IJobManager jobManager,
        Guid id,
        CancellationToken cancellationToken)
    {
        var trigger = await jobManager.GetEventTriggerAsync(id, cancellationToken).ConfigureAwait(false);

        return trigger is null ? Results.NotFound() : Results.Ok(trigger);
    }

    [MagpieCommand("create", "POST", "", "Create an event trigger")]
    internal async Task<IResult> CreateEventTriggerAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        [FromBody] EventTriggerCreate create,
        CancellationToken cancellationToken)
    {
        var trigger = await jobManager.CreateEventTriggerAsync(create, user.Id, user.Role, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{trigger.Id}", trigger);
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on an event trigger")]
    internal async Task<IResult> PatchEventTriggerAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        Guid id,
        [FromBody] EventTriggerPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await jobManager
            .PatchEventTriggerAsync(id, patch, user.Id, user.Role, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("delete", "DELETE", "{id:guid}", "Delete an event trigger")]
    internal async Task<IResult> DeleteEventTriggerAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await jobManager
            .DeleteEventTriggerAsync(id, user.Id, user.Role, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
