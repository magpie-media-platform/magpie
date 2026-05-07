import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import { healthStatusLabels, healthStatusVariants } from '@/types/healthStatus';
import type { HealthStatusValue } from '@/types/healthStatus';

export type IndexerStatusValue = HealthStatusValue;

export const indexerStatusLabels = healthStatusLabels;
export const indexerStatusVariants = healthStatusVariants;

export type IndexerAvailabilityValue = 0 | 1 | 2 | 3;

export interface ApiIndexer {
    id: string;

    feedId?: string | null;

    extensionId?: string | null;

    providerType: string;

    name: string;

    description?: string | null;

    availability: IndexerAvailabilityValue;

    categories?: string[];

    languages?: string[];

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface ApiIndexerConnection {
    id: string;

    indexerId: string;

    name: string;

    enabled: boolean;

    status: IndexerStatusValue;

    settings?: Record<string, string>;

    lastChecked?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface ApiIndexerFeed {
    id: string;

    extensionId?: string | null;

    providerType: string;

    name: string;

    description?: string | null;

    settings?: Record<string, string>;

    enabled: boolean;

    lastSync?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface IndexerFeedItem {
    id: string;

    name: string;

    description?: string | null;

    provider: string;

    enabled: boolean;

    lastSync?: string | null;

    createdAt: string;

    settings: Record<string, string>;
}

export interface IndexerCreateRequest {
    providerType: string;

    name: string;

    description?: string;

    availability?: IndexerAvailabilityValue;

    categories?: string[];

    languages?: string[];
}

export interface IndexerUpdateRequest {
    providerType?: string;

    name?: string;

    description?: string;

    availability?: IndexerAvailabilityValue;

    categories?: string[];

    languages?: string[];
}

export interface IndexerFeedCreateRequest {
    providerType: string;

    name: string;

    description?: string;

    settings?: Record<string, string>;

    enabled?: boolean;
}

export interface IndexerFeedUpdateRequest {
    providerType?: string;

    name?: string;

    description?: string;

    settings?: Record<string, string>;

    enabled?: boolean;
}

export const indexerQueryKeys = {
    all: ['indexers'] as const,
    list: () => ['indexers', 'list'] as const,
    connections: () => ['indexers', 'connections'] as const,
    feeds: () => ['indexers', 'feeds'] as const,
    detail: (id: string) => ['indexers', 'detail', id] as const,
    feedDetail: (id: string) => ['indexers', 'feeds', id] as const,
};

function transformFeed(feed: ApiIndexerFeed): IndexerFeedItem {
    return {
        id: feed.id,
        name: feed.name,
        description: feed.description,
        provider: feed.providerType,
        enabled: feed.enabled,
        lastSync: feed.lastSync,
        createdAt: feed.createdAt,
        settings: feed.settings ?? {},
    };
}

async function fetchIndexers(): Promise<ApiIndexer[]> {
    return apiGet<ApiIndexer[]>('/indexers');
}

async function fetchIndexerFeeds(): Promise<ApiIndexerFeed[]> {
    return apiGet<ApiIndexerFeed[]>('/indexer-feeds');
}

async function createIndexer(request: IndexerCreateRequest): Promise<ApiIndexer> {
    return apiPost<ApiIndexer>('/indexers', request);
}

async function updateIndexer(id: string, request: IndexerUpdateRequest): Promise<void> {
    return apiPatch(`/indexers/${id}`, request);
}

async function deleteIndexer(id: string): Promise<void> {
    return apiDelete(`/indexers/${id}`);
}

async function enableIndexerFeed(id: string): Promise<void> {
    return apiPost(`/indexer-feeds/${id}/enable`);
}

async function disableIndexerFeed(id: string): Promise<void> {
    return apiPost(`/indexer-feeds/${id}/disable`);
}

async function createIndexerFeed(request: IndexerFeedCreateRequest): Promise<ApiIndexerFeed> {
    return apiPost<ApiIndexerFeed>('/indexer-feeds', request);
}

async function updateIndexerFeed(id: string, request: IndexerFeedUpdateRequest): Promise<void> {
    return apiPatch(`/indexer-feeds/${id}`, request);
}

async function deleteIndexerFeed(id: string): Promise<void> {
    return apiDelete(`/indexer-feeds/${id}`);
}

export function useIndexers() {
    const queryClient = useQueryClient();

    const indexersQuery = useQuery({
        queryKey: indexerQueryKeys.list(),
        queryFn: fetchIndexers,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const feedsQuery = useQuery({
        queryKey: indexerQueryKeys.feeds(),
        queryFn: fetchIndexerFeeds,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const deleteIndexerMutation = useMutation({
        mutationFn: deleteIndexer,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: indexerQueryKeys.list() });
        },
    });

    const enableFeedMutation = useMutation({
        mutationFn: enableIndexerFeed,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
        },
    });

    const disableFeedMutation = useMutation({
        mutationFn: disableIndexerFeed,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
        },
    });

    const deleteFeedMutation = useMutation({
        mutationFn: deleteIndexerFeed,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
        },
    });

    const createIndexerMutation = useMutation({
        mutationFn: createIndexer,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: indexerQueryKeys.list() });
        },
    });

    const updateIndexerMutation = useMutation({
        mutationFn: ({ id, request }: { id: string; request: IndexerUpdateRequest }) => updateIndexer(id, request),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: indexerQueryKeys.list() });
        },
    });

    const createFeedMutation = useMutation({
        mutationFn: createIndexerFeed,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
        },
    });

    const updateFeedMutation = useMutation({
        mutationFn: ({ id, request }: { id: string; request: IndexerFeedUpdateRequest }) =>
            updateIndexerFeed(id, request),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
        },
    });

    const indexers: ApiIndexer[] = indexersQuery.data ?? [];
    const feeds: IndexerFeedItem[] = feedsQuery.data?.map(transformFeed) ?? [];

    const toggleFeedEnabled = async (id: string, enabled: boolean): Promise<void> => {
        if (enabled) {
            await enableFeedMutation.mutateAsync(id);
        } else {
            await disableFeedMutation.mutateAsync(id);
        }
    };

    return {
        indexers,
        feeds,
        isLoading: indexersQuery.isLoading || feedsQuery.isLoading,
        isLoadingIndexers: indexersQuery.isLoading,
        isLoadingFeeds: feedsQuery.isLoading,
        error: queryErrorOrNull(indexersQuery),
        feedsError: queryErrorOrNull(feedsQuery),

        createIndexer: createIndexerMutation.mutateAsync,
        updateIndexer: (id: string, request: IndexerUpdateRequest) =>
            updateIndexerMutation.mutateAsync({ id, request }),
        deleteIndexer: deleteIndexerMutation.mutateAsync,
        isCreatingIndexer: createIndexerMutation.isPending,
        isUpdatingIndexer: updateIndexerMutation.isPending,
        isDeletingIndexer: deleteIndexerMutation.isPending,

        createFeed: createFeedMutation.mutateAsync,
        updateFeed: (id: string, request: IndexerFeedUpdateRequest) => updateFeedMutation.mutateAsync({ id, request }),
        toggleFeedEnabled,
        deleteFeed: deleteFeedMutation.mutateAsync,
        isCreatingFeed: createFeedMutation.isPending,
        isUpdatingFeed: updateFeedMutation.isPending,
        isTogglingFeed: enableFeedMutation.isPending || disableFeedMutation.isPending,
        isDeletingFeed: deleteFeedMutation.isPending,

        refetch: () => {
            indexersQuery.refetch();
            feedsQuery.refetch();
        },
        refetchIndexers: indexersQuery.refetch,
        refetchFeeds: feedsQuery.refetch,
    };
}
