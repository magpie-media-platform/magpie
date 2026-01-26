using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Content;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

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

    private async Task<HttpResult> GetAllSubjects(CancellationToken cancellationToken)
    {
        var result = await subjectRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Subject>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Subject>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetSubjectById(Guid id, CancellationToken cancellationToken)
    {
        var result = await subjectRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Subject?> { Value: null } => Results.NotFound(),
            SuccessResult<Subject?> success => Results.Ok(success.Value),
            FailureResult<Subject?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetSubjectsByKind(string kind, CancellationToken cancellationToken)
    {
        var result = await subjectRepository.GetByKindAsync(kind, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Subject>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Subject>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetSubjectsByTag(string tag, CancellationToken cancellationToken)
    {
        var result = await subjectRepository.GetByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Subject>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Subject>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateSubject(Subject request, CancellationToken cancellationToken)
    {
        var subject = request with
        {
            Id = Guid.NewGuid(),
            DateLastRefreshed = null,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await subjectRepository.CreateAsync(subject, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Subject> success =>
                Results.Created($"/api/subjects/{success.Value.Id}", success.Value),
            FailureResult<Subject> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchSubject(Guid id, SubjectPatch request, CancellationToken cancellationToken)
    {
        var patch = request with { Id = id };

        var result = await subjectRepository.PatchAsync(patch, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure => Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteSubject(Guid id, CancellationToken cancellationToken)
    {
        var result = await subjectRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
