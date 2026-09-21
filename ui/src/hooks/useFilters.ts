import type { ScalarPatch } from '@/types/patch';
import { useMemo } from 'react';
import { useQuery, useMutation, useQueries, useQueryClient, type UseQueryResult } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { formatMagpieQlCriteria } from '@/lib/magpieQl';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';
import type { ContentFilterCriteria, CriteriaSet } from '@/types/filters';

export type { ContentFilterCriteria, CriteriaSet } from '@/types/filters';

export interface ContentFilter {
    id: string;
    ownerUserId: string | null;
    name: string;
    criteria: ContentFilterCriteria;
    ownerLock: boolean | null;
    createdAt: string;
    createdByUserId: string | null;
    modifiedAt: string | null;
    modifiedByUserId: string | null;
}

interface CreateContentFilterRequest {
    name: string;
    criteria: ContentFilterCriteria;
    ownerLock?: boolean | null;
}

interface UpdateContentFilterRequest {
    name?: ScalarPatch<string>;
    criteria?: ScalarPatch<ContentFilterCriteria>;
    ownerLock?: ScalarPatch<boolean | null>;
}

export interface ContentFilterItem {
    id: string;
    name: string;
    ownerId: string | null;
    criteriaCount: number;
    ownerLock: boolean;
    query: string;
    criteria: ContentFilterCriteria;
    createdAt: string;
    modifiedAt: string | null;
}

export const contentFilterQueryKeys = {
    all: ['content-filters'] as const,
    shared: ['content-filters', 'shared'] as const,
    my: ['content-filters', 'my'] as const,
    formattedCriteria: (fingerprint: string) => ['content-filters', 'formatted-criteria', fingerprint] as const,
    filterFormattedCriteria: (id: string, fingerprint: string) =>
        ['content-filters', 'formatted-criteria', id, fingerprint] as const,
};

async function fetchSharedContentFilters(): Promise<ContentFilter[]> {
    return apiGet<ContentFilter[]>('/content-filters');
}

async function fetchMyContentFilters(): Promise<ContentFilter[]> {
    return apiGet<ContentFilter[]>('/content-filters/my');
}

function countTopLevelClauses(set: CriteriaSet | null | undefined): number {
    if (!set || typeof set !== 'object') {
        return 0;
    }
    let count = 0;
    if (Array.isArray(set.and)) {
        count += set.and.length;
    }
    if (Array.isArray(set.or)) {
        count += set.or.length;
    }
    if (set.not !== undefined && set.not !== null) {
        count += 1;
    }
    return count;
}

function countCriteria(criteria: ContentFilterCriteria | null | undefined): number {
    if (!criteria) {
        return 0;
    }
    return countTopLevelClauses(criteria.contentHierarchyItem) + countTopLevelClauses(criteria.mediaFiles);
}

export function isCriteriaEmpty(criteria: ContentFilterCriteria | null | undefined): boolean {
    return countCriteria(criteria) === 0;
}

function createCriteriaFingerprint(criteria: ContentFilterCriteria | null | undefined): string {
    return criteria ? JSON.stringify(criteria) : '';
}

async function formatContentCriteria(criteria: ContentFilterCriteria): Promise<string> {
    const result = await formatMagpieQlCriteria(criteria, 'content');
    return result.query ?? '';
}

export function useFormattedCriteria(
    criteria: ContentFilterCriteria | null | undefined,
): UseQueryResult<string, Error> {
    const fingerprint = useMemo(() => createCriteriaFingerprint(criteria), [criteria]);
    const enabled = !!criteria && !isCriteriaEmpty(criteria);

    return useQuery<string, Error>({
        queryKey: [...contentFilterQueryKeys.formattedCriteria(fingerprint), criteria],
        queryFn: () => formatContentCriteria(criteria ?? {}),
        enabled,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}

function transformContentFilter(filter: ContentFilter): ContentFilterItem {
    return {
        id: filter.id,
        name: filter.name,
        ownerId: filter.ownerUserId,
        criteriaCount: countCriteria(filter.criteria),
        ownerLock: filter.ownerLock ?? false,
        query: '',
        criteria: filter.criteria,
        createdAt: filter.createdAt,
        modifiedAt: filter.modifiedAt,
    };
}

export function useContentFilters() {
    useInvalidateQueriesOnEvents(
        ['contentFilter.created', 'contentFilter.updated', 'contentFilter.deleted'],
        [contentFilterQueryKeys.shared, contentFilterQueryKeys.my],
        { exact: true, owner: 'content-filter-shell' },
    );
    const sharedQuery = useQuery({
        queryKey: contentFilterQueryKeys.shared,
        queryFn: fetchSharedContentFilters,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const myQuery = useQuery({
        queryKey: contentFilterQueryKeys.my,
        queryFn: fetchMyContentFilters,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const isLoading = sharedQuery.isLoading || myQuery.isLoading;
    const error = sharedQuery.error || myQuery.error;

    const allRawFilters = useMemo<ContentFilter[]>(() => {
        const byId = new Map<string, ContentFilter>();
        for (const filter of [...(sharedQuery.data ?? []), ...(myQuery.data ?? [])]) {
            if (!byId.has(filter.id)) {
                byId.set(filter.id, filter);
            }
        }
        return [...byId.values()];
    }, [sharedQuery.data, myQuery.data]);

    const formattedCriteriaInputs = useMemo(() => {
        return allRawFilters.map((filter) => ({
            id: filter.id,
            criteria: filter.criteria,
            criteriaCount: countCriteria(filter.criteria),
            fingerprint: createCriteriaFingerprint(filter.criteria),
        }));
    }, [allRawFilters]);

    const formattedCriteriaQueries = useQueries({
        queries: formattedCriteriaInputs.map((input) => ({
            queryKey: contentFilterQueryKeys.filterFormattedCriteria(input.id, input.fingerprint),
            queryFn: () => formatContentCriteria(input.criteria),
            enabled: input.criteriaCount > 0,
            staleTime: POLLING_INTERVALS.normal / 2,
        })),
    });

    const formatterError = formattedCriteriaQueries.find((query) => query.error)?.error;

    const filters: ContentFilterItem[] = allRawFilters.map((filter, index) => ({
        ...transformContentFilter(filter),
        query: formattedCriteriaQueries[index]?.data ?? '',
    }));

    return {
        filters,
        isLoading,
        error: queryErrorOrNull({ error: error || formatterError }),
        refetch: async () => {
            await Promise.all([
                sharedQuery.refetch(),
                myQuery.refetch(),
                ...formattedCriteriaQueries.filter((query) => query.isError).map((query) => query.refetch()),
            ]);
        },
    };
}

export function useCreateContentFilter() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async (request: CreateContentFilterRequest) => {
            return apiPost<ContentFilter>('/content-filters', request);
        },
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: contentFilterQueryKeys.all });
        },
    });
}

export function useUpdateContentFilter() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async ({ id, ...request }: UpdateContentFilterRequest & { id: string }) => {
            return apiPatch<void>(`/content-filters/${id}`, request);
        },
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: contentFilterQueryKeys.all });
        },
    });
}

export function useDeleteContentFilter() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async ({ id }: { id: string }) => {
            return apiDelete<void>(`/content-filters/${id}`);
        },
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: contentFilterQueryKeys.all });
        },
    });
}
