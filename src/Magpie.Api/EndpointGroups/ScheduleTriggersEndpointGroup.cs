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

internal sealed class ScheduleTriggersEndpointGroup(
    IScheduleTriggerRepository scheduleTriggerRepository,
    IJobManager jobManager) : IApiEndpointGroup
{
    public string Name => "schedule-triggers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllScheduleTriggers)
            .WithName("GetAllScheduleTriggers")
            .WithTags("ScheduleTriggers")
            .Produces<ScheduleTrigger[]>()
            .RequireViewer();

        builder.MapGet("/{id:guid}", GetScheduleTriggerById)
            .WithName("GetScheduleTriggerById")
            .WithTags("ScheduleTriggers")
            .Produces<ScheduleTrigger>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateScheduleTrigger)
            .WithName("CreateScheduleTrigger")
            .WithTags("ScheduleTriggers")
            .Produces<ScheduleTrigger>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("/{id:guid}", PatchScheduleTrigger)
            .WithName("PatchScheduleTrigger")
            .WithTags("ScheduleTriggers")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("/{id:guid}", DeleteScheduleTrigger)
            .WithName("DeleteScheduleTrigger")
            .WithTags("ScheduleTriggers")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetAllScheduleTriggers(CancellationToken cancellationToken)
    {
        var triggers = await scheduleTriggerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(triggers.ToArray());
    }

    private async Task<IResult> GetScheduleTriggerById(Guid id, CancellationToken cancellationToken)
    {
        var trigger = await scheduleTriggerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return trigger is null
            ? Results.NotFound()
            : Results.Ok(trigger);
    }

    private async Task<IResult> CreateScheduleTrigger(
        HttpContext httpContext,
        ScheduleTriggerCreate request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var trigger = await jobManager.CreateScheduleTriggerAsync(request, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/schedule-triggers/{trigger.Id}", trigger);
    }

    private async Task<IResult> PatchScheduleTrigger(
        HttpContext httpContext,
        Guid id,
        ScheduleTriggerPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var updated = await jobManager.UpdateScheduleTriggerAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> DeleteScheduleTrigger(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await scheduleTriggerRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
