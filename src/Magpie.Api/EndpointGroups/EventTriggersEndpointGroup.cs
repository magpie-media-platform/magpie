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
        builder.MapGet("", GetAllEventTriggers)
            .WithName("GetAllEventTriggers")
            .WithTags("EventTriggers")
            .Produces<EventTrigger[]>()
            .RequireViewer();

        builder.MapGet("/{id:guid}", GetEventTriggerById)
            .WithName("GetEventTriggerById")
            .WithTags("EventTriggers")
            .Produces<EventTrigger>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("/by-extension/{extensionId:guid}", GetEventTriggersByExtension)
            .WithName("GetEventTriggersByExtension")
            .WithTags("EventTriggers")
            .Produces<EventTrigger[]>()
            .RequireViewer();

        builder.MapGet("/enabled", GetEnabledEventTriggers)
            .WithName("GetEnabledEventTriggers")
            .WithTags("EventTriggers")
            .Produces<EventTrigger[]>()
            .RequireViewer();

        builder.MapPost("", CreateEventTrigger)
            .WithName("CreateEventTrigger")
            .WithTags("EventTriggers")
            .Produces<EventTrigger>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("/{id:guid}", PatchEventTrigger)
            .WithName("PatchEventTrigger")
            .WithTags("EventTriggers")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("/{id:guid}", DeleteEventTrigger)
            .WithName("DeleteEventTrigger")
            .WithTags("EventTriggers")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetAllEventTriggers(CancellationToken cancellationToken)
    {
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

    private async Task<IResult> GetEventTriggersByExtension(Guid extensionId, CancellationToken cancellationToken)
    {
        var triggers = await eventTriggerRepository.ListByExtensionIdAsync(extensionId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(triggers.ToArray());
    }

    private async Task<IResult> GetEnabledEventTriggers(CancellationToken cancellationToken)
    {
        var triggers = await eventTriggerRepository.ListEnabledAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(triggers.ToArray());
    }

    private async Task<IResult> CreateEventTrigger(
        HttpContext httpContext,
        EventTriggerCreate request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var trigger = await jobManager.CreateEventTriggerAsync(request, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/event-triggers/{trigger.Id}", trigger);
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
