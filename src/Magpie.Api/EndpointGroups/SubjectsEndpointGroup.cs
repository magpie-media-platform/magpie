using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SubjectsEndpointGroup(ISubjectRepository subjectRepository) : IApiEndpointGroup
{
    public string Name => "subjects";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllSubjects)
            .WithName("GetAllSubjects")
            .WithTags("Subjects")
            .Produces<Subject[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetSubjectById)
            .WithName("GetSubjectById")
            .WithTags("Subjects")
            .Produces<Subject>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-kind/{kind}", GetSubjectsByKind)
            .WithName("GetSubjectsByKind")
            .WithTags("Subjects")
            .Produces<Subject[]>()
            .RequireAuthorization();

        builder.MapGet("/by-tag/{tag}", GetSubjectsByTag)
            .WithName("GetSubjectsByTag")
            .WithTags("Subjects")
            .Produces<Subject[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateSubject)
            .WithName("CreateSubject")
            .WithTags("Subjects")
            .Produces<Subject>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchSubject)
            .WithName("PatchSubject")
            .WithTags("Subjects")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteSubject)
            .WithName("DeleteSubject")
            .WithTags("Subjects")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<IResult> GetAllSubjects(CancellationToken cancellationToken)
    {
        var subjects = await subjectRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(subjects.ToArray());
    }

    private async Task<IResult> GetSubjectById(Guid id, CancellationToken cancellationToken)
    {
        var subject = await subjectRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (subject is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(subject);
    }

    private async Task<IResult> GetSubjectsByKind(string kind, CancellationToken cancellationToken)
    {
        var subjects = await subjectRepository.GetByKindAsync(kind, cancellationToken).ConfigureAwait(false);
        return Results.Ok(subjects.ToArray());
    }

    private async Task<IResult> GetSubjectsByTag(string tag, CancellationToken cancellationToken)
    {
        var subjects = await subjectRepository.GetByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        return Results.Ok(subjects.ToArray());
    }

    private async Task<IResult> CreateSubject(
        HttpContext httpContext,
        Subject request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var subject = request with
        {
            Id = Guid.NewGuid(),
            DateLastRefreshed = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await subjectRepository.CreateAsync(subject, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/subjects/{created.Id}", created);
    }

    private async Task<IResult> PatchSubject(
        HttpContext httpContext,
        Guid id,
        TagsPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var updated = await subjectRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!updated)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    private async Task<IResult> DeleteSubject(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var deleted = await subjectRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }
}
