using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class JobManager(
    IJobRuntime runtime,
    IJobExecutionRepository executionRepository,
    IScheduleTriggerRepository scheduleTriggerRepository,
    IEventTriggerRepository eventTriggerRepository,
    ILogger<JobManager> logger) : IJobManager
{
    public async Task<Guid> ExecuteAsync(
        string jobType,
        Guid? extensionId,
        IReadOnlyDictionary<string, string>? parameters,
        JobExecutionMode mode,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Executing job {JobType} by user {UserId} with mode {Mode}", jobType, userId, mode);

        var execution = new JobExecution
        {
            ExtensionId = extensionId,
            JobType = jobType,
            Scope = JobScope.System,
            Parameters = parameters is not null ? new Dictionary<string, string>(parameters) : [],
            Status = JobExecutionStatus.Queued,
        };

        var created = await executionRepository.CreateAsync(execution, userId, cancellationToken).ConfigureAwait(false);

        if (mode is JobExecutionMode.Immediate)
        {
            await runtime.ExecuteAsync(created.Id, cancellationToken).ConfigureAwait(false);
        }

        return created.Id;
    }

    public async Task<PagedList<JobExecution>> QueryExecutionsAsync(
        JobExecutionQuery query,
        PagingParameter? paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        return await executionRepository.QueryAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JobExecution?> GetExecutionAsync(
        Guid executionId,
        CancellationToken cancellationToken)
    {
        return await executionRepository.GetByIdAsync(executionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RequestCancellationAsync(
        Guid executionId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Requesting cancellation of execution {ExecutionId}", executionId);

        return await runtime.TryCancelAsync(executionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ScheduleTrigger> CreateScheduleTriggerAsync(
        ScheduleTriggerCreate create,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var trigger = new ScheduleTrigger
        {
            ExtensionId = create.ExtensionId,
            JobType = create.JobType,
            Scope = create.Scope,
            Name = create.Name,
            Schedule = create.Schedule,
            ExecutionMode = create.ExecutionMode,
            Enabled = create.Enabled,
            Parameters = create.Parameters,
        };

        return await scheduleTriggerRepository.CreateAsync(trigger, userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> UpdateScheduleTriggerAsync(
        ScheduleTriggerPatch patch,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return await scheduleTriggerRepository.PatchAsync(patch, userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EventTrigger> CreateEventTriggerAsync(
        EventTriggerCreate create,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var trigger = new EventTrigger
        {
            ExtensionId = create.ExtensionId,
            JobType = create.JobType,
            Scope = create.Scope,
            Name = create.Name,
            EventFilter = create.EventFilter,
            ExecutionMode = create.ExecutionMode,
            Enabled = create.Enabled,
            Parameters = create.Parameters,
        };

        return await eventTriggerRepository.CreateAsync(trigger, userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> UpdateEventTriggerAsync(
        EventTriggerPatch patch,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return await eventTriggerRepository.PatchAsync(patch, userId, cancellationToken)
            .ConfigureAwait(false);
    }
}
