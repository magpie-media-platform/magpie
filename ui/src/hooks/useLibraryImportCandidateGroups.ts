import { useQuery } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { LibraryImportCandidateStatus } from '@/hooks/useLibraryImportCandidates';
import { apiGet, buildUrl } from '@/lib/api';
import { queryErrorOrNull } from '@/lib/utils';
import type { PagedList } from '@/types/paging';

interface LibraryImportGroupMatch {
    confidence: number;

    providerType: string;

    externalIdKey: string;

    externalId: string;

    title: string;

    subtitle?: string | null;

    imageUrl?: string | null;

    levelSlug?: string | null;
}

export type LibraryImportCandidateStatusCounts = Partial<Record<LibraryImportCandidateStatus, number>>;

export type LibraryImportCandidateGroupKind = 'Directory' | 'Match' | 'Query';

export interface LibraryImportCandidateGroup {
    groupKey: string;

    kind: LibraryImportCandidateGroupKind;

    sourcePath?: string | null;

    title: string;

    contentType: string;

    subtitle?: string | null;

    match?: LibraryImportGroupMatch | null;

    leafCount: number;

    topLevelSlug: string;

    leafLevelSlug: string;

    leafLevelDisplayName: string;

    childLevelSlug?: string | null;

    childLevelDisplayName?: string | null;

    statusCounts: LibraryImportCandidateStatusCounts;
}

export type PagedLibraryImportCandidateGroups = PagedList<LibraryImportCandidateGroup>;

export interface LibraryImportCandidateGroupQueryParams {
    rootFolderId?: string;

    pageNumber?: number;

    pageSize?: number;
}

export const libraryImportCandidateGroupQueryKeys = {
    all: ['import/candidate-groups'] as const,
    list: (params?: LibraryImportCandidateGroupQueryParams) =>
        ['import/candidate-groups', 'list', params ?? {}] as const,
};

async function fetchLibraryImportCandidateGroups(
    params?: LibraryImportCandidateGroupQueryParams,
): Promise<PagedLibraryImportCandidateGroups> {
    const url = buildUrl('/import/candidate-groups', params);
    return apiGet<PagedLibraryImportCandidateGroups>(url);
}

export function useLibraryImportCandidateGroups(params?: LibraryImportCandidateGroupQueryParams) {
    useInvalidateQueriesOnEvents(
        ['library.scan.completed', 'library.importCandidate.updated'],
        [libraryImportCandidateGroupQueryKeys.list(params)],
        { debounceMs: 300, exact: true, maxWaitMs: 1200, owner: 'library-import-candidate-groups' },
    );

    const query = useQuery({
        queryKey: libraryImportCandidateGroupQueryKeys.list(params),
        queryFn: () => fetchLibraryImportCandidateGroups(params),
    });

    const items: LibraryImportCandidateGroup[] = query.data?.items ?? [];
    const pagination = query.data
        ? {
              pageNumber: query.data.pageNumber,
              pageSize: query.data.pageSize,
              totalItems: query.data.totalItems,
          }
        : null;

    return {
        items,
        pagination,
        isLoading: query.isLoading,
        isFetching: query.isFetching,
        isError: query.isError,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}
