using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("subjects", "subjects", "Manage subject metadata")]
internal sealed class SubjectsEndpointGroup : IApiEndpointGroup
{
    public string Name => "subjects";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetSubjectsAsync)
            .WithName(nameof(GetSubjectsAsync))
            .Produces<PagedList<Subject>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapGet("{id:guid}", GetSubjectByIdAsync)
            .WithName(nameof(GetSubjectByIdAsync))
            .Produces<Subject>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchSubjectAsync)
            .WithName(nameof(PatchSubjectAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapDelete("{id:guid}", DeleteSubjectAsync)
            .WithName(nameof(DeleteSubjectAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/images/{imageId:guid}/primary", SetPrimaryImageAsync)
            .WithName(nameof(SetPrimaryImageAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapGet("{id:guid}/metadata", GetSubjectMetadataAsync)
            .WithName(nameof(GetSubjectMetadataAsync))
            .Produces<MaterializedMetadata>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("{id:guid}/metadata/contributions", GetSubjectContributionsAsync)
            .WithName(nameof(GetSubjectContributionsAsync))
            .Produces<IReadOnlyList<MetadataContribution>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}/metadata", PatchSubjectMetadataAsync)
            .WithName(nameof(PatchSubjectMetadataAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    internal async Task<IResult> GetSubjectsAsync(
        [FromServices] IFilterService filterService,
        [FromServices] ISubjectService subjectService,
        string? filter,
        string? kind,
        string? tag,
        Guid? relatedContentId,
        string? sortBy,
        SortDirection? sortDirection,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SubjectFilterCriteria? criteria = null;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var parseResult = filterService.ParseSubjectCriteria(filter);
            if (!parseResult.IsSuccess)
            {
                return Results.BadRequest(parseResult.Error);
            }

            criteria = parseResult.Value;
        }

        var query = new SubjectQuery
        {
            Criteria = criteria,
            Kind = kind,
            Tag = tag,
            RelatedContentHierarchyItemId = relatedContentId
        };

        var paging = new PagingParameter
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 25
        };

        var sort = sortBy is not null
            ? new SortParameter
            {
                PropertyName = sortBy,
                Direction = sortDirection ?? SortDirection.Ascending
            }
            : null;

        var result = await subjectService.QueryAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> GetSubjectByIdAsync(
        [FromServices] ISubjectService subjectService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var subject = await subjectService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return subject is null ? Results.NotFound() : Results.Ok(subject);
    }

    internal async Task<IResult> PatchSubjectAsync(
        [FromServices] ISubjectService subjectService,
        AuthenticatedUser user,
        Guid id,
        SubjectPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await subjectService.PatchAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteSubjectAsync(
        [FromServices] ISubjectService subjectService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await subjectService.DeleteAsync(id, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> SetPrimaryImageAsync(
        [FromServices] ISubjectService subjectService,
        AuthenticatedUser user,
        Guid id,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var marked = await subjectService.PatchAsync(
            id,
            new SubjectPatch { PrimaryImageId = new() { Value = imageId } },
            user.Id,
            cancellationToken)
            .ConfigureAwait(false);

        return marked ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> GetSubjectMetadataAsync(
        [FromServices] IMetadataService metadataService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var metadata = await metadataService.GetSubjectMetadataAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return metadata is null ? Results.NotFound() : Results.Ok(metadata);
    }

    internal async Task<IResult> GetSubjectContributionsAsync(
        [FromServices] IMetadataService metadataService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var contributions = await metadataService.GetSubjectContributionsAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return contributions is null ? Results.NotFound() : Results.Ok(contributions);
    }

    internal async Task<IResult> PatchSubjectMetadataAsync(
        [FromServices] IMetadataService metadataService,
        AuthenticatedUser user,
        Guid id,
        MetadataContainerPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await metadataService.PatchSubjectContributionAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }
}

