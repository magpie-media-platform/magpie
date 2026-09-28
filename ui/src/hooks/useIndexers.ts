import { useCallback, useRef } from 'react';
import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { executeImmediateJobRequest, type JobActionDisposition } from '@/lib/jobActions';
import type { ConnectionTestResult } from '@/hooks/useTestConnection';
import { useJobAction, type JobActionLifecycleOptions } from '@/hooks/useJobAction';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { DictionaryPatch, ScalarPatch } from '@/types/patch';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import { healthStatusLabels, healthStatusVariants } from '@/types/healthStatus';
import type { HealthStatusValue } from '@/types/healthStatus';

export type IndexerStatusValue = HealthStatusValue;

export const indexerStatusLabels = healthStatusLabels;
export const indexerStatusVariants = healthStatusVariants;

export type IndexerAvailabilityValue = 'Unknown' | 'Public' | 'Private' | 'SemiPrivate';

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

    connectionCount?: number;

    definitionType?: string | null;

    searchFacets?: string[][];

    supportsKeywordSearch?: boolean;
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

    enabled: boolean;

    lastSync?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    settings?: Record<string, string>;
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

interface IndexerCreateRequest {
    providerType: string;

    name: string;

    description?: string;

    availability?: IndexerAvailabilityValue;

    categories?: string[];

    languages?: string[];

    settings?: Record<string, string>;
}

interface IndexerUpdateRequest {
    providerType?: ScalarPatch<string>;

    name?: ScalarPatch<string>;

    description?: ScalarPatch<string | null>;

    availability?: ScalarPatch<IndexerAvailabilityValue>;

    categories?: ScalarPatch<string[]>;

    languages?: ScalarPatch<string[]>;
}

interface IndexerConnectionUpdateRequest {
    name?: ScalarPatch<string>;

    enabled?: ScalarPatch<boolean>;

    settings?: DictionaryPatch<string, string>;
}

interface IndexerTestSettingsRequest {
    providerType?: string;

    id?: string;

    settings?: Record<string, string>;
}

interface IndexerFeedCreateRequest {
    providerType: string;

    name: string;

    description?: string;

    settings?: Record<string, string>;

    enabled?: boolean;
}

interface IndexerFeedUpdateRequest {
    providerType?: ScalarPatch<string>;

    name?: ScalarPatch<string>;

    description?: ScalarPatch<string | null>;

    settings?: DictionaryPatch<string, string>;

    enabled?: ScalarPatch<boolean>;
}

export const indexerQueryKeys = {
    list: () => ['indexers', 'list'] as const,
    connections: () => ['indexers', 'connections'] as const,
    connectionsByIndexer: (indexerId: string) => ['indexers', 'connections', indexerId] as const,
    feeds: () => ['indexers', 'feeds'] as const,
};

export function countConfiguredIndexerInstances(indexers: readonly ApiIndexer[]): number {
    return indexers.reduce((total, indexer) => total + Math.max(0, indexer.connectionCount ?? 0), 0);
}

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

