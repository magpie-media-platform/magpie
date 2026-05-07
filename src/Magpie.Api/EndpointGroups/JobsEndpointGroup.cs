using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Security;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class JobsEndpointGroup(IJobManager jobManager) : IApiEndpointGroup
{
    public string Name => "jobs";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", QueryJobExecutions)
            .WithName(nameof(QueryJobExecutions))
            .Produces<PagedList<JobExecution>>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetJobExecutionById)
            .WithName(nameof(GetJobExecutionById))
            .Produces<JobExecution>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", RunJob)
            .WithName(nameof(RunJob))
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("{id:guid}/cancel", RequestJobCancellation)
            .WithName(nameof(RequestJobCancellation))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> QueryJobExecutions(
        string? jobType,
        Guid? extensionId,
        Guid? scheduleTriggerId,
        Guid? eventTriggerId,
        JobExecutionStatus? status,
        int? pageNumber,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var query = new JobExecutionQuery
        {
            JobType = jobType,
            ExtensionId = extensionId,
            ScheduleTriggerId = scheduleTriggerId,
            EventTriggerId = eventTriggerId,
            Status = status
        };

        var paging = pageNumber is not null && pageSize is not null
            ? new PagingParameter { PageNumber = pageNumber.Value, PageSize = pageSize.Value }
            : null;

        var sort = !string.IsNullOrEmpty(sortBy)
            ? new SortParameter { PropertyName = sortBy, Direction = sortDirection ?? SortDirection.Descending }
            : null;

        var result = await jobManager.QueryExecutionsAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private async Task<IResult> GetJobExecutionById(Guid id, CancellationToken cancellationToken)
    {
        var execution = await jobManager.GetExecutionAsync(id, cancellationToken).ConfigureAwait(false);

        return execution is null
            ? Results.NotFound()
            : Results.Ok(execution);
    }

    private async Task<IResult> RunJob(
        HttpContext httpContext,
        RunJobRequest request,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        var executionId = await jobManager.ExecuteAsync(
            request.JobType,
            request.ExtensionId,
            request.Parameters,
            request.Mode,
            userId,
            cancellationToken).ConfigureAwait(false);

        return Results.Accepted($"api/{Name}/{executionId}", executionId);
    }

    private async Task<IResult> RequestJobCancellation(Guid id, CancellationToken cancellationToken)
    {
        var cancelled = await jobManager.RequestCancellationAsync(id, cancellationToken).ConfigureAwait(false);

        return cancelled
            ? Results.NoContent()
            : Results.NotFound();
    }
}
