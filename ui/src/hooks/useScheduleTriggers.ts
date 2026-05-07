import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
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

export interface ScalarPatch<T> {
    value: T;
}

export interface DictionaryPatch<K extends string, V> {
    add?: Record<K, V>;
    remove?: K[];
    update?: Record<K, V>;
}

export interface ScheduleTriggerPatch {
    id?: string;

    name?: ScalarPatch<string>;

    schedule?: ScalarPatch<string>;

    executionMode?: ScalarPatch<JobExecutionModeValue>;

    enabled?: ScalarPatch<boolean>;

    parameters?: DictionaryPatch<string, string>;
}

export const scheduleTriggerQueryKeys = {
    all: ['schedule-triggers'] as const,
    list: () => ['schedule-triggers', 'list'] as const,
    detail: (id: string) => ['schedule-triggers', 'detail', id] as const,
};

async function fetchScheduleTriggers(): Promise<ApiScheduleTrigger[]> {
    return apiGet<ApiScheduleTrigger[]>('/schedule-triggers');
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

export function useScheduleTriggers() {
    const queryClient = useQueryClient();

    const triggersQuery = useQuery({
        queryKey: scheduleTriggerQueryKeys.list(),
        queryFn: fetchScheduleTriggers,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const createMutation = useMutation({
        mutationFn: createScheduleTrigger,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: scheduleTriggerQueryKeys.all });
        },
    });

    const updateMutation = useMutation({
        mutationFn: updateScheduleTrigger,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: scheduleTriggerQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteScheduleTrigger,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: scheduleTriggerQueryKeys.all });
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
    const triggerQuery = useQuery({
        queryKey: scheduleTriggerQueryKeys.detail(id),
        queryFn: () => fetchScheduleTriggerById(id),
        enabled: enabled && Boolean(id),
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    return {
        trigger: triggerQuery.data ?? null,
        isLoading: triggerQuery.isLoading,
        error: queryErrorOrNull(triggerQuery),
        refetch: triggerQuery.refetch,
    };
}
