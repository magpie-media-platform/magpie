import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { JobScopeValue, JobExecutionModeValue } from './useJobs';
import type { ScalarPatch, DictionaryPatch } from './useScheduleTriggers';

export interface PropertyCriterion<T> {
    operator?: string;

    value?: T;

    negate?: boolean;
}

export interface DictionaryCriteria<TCriterion> {
    [key: string]: TCriterion;
}

export interface ApiEventFilter {
    eventName?: PropertyCriterion<string>;

    eventContext?: DictionaryCriteria<PropertyCriterion<string>>;
}

export interface ApiEventTrigger {
    id: string;

    extensionId?: string | null;

    jobType: string;

    scope: JobScopeValue;

    executionMode: JobExecutionModeValue;

    eventFilter: ApiEventFilter;

    name: string;

    enabled: boolean;

    parameters?: Record<string, string>;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface EventTriggerQueryParams {
    extensionId?: string;

    enabled?: boolean;
}

export interface EventTriggerCreate {
    extensionId?: string | null;

    jobType: string;

    scope: JobScopeValue;

    name: string;

    eventFilter: ApiEventFilter;

    executionMode?: JobExecutionModeValue;

    enabled?: boolean;

    parameters?: Record<string, string>;
}

export interface EventTriggerPatch {
    id?: string;

    name?: ScalarPatch<string>;

    eventFilter?: ScalarPatch<ApiEventFilter>;

    executionMode?: ScalarPatch<JobExecutionModeValue>;

    enabled?: ScalarPatch<boolean>;

    parameters?: DictionaryPatch<string, string>;
}

export const eventTriggerQueryKeys = {
    all: ['event-triggers'] as const,
    list: (params?: EventTriggerQueryParams) => ['event-triggers', 'list', params ?? {}] as const,
    detail: (id: string) => ['event-triggers', 'detail', id] as const,
};

async function fetchEventTriggers(params?: EventTriggerQueryParams): Promise<ApiEventTrigger[]> {
    const url = buildUrl('/event-triggers', params as Record<string, string | number | boolean | undefined>);
    return apiGet<ApiEventTrigger[]>(url);
}

async function fetchEventTriggerById(id: string): Promise<ApiEventTrigger> {
    return apiGet<ApiEventTrigger>(`/event-triggers/${id}`);
}

async function createEventTrigger(request: EventTriggerCreate): Promise<ApiEventTrigger> {
    return apiPost<ApiEventTrigger>('/event-triggers', request);
}

async function updateEventTrigger(params: { id: string; patch: EventTriggerPatch }): Promise<void> {
    return apiPatch(`/event-triggers/${params.id}`, params.patch);
}

async function deleteEventTrigger(id: string): Promise<void> {
    return apiDelete(`/event-triggers/${id}`);
}

export function useEventTriggers(params?: EventTriggerQueryParams) {
    const queryClient = useQueryClient();

    const triggersQuery = useQuery({
        queryKey: eventTriggerQueryKeys.list(params),
        queryFn: () => fetchEventTriggers(params),
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const createMutation = useMutation({
        mutationFn: createEventTrigger,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: eventTriggerQueryKeys.all });
        },
    });

    const updateMutation = useMutation({
        mutationFn: updateEventTrigger,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: eventTriggerQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteEventTrigger,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: eventTriggerQueryKeys.all });
        },
    });

    const triggers: ApiEventTrigger[] = triggersQuery.data ?? [];

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

export function useEventTrigger(id: string, enabled = true) {
    const triggerQuery = useQuery({
        queryKey: eventTriggerQueryKeys.detail(id),
        queryFn: () => fetchEventTriggerById(id),
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
