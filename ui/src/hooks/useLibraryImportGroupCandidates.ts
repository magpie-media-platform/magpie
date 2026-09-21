import { skipToken, useQuery } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { LibraryImportCandidate } from '@/hooks/useLibraryImportCandidates';
import { apiGet, buildUrl } from '@/lib/api';
import { MAX_PAGE_FETCHES } from '@/lib/paging';
import { queryErrorOrNull } from '@/lib/utils';
import type { PagedList } from '@/types/paging';

const GROUP_CANDIDATES_PAGE_SIZE = 500;

export interface LibraryImportGroupCandidatesParams {
    groupKey: string;

    rootFolderId?: string;
}

async function fetchAllGroupCandidates(params: LibraryImportGroupCandidatesParams) {
    const candidates: LibraryImportCandidate[] = [];
    let totalItems = 0;

    for (let pageNumber = 1; pageNumber <= MAX_PAGE_FETCHES; pageNumber += 1) {
        const url = buildUrl('/import/candidates', {
            groupKey: params.groupKey,
            rootFolderId: params.rootFolderId,
            pageNumber,
            pageSize: GROUP_CANDIDATES_PAGE_SIZE,
        });
        const page = await apiGet<PagedList<LibraryImportCandidate>>(url);
        candidates.push(...page.items);
        totalItems = page.totalItems;

        if (page.items.length < GROUP_CANDIDATES_PAGE_SIZE || candidates.length >= page.totalItems) {
            break;
        }
    }
    return { items: candidates, truncated: candidates.length < totalItems };
}

export const libraryImportGroupCandidatesQueryKeys = {
    list: (params: LibraryImportGroupCandidatesParams) => ['import/candidates', 'group', params] as const,
};

export function useLibraryImportGroupCandidates(params: LibraryImportGroupCandidatesParams | null) {
    useInvalidateQueriesOnEvents(
        params ? ['library.scan.completed', 'library.importCandidate.updated'] : [],
        params ? [libraryImportGroupCandidatesQueryKeys.list(params)] : [],
        { debounceMs: 300, exact: true, maxWaitMs: 1200, owner: 'library-import-group-candidates' },
    );

    const query = useQuery({
        queryKey: libraryImportGroupCandidatesQueryKeys.list(params ?? { groupKey: '' }),
        queryFn: params ? () => fetchAllGroupCandidates(params) : skipToken,
    });

    return {
        items: query.data?.items ?? [],
        isTruncated: query.data?.truncated ?? false,
        isLoading: query.isLoading,
        isFetching: query.isFetching,
        isError: query.isError,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}
