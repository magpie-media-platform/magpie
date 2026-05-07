import { useQuery, useMutation, useQueryClient, type Query } from '@tanstack/react-query';

import { apiGet, apiPost, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { PagedList } from '@/types/paging';

export type JobExecutionStatusValue = 0 | 1 | 2 | 3 | 4 | 5 | 6;

export const jobExecutionStatusLabels: Record<JobExecutionStatusValue, string> = {
    0: 'Created',
    1: 'Queued',
    2: 'Running',
    3: 'Completed',
    4: 'Failed',
    5: 'Cancelled',
    6: 'Cancellation Requested',
};

// Mirrors Magpie.Models.Enums.JobExecutionStatus terminal states (Completed, Failed, Cancelled).
export const terminalJobExecutionStatuses: ReadonlySet<JobExecutionStatusValue> = new Set([3, 4, 5]);

export function isTerminalJobExecutionStatus(status: JobExecutionStatusValue | undefined): boolean {
    return status !== undefined && terminalJobExecutionStatuses.has(status);
}

export const jobExecutionStatusVariants: Record<JobExecutionStatusValue, string> = {
    0: 'created',
    1: 'queued',
    2: 'running',
    3: 'completed',
    4: 'failed',
    5: 'cancelled',
    6: 'cancelling',
};

export type JobScopeValue = 0 | 1 | 2 | 3;

export const jobScopeLabels: Record<JobScopeValue, string> = {
    0: 'System',
    1: 'Administrative',
    2: 'Content',
    3: 'User',
};

export interface ApiJobExecution {
    id: string;

    scheduleTriggerId?: string | null;

    eventTriggerId?: string | null;

    extensionId?: string | null;

    jobType: string;

    scope: JobScopeValue;

    triggerContext?: Record<string, string>;

    parameters?: Record<string, string>;

    progress?: number | null;

    status: JobExecutionStatusValue;

    statusMessage?: string | null;

    nodeId?: string | null;

    lastHeartbeat?: string | null;

    timeoutAt?: string | null;

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

    status?: JobExecutionStatusValue;

    pageNumber?: number;

    pageSize?: number;

    sortBy?: string;

    sortDirection?: 0 | 1 | 2;
}

export type JobExecutionModeValue = 0 | 1;

export interface RunJobRequest {
    jobType: string;

    extensionId?: string | null;

    parameters?: Record<string, string>;

    mode?: JobExecutionModeValue;
}

export const jobQueryKeys = {
    all: ['jobs'] as const,
    list: (params?: JobExecutionQueryParams) => ['jobs', 'list', params ?? {}] as const,
    detail: (id: string) => ['jobs', 'detail', id] as const,
};

async function fetchJobExecutions(params?: JobExecutionQueryParams): Promise<PagedJobExecutions> {
    const url = buildUrl('/jobs', params);
    return apiGet<PagedJobExecutions>(url);
}

async function fetchJobExecutionById(id: string): Promise<ApiJobExecution> {
    return apiGet<ApiJobExecution>(`/jobs/${id}`);
}

async function runJob(request: RunJobRequest): Promise<string> {
    return apiPost<string>('/jobs', request);
}

async function cancelJob(id: string): Promise<void> {
    return apiPost(`/jobs/${id}/cancel`);
}

export function useJobs(params?: JobExecutionQueryParams) {
    const queryClient = useQueryClient();

    const jobsQuery = useQuery({
        queryKey: jobQueryKeys.list(params),
        queryFn: () => fetchJobExecutions(params),
        refetchInterval: POLLING_INTERVALS.fast,
        staleTime: POLLING_INTERVALS.fast / 2,
    });

    const runMutation = useMutation({
        mutationFn: runJob,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: jobQueryKeys.all });
        },
    });

    const cancelMutation = useMutation({
        mutationFn: cancelJob,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: jobQueryKeys.all });
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
    const jobQuery = useQuery({
        queryKey: jobQueryKeys.detail(id),
        queryFn: () => fetchJobExecutionById(id),
        enabled: enabled && Boolean(id),
        refetchInterval: (query: Query<ApiJobExecution, Error>) => {
            if (isTerminalJobExecutionStatus(query.state.data?.status)) {
                return false;
            }
            return POLLING_INTERVALS.fast;
        },
        staleTime: POLLING_INTERVALS.fast / 2,
    });

    return {
        job: jobQuery.data ?? null,
        isLoading: jobQuery.isLoading,
        error: queryErrorOrNull(jobQuery),
        refetch: jobQuery.refetch,
    };
}
