import { useQuery, useQueryClient } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import { normalizeJobActionDisposition, type JobActionDisposition, type JobActionResponse } from '@/lib/jobActions';
import { queryErrorOrNull } from '@/lib/utils';
import { useJobAction, type JobActionLifecycleOptions } from '@/hooks/useJobAction';
import type { DownloadStatusValue } from '@/types/content';

import { contentQueryKeys } from './useContent';
import { contentReleaseQueryKeys } from './useContentReleases';
import type { DownloadFailureReasonValue } from './useDownloads';
import type { ApiRelease } from './useReleases';

type ReleaseMatchResult = 'NoMatch' | 'Indeterminate' | 'Confident' | 'Identity';

export interface InteractiveSearchCandidate {
    release: ApiRelease;
    matchResult: ReleaseMatchResult;
    indexerName?: string | null;
    score?: number | null;
    isAcceptable?: boolean | null;
    meetsCutoff?: boolean | null;
    acquisitionStatus?: DownloadStatusValue | null;
    failureReason?: DownloadFailureReasonValue | null;
}

const interactiveSearchQueryKeys = {
    forItem: (contentHierarchyItemId: string) => ['interactive-search', contentHierarchyItemId] as const,
};

export interface InteractiveSearchResult {
    candidates: InteractiveSearchCandidate[];
    partial: boolean;
    warning?: string | null;
    retryAt?: string | null;
}

async function runInteractiveSearch(contentHierarchyItemId: string): Promise<InteractiveSearchResult> {
    const result = await apiPost<InteractiveSearchResult | InteractiveSearchCandidate[]>(
        `/interactive-search/${contentHierarchyItemId}`,
    );
    return Array.isArray(result) ? { candidates: result, partial: false } : result;
}

export function useInteractiveSearch(contentHierarchyItemId: string | undefined, options?: { enabled?: boolean }) {
    const query = useQuery({
        queryKey: interactiveSearchQueryKeys.forItem(contentHierarchyItemId ?? ''),
        queryFn: () => runInteractiveSearch(contentHierarchyItemId!),
        enabled: (options?.enabled ?? true) && !!contentHierarchyItemId,
        refetchOnWindowFocus: false,
        staleTime: Infinity,
        gcTime: 0,
        retry: false,
    });

    return {
        candidates: query.data?.candidates ?? [],
        partial: query.data?.partial ?? false,
        warning: query.data?.warning,
        retryAt: query.data?.retryAt,
        isLoading: query.isLoading,
        isFetching: query.isFetching,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}

interface GrabInteractiveCandidateVariables {
    contentHierarchyItemId: string;
    release: ApiRelease;
}

async function grabCandidate(variables: GrabInteractiveCandidateVariables): Promise<JobActionDisposition> {
    const response = await apiPost<JobActionResponse>(
        `/interactive-search/${variables.contentHierarchyItemId}/grab`,
        variables.release,
    );
    return normalizeJobActionDisposition(response);
}

export function useGrabInteractiveCandidate(options?: JobActionLifecycleOptions<GrabInteractiveCandidateVariables>) {
    const queryClient = useQueryClient();

    return useJobAction<GrabInteractiveCandidateVariables>({
        mutationFn: grabCandidate,
        onJobStarted: (disposition, variables) => {
            void queryClient.invalidateQueries({
                queryKey: contentReleaseQueryKeys.forContent(variables.contentHierarchyItemId),
            });
            void queryClient.invalidateQueries({
                queryKey: contentQueryKeys.detail(variables.contentHierarchyItemId),
            });
            options?.onJobStarted?.(disposition, variables);
        },
        onJobExecutionSettled: options?.onJobExecutionSettled,
        trackExecution: options?.trackExecution,
    });
}
