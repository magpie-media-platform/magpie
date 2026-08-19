import { useMutation, useQuery } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import type { IndexerStatusValue } from '@/hooks/useIndexers';
import type { ApiRelease } from '@/hooks/useReleases';

export interface IndexerSearchQuery {
    title?: string;

    criteria?: Record<string, string[]>;
}

export interface IndexerSearchConnectionResult {
    connectionId: string;

    indexerId: string;

    connectionName: string;

    enabled: boolean;

    status: IndexerStatusValue;

    attempted: boolean;

    skippedReason?: string | null;

    warning?: string | null;
}

export interface IndexerSearchCandidate {
    key: string;

    release: ApiRelease;

    connectionIds: string[];
}

export interface IndexerSearchResult {
    candidates: IndexerSearchCandidate[];

    connections: IndexerSearchConnectionResult[];

    connectionsAttempted: number;

    connectionsSkipped: number;
}

export interface MatchIndexerReleaseVariables {
    contentHierarchyItemId: string;

    release: ApiRelease;
}

export const indexerSearchQueryKeys = {
    all: ['indexer-search'] as const,
    run: (connectionIds: readonly string[], query: IndexerSearchQuery | null) =>
        ['indexer-search', connectionIds, query] as const,
};

export function useIndexerSearch(connectionIds: string[], query: IndexerSearchQuery | null) {
    return useQuery({
        queryKey: indexerSearchQueryKeys.run(connectionIds, query),
        queryFn: ({ signal }) => apiPost<IndexerSearchResult>('/indexer-search', { connectionIds, query }, { signal }),
        enabled: connectionIds.length > 0 && query !== null,
        staleTime: Infinity,
        gcTime: 0,
        retry: false,
        refetchOnWindowFocus: false,
    });
}

export function useMatchIndexerRelease() {
    return useMutation({
        mutationFn: ({ contentHierarchyItemId, release }: MatchIndexerReleaseVariables) =>
            apiPost<string>(`/indexer-search/${contentHierarchyItemId}/match`, release),
    });
}
