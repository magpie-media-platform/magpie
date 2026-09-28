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

internal sealed class ScheduleTriggersEndpointGroup : IApiEndpointGroup
{
    public string Name => "schedule-triggers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetScheduleTriggersAsync)
            .WithName(nameof(GetScheduleTriggersAsync))
            .Produces<IReadOnlyList<ScheduleTrigger>>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetScheduleTriggerAsync)
            .WithName(nameof(GetScheduleTriggerAsync))
            .Produces<ScheduleTrigger>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateScheduleTriggerAsync)
            .WithName(nameof(CreateScheduleTriggerAsync))
            .Produces<ScheduleTrigger>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapPatch("{id:guid}", PatchScheduleTriggerAsync)
            .WithName(nameof(PatchScheduleTriggerAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapDelete("{id:guid}", DeleteScheduleTriggerAsync)
            .WithName(nameof(DeleteScheduleTriggerAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();
    }

    internal async Task<IResult> GetScheduleTriggersAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        bool? mine,
        CancellationToken cancellationToken)
    {
        var triggers = await jobManager.ListScheduleTriggersAsync(mine ?? false, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(triggers);
    }

    internal async Task<IResult> GetScheduleTriggerAsync(
        [FromServices] IJobManager jobManager,
        Guid id,
        CancellationToken cancellationToken)
    {
        var trigger = await jobManager.GetScheduleTriggerAsync(id, cancellationToken).ConfigureAwait(false);

        return trigger is null ? Results.NotFound() : Results.Ok(trigger);
    }

    internal async Task<IResult> CreateScheduleTriggerAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        ScheduleTriggerCreate create,
        CancellationToken cancellationToken)
    {
        var trigger = await jobManager.CreateScheduleTriggerAsync(create, user.Id, user.Role, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{trigger.Id}", trigger);
    }

    internal async Task<IResult> PatchScheduleTriggerAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        Guid id,
        ScheduleTriggerPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await jobManager
            .PatchScheduleTriggerAsync(id, patch, user.Id, user.Role, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteScheduleTriggerAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await jobManager
            .DeleteScheduleTriggerAsync(id, user.Id, user.Role, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
