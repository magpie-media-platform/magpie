using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("jobs", "jobs", "Manage and run jobs")]
internal sealed class JobsEndpointGroup : IApiEndpointGroup
{
    private const int DefaultExecutionTreeNodes = 500;

    public string Name => "jobs";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", QueryJobExecutionsAsync)
            .WithName(nameof(QueryJobExecutionsAsync))
            .Produces<PagedList<JobExecution>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapGet("specs", GetJobSpecs)
            .WithName(nameof(GetJobSpecs))
            .Produces<IReadOnlyList<JobSpec>>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetJobExecutionByIdAsync)
            .WithName(nameof(GetJobExecutionByIdAsync))
            .Produces<JobExecution>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("{id:guid}/execution-tree", GetJobExecutionTreeAsync)
            .WithName(nameof(GetJobExecutionTreeAsync))
            .Produces<JobExecutionTree>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("enqueue", EnqueueJobAsync)
            .WithName(nameof(EnqueueJobAsync))
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapPost("execute-immediate", ExecuteImmediateJobAsync)
            .WithName(nameof(ExecuteImmediateJobAsync))
            .Produces<RunJobResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapPost("{id:guid}/cancel", RequestJobCancellationAsync)
            .WithName(nameof(RequestJobCancellationAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    [MagpieCommand("list", "GET", "", "List job executions")]
    internal async Task<IResult> QueryJobExecutionsAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        string? jobType,
        Guid? extensionId,
        Guid? scheduleTriggerId,
        Guid? eventTriggerId,
        Guid? correlationId,
        string[]? status,
        bool? mine,
        bool? rootsOnly,
        int? pageNumber,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var parsedStatuses = new List<JobExecutionStatus>();
        var pending = false;
        if (status is { Length: > 0 })
        {
            foreach (var value in status)
            {
                if (string.Equals(value, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    pending = true;
                }
                else if (Enum.TryParse<JobExecutionStatus>(value, ignoreCase: true, out var parsed))
                {
                    parsedStatuses.Add(parsed);
                }
                else
                {
                    return Results.Problem(
                        "A status filter value is not a known status.",
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }
        }

        var statuses = parsedStatuses.Distinct().ToArray();

        var query = new JobExecutionQuery
        {
            JobType = jobType,
            ExtensionId = extensionId,
            ScheduleTriggerId = scheduleTriggerId,
            EventTriggerId = eventTriggerId,
            CorrelationId = correlationId,
            Statuses = statuses is { Length: > 0 } ? statuses : null,
            Pending = pending ? true : null,
            CreatedByUserId = mine == true ? user.Id : null,
            RootsOnly = rootsOnly ?? (mine == true),
        };

        var paging = new PagingParameter
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 25,
        };

        var sort = !string.IsNullOrEmpty(sortBy)
            ? new SortParameter { PropertyName = sortBy, Direction = sortDirection ?? SortDirection.Descending }
            : null;

        var result = await jobManager.QueryExecutionsAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    [MagpieCommand("specs", "GET", "specs", "List registered job specs")]
    internal IResult GetJobSpecs([FromServices] IJobManager jobManager, JobTriggerMode? userTriggerMode)
    {
        return Results.Ok(jobManager.GetSpecs(userTriggerMode));
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show a job execution")]
    internal async Task<IResult> GetJobExecutionByIdAsync(
        [FromServices] IJobManager jobManager,
        Guid id,
        CancellationToken cancellationToken)
    {
        var execution = await jobManager.GetExecutionAsync(id, cancellationToken).ConfigureAwait(false);

        return execution is null ? Results.NotFound() : Results.Ok(execution);
    }

    [MagpieCommand(
        "execution-tree",
        "GET",
        "{id:guid}/execution-tree",
        "Show a job execution's tree of related executions")]
    internal async Task<IResult> GetJobExecutionTreeAsync(
        [FromServices] IJobManager jobManager,
        Guid id,
        [FromQuery(Name = "include-siblings")] bool? includeSiblings,
        [FromQuery(Name = "max-nodes")] int? maxNodes,
        CancellationToken cancellationToken)
    {
        if (maxNodes < 1)
        {
            return Results.Problem(
                "The maximum node count must be 1 or more.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var tree = await jobManager
            .GetExecutionTreeAsync(
                id,
                includeSiblings ?? false,
                maxNodes ?? DefaultExecutionTreeNodes,
                cancellationToken)
            .ConfigureAwait(false);

        return tree is null ? Results.NotFound() : Results.Ok(tree);
    }

    [MagpieCommand("enqueue", "POST", "enqueue", "Enqueue a job to run in the background")]
    internal async Task<IResult> EnqueueJobAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        [FromBody] RunJobRequest request,
        CancellationToken cancellationToken)
    {
        var executionId = await jobManager.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = request.JobType,
                ExtensionId = request.ExtensionId,
                Parameters = request.Parameters,
                Mode = request.Mode,
                UserId = user.Id,
            },
            user.Id,
            user.Role,
            cancellationToken)
            .ConfigureAwait(false);

        return Results.Accepted($"/api/{Name}/{executionId}", executionId);
    }

    [MagpieCommand("run", "POST", "execute-immediate", "Run a job immediately")]
    internal async Task<IResult> ExecuteImmediateJobAsync(
        [FromServices] IJobManager jobManager,
        AuthenticatedUser user,
        [FromBody] RunJobRequest request,
        CancellationToken cancellationToken)
    {
        var execution = await jobManager.ExecuteImmediateAsync(
            new JobEnqueueRequest
            {
                JobType = request.JobType,
                ExtensionId = request.ExtensionId,
                Parameters = request.Parameters,
                Mode = JobExecutionMode.Immediate,
                UserId = user.Id,
            },
            user.Id,
            user.Role,
            cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new RunJobResponse
        {
            ExecutionId = execution.Id,
            Status = execution.Status,
            StatusMessage = execution.StatusMessage,
        });
    }

    [MagpieCommand("cancel", "POST", "{id:guid}/cancel", "Cancel a job execution")]
    internal async Task<IResult> RequestJobCancellationAsync(
        [FromServices] IJobManager jobManager,
        Guid id,
        CancellationToken cancellationToken)
    {
        var cancelled = await jobManager.RequestCancellationAsync(id, cancellationToken).ConfigureAwait(false);

        return cancelled ? Results.NoContent() : Results.NotFound();
    }
}
