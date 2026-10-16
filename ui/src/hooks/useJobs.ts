import { EXTENSION_CATALOG_EVENTS } from '@/lib/extensionCatalogEvents';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { useAuth } from '@/auth';
import { apiGet, apiPost, buildUrl } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { EventStreamPayload } from '@/hooks/useEventStream';
import {
    executeJobRequest,
    type JobActionDisposition,
    type JobExecutionModeValue as JobActionExecutionModeValue,
    type JobExecutionRequest,
} from '@/lib/jobActions';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type {
    JobExecutionEffectiveStatusValue,
    JobExecutionStatusValue,
    JobParameterDefinition,
    JobScopeValue,
    JobTriggerModeValue,
} from '@/types/jobs';
import type { PagedList, SortDirectionName } from '@/types/paging';

export type {
    JobExecutionEffectiveStatusValue,
    JobExecutionStatusValue,
    JobParameterDefinition,
    JobScopeValue,
    JobTriggerModeValue,
} from '@/types/jobs';

export const terminalJobExecutionStatuses: ReadonlySet<JobExecutionStatusValue> = new Set([
    'Completed',
    'Failed',
    'Cancelled',
]);

export function isTerminalJobExecutionStatus(status: JobExecutionStatusValue | undefined): boolean {
    return status !== undefined && terminalJobExecutionStatuses.has(status);
}

export interface ApiJobSpec {
    jobType: string;

    displayName: string;

    description?: string | null;

    category: string;

    scope: JobScopeValue;

    enabled: boolean;

    onDemand: boolean;

    supportsImmediate?: boolean;

    supportedTriggerModes: JobTriggerModeValue[];

    requesterTriggerModes?: JobTriggerModeValue[];

    defaultSchedule?: string | null;

    parameters?: JobParameterDefinition[];
}

export interface ApiJobExecution {
    id: string;

    parentExecutionId?: string | null;

    scheduleTriggerId?: string | null;

    eventTriggerId?: string | null;

    extensionId?: string | null;

    jobType: string;

    scope: JobScopeValue;

    triggerContext?: Record<string, string>;

    parameters?: Record<string, string>;

    correlationId?: string | null;

    progress?: number | null;

    status: JobExecutionStatusValue;

    statusMessage?: string | null;

    nodeId?: string | null;

    lastHeartbeat?: string | null;

    notBeforeAt?: string | null;

    attempt?: number | null;

    originalExecutionId?: string | null;

    startedAt?: string | null;

    completedAt?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;
}

export type PagedJobExecutions = PagedList<ApiJobExecution>;

export interface JobExecutionQueryParams {
    jobType?: string;

    extensionId?: string;

    scheduleTriggerId?: string;

    eventTriggerId?: string;

    correlationId?: string;

    originalExecutionId?: string;

    status?: JobExecutionEffectiveStatusValue[];

    mine?: boolean;

    rootsOnly?: boolean;

    pageNumber?: number;

    pageSize?: number;

    sortBy?: string;

    sortDirection?: SortDirectionName;
}

export const displayStatusToWireStatuses: Record<string, JobExecutionEffectiveStatusValue[]> = {
    pending: ['Pending'],
    queued: ['Queued'],
    running: ['Running', 'CancellationRequested'],
    completed: ['Completed'],
    failed: ['Failed'],
    cancelled: ['Cancelled'],
};

export type JobExecutionModeValue = JobActionExecutionModeValue;

export type RunJobRequest = JobExecutionRequest;

export type RunJobResult = JobActionDisposition;

export const jobQueryKeys = {
    all: ['jobs'] as const,
    specs: (userTriggerMode?: JobTriggerModeValue) => ['jobs', 'specs', userTriggerMode ?? 'all'] as const,
    list: (params?: JobExecutionQueryParams) => ['jobs', 'list', params ?? {}] as const,
    detail: (id: string) => ['jobs', 'detail', id] as const,
};

export const jobExecutionListEvents = [
    'job.execution.created',
    'job.execution.queued',
    'job.execution.started',
    'job.execution.progress.changed',
    'job.execution.completed',
    'job.execution.failed',
    'job.execution.cancellationRequested',
    'job.execution.cancelled',
] as const;

function useJobExecutionInvalidation(
    queryKey: readonly unknown[],
    eventNames: readonly string[],
    enabled = true,
    entityIds?: readonly string[],
    predicate?: (event: EventStreamPayload) => boolean,
    unprovableMembership = false,
    prePredicate?: (event: EventStreamPayload) => boolean,
) {
    useInvalidateQueriesOnEvents(enabled ? eventNames : [], [queryKey], {
        debounceMs: 150,
        entityIds,
        exact: true,
        maxWaitMs: 600,
        prePredicate,
        predicate,
        unprovableMembership,
    });
}

async function fetchJobSpecs(userTriggerMode?: JobTriggerModeValue): Promise<ApiJobSpec[]> {
    const url = buildUrl('/jobs/specs', { userTriggerMode });
    return apiGet<ApiJobSpec[]>(url);
}

async function fetchJobExecutions(params?: JobExecutionQueryParams): Promise<PagedJobExecutions> {
    const url = buildUrl('/jobs', params);
    return apiGet<PagedJobExecutions>(url);
}

