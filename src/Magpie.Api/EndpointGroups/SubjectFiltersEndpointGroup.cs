using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SubjectFiltersEndpointGroup : IApiEndpointGroup
{
    public string Name => "subject-filters";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetSubjectFiltersAsync)
            .WithName(nameof(GetSubjectFiltersAsync))
            .Produces<IReadOnlyList<SubjectFilter>>()
            .RequireViewer();

        builder.MapPost("", CreateSubjectFilterAsync)
            .WithName(nameof(CreateSubjectFilterAsync))
            .Produces<SubjectFilter>(StatusCodes.Status201Created)
            .RequireViewer();

        builder.MapGet("my", GetMySubjectFiltersAsync)
            .WithName(nameof(GetMySubjectFiltersAsync))
            .Produces<IReadOnlyList<SubjectFilter>>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetSubjectFilterAsync)
            .WithName(nameof(GetSubjectFilterAsync))
            .Produces<SubjectFilter>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchSubjectFilterAsync)
            .WithName(nameof(PatchSubjectFilterAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapDelete("{id:guid}", DeleteSubjectFilterAsync)
            .WithName(nameof(DeleteSubjectFilterAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();
    }

    internal async Task<IResult> GetSubjectFiltersAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        string? name,
        string? tag,
        CancellationToken cancellationToken)
    {
        var filters = await filterService.ListVisibleSubjectFiltersAsync(name, tag, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(filters);
    }

    internal async Task<IResult> CreateSubjectFilterAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        SubjectFilter filter,
        CancellationToken cancellationToken)
    {
        var created = await filterService.CreateSubjectFilterAsync(filter, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    internal async Task<IResult> GetMySubjectFiltersAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        CancellationToken cancellationToken)
    {
        var filters = await filterService.ListOwnSubjectFiltersAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(filters);
    }

    internal async Task<IResult> GetSubjectFilterAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var filter = await filterService
            .GetSubjectFilterAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return filter is null ? Results.NotFound() : Results.Ok(filter);
    }

    internal async Task<IResult> PatchSubjectFilterAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        Guid id,
        SubjectFilterPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await filterService
            .PatchSubjectFilterAsync(id, patch, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteSubjectFilterAsync(
        [FromServices] IFilterService filterService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await filterService
            .DeleteSubjectFilterAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}

