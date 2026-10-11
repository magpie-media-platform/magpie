import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet, apiPatch } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { DownloadStatusValue, ReleaseScopeStatus } from '@/types';

export interface ContentReleaseRow {
    id: string;
    title: string;
    indexerName?: string | null;
    protocol: string;
    size?: number | null;
    publishDate: string;
    infoUrl?: string | null;
    externalId?: string | null;
    categories?: string[];
    languages?: string[];
    properties?: string | null;
    acquisitionStatus?: DownloadStatusValue | null;
    failureReason?: string | null;
    decisionReason?: string | null;
    status?: ReleaseScopeStatus;
}

export interface SetContentReleaseUnmatchedVariables {
    contentId: string;
    releaseId: string;
    status: ReleaseScopeStatus;
}

export const contentReleaseQueryKeys = {
    forContent: (contentId: string) => ['content-releases', contentId] as const,
};

async function fetchContentReleases(contentId: string): Promise<ContentReleaseRow[]> {
    return apiGet<ContentReleaseRow[]>(`/content/${contentId}/releases`);
}

export function useContentReleases(contentId: string | null | undefined, options?: { enabled?: boolean }) {
    const query = useQuery({
        queryKey: contentReleaseQueryKeys.forContent(contentId ?? ''),
        queryFn: () => fetchContentReleases(contentId!),
        enabled: (options?.enabled ?? true) && !!contentId,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
    const relatedIds = [contentId, ...(query.data?.map((release) => release.id) ?? [])].filter(
        (id): id is string => !!id,
    );

    useInvalidateQueriesOnEvents(
        (options?.enabled ?? true) && contentId
            ? [
                  'releaseSearch.completed',
                  'releaseGrab.rejected',
                  'download.created',
                  'download.updated',
                  'download.deleted',
                  'download.completed',
                  'download.failed',
                  'download.imported',
                  'download.stalled',
              ]
            : [],
        [contentReleaseQueryKeys.forContent(contentId ?? '')],
        {
            debounceMs: 200,
            entityIds: relatedIds,
            exact: true,
            maxWaitMs: 800,
        },
    );

    return {
        rows: query.data ?? [],
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}

export function useSetContentReleaseUnmatched() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: ({ contentId, releaseId, status }: SetContentReleaseUnmatchedVariables) =>
            apiPatch(`/content/${contentId}/releases/${releaseId}`, { status: { value: status } }),
        onSuccess: (_, { contentId }) =>
            queryClient.invalidateQueries({ queryKey: contentReleaseQueryKeys.forContent(contentId) }),
    });
}
