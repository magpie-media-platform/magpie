using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Jobs;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class JobsEndpointGroup(IJobExecutionRepository jobExecutionRepository) : IApiEndpointGroup
{
    public string Name => "jobs";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllJobExecutions)
            .WithName("GetAllJobExecutions")
            .WithTags("Jobs")
            .Produces<JobExecution[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetJobExecutionById)
            .WithName("GetJobExecutionById")
            .WithTags("Jobs")
            .Produces<JobExecution>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-job-template/{jobTemplateId:guid}", GetJobExecutionsByJobTemplate)
            .WithName("GetJobExecutionsByJobTemplate")
            .WithTags("Jobs")
            .Produces<JobExecution[]>()
            .RequireAuthorization();

        builder.MapGet("/by-schedule-trigger/{scheduleTriggerId:guid}", GetJobExecutionsByScheduleTrigger)
            .WithName("GetJobExecutionsByScheduleTrigger")
            .WithTags("Jobs")
            .Produces<JobExecution[]>()
            .RequireAuthorization();

        builder.MapGet("/by-event-trigger/{eventTriggerId:guid}", GetJobExecutionsByEventTrigger)
            .WithName("GetJobExecutionsByEventTrigger")
            .WithTags("Jobs")
            .Produces<JobExecution[]>()
            .RequireAuthorization();

        builder.MapGet("/by-status/{status}", GetJobExecutionsByStatus)
            .WithName("GetJobExecutionsByStatus")
            .WithTags("Jobs")
            .Produces<JobExecution[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateJobExecution)
            .WithName("CreateJobExecution")
            .WithTags("Jobs")
            .Produces<JobExecution>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchJobExecution)
            .WithName("PatchJobExecution")
            .WithTags("Jobs")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteJobExecution)
            .WithName("DeleteJobExecution")
            .WithTags("Jobs")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/by-job-template/{jobTemplateId:guid}", DeleteJobExecutionsByJobTemplate)
            .WithName("DeleteJobExecutionsByJobTemplate")
            .WithTags("Jobs")
            .Produces<int>()
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllJobExecutions(CancellationToken cancellationToken)
    {
        var result = await jobExecutionRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<JobExecution>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<JobExecution>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetJobExecutionById(Guid id, CancellationToken cancellationToken)
    {
        var result = await jobExecutionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<JobExecution?> { Value: null } => Results.NotFound(),
            SuccessResult<JobExecution?> success => Results.Ok(success.Value),
            FailureResult<JobExecution?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetJobExecutionsByJobTemplate(
        Guid jobTemplateId,
        CancellationToken cancellationToken)
    {
        var result = await jobExecutionRepository.GetByJobTemplateIdAsync(jobTemplateId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<JobExecution>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<JobExecution>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetJobExecutionsByScheduleTrigger(
        Guid scheduleTriggerId,
        CancellationToken cancellationToken)
    {
        var result = await jobExecutionRepository.GetByScheduleTriggerIdAsync(scheduleTriggerId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<JobExecution>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<JobExecution>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetJobExecutionsByEventTrigger(
        Guid eventTriggerId,
        CancellationToken cancellationToken)
    {
        var result = await jobExecutionRepository.GetByEventTriggerIdAsync(eventTriggerId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<JobExecution>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<JobExecution>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetJobExecutionsByStatus(
        JobExecutionStatus status,
        CancellationToken cancellationToken)
    {
        var result = await jobExecutionRepository.GetByStatusAsync(status, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<JobExecution>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<JobExecution>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateJobExecution(
        JobExecution request,
        CancellationToken cancellationToken)
    {
        var jobExecution = request with
        {
            Id = Guid.NewGuid(),
            Status = request.Status == default ? JobExecutionStatus.Queued : request.Status,
            Parameters = request.Parameters ?? new Dictionary<string, string?>(),
            StartedAt = request.StartedAt == default ? DateTimeOffset.UtcNow : request.StartedAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await jobExecutionRepository.CreateAsync(jobExecution, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<JobExecution> success =>
                Results.Created($"/api/jobs/{success.Value.Id}", success.Value),
            FailureResult<JobExecution> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchJobExecution(
        Guid id,
        JobExecution request,
        CancellationToken cancellationToken)
    {
        var existingResult = await jobExecutionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return existingResult switch
        {
            SuccessResult<JobExecution?> { Value: null } => Results.NotFound(),
            SuccessResult<JobExecution?> { Value: { } existing } => await UpdateExistingJobExecution(
                id, request, existing, cancellationToken).ConfigureAwait(false),
            FailureResult<JobExecution?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> UpdateExistingJobExecution(
        Guid id,
        JobExecution request,
        JobExecution existing,
        CancellationToken cancellationToken)
    {
        var jobExecution = request with
        {
            Id = id,
            Parameters = request.Parameters ?? new Dictionary<string, string?>(),
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId,
            ModifiedAt = DateTimeOffset.UtcNow,
            ModifiedByUserId = null
        };

        var result = await jobExecutionRepository.UpdateAsync(jobExecution, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteJobExecution(Guid id, CancellationToken cancellationToken)
    {
        var result = await jobExecutionRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
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

    private async Task<HttpResult> DeleteJobExecutionsByJobTemplate(
        Guid jobTemplateId,
        CancellationToken cancellationToken)
    {
        var result = await jobExecutionRepository
            .DeleteByJobTemplateIdAsync(jobTemplateId, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<int> success => Results.Ok(success.Value),
            FailureResult<int> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
