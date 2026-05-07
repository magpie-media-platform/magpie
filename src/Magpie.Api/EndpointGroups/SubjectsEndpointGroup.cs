using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SubjectsEndpointGroup(ISubjectRepository subjectRepository) : IApiEndpointGroup
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Subject.Kind),
        nameof(Subject.DateLastRefreshed),
        nameof(Subject.CreatedAt),
        nameof(Subject.ModifiedAt)
    };

    public string Name => "subjects";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetSubjects)
            .WithName(nameof(GetSubjects))
            .Produces<Subject[]>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
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

    private async Task<IResult> GetSubjects(
        string? kind,
        string? tag,
        string? filter,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            return Results.BadRequest(new
            {
                message = "Subject filter expressions are not yet supported. "
                    + "Use the kind or tag query parameters to narrow results."
            });
        }

        if (sortBy is not null && !AllowedSortFields.Contains(sortBy))
        {
            return Results.BadRequest(new
            {
                message = $"Unknown sortBy field '{sortBy}'. "
                    + $"Allowed: {string.Join(", ", AllowedSortFields)}."
            });
        }

        IEnumerable<Subject> subjects;
        if (kind is not null)
        {
            subjects = await subjectRepository.GetByKindAsync(kind, cancellationToken).ConfigureAwait(false);
        }
        else if (tag is not null)
        {
            subjects = await subjectRepository.GetByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            subjects = await subjectRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        }

        if (sortBy is not null)
        {
            subjects = ApplySort(subjects, sortBy, sortDirection ?? SortDirection.Ascending);
        }

        return Results.Ok(subjects.ToArray());
    }

    private static IEnumerable<Subject> ApplySort(
        IEnumerable<Subject> subjects,
        string sortBy,
        SortDirection direction)
    {
        Func<Subject, object?> keySelector = sortBy switch
        {
            var s when s.Equals(nameof(Subject.Kind), StringComparison.OrdinalIgnoreCase) => s => s.Kind,
            var s when s.Equals(nameof(Subject.DateLastRefreshed), StringComparison.OrdinalIgnoreCase) =>
                s => s.DateLastRefreshed,
            var s when s.Equals(nameof(Subject.CreatedAt), StringComparison.OrdinalIgnoreCase) => s => s.CreatedAt,
            var s when s.Equals(nameof(Subject.ModifiedAt), StringComparison.OrdinalIgnoreCase) => s => s.ModifiedAt,
            _ => s => s.Kind
        };

        return direction == SortDirection.Descending
            ? subjects.OrderByDescending(keySelector)
            : subjects.OrderBy(keySelector);
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
