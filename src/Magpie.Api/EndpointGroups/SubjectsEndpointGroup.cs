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
        builder.MapGet("", GetSubjects)
            .WithName(nameof(GetSubjects))
            .Produces<Subject[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetSubjectById)
            .WithName(nameof(GetSubjectById))
            .Produces<Subject>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateSubject)
            .WithName(nameof(CreateSubject))
            .Produces<Subject>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapPatch("{id:guid}", PatchSubject)
            .WithName(nameof(PatchSubject))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapDelete("{id:guid}", DeleteSubject)
            .WithName(nameof(DeleteSubject))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetSubjects(string? kind, string? tag, CancellationToken cancellationToken)
    {
        if (kind is not null)
        {
            var subjectsByKind = await subjectRepository.GetByKindAsync(kind, cancellationToken).ConfigureAwait(false);
            return Results.Ok(subjectsByKind.ToArray());
        }

        if (tag is not null)
        {
            var subjectsByTag = await subjectRepository.GetByTagAsync(tag, cancellationToken).ConfigureAwait(false);
            return Results.Ok(subjectsByTag.ToArray());
        }

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

        return Results.Created($"api/{Name}/{created.Id}", created);
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
