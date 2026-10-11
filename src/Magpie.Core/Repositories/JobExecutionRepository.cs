using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Jobs;
using Magpie.Data;
using Magpie.Data.Configuration;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;

using ContentEventImpact = Magpie.Sdk.Events.ContentEventImpact;

namespace Magpie.Core.Repositories;

internal sealed class JobExecutionRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<JobExecutionRepository> logger,
    TimeProvider timeProvider,
    IJobRegistry jobRegistry) : IJobExecutionRepository
{
    private readonly TimeProvider _time = timeProvider;

    private const string DedupInsertSavepoint = "job_execution_dedup_insert";

    private const string CoalescingLockEntityName = "JobEnqueueCoalescingLock";

    private const string CoalescingLockKeyProperty = "Key";

    private static ContentEventImpact? ParameterImpact(JobExecution execution)
    {
        var hierarchyItemId = ParameterId(execution, MagpieJobs.Parameters.ContentHierarchyItemId);
        var contentItemId = ParameterId(execution, MagpieJobs.Parameters.ContentItemId);
        return hierarchyItemId is null && contentItemId is null
            ? null
            : new() { HierarchyItemId = hierarchyItemId, ItemId = contentItemId };
    }

    private static Guid? ParameterId(JobExecution execution, string parameterName)
    {
        return execution.Parameters.TryGetValue(parameterName, out var raw) && Guid.TryParse(raw, out var id)
            ? id
            : null;
    }

    private static bool IsTrue(IReadOnlyDictionary<string, string> parameters, string field) =>
        parameters.TryGetValue(field, out var raw) && bool.TryParse(raw, out var value) && value;

    private static string? ResolveParameter(JobExecution execution, JobSpec? spec, string field)
    {
        var value = execution.Parameters.GetValueOrDefault(field);
        if (string.IsNullOrWhiteSpace(value) &&
            spec?.LockPartitionEventContextKeys.GetValueOrDefault(field) is { } eventContextKey)
        {
            value = execution.TriggerContext.GetValueOrDefault(eventContextKey.ToEventContextKey());
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateTime? ReconcileNotBeforeAt(DateTime? existing, DateTime? incoming, DateTime now)
    {
        if (existing is { } existingDeferral &&
            existingDeferral > now &&
            incoming is { } incomingDeferral &&
            incomingDeferral > now &&
            incomingDeferral < existingDeferral)
        {
            return incomingDeferral;
        }

        return existing;
    }

    private static string NormalizeReferenceValue(string value)
    {
        var bounded = value.Length > JobExecutionReferenceColumns.MaxLength
            ? value[..JobExecutionReferenceColumns.MaxLength]
            : value;
        return bounded.ToLowerInvariant();
    }

    private static string BuildCoalescingLockKey(string jobType, string lockPartition)
    {
        var jobTypeBytes = Encoding.UTF8.GetBytes(jobType);
        var lockPartitionBytes = Encoding.UTF8.GetBytes(lockPartition);
        var partitionLengthOffset = sizeof(int) + jobTypeBytes.Length;
        var identity = new byte[partitionLengthOffset + sizeof(int) + lockPartitionBytes.Length];

        BinaryPrimitives.WriteInt32BigEndian(identity, jobTypeBytes.Length);
        jobTypeBytes.CopyTo(identity.AsSpan(sizeof(int)));
        BinaryPrimitives.WriteInt32BigEndian(identity.AsSpan(partitionLengthOffset), lockPartitionBytes.Length);
        lockPartitionBytes.CopyTo(identity.AsSpan(partitionLengthOffset + sizeof(int)));

        return Convert.ToHexString(SHA256.HashData(identity));
    }

    private static bool IsPartitionUniqueViolation(DbException exception)
    {
        return exception.SqlState == "23505" &&
                exception.Message.Contains("UX_job_execution_LockPartition_Running", StringComparison.Ordinal) ||
            exception.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
                exception.GetType().GetProperty("Number")?.GetValue(exception) is 2601 or 2627 &&
                exception.Message.Contains("UX_job_execution_LockPartition_Running", StringComparison.Ordinal) ||
            exception.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException" &&
                exception.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(exception) is 2067 &&
                exception.Message.Contains("job_execution.LockPartition", StringComparison.Ordinal);
    }

    private async Task<EntityEntry<Dictionary<string, object>>> AcquireCoalescingLockAsync(
        string jobType,
        string lockPartition,
        CancellationToken cancellationToken)
    {
        var key = BuildCoalescingLockKey(jobType, lockPartition);
        var lockRows = context.Set<Dictionary<string, object>>(CoalescingLockEntityName);
        var lockRow = new Dictionary<string, object> { [CoalescingLockKeyProperty] = key };

        var lockEntry = lockRows.Add(lockRow);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return lockEntry;
    }

    private async Task ReleaseCoalescingLockAsync(EntityEntry<Dictionary<string, object>> lockEntry)
    {
        var lockRow = lockEntry.Entity;
        var key = (string)lockRow[CoalescingLockKeyProperty];
        var lockRows = context.Set<Dictionary<string, object>>(CoalescingLockEntityName);
        int deleted;

        try
        {
            deleted = await lockRows
                .Where(row => EF.Property<string>(row, CoalescingLockKeyProperty) == key)
                .ExecuteDeleteAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            lockEntry.State = EntityState.Detached;
        }

        if (deleted != 1)
        {
            throw new InvalidOperationException(
                $"Expected one job execution coalescing lock row for key {key}, but deleted {deleted}.");
        }
    }

    public async Task<IEnumerable<JobExecution>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.JobExecutions
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The job execution repository failed to list job executions.");
            throw;
        }
    }

    public async Task<JobExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.JobExecutions
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The job execution repository failed to read job execution {ExecutionId}.", id);
            throw;
        }
    }

    public async Task<PagedList<JobExecution>> QueryAsync(
        JobExecutionQuery query,
        PagingParameter page,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var jobExecutions = BuildFilteredQuery(query, now);

            var totalCount = await jobExecutions.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = context.Model.FindEntityType(typeof(JobExecution))!.GetProperties()
                .Where(property => !property.IsShadowProperty() &&
                    (property.ClrType.IsValueType || property.ClrType == typeof(string) ||
                        property.ClrType == typeof(Uri)))
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && string.Equals(
                sort.PropertyName,
                nameof(JobExecutionQuery.ExecutionTimestamp),
                StringComparison.OrdinalIgnoreCase))
            {
                jobExecutions = sort.Direction == SortDirection.Ascending
                    ? jobExecutions.OrderBy(e => e.CompletedAt ?? e.StartedAt ?? e.CreatedAt).ThenBy(e => e.Id)
                    : jobExecutions.OrderByDescending(e => e.CompletedAt ?? e.StartedAt ?? e.CreatedAt)
                        .ThenBy(e => e.Id);
            }
            else if (sort is not null && sortProperty is not null)
            {
                jobExecutions = sort.Direction == SortDirection.Ascending
                    ? jobExecutions.OrderBy(e => EF.Property<object>(e, sortProperty)).ThenBy(e => e.Id)
                    : jobExecutions.OrderByDescending(e => EF.Property<object>(e, sortProperty))
                        .ThenBy(e => e.Id);
            }
            else
            {
                jobExecutions = jobExecutions.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id);
            }

            jobExecutions = jobExecutions.Skip((page.PageNumber - 1) * page.PageSize).Take(page.PageSize);

            var items = await jobExecutions.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The job execution repository failed to query job executions.");
            throw;
        }
    }

    public async Task<int> DeleteWhereAsync(JobExecutionQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            return await BuildFilteredQuery(query, now)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The job execution repository failed to delete job executions by query.");
            throw;
        }
    }

    private IQueryable<JobExecution> BuildFilteredQuery(JobExecutionQuery query, DateTime now)
    {
        var jobExecutions = query.ParameterValueReferences is { Count: > 0 } references
            ? BuildParameterValueReferenceQuery(references)
            : context.JobExecutions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.JobType))
        {
            jobExecutions = jobExecutions.Where(e => e.JobType == query.JobType);
        }

        if (query.JobTypes is { Count: > 0 } jobTypes)
        {
            var types = jobTypes as string[] ?? [.. jobTypes];
            jobExecutions = jobExecutions.Where(e => types.Contains(e.JobType));
        }

        if (query.ExtensionId is not null)
        {
            jobExecutions = jobExecutions.Where(e => e.ExtensionId == query.ExtensionId);
        }

        if (query.ScheduleTriggerId is not null)
        {
            jobExecutions = jobExecutions.Where(e => e.ScheduleTriggerId == query.ScheduleTriggerId);
        }

        if (query.EventTriggerId is not null)
        {
            jobExecutions = jobExecutions.Where(e => e.EventTriggerId == query.EventTriggerId);
        }

        if (query.NodeId is { } nodeId)
        {
            jobExecutions = jobExecutions.Where(e => e.NodeId == nodeId);
        }

        var statusFilter = query.Statuses is { Count: > 0 }
            ? query.Statuses as JobExecutionStatus[] ?? [.. query.Statuses]
            : query.Status is { } singleStatus ? [singleStatus] : [];
        if (statusFilter.Length > 0)
        {
            jobExecutions = jobExecutions.Where(e => statusFilter.Contains(e.Status));
        }

        if (query.Pending is { } pending)
        {
            jobExecutions = pending
                ? jobExecutions.Where(e => e.Status == JobExecutionStatus.Queued &&
                    e.NotBeforeAt != null &&
                    e.NotBeforeAt > now)
                : jobExecutions.Where(e => !(e.Status == JobExecutionStatus.Queued &&
                    e.NotBeforeAt != null &&
                    e.NotBeforeAt > now));
        }

        if (query.CorrelationId is not null)
        {
            jobExecutions = jobExecutions.Where(e => e.CorrelationId == query.CorrelationId);
        }

        if (query.RootsOnly)
        {
            jobExecutions = jobExecutions.Where(e => e.ParentExecutionId == null);
        }

        if (query.CreatedByUserId is not null)
        {
            jobExecutions = jobExecutions.Where(e => e.CreatedByUserId == query.CreatedByUserId);
        }

        if (query.Timestamp?.Min is not null)
        {
            jobExecutions = jobExecutions.Where(e => e.CreatedAt >= query.Timestamp.Min);
        }

        if (query.Timestamp?.Max is not null)
        {
            jobExecutions = jobExecutions.Where(e => e.CreatedAt <= query.Timestamp.Max);
        }

        if (query.CompletedAt?.Min is { } completedAtMin)
        {
            jobExecutions = jobExecutions.Where(e => e.CompletedAt.HasValue && e.CompletedAt.Value >= completedAtMin);
        }

        if (query.CompletedAt?.Max is { } completedAtMax)
        {
            jobExecutions = jobExecutions.Where(e => e.CompletedAt.HasValue && e.CompletedAt.Value <= completedAtMax);
        }

        if (query.ExecutionTimestamp?.Min is { } executionTimestampMin)
        {
            jobExecutions = jobExecutions.Where(e =>
                (e.CompletedAt ?? e.StartedAt ?? e.CreatedAt) >= executionTimestampMin);
        }

        if (query.ExecutionTimestamp?.Max is { } executionTimestampMax)
        {
            jobExecutions = jobExecutions.Where(e =>
                (e.CompletedAt ?? e.StartedAt ?? e.CreatedAt) <= executionTimestampMax);
        }

        if (query.LastHeartbeat?.Min is { } lastHeartbeatMin)
        {
            jobExecutions = jobExecutions.Where(e =>
                e.LastHeartbeat.HasValue && e.LastHeartbeat.Value >= lastHeartbeatMin);
        }

        if (query.LastHeartbeat?.Max is { } lastHeartbeatMax)
        {
            jobExecutions = jobExecutions.Where(e =>
                e.LastHeartbeat.HasValue && e.LastHeartbeat.Value <= lastHeartbeatMax);
        }

        return jobExecutions;
    }

    private IQueryable<JobExecution> BuildParameterValueReferenceQuery(IReadOnlyCollection<string> references)
    {
        var distinct = references
            .Select(NormalizeReferenceValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var hierarchyItemTargetTypes = ListEventTargetJobTypes(MagpieEvents.Keys.ContentHierarchyItemId);
        var contentItemTargetTypes = ListEventTargetJobTypes(MagpieEvents.Keys.ContentItemId);
        var downloadTargetTypes = ListEventTargetJobTypes(MagpieEvents.Keys.DownloadId);

        return context.JobExecutions
            .AsNoTracking()
            .Where(execution =>
                distinct.Contains(EF.Property<string>(
                    execution,
                    JobExecutionReferenceColumns.ParameterContentHierarchyItemId)) ||
                distinct.Contains(EF.Property<string>(
                    execution,
                    JobExecutionReferenceColumns.ParameterContentItemId)) ||
                distinct.Contains(EF.Property<string>(execution, JobExecutionReferenceColumns.ParameterDownloadId)) ||
                (hierarchyItemTargetTypes.Contains(execution.JobType) &&
                    distinct.Contains(EF.Property<string>(
                        execution,
                        JobExecutionReferenceColumns.TriggerContextContentHierarchyItemId))) ||
                (contentItemTargetTypes.Contains(execution.JobType) &&
                    distinct.Contains(EF.Property<string>(
                        execution,
                        JobExecutionReferenceColumns.TriggerContextContentItemId))) ||
                (downloadTargetTypes.Contains(execution.JobType) &&
                    distinct.Contains(EF.Property<string>(
                        execution,
                        JobExecutionReferenceColumns.TriggerContextDownloadId))));
    }

    private string[] ListEventTargetJobTypes(string eventContextKey) =>
    [
        .. jobRegistry.ListJobSpecs()
            .Where(spec => spec.LockPartitionEventContextKeys.Values.Contains(eventContextKey))
            .Select(spec => spec.JobType)
    ];

    /// <inheritdoc />
    public Task<JobExecution> CreateAsync(
        JobExecution model,
        Guid? createdByUserId,
        CancellationToken cancellationToken) =>
        CreateCoreAsync(model, createdByUserId, false, cancellationToken);

    /// <inheritdoc />
    public Task<JobExecution> CreateIfInactiveAsync(
        JobExecution model,
        Guid? createdByUserId,
        CancellationToken cancellationToken) =>
        CreateCoreAsync(model, createdByUserId, true, cancellationToken);

    private async Task<JobExecution> CreateCoreAsync(
        JobExecution model,
        Guid? createdByUserId,
        bool onlyIfInactive,
        CancellationToken cancellationToken)
    {
        try
        {
            if (onlyIfInactive && (string.IsNullOrWhiteSpace(model.LockPartition) ||
                model.Status != JobExecutionStatus.Queued || model.ExecutionMode != JobExecutionMode.Queue))
            {
                throw new ArgumentException(
                    "Inactive-only enqueue requires a queued execution and lock partition.",
                    nameof(model));
            }

            var inserted = false;
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    EntityEntry<Dictionary<string, object>>? coalescingLock = null;
                    if (model.LockPartition is { } lockPartition)
                    {
                        coalescingLock = await AcquireCoalescingLockAsync(
                            model.JobType,
                            lockPartition,
                            cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var failed = false;
                    try
                    {
                        var now = _time.GetUtcNow().UtcDateTime;
                        model.CreatedAt = now;
                        model.CreatedByUserId = createdByUserId;

                        if (!string.IsNullOrEmpty(model.DedupKey))
                        {
                            var existingByKey = await context.JobExecutions
                                .AsNoTracking()
                                .FirstOrDefaultAsync(e => e.DedupKey == model.DedupKey, cancellationToken)
                                .ConfigureAwait(false);
                            if (existingByKey is not null)
                            {
                                return existingByKey;
                            }
                        }

                        if (model.LockPartition is { } partition)
                        {
                            var coalesced = await TryCoalesceAsync(
                                model,
                                partition,
                                createdByUserId,
                                onlyIfInactive,
                                cancellationToken)
                                .ConfigureAwait(false);
                            if (coalesced is not null)
                            {
                                return coalesced;
                            }
                        }

                        var transaction = context.Database.CurrentTransaction;
                        var savepointTaken = transaction is { SupportsSavepoints: true };
                        if (savepointTaken)
                        {
                            await transaction!.CreateSavepointAsync(DedupInsertSavepoint, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        try
                        {
                            context.JobExecutions.Add(model);
                            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (DbUpdateException ex)
                            when (model.LockPartition is not null || !string.IsNullOrEmpty(model.DedupKey))
                        {
                            context.Entry(model).State = EntityState.Detached;

                            if (savepointTaken)
                            {
                                await transaction!.RollbackToSavepointAsync(DedupInsertSavepoint, cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            if (!string.IsNullOrEmpty(model.DedupKey))
                            {
                                var existingByKey = await context.JobExecutions
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(e => e.DedupKey == model.DedupKey, cancellationToken)
                                    .ConfigureAwait(false);
                                if (existingByKey is not null)
                                {
                                    logger.LogDebug(
                                        ex,
                                        "The {JobType} event enqueue lost the {DedupKey} race " +
                                        "and will use {ExecutionId}",
                                        model.JobType,
                                        model.DedupKey,
                                        existingByKey.Id);
                                    return existingByKey;
                                }
                            }

                            if (model.LockPartition is { } conflictPartition)
                            {
                                var coalesced = await TryCoalesceAsync(
                                    model,
                                    conflictPartition,
                                    createdByUserId,
                                    onlyIfInactive,
                                    cancellationToken)
                                    .ConfigureAwait(false);
                                if (coalesced is not null)
                                {
                                    logger.LogDebug(
                                        ex,
                                        "The {JobType} enqueue lost the {LockPartition} race " +
                                        "and will use {ExecutionId}",
                                        model.JobType,
                                        conflictPartition,
                                        coalesced.Id);
                                    return coalesced;
                                }
                            }

                            throw;
                        }

                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Jobs,
                                Operation = "Create",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(JobExecution),
                                Detail = $"Created job run for {model.JobType}.",
                                Timestamp = now,
                                UserId = createdByUserId,
                                CorrelationId = model.CorrelationId
                            },
                            cancellationToken).ConfigureAwait(false);

                        inserted = true;
                        return model;
                    }
                    catch
                    {
                        failed = true;
                        throw;
                    }
                    finally
                    {
                        if (coalescingLock is not null)
                        {
                            try
                            {
                                await ReleaseCoalescingLockAsync(coalescingLock).ConfigureAwait(false);
                            }
                            catch (Exception ex) when (failed)
                            {
                                logger.LogError(ex, "The coalescing lock release failed after an enqueue failure.");
                            }
                        }
                    }
                },
                cancellationToken).ConfigureAwait(false);

            if (inserted)
            {
                await transactionManager.OnCommittedAsync(
                    async token =>
                    {
                        var impact = ParameterImpact(created);
                        await eventBus.PublishAsync(
                            JobEventBuilder.BuildJobExecutionStateChangedEvent(
                                MagpieEvents.Names.JobExecutionCreated,
                                created.Id,
                                impact,
                                created.JobType),
                            token).ConfigureAwait(false);
                        if (created.Status is JobExecutionStatus.Queued)
                        {
                            await eventBus.PublishAsync(
                                JobEventBuilder.BuildJobExecutionStateChangedEvent(
                                    MagpieEvents.Names.JobExecutionQueued,
                                    created.Id,
                                    impact,
                                    created.JobType),
                                token).ConfigureAwait(false);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to create a job execution for {JobType}.",
                model.JobType);
            throw;
        }
    }

    private async Task<JobExecution?> TryCoalesceAsync(
        JobExecution incoming,
        string partition,
        Guid? createdByUserId,
        bool onlyIfInactive,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var matching = context.JobExecutions
            .AsNoTracking()
            .Where(e => e.JobType == incoming.JobType && e.LockPartition == partition);

        if (onlyIfInactive)
        {
            return await matching
                .Where(e => e.Status == JobExecutionStatus.Queued || e.Status == JobExecutionStatus.Running ||
                    e.Status == JobExecutionStatus.CancellationRequested)
                .OrderBy(e => e.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        JobExecution? existing;
        if (incoming.NotBeforeAt is { } incomingDeferral && incomingDeferral > now)
        {
            existing = await matching
                .Where(e => e.Status == JobExecutionStatus.Queued && e.NotBeforeAt != null && e.NotBeforeAt > now)
                .OrderBy(e => e.NotBeforeAt)
                .ThenBy(e => e.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing = await matching
                .Where(e => e.Status == JobExecutionStatus.Queued &&
                    (e.NotBeforeAt == null || e.NotBeforeAt <= now))
                .OrderBy(e => e.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (existing is null)
        {
            return null;
        }

        var upstreamDeferred = existing.StatusMessage == MagpieJobs.StatusMessages.UpstreamThrottled;
        var reconciled = upstreamDeferred ? existing.NotBeforeAt
            : ReconcileNotBeforeAt(existing.NotBeforeAt, incoming.NotBeforeAt, now);
        jobRegistry.TryGetJobSpec(incoming.JobType, out var spec);
        var sameTarget = string.Equals(
            ResolveParameter(existing, spec, MagpieJobs.Parameters.ContentHierarchyItemId),
            ResolveParameter(incoming, spec, MagpieJobs.Parameters.ContentHierarchyItemId),
            StringComparison.OrdinalIgnoreCase);
        var statusMessage = upstreamDeferred
            ? MagpieJobs.Parameters.IsForced(incoming.Parameters) ? incoming.StatusMessage : existing.StatusMessage
            : sameTarget ? incoming.StatusMessage ?? existing.StatusMessage : existing.StatusMessage;

        var trueWins = (spec?.CoalesceTrueWinsFields ?? [])
            .Where(field => IsTrue(incoming.Parameters, field) && !IsTrue(existing.Parameters, field))
            .ToList();
        var widening = (spec?.CoalesceWideningFields ?? [])
            .Where(field => ResolveParameter(existing, spec, field) is { } value &&
                !string.Equals(value, ResolveParameter(incoming, spec, field), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (reconciled != existing.NotBeforeAt ||
            statusMessage != existing.StatusMessage ||
            trueWins.Count > 0 ||
            widening.Count > 0)
        {
            foreach (var field in widening)
            {
                existing.Parameters.Remove(field);
                if (spec?.LockPartitionEventContextKeys.GetValueOrDefault(field) is { } eventContextKey)
                {
                    existing.TriggerContext.Remove(eventContextKey.ToEventContextKey());
                }
            }

            foreach (var field in trueWins)
            {
                existing.Parameters[field] = "true";
            }

            existing.NotBeforeAt = reconciled;
            existing.StatusMessage = statusMessage;
            existing.ModifiedAt = _time.GetUtcNow().UtcDateTime;
            var written = await context.JobExecutions
                .Where(e => e.Id == existing.Id && e.Status == JobExecutionStatus.Queued)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(e => e.Parameters, existing.Parameters)
                        .SetProperty(e => e.TriggerContext, existing.TriggerContext)
                        .SetProperty(e => e.NotBeforeAt, existing.NotBeforeAt)
                        .SetProperty(e => e.StatusMessage, existing.StatusMessage)
                        .SetProperty(e => e.ModifiedAt, existing.ModifiedAt),
                    cancellationToken)
                .ConfigureAwait(false);
            if (written == 0)
            {
                logger.LogDebug(
                    "The {JobType} enqueue could not join execution {ExecutionId} because it left the queue",
                    incoming.JobType,
                    existing.Id);
                return null;
            }

            await auditRecordRepository.CreateAsync(
                new()
                {
                    Category = AuditCategories.Jobs,
                    Operation = "Coalesce",
                    EntityId = existing.Id.ToString(),
                    EntityType = nameof(JobExecution),
                    Detail = $"Coalesced enqueue for {incoming.JobType} onto {existing.Id}; " +
                        (trueWins.Count > 0 || widening.Count > 0
                            ? string.Join(
                                ", ",
                                trueWins.Select(field => $"set {field} to true")
                                    .Concat(widening.Select(field => $"removed {field}")))
                            : "updated queue eligibility or the waiting reason"),
                    Timestamp = existing.ModifiedAt.Value,
                    UserId = createdByUserId,
                    CorrelationId = existing.CorrelationId
                },
                cancellationToken).ConfigureAwait(false);
        }

        logger.LogDebug(
            "The {JobType} enqueue will use existing execution {ExecutionId} that holds partition {LockPartition}",
            incoming.JobType,
            existing.Id,
            partition);

        return existing;
    }

    public async Task<bool> UpdateAsync(JobExecution model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var updated = await context.JobExecutions
                        .Where(e => e.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(e => e.ScheduleTriggerId, model.ScheduleTriggerId)
                                .SetProperty(e => e.EventTriggerId, model.EventTriggerId)
                                .SetProperty(e => e.ExtensionId, model.ExtensionId)
                                .SetProperty(e => e.JobType, model.JobType)
                                .SetProperty(e => e.Scope, model.Scope)
                                .SetProperty(e => e.TriggerContext, model.TriggerContext)
                                .SetProperty(e => e.Parameters, model.Parameters)
                                .SetProperty(e => e.ExecutionMode, model.ExecutionMode)
                                .SetProperty(e => e.Progress, model.Progress)
                                .SetProperty(e => e.Status, model.Status)
                                .SetProperty(e => e.StatusMessage, model.StatusMessage)
                                .SetProperty(e => e.NodeId, model.NodeId)
                                .SetProperty(e => e.LastHeartbeat, model.LastHeartbeat)
                                .SetProperty(e => e.NotBeforeAt, model.NotBeforeAt)
                                .SetProperty(e => e.Attempt, model.Attempt)
                                .SetProperty(e => e.OriginalExecutionId, model.OriginalExecutionId)
                                .SetProperty(e => e.ParentExecutionId, model.ParentExecutionId)
                                .SetProperty(e => e.CorrelationId, model.CorrelationId)
                                .SetProperty(e => e.StartedAt, model.StartedAt)
                                .SetProperty(e => e.CompletedAt, model.CompletedAt)
                                .SetProperty(e => e.CancellationRequestedAt, model.CancellationRequestedAt)
                                .SetProperty(e => e.ModifiedAt, now),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Jobs,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(JobExecution),
                                Detail = $"Updated job run {model.Id}.",
                                Timestamp = now,
                                UserId = modifiedByUserId,
                                CorrelationId = model.CorrelationId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The job execution repository failed to update job execution {ExecutionId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var deleted = await context.JobExecutions
                        .Where(e => e.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Jobs,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(JobExecution),
                                Detail = $"Deleted job run {id}.",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The job execution repository failed to delete job execution {ExecutionId}.", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<JobClaimCandidate>> GetClaimableCandidatesAsync(
        PagingParameter paging,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var claimable = context.JobExecutions.AsNoTracking()
                .Where(e => e.Status == JobExecutionStatus.Queued &&
                    e.NodeId == null &&
                    (e.NotBeforeAt == null || e.NotBeforeAt <= now) &&
                    (e.LockPartition == null ||
                        !context.JobExecutions.Any(x =>
                            (x.Status == JobExecutionStatus.Running ||
                                x.Status == JobExecutionStatus.CancellationRequested) &&
                            x.LockPartition != null &&
                            x.LockPartition == e.LockPartition)));

            var jobTypes = await claimable
                .Select(e => e.JobType)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (jobTypes.Count == 0)
            {
                return [];
            }

            var headPerJobType = Math.Max(1, paging.PageSize / jobTypes.Count);
            var heads = jobTypes
                .Select(jobType => claimable
                    .Where(e => e.JobType == jobType)
                    .OrderBy(e => e.CreatedAt)
                    .ThenBy(e => e.Id)
                    .Take(headPerJobType))
                .Aggregate(Queryable.Concat);
            return await heads
                .OrderBy(e => e.CreatedAt)
                .ThenBy(e => e.Id)
                .Select(e => new JobClaimCandidate(e.Id, e.JobType, e.LockPartition))
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The job execution repository failed to query claimable job matches.");
            throw;
        }
    }

    public async Task<JobExecution?> TryClaimAsync(Guid executionId, string nodeId, CancellationToken cancellationToken)
    {
        try
        {
            var claimed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var partition = await (
                        from e in context.JobExecutions.AsNoTracking()
                        where e.Id == executionId
                        select e.LockPartition)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var transaction = context.Database.CurrentTransaction;
                    const string savepoint = "job_execution_partition_claim";
                    if (transaction is { SupportsSavepoints: true })
                    {
                        await transaction.CreateSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
                    }

                    var updated = 0;
                    try
                    {
                        updated = await context.JobExecutions
                            .Where(e => e.Id == executionId &&
                                e.Status == JobExecutionStatus.Queued &&
                                (e.NotBeforeAt == null || e.NotBeforeAt <= now) &&
                                e.NodeId == null &&
                                (partition == null ||
                                    !context.JobExecutions.Any(x =>
                                        (x.Status == JobExecutionStatus.Running ||
                                            x.Status == JobExecutionStatus.CancellationRequested) &&
                                        x.LockPartition == partition)))
                            .ExecuteUpdateAsync(
                                s => s
                                    .SetProperty(e => e.Status, JobExecutionStatus.Running)
                                    .SetProperty(e => e.NodeId, nodeId)
                                    .SetProperty(e => e.StartedAt, now)
                                    .SetProperty(e => e.LastHeartbeat, now)
                                    .SetProperty(e => e.ModifiedAt, now),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (DbException ex) when (IsPartitionUniqueViolation(ex))
                    {
                        if (transaction is { SupportsSavepoints: true })
                        {
                            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        logger.LogDebug(
                            ex,
                            "The claim on partition {LockPartition} for candidate {ExecutionId} lost the race",
                            partition,
                            executionId);
                        return null;
                    }

                    if (updated == 0)
                    {
                        return null;
                    }

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Jobs,
                            Operation = "Dequeue",
                            EntityId = executionId.ToString(),
                            EntityType = nameof(JobExecution),
                            Detail = $"Job run {executionId} was assigned to Magpie instance {nodeId}.",
                            Timestamp = now
                        },
                        cancellationToken).ConfigureAwait(false);

                    return await context.JobExecutions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (claimed is not null)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        JobEventBuilder.BuildJobExecutionStateChangedEvent(
                            MagpieEvents.Names.JobExecutionStarted,
                            executionId,
                            ParameterImpact(claimed),
                            claimed.JobType),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return claimed;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to claim job execution {ExecutionId} for node {NodeId}.",
                executionId,
                nodeId);
            throw;
        }
    }

    public async Task<bool> TryStartAsync(Guid executionId, string nodeId, CancellationToken cancellationToken)
    {
        try
        {
            var started = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var partition = await (
                        from e in context.JobExecutions.AsNoTracking()
                        where e.Id == executionId
                        select e.LockPartition)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var transaction = context.Database.CurrentTransaction;
                    const string savepoint = "job_execution_partition_claim";
                    if (transaction is { SupportsSavepoints: true })
                    {
                        await transaction.CreateSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
                    }

                    var updated = 0;
                    try
                    {
                        updated = await context.JobExecutions
                            .Where(e => e.Id == executionId &&
                                e.Status == JobExecutionStatus.Queued &&
                                (e.NotBeforeAt == null || e.NotBeforeAt <= now) &&
                                (e.NodeId == null || e.NodeId == nodeId) &&
                                (partition == null ||
                                    !context.JobExecutions.Any(x =>
                                        (x.Status == JobExecutionStatus.Running ||
                                            x.Status == JobExecutionStatus.CancellationRequested) &&
                                        x.LockPartition == partition)))
                            .ExecuteUpdateAsync(
                                s => s
                                    .SetProperty(e => e.Status, JobExecutionStatus.Running)
                                    .SetProperty(e => e.NodeId, nodeId)
                                    .SetProperty(e => e.StartedAt, now)
                                    .SetProperty(e => e.LastHeartbeat, now)
                                    .SetProperty(e => e.ModifiedAt, now),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (DbException ex) when (IsPartitionUniqueViolation(ex))
                    {
                        if (transaction is { SupportsSavepoints: true })
                        {
                            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        logger.LogDebug(
                            ex,
                            "The start on partition {LockPartition} for execution {ExecutionId} lost the race",
                            partition,
                            executionId);
                        return false;
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (started)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        JobEventBuilder.BuildJobExecutionStateChangedEvent(
                            MagpieEvents.Names.JobExecutionStarted,
                            executionId,
                            null,
                            null),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return started;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to start job execution {ExecutionId}.",
                executionId);
            throw;
        }
    }

    public async Task<bool> TryMarkRunningCompletedAsync(Guid executionId, CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var updated = await context.JobExecutions
                .Where(e => e.Id == executionId && e.Status == JobExecutionStatus.Running)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(e => e.Status, JobExecutionStatus.Completed)
                        .SetProperty(e => e.CompletedAt, now)
                        .SetProperty(e => e.LastHeartbeat, now)
                        .SetProperty(e => e.Progress, 100.0)
                        .SetProperty(e => e.ModifiedAt, now),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        JobEventBuilder.BuildJobExecutionStateChangedEvent(
                            MagpieEvents.Names.JobExecutionCompleted,
                            executionId,
                            null,
                            null),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to mark job execution {ExecutionId} complete.",
                executionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryDeferRunningAsync(
        Guid executionId,
        string nodeId,
        DateTime notBeforeAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async token =>
            {
                var now = _time.GetUtcNow().UtcDateTime;
                var updated = await context.JobExecutions
                    .Where(e => e.Id == executionId && e.NodeId == nodeId && e.Status == JobExecutionStatus.Running)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(e => e.Status, JobExecutionStatus.Queued)
                        .SetProperty(e => e.NodeId, (string?)null)
                        .SetProperty(e => e.StartedAt, (DateTime?)null)
                        .SetProperty(e => e.LastHeartbeat, (DateTime?)null)
                        .SetProperty(e => e.NotBeforeAt, notBeforeAt)
                        .SetProperty(e => e.StatusMessage, MagpieJobs.StatusMessages.UpstreamThrottled)
                        .SetProperty(e => e.ModifiedAt, now),
                        token).ConfigureAwait(false);
                if (updated == 0)
                {
                    return false;
                }

                await transactionManager.OnCommittedAsync(
                    eventToken => eventBus.PublishAsync(
                        JobEventBuilder.BuildJobExecutionStateChangedEvent(
                            MagpieEvents.Names.JobExecutionQueued,
                            executionId,
                            null,
                            null),
                        eventToken),
                    token).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The job execution repository failed to defer execution {ExecutionId}.", executionId);
            throw;
        }
    }

    public Task<bool> TryMarkRunningFailedAsync(
        Guid executionId,
        string errorMessage,
        CancellationToken cancellationToken) =>
        TryMarkRunningFailedCoreAsync(executionId, errorMessage, null, cancellationToken);

    /// <inheritdoc />
    public Task<bool> TryRecoverInactiveAsync(
        JobExecution expected,
        string reason,
        CancellationToken cancellationToken) => expected.Status switch
        {
            JobExecutionStatus.Running =>
                TryMarkRunningFailedCoreAsync(expected.Id, reason, expected, cancellationToken),
            JobExecutionStatus.CancellationRequested =>
                TryMarkCancellationRequestedCancelledCoreAsync(expected.Id, reason, expected, cancellationToken),
            _ => Task.FromResult(false)
        };

    private async Task<bool> TryMarkRunningFailedCoreAsync(
        Guid executionId,
        string errorMessage,
        JobExecution? expected,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var query = context.JobExecutions
                .Where(e => e.Id == executionId && e.Status == JobExecutionStatus.Running);
            if (expected is not null)
            {
                query = query.Where(e => e.NodeId == expected.NodeId && e.LastHeartbeat == expected.LastHeartbeat);
            }

            var updated = await query
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(e => e.Status, JobExecutionStatus.Failed)
                        .SetProperty(e => e.CompletedAt, now)
                        .SetProperty(e => e.LastHeartbeat, now)
                        .SetProperty(e => e.StatusMessage, errorMessage)
                        .SetProperty(e => e.ModifiedAt, now),
                    cancellationToken)
                .ConfigureAwait(false);

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to reclaim running job execution {ExecutionId}.",
                executionId);
            throw;
        }
    }

    public async Task<bool> TryMarkQueuedFailedAsync(
        Guid executionId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var updated = await context.JobExecutions
                .Where(e => e.Id == executionId && e.Status == JobExecutionStatus.Queued)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(e => e.Status, JobExecutionStatus.Failed)
                        .SetProperty(e => e.CompletedAt, now)
                        .SetProperty(e => e.LastHeartbeat, now)
                        .SetProperty(e => e.StatusMessage, errorMessage)
                        .SetProperty(e => e.ModifiedAt, now),
                    cancellationToken)
                .ConfigureAwait(false);

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to fail queued job execution {ExecutionId}.",
                executionId);
            throw;
        }
    }

    public Task<bool> TryMarkCancellationRequestedCancelledAsync(
        Guid executionId,
        string? reason,
        CancellationToken cancellationToken) =>
        TryMarkCancellationRequestedCancelledCoreAsync(executionId, reason, null, cancellationToken);

    private async Task<bool> TryMarkCancellationRequestedCancelledCoreAsync(
        Guid executionId,
        string? reason,
        JobExecution? expected,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async transactionToken =>
                {
                    var query = context.JobExecutions
                        .Where(e => e.Id == executionId && e.Status == JobExecutionStatus.CancellationRequested);
                    if (expected is not null)
                    {
                        query = query.Where(e =>
                            e.NodeId == expected.NodeId && e.LastHeartbeat == expected.LastHeartbeat);
                    }

                    var execution = await query.AsNoTracking()
                        .Select(e => new
                        {
                            e.CreatedByUserId,
                            e.CorrelationId
                        })
                        .FirstOrDefaultAsync(transactionToken)
                        .ConfigureAwait(false);
                    if (execution is null)
                    {
                        return false;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    var updated = reason is null
                        ? await query
                            .ExecuteUpdateAsync(
                                s => s
                                    .SetProperty(e => e.Status, JobExecutionStatus.Cancelled)
                                    .SetProperty(e => e.CompletedAt, now)
                                    .SetProperty(e => e.LastHeartbeat, now)
                                    .SetProperty(e => e.ModifiedAt, now),
                                transactionToken)
                            .ConfigureAwait(false)
                        : await query
                            .ExecuteUpdateAsync(
                                s => s
                                    .SetProperty(e => e.Status, JobExecutionStatus.Cancelled)
                                    .SetProperty(e => e.CompletedAt, now)
                                    .SetProperty(e => e.LastHeartbeat, now)
                                    .SetProperty(e => e.StatusMessage, reason)
                                    .SetProperty(e => e.ModifiedAt, now),
                                transactionToken)
                            .ConfigureAwait(false);
                    if (updated == 0)
                    {
                        return false;
                    }

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Jobs,
                            Operation = "JobCancelled",
                            EntityId = executionId.ToString(),
                            EntityType = nameof(JobExecution),
                            Detail = $"Canceled job run {executionId}.",
                            Timestamp = now,
                            UserId = execution.CreatedByUserId,
                            CorrelationId = execution.CorrelationId
                        },
                        transactionToken).ConfigureAwait(false);
                    await transactionManager.OnCommittedAsync(
                        token => eventBus.PublishAsync(
                            JobEventBuilder.BuildJobExecutionStateChangedEvent(
                                MagpieEvents.Names.JobExecutionCancelled,
                                executionId,
                                null,
                                null),
                            token),
                        transactionToken).ConfigureAwait(false);

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to mark job execution {ExecutionId} cancelled.",
                executionId);
            throw;
        }
    }

    public Task<JobCancellation?> CancelAsync(
        Guid executionId,
        CancellationToken cancellationToken) =>
        CancelExecutionAsync(executionId, checkModifiedAt: false, modifiedAt: null, cancellationToken);

    public Task<JobCancellation?> CancelAsync(
        JobExecution execution,
        CancellationToken cancellationToken) =>
        CancelExecutionAsync(execution.Id, checkModifiedAt: true, execution.ModifiedAt, cancellationToken);

    private async Task<JobCancellation?> CancelExecutionAsync(
        Guid executionId,
        bool checkModifiedAt,
        DateTime? modifiedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var existing = await (
                        from e in context.JobExecutions.AsNoTracking()
                        where e.Id == executionId
                        select new { e.NodeId, e.Status })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (existing is null)
                    {
                        return null;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    if (existing.Status is JobExecutionStatus.Queued)
                    {
                        var cancelled = await context.JobExecutions
                            .Where(e => e.Id == executionId && e.Status == JobExecutionStatus.Queued)
                            .Where(e => !checkModifiedAt || e.ModifiedAt == modifiedAt)
                            .ExecuteUpdateAsync(
                                s => s
                                    .SetProperty(e => e.Status, JobExecutionStatus.Cancelled)
                                    .SetProperty(e => e.CompletedAt, now)
                                    .SetProperty(e => e.CancellationRequestedAt, now)
                                    .SetProperty(e => e.LastHeartbeat, now)
                                    .SetProperty(e => e.ModifiedAt, now),
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (cancelled == 0)
                        {
                            return null;
                        }

                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Jobs,
                                Operation = "Cancel",
                                EntityId = executionId.ToString(),
                                EntityType = nameof(JobExecution),
                                Detail = $"Cancelled queued job {executionId}.",
                                Timestamp = now
                            },
                            cancellationToken).ConfigureAwait(false);

                        return new JobCancellation(null, CancelledBeforeStart: true);
                    }

                    if (existing.Status is not JobExecutionStatus.Running)
                    {
                        return null;
                    }

                    var requested = await context.JobExecutions
                        .Where(e => e.Id == executionId && e.Status == JobExecutionStatus.Running)
                        .Where(e => !checkModifiedAt || e.ModifiedAt == modifiedAt)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(e => e.Status, JobExecutionStatus.CancellationRequested)
                                .SetProperty(e => e.CancellationRequestedAt, now)
                                .SetProperty(e => e.ModifiedAt, now),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (requested == 0)
                    {
                        return null;
                    }

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Jobs,
                            Operation = "RequestCancellation",
                            EntityId = executionId.ToString(),
                            EntityType = nameof(JobExecution),
                            Detail = $"Requested cancellation of job {executionId}.",
                            Timestamp = now
                        },
                        cancellationToken).ConfigureAwait(false);

                    return new JobCancellation(existing.NodeId, CancelledBeforeStart: false);
                },
                cancellationToken).ConfigureAwait(false);

            if (result is not null)
            {
                var magpieEvent = result.CancelledBeforeStart
                    ? JobEventBuilder.BuildJobExecutionStateChangedEvent(
                        MagpieEvents.Names.JobExecutionCancelled,
                        executionId,
                        null,
                        null)
                    : JobEventBuilder.BuildJobExecutionStateChangedEvent(
                        MagpieEvents.Names.JobExecutionCancellationRequested,
                        executionId,
                        null,
                        null);
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(magpieEvent, token),
                    cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to cancel job execution {ExecutionId}.",
                executionId);
            throw;
        }
    }

    public async Task<bool> TouchHeartbeatAsync(Guid executionId, string nodeId, CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var updated = await context.JobExecutions
                .Where(e => e.Id == executionId &&
                    e.NodeId == nodeId &&
                    (e.Status == JobExecutionStatus.Running ||
                        e.Status == JobExecutionStatus.CancellationRequested))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastHeartbeat, now), cancellationToken)
                .ConfigureAwait(false);

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to update the heartbeat of execution {ExecutionId}.",
                executionId);
            throw;
        }
    }

    public async Task<bool> UpdateProgressAsync(
        Guid executionId,
        double? progress,
        string? statusMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var updated = await context.JobExecutions
                .Where(e => e.Id == executionId && e.Status == JobExecutionStatus.Running)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(e => e.Progress, progress)
                        .SetProperty(e => e.StatusMessage, statusMessage)
                        .SetProperty(e => e.LastHeartbeat, now)
                        .SetProperty(e => e.ModifiedAt, now),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        JobEventBuilder.BuildJobExecutionStateChangedEvent(
                            MagpieEvents.Names.JobExecutionProgressChanged,
                            executionId,
                            null,
                            null),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to update the progress of execution {ExecutionId}.",
                executionId);
            throw;
        }
    }

    public async Task<IReadOnlyList<JobExecution>> GetLineageAsync(
        Guid executionId,
        int pageSize,
        int maxNodes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);

        try
        {
            var execution = await context.JobExecutions
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
                .ConfigureAwait(false);
            if (execution is null)
            {
                return [];
            }

            var collected = new Dictionary<Guid, JobExecution> { [execution.Id] = execution };
            var chain = new List<Guid> { execution.Id };
            var root = execution;
            while (root.ParentExecutionId is { } parentId &&
                collected.Count < maxNodes &&
                !collected.ContainsKey(parentId))
            {
                var parent = await context.JobExecutions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == parentId, cancellationToken)
                    .ConfigureAwait(false);
                if (parent is null)
                {
                    break;
                }

                collected[parent.Id] = parent;
                chain.Add(parent.Id);
                root = parent;
            }

            async Task CollectDescendantsAsync(IReadOnlyList<Guid> seeds)
            {
                var frontier = seeds;
                while (frontier.Count > 0 && collected.Count < maxNodes)
                {
                    var nextFrontier = new List<Guid>();
                    for (var parentOffset = 0;
                        parentOffset < frontier.Count && collected.Count < maxNodes;
                        parentOffset += pageSize)
                    {
                        var parentIds = frontier.Skip(parentOffset).Take(pageSize).ToList();
                        var pageNumber = 1;
                        while (collected.Count < maxNodes)
                        {
                            var children = await context.JobExecutions
                                .AsNoTracking()
                                .Where(e => e.ParentExecutionId != null &&
                                    parentIds.Contains(e.ParentExecutionId.Value))
                                .OrderBy(e => e.CreatedAt)
                                .ThenBy(e => e.Id)
                                .Skip((pageNumber - 1) * pageSize)
                                .Take(pageSize)
                                .ToListAsync(cancellationToken)
                                .ConfigureAwait(false);

                            foreach (var child in children)
                            {
                                if (collected.Count >= maxNodes)
                                {
                                    break;
                                }

                                if (collected.TryAdd(child.Id, child))
                                {
                                    nextFrontier.Add(child.Id);
                                }
                            }

                            if (children.Count < pageSize)
                            {
                                break;
                            }

                            pageNumber++;
                        }
                    }

                    frontier = nextFrontier;
                }
            }

            await CollectDescendantsAsync([execution.Id]).ConfigureAwait(false);
            await CollectDescendantsAsync([.. chain.Skip(1)]).ConfigureAwait(false);

            return [.. collected.Values];
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to read the lineage of execution {ExecutionId}.",
                executionId);
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<string, DateTime>> GetLastAttemptTimesByLockPartitionAsync(
        string jobType,
        IReadOnlyCollection<string> lockPartitions,
        CancellationToken cancellationToken)
    {
        if (lockPartitions.Count == 0)
        {
            return new Dictionary<string, DateTime>();
        }

        try
        {
            var partitions = lockPartitions.Distinct(StringComparer.Ordinal).ToArray();
            var rows = await (
                from e in context.JobExecutions.AsNoTracking()
                where e.JobType == jobType && e.LockPartition != null && partitions.Contains(e.LockPartition)
                group e by e.LockPartition! into g
                select new
                {
                    LockPartition = g.Key,
                    LastAttempt = g.Max(e => e.CompletedAt ?? e.CreatedAt)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows.ToDictionary(r => r.LockPartition, r => r.LastAttempt, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job execution repository failed to query last attempt times for {JobType} across " +
                "{PartitionCount} lock partitions.",
                jobType,
                lockPartitions.Count);
            throw;
        }
    }
}
