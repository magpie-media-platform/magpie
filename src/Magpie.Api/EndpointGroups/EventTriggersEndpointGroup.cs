using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class EventTriggersEndpointGroup(
    IEventTriggerRepository eventTriggerRepository,
    IJobManager jobManager) : IApiEndpointGroup
{
    public string Name => "event-triggers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetEventTriggers)
            .WithName(nameof(GetEventTriggers))
            .Produces<EventTrigger[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetEventTriggerById)
            .WithName(nameof(GetEventTriggerById))
            .Produces<EventTrigger>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateEventTrigger)
            .WithName(nameof(CreateEventTrigger))
            .Produces<EventTrigger>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchEventTrigger)
            .WithName(nameof(PatchEventTrigger))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteEventTrigger)
            .WithName(nameof(DeleteEventTrigger))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetEventTriggers(
        Guid? extensionId,
        bool? enabled,
        CancellationToken cancellationToken)
    {
        if (extensionId is not null)
        {
            var triggersByExtension = await eventTriggerRepository
                .ListByExtensionIdAsync(extensionId.Value, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(triggersByExtension.ToArray());
        }

        if (enabled is true)
        {
            var enabledTriggers = await eventTriggerRepository.ListEnabledAsync(cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(enabledTriggers.ToArray());
        }

        var triggers = await eventTriggerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(triggers.ToArray());
    }

    private async Task<IResult> GetEventTriggerById(Guid id, CancellationToken cancellationToken)
    {
        var trigger = await eventTriggerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return trigger is null
            ? Results.NotFound()
            : Results.Ok(trigger);
    }

    private async Task<IResult> CreateEventTrigger(
        HttpContext httpContext,
        EventTriggerCreate request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var trigger = await jobManager.CreateEventTriggerAsync(request, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{trigger.Id}", trigger);
    }

    private async Task<IResult> PatchEventTrigger(
        HttpContext httpContext,
        Guid id,
        EventTriggerPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };
        var updated = await jobManager.UpdateEventTriggerAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> DeleteEventTrigger(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var deleted = await eventTriggerRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
