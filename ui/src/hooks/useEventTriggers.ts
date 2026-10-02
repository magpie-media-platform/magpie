import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import type { ApiEventFilter } from '@/lib/eventContextCriteria';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';
import type { JobScopeValue } from './useJobs';
import type { ScalarPatch, DictionaryPatch } from '@/types/patch';

export type { ApiEventFilter, DictionaryCriteria, PropertyCriterion } from '@/lib/eventContextCriteria';

export interface ApiEventTrigger {
    id: string;

    extensionId?: string | null;

    jobType: string;

    scope: JobScopeValue;

    eventFilter: ApiEventFilter;

    name: string;

    enabled: boolean;

    delaySeconds: number;

    parameters?: Record<string, string>;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface EventTriggerQueryParams {
    extensionId?: string;

    enabled?: boolean;

    mine?: boolean;
}

export interface EventTriggerCreate {
    extensionId?: string | null;

    jobType: string;

    scope: JobScopeValue;

    name: string;

    eventFilter: ApiEventFilter;

    enabled?: boolean;

    delaySeconds?: number;

    parameters?: Record<string, string>;
}

export interface EventTriggerPatch {
    id?: string;

    name?: ScalarPatch<string>;

    eventFilter?: ScalarPatch<ApiEventFilter>;

    enabled?: ScalarPatch<boolean>;

    delaySeconds?: ScalarPatch<number>;

    parameters?: DictionaryPatch<string, string>;
}

const eventTriggerQueryKeys = {
    all: ['event-triggers'] as const,
    list: (params?: EventTriggerQueryParams) => ['event-triggers', 'list', params ?? {}] as const,
};

async function fetchEventTriggers(params?: EventTriggerQueryParams): Promise<ApiEventTrigger[]> {
    const url = buildUrl('/event-triggers', params as Record<string, string | number | boolean | undefined>);
    return apiGet<ApiEventTrigger[]>(url);
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
    useInvalidateQueriesOnEvents(
        ['eventTrigger.created', 'eventTrigger.updated', 'eventTrigger.deleted'],
        [eventTriggerQueryKeys.list(params)],
        { exact: true },
    );
    const queryClient = useQueryClient();

    const triggersQuery = useQuery({
        queryKey: eventTriggerQueryKeys.list(params),
        queryFn: () => fetchEventTriggers(params),
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const createMutation = useMutation({
        mutationFn: createEventTrigger,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: eventTriggerQueryKeys.all });
        },
    });

    const updateMutation = useMutation({
        mutationFn: updateEventTrigger,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: eventTriggerQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteEventTrigger,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: eventTriggerQueryKeys.all });
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