async function fetchIndexerConnections(indexerId: string): Promise<ApiIndexerConnection[]> {
    return apiGet<ApiIndexerConnection[]>(`/indexers/${indexerId}/connections`);
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

interface IndexerConnectionCreateRequest {
    name: string;
    enabled: boolean;
    settings: Record<string, string>;
}

async function createIndexerConnection(
    indexerId: string,
    request: IndexerConnectionCreateRequest,
): Promise<ApiIndexerConnection> {
    return apiPost<ApiIndexerConnection>(`/indexers/${indexerId}/connections`, request);
}

async function updateIndexerConnection(
    indexerId: string,
    connectionId: string,
    request: IndexerConnectionUpdateRequest,
): Promise<void> {
    return apiPatch(`/indexers/${indexerId}/connections/${connectionId}`, request);
}

async function deleteIndexerConnection(indexerId: string, connectionId: string): Promise<void> {
    return apiDelete(`/indexers/${indexerId}/connections/${connectionId}`);
}

async function testIndexerConnection(indexerId: string, connectionId: string): Promise<ConnectionTestResult> {
    return apiPost<ConnectionTestResult>(`/indexers/${indexerId}/connections/${connectionId}/test`);
}

async function testIndexerSettings(
    request: IndexerTestSettingsRequest,
    signal?: AbortSignal,
): Promise<ConnectionTestResult> {
    return apiPost<ConnectionTestResult>('/indexers/test', request, { signal });
}

async function enableIndexerFeed(id: string): Promise<void> {
    return apiPatch(`/indexer-feeds/${id}`, { enabled: { value: true } });
}

async function disableIndexerFeed(id: string): Promise<void> {
    return apiPatch(`/indexer-feeds/${id}`, { enabled: { value: false } });
}

async function syncIndexerFeed(id: string): Promise<JobActionDisposition> {
    return executeImmediateJobRequest({
        jobType: 'IndexerFeedSyncItem',
        parameters: { feedId: id },
    });
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

export function useConfiguredIndexerCount(): number | undefined {
    return useQuery({
        queryKey: indexerQueryKeys.list(),
        queryFn: fetchIndexers,
        select: countConfiguredIndexerInstances,
        staleTime: POLLING_INTERVALS.normal / 2,
    }).data;
}

export function useIndexers(options?: { syncFeed?: JobActionLifecycleOptions<string> }) {
    useInvalidateQueriesOnEvents(
        [
            'indexer.created',
            'indexer.updated',
            'indexer.deleted',
            'indexerFeed.created',
            'indexerFeed.updated',
            'indexerFeed.deleted',
        ],
        [indexerQueryKeys.list(), indexerQueryKeys.feeds()],
        { exact: true, owner: 'indexer-settings' },
    );
    useInvalidateQueriesOnEvents(['integration.health.changed'], [indexerQueryKeys.list()], {
        context: { 'integration.type': 'indexer' },
        debounceMs: 150,
        exact: true,
        maxWaitMs: 600,
        owner: 'indexer-settings',
    });
    const queryClient = useQueryClient();

    const indexersQuery = useQuery({
        queryKey: indexerQueryKeys.list(),
        queryFn: fetchIndexers,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const feedsQuery = useQuery({
        queryKey: indexerQueryKeys.feeds(),
        queryFn: fetchIndexerFeeds,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const deleteIndexerMutation = useMutation({
        mutationFn: deleteIndexer,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.list() });
        },
    });

    const enableFeedMutation = useMutation({
        mutationFn: enableIndexerFeed,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
        },
    });

    const disableFeedMutation = useMutation({
        mutationFn: disableIndexerFeed,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
        },
    });

    const deleteFeedMutation = useMutation({
        mutationFn: deleteIndexerFeed,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
        },
    });

    const syncFeedMutation = useJobAction<string>({
        mutationFn: syncIndexerFeed,
        onJobStarted: (disposition, id) => {
            void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
            options?.syncFeed?.onJobStarted?.(disposition, id);
        },
        onJobExecutionSettled: options?.syncFeed?.onJobExecutionSettled,
        trackExecution: options?.syncFeed?.trackExecution,
    });

    const createIndexerMutation = useMutation({
        mutationFn: createIndexer,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.list() });
        },
    });

    const updateIndexerMutation = useMutation({
        mutationFn: ({ id, request }: { id: string; request: IndexerUpdateRequest }) => updateIndexer(id, request),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.list() });
        },
    });

    const createFeedMutation = useMutation({
        mutationFn: createIndexerFeed,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
        },
    });

    const updateFeedMutation = useMutation({
        mutationFn: ({ id, request }: { id: string; request: IndexerFeedUpdateRequest }) =>
            updateIndexerFeed(id, request),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.feeds() });
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
        syncFeed: syncFeedMutation.mutateAsync,
        isCreatingFeed: createFeedMutation.isPending,
        isUpdatingFeed: updateFeedMutation.isPending,
        isTogglingFeed: enableFeedMutation.isPending || disableFeedMutation.isPending,
        isDeletingFeed: deleteFeedMutation.isPending,
        isSyncingFeed: syncFeedMutation.isPending,
        syncingFeedId: syncFeedMutation.isPending ? syncFeedMutation.variables : undefined,

        refetch: async () => {
            await Promise.all([indexersQuery.refetch(), feedsQuery.refetch()]);
        },
        refetchIndexers: indexersQuery.refetch,
        refetchFeeds: feedsQuery.refetch,
    };
}

