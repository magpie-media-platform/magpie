import type { ScalarPatch, DictionaryPatch } from '@/types/patch';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';
import type { JobScopeValue, JobExecutionModeValue } from './useJobs';

export interface ApiScheduleTrigger {
    id: string;

    extensionId?: string | null;

    jobType: string;

    scope: JobScopeValue;

    name: string;

    schedule: string;

    executionMode: JobExecutionModeValue;

    enabled: boolean;

    parameters?: Record<string, string>;

    lastRunAt?: string | null;

    nextRunAt?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface ScheduleTriggerCreate {
    extensionId?: string | null;

    jobType: string;

    scope: JobScopeValue;

    name: string;

    schedule: string;

    executionMode?: JobExecutionModeValue;

    enabled?: boolean;

    parameters?: Record<string, string>;
}

export interface ScheduleTriggerPatch {
    id?: string;

    name?: ScalarPatch<string>;

    schedule?: ScalarPatch<string>;

    executionMode?: ScalarPatch<JobExecutionModeValue>;

    enabled?: ScalarPatch<boolean>;

    parameters?: DictionaryPatch<string, string>;
}

export interface ScheduleTriggerQueryParams {
    mine?: boolean;
}

const scheduleTriggerQueryKeys = {
    all: ['schedule-triggers'] as const,
    list: (params?: ScheduleTriggerQueryParams) => ['schedule-triggers', 'list', params ?? {}] as const,
    detail: (id: string) => ['schedule-triggers', 'detail', id] as const,
};

async function fetchScheduleTriggers(params?: ScheduleTriggerQueryParams): Promise<ApiScheduleTrigger[]> {
    const url = buildUrl('/schedule-triggers', params as Record<string, string | number | boolean | undefined>);
    return apiGet<ApiScheduleTrigger[]>(url);
}

async function fetchScheduleTriggerById(id: string): Promise<ApiScheduleTrigger> {
    return apiGet<ApiScheduleTrigger>(`/schedule-triggers/${id}`);
}

async function createScheduleTrigger(request: ScheduleTriggerCreate): Promise<ApiScheduleTrigger> {
    return apiPost<ApiScheduleTrigger>('/schedule-triggers', request);
}

async function updateScheduleTrigger(params: { id: string; patch: ScheduleTriggerPatch }): Promise<void> {
    return apiPatch(`/schedule-triggers/${params.id}`, params.patch);
}

async function deleteScheduleTrigger(id: string): Promise<void> {
    return apiDelete(`/schedule-triggers/${id}`);
}

export function useScheduleTriggers(params?: ScheduleTriggerQueryParams) {
    useInvalidateQueriesOnEvents(
        ['scheduleTrigger.created', 'scheduleTrigger.updated', 'scheduleTrigger.deleted'],
        [scheduleTriggerQueryKeys.list(params)],
        { exact: true },
    );
    const queryClient = useQueryClient();

    const triggersQuery = useQuery({
        queryKey: scheduleTriggerQueryKeys.list(params),
        queryFn: () => fetchScheduleTriggers(params),
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const createMutation = useMutation({
        mutationFn: createScheduleTrigger,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: scheduleTriggerQueryKeys.all });
        },
    });

    const updateMutation = useMutation({
        mutationFn: updateScheduleTrigger,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: scheduleTriggerQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteScheduleTrigger,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: scheduleTriggerQueryKeys.all });
        },
    });

    const triggers: ApiScheduleTrigger[] = triggersQuery.data ?? [];

    return {
        triggers,
        isLoading: triggersQuery.isLoading,
        error: queryErrorOrNull(triggersQuery),

        createTrigger: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        updateTrigger: updateMutation.mutateAsync,
        isUpdating: updateMutation.isPending,

        deleteTrigger: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        refetch: triggersQuery.refetch,
    };
}

export function useScheduleTrigger(id: string, enabled = true) {
    useInvalidateQueriesOnEvents(
        ['scheduleTrigger.created', 'scheduleTrigger.updated', 'scheduleTrigger.deleted'],
        [scheduleTriggerQueryKeys.detail(id)],
        { entityIds: id ? [id] : [], exact: true },
    );
    const triggerQuery = useQuery({
        queryKey: scheduleTriggerQueryKeys.detail(id),
        queryFn: () => fetchScheduleTriggerById(id),
        enabled: enabled && Boolean(id),
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    return {
        trigger: triggerQuery.data ?? null,
        isLoading: triggerQuery.isLoading,
        error: queryErrorOrNull(triggerQuery),
        refetch: triggerQuery.refetch,
    };
}
