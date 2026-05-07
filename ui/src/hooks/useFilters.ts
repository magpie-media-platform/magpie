import { useState, useEffect, useRef, useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { formatFilterCriteria } from '@/lib/filterQuery';
import type { MetadataContainer } from '@/lib/metadata';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

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

export interface ContentFilterCriteria {
    contentHierarchyItem?: CriteriaSet;
    mediaFiles?: CriteriaSet;
}

export interface CriteriaSet {
    and?: unknown[];
    or?: unknown[];
    not?: unknown;
}

export interface CreateContentFilterRequest {
    name: string;
    criteria: ContentFilterCriteria;
    ownerLock?: boolean | null;
}

export interface UpdateContentFilterRequest {
    name?: { value: string };
    criteria?: { value: ContentFilterCriteria };
    ownerLock?: { value: boolean | null };
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
    detail: (id: string) => ['content-filters', 'detail', id] as const,
    preview: (id: string, isShared: boolean, page?: number, pageSize?: number) =>
        ['content-filters', 'preview', isShared ? 'shared' : 'my', id, { page, pageSize }] as const,
};

export interface FilterPreviewResponse {
    items: FilterPreviewItem[];
    pageNumber?: number;
    pageSize?: number;
    totalItems: number;
}

export interface FilterPreviewItem {
    id: string;
    contentType: string;
    level: string;
    slug: string;
    name: string;
    monitoring: number;
    contentItem?: {
        id: string;
        fulfillment: number;
        createdAt: string;
    } | null;
    metadataContainer?: MetadataContainer | null;
    createdAt: string;
}

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
    const [formattedQueries, setFormattedQueries] = useState<Record<string, string>>({});
    // Stale-resolution guard: drop a formatter result whose criteria fingerprint no longer matches the latest in-flight request for that filter id.
    const inFlightFingerprintsRef = useRef<Map<string, string>>(new Map());

    const sharedQuery = useQuery({
        queryKey: contentFilterQueryKeys.shared,
        queryFn: fetchSharedContentFilters,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const myQuery = useQuery({
        queryKey: contentFilterQueryKeys.my,
        queryFn: fetchMyContentFilters,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const isLoading = sharedQuery.isLoading || myQuery.isLoading;
    const error = sharedQuery.error || myQuery.error;

    const allRawFilters = useMemo<ContentFilter[]>(
        () => [...(sharedQuery.data ?? []), ...(myQuery.data ?? [])],
        [sharedQuery.data, myQuery.data],
    );

    const criteriaFingerprints = useMemo(() => {
        const map: Record<string, string> = {};
        for (const filter of allRawFilters) {
            map[filter.id] = JSON.stringify(filter.criteria);
        }
        return map;
    }, [allRawFilters]);

    useEffect(() => {
        let cancelled = false;
        const inFlight = inFlightFingerprintsRef.current;

        for (const filter of allRawFilters) {
            const count = countCriteria(filter.criteria);
            const fingerprint = criteriaFingerprints[filter.id];
            if (count > 0 && inFlight.get(filter.id) !== fingerprint) {
                inFlight.set(filter.id, fingerprint);

                formatFilterCriteria(filter.criteria, 'content')
                    .then((result) => {
                        if (cancelled) {
                            return;
                        }
                        if (inFlight.get(filter.id) !== fingerprint) {
                            return;
                        }
                        setFormattedQueries((prev) => ({ ...prev, [filter.id]: result.query ?? '' }));
                    })
                    .catch(() => {});
            }
        }

        return () => {
            cancelled = true;
        };
    }, [allRawFilters, criteriaFingerprints]);

    const filters: ContentFilterItem[] = allRawFilters.map((filter) => ({
        ...transformContentFilter(filter),
        query: formattedQueries[filter.id] ?? '',
    }));

    return {
        filters,
        isLoading,
        error: queryErrorOrNull({ error }),
        refetch: async () => {
            await Promise.all([sharedQuery.refetch(), myQuery.refetch()]);
        },
    };
}

export function useCreateContentFilter() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async ({ isShared, ...request }: CreateContentFilterRequest & { isShared: boolean }) => {
            const endpoint = isShared ? '/content-filters' : '/content-filters/my';
            return apiPost<ContentFilter>(endpoint, request);
        },
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: contentFilterQueryKeys.all });
        },
    });
}

export function useUpdateContentFilter() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async ({
            id,
            isShared,
            ...request
        }: UpdateContentFilterRequest & { id: string; isShared: boolean }) => {
            const endpoint = isShared ? `/content-filters/${id}` : `/content-filters/my/${id}`;
            return apiPatch<void>(endpoint, request);
        },
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: contentFilterQueryKeys.all });
        },
    });
}

export function useDeleteContentFilter() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async ({ id, isShared }: { id: string; isShared: boolean }) => {
            const endpoint = isShared ? `/content-filters/${id}` : `/content-filters/my/${id}`;
            return apiDelete<void>(endpoint);
        },
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: contentFilterQueryKeys.all });
        },
    });
}

async function fetchFilterPreview(
    filterId: string,
    isShared: boolean,
    page?: number,
    pageSize?: number,
): Promise<FilterPreviewResponse> {
    const endpoint = isShared ? `/content-filters/${filterId}/preview` : `/content-filters/my/${filterId}/preview`;
    return apiGet<FilterPreviewResponse>(buildUrl(endpoint, { page, pageSize }));
}

export function useFilterPreview(
    filterId: string | null,
    isShared: boolean = true,
    page?: number,
    pageSize: number = 10,
    options?: { enabled?: boolean },
) {
    return useQuery({
        queryKey: contentFilterQueryKeys.preview(filterId ?? '', isShared, page, pageSize),
        queryFn: () => fetchFilterPreview(filterId!, isShared, page, pageSize),
        enabled: (options?.enabled ?? true) && !!filterId,
        staleTime: POLLING_INTERVALS.normal,
    });
}