async function fetchJobExecutionById(id: string): Promise<ApiJobExecution> {
    return apiGet<ApiJobExecution>(`/jobs/${id}`);
}

async function runJob(request: RunJobRequest): Promise<RunJobResult> {
    return executeJobRequest(request);
}

async function cancelJob(id: string): Promise<void> {
    return apiPost(`/jobs/${id}/cancel`);
}

export function useJobSpecs(userTriggerMode?: JobTriggerModeValue) {
    useInvalidateQueriesOnEvents(EXTENSION_CATALOG_EVENTS, [jobQueryKeys.specs(userTriggerMode)], {
        exact: true,
    });
    const specsQuery = useQuery({
        queryKey: jobQueryKeys.specs(userTriggerMode),
        queryFn: () => fetchJobSpecs(userTriggerMode),
        staleTime: POLLING_INTERVALS.normal,
    });

    return {
        specs: specsQuery.data ?? [],
        isLoading: specsQuery.isLoading,
        error: queryErrorOrNull(specsQuery),
        refetch: specsQuery.refetch,
    };
}

export function useJobs(params?: JobExecutionQueryParams) {
    const { user } = useAuth();
    const queryClient = useQueryClient();
    const hasFilters = !!(
        params?.correlationId ||
        params?.originalExecutionId ||
        params?.eventTriggerId ||
        params?.extensionId ||
        params?.jobType ||
        params?.mine ||
        params?.rootsOnly ||
        params?.scheduleTriggerId ||
        params?.status?.length
    );
    useJobExecutionInvalidation(
        jobQueryKeys.list(params),
        jobExecutionListEvents,
        true,
        undefined,
        (event) => {
            const current = queryClient.getQueryData<PagedJobExecutions>(jobQueryKeys.list(params));
            if (current?.items.some((execution) => execution.id === event.context['execution.id'])) {
                return true;
            }
            if (!hasFilters) {
                return true;
            }
            const effectiveStatus = event.context['execution.effectiveStatus'];
            return (
                (!params?.eventTriggerId || event.context['eventTrigger.id'] === params.eventTriggerId) &&
                (!params?.extensionId || event.context['extension.id'] === params.extensionId) &&
                (!params?.jobType || event.context['execution.jobType'] === params.jobType) &&
                (!params?.mine || event.context['user.id'] === user?.id) &&
                (!params?.rootsOnly || !event.context['execution.parentId']) &&
                (!params?.scheduleTriggerId || event.context['scheduleTrigger.id'] === params.scheduleTriggerId) &&
                (!params?.status?.length ||
                    (!!effectiveStatus && params.status.includes(effectiveStatus as JobExecutionEffectiveStatusValue)))
            );
        },
        hasFilters,
        (event) => {
            const context = event.context;
            const effectiveStatus = context['execution.effectiveStatus'];
            return !(
                (params?.eventTriggerId &&
                    context['eventTrigger.id'] &&
                    context['eventTrigger.id'] !== params.eventTriggerId) ||
                (params?.extensionId && context['extension.id'] && context['extension.id'] !== params.extensionId) ||
                (params?.jobType && context['execution.jobType'] && context['execution.jobType'] !== params.jobType) ||
                (params?.mine && context['user.id'] && context['user.id'] !== user?.id) ||
                (params?.rootsOnly && context['execution.parentId']) ||
                (params?.scheduleTriggerId &&
                    context['scheduleTrigger.id'] &&
                    context['scheduleTrigger.id'] !== params.scheduleTriggerId) ||
                (params?.status?.length &&
                    effectiveStatus &&
                    !params.status.includes(effectiveStatus as JobExecutionEffectiveStatusValue))
            );
        },
    );

    const jobsQuery = useQuery({
        queryKey: jobQueryKeys.list(params),
        queryFn: () => fetchJobExecutions(params),
    });

    const runMutation = useMutation({
        mutationFn: runJob,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: jobQueryKeys.all });
        },
    });

    const cancelMutation = useMutation({
        mutationFn: cancelJob,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: jobQueryKeys.all });
        },
    });

    const jobs: ApiJobExecution[] = jobsQuery.data?.items ?? [];
    const pagination = jobsQuery.data
        ? {
              pageNumber: jobsQuery.data.pageNumber,
              pageSize: jobsQuery.data.pageSize,
              totalItems: jobsQuery.data.totalItems,
          }
        : null;

    return {
        jobs,
        pagination,
        isLoading: jobsQuery.isLoading,
        error: queryErrorOrNull(jobsQuery),

        runJob: runMutation.mutateAsync,
        isRunning: runMutation.isPending,

        cancelJob: cancelMutation.mutateAsync,
        isCancelling: cancelMutation.isPending,

        refetch: jobsQuery.refetch,
    };
}

export function useJobExecution(id: string, enabled = true) {
    useJobExecutionInvalidation(jobQueryKeys.detail(id), jobExecutionListEvents, enabled && Boolean(id), [id]);
    const jobQuery = useQuery({
        queryKey: jobQueryKeys.detail(id),
        queryFn: () => fetchJobExecutionById(id),
        enabled: enabled && Boolean(id),
    });

    return {
        job: jobQuery.data ?? null,
        isLoading: jobQuery.isLoading,
        error: queryErrorOrNull(jobQuery),
        refetch: jobQuery.refetch,
    };
}