export interface ConfiguredConnectionRow {
    connection: ApiIndexerConnection;
    indexer: ApiIndexer;
}

export function useAllIndexerConnections(indexers: ApiIndexer[]) {
    useInvalidateQueriesOnEvents(['indexer.updated', 'indexer.deleted'], [indexerQueryKeys.connections()], {
        exact: false,
        owner: 'indexer-connections',
    });
    useInvalidateQueriesOnEvents(['integration.health.changed'], [indexerQueryKeys.connections()], {
        context: { 'integration.type': 'indexer' },
        debounceMs: 150,
        exact: false,
        maxWaitMs: 600,
        owner: 'indexer-connections',
    });
    const configured = indexers.filter((i) => (i.connectionCount ?? 0) > 0);

    const results = useQueries({
        queries: configured.map((indexer) => ({
            queryKey: indexerQueryKeys.connectionsByIndexer(indexer.id),
            queryFn: () => fetchIndexerConnections(indexer.id),
            staleTime: POLLING_INTERVALS.normal / 2,
        })),
    });

    const byId = new Map(configured.map((i) => [i.id, i]));
    const rows: ConfiguredConnectionRow[] = [];
    for (const result of results) {
        for (const connection of result.data ?? []) {
            const indexer = byId.get(connection.indexerId);
            if (indexer) {
                rows.push({ connection, indexer });
            }
        }
    }

    return {
        rows,
        isLoading: results.some((r) => r.isLoading),
    };
}

export function useIndexerConnectionMutations() {
    const queryClient = useQueryClient();

    const invalidate = () => {
        void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.list() });
        void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.connections() });
    };

    const createMutation = useMutation({
        mutationFn: ({ indexerId, request }: { indexerId: string; request: IndexerConnectionCreateRequest }) =>
            createIndexerConnection(indexerId, request),
        onSuccess: invalidate,
    });

    const updateMutation = useMutation({
        mutationFn: ({
            indexerId,
            connectionId,
            request,
        }: {
            indexerId: string;
            connectionId: string;
            request: IndexerConnectionUpdateRequest;
        }) => updateIndexerConnection(indexerId, connectionId, request),
        onSuccess: invalidate,
    });

    const deleteMutation = useMutation({
        mutationFn: ({ indexerId, connectionId }: { indexerId: string; connectionId: string }) =>
            deleteIndexerConnection(indexerId, connectionId),
        onSuccess: invalidate,
    });

    const testMutation = useMutation({
        mutationFn: ({ indexerId, connectionId }: { indexerId: string; connectionId: string }) =>
            testIndexerConnection(indexerId, connectionId),
        onSettled: invalidate,
    });

    const testSettingsAbortRef = useRef<AbortController | null>(null);
    const testSettingsMutation = useMutation({
        mutationFn: (request: IndexerTestSettingsRequest) => {
            testSettingsAbortRef.current?.abort();
            const controller = new AbortController();
            testSettingsAbortRef.current = controller;
            return testIndexerSettings(request, controller.signal);
        },
    });

    const testSettingsMutationRef = useRef(testSettingsMutation);
    testSettingsMutationRef.current = testSettingsMutation;

    const cancelTestSettings = useCallback(() => {
        testSettingsAbortRef.current?.abort();
        testSettingsAbortRef.current = null;
        testSettingsMutationRef.current.reset();
    }, []);

    return {
        createConnection: (indexerId: string, request: IndexerConnectionCreateRequest) =>
            createMutation.mutateAsync({ indexerId, request }),
        updateConnection: (indexerId: string, connectionId: string, request: IndexerConnectionUpdateRequest) =>
            updateMutation.mutateAsync({ indexerId, connectionId, request }),
        deleteConnection: (indexerId: string, connectionId: string) =>
            deleteMutation.mutateAsync({ indexerId, connectionId }),
        testConnection: (indexerId: string, connectionId: string) =>
            testMutation.mutateAsync({ indexerId, connectionId }),
        testSettings: (request: IndexerTestSettingsRequest) => testSettingsMutation.mutateAsync(request),
        cancelTestSettings,
        isCreating: createMutation.isPending,
        isUpdating: updateMutation.isPending,
        isDeleting: deleteMutation.isPending,
        isTesting: testMutation.isPending,
        isTestingSettings: testSettingsMutation.isPending,
    };
}
