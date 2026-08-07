import { useQuery } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet, buildUrl } from '@/lib/api';
import { queryErrorOrNull } from '@/lib/utils';
import type { PagedList } from '@/types/paging';

type LibraryImportCandidateStatus = 'Pending' | 'Failed' | 'Invalidated' | 'Queued' | 'Importing';

interface LibraryImportMatch {
    confidence: number;

    providerType: string;

    externalIdKey: string;

    externalId: string;

    title: string;

    subtitle?: string | null;

    imageUrl?: string | null;

    levelSlug?: string | null;
}

export type ParsedSignalBag = Record<string, string[] | undefined>;

export const PARSED_FILE_SIGNAL_TITLE = 'title';
export const PARSED_FILE_SIGNAL_YEAR = 'year';

export interface LibraryImportCandidate {
    id: string;

    rootFolderId: string;

    scanRunId: string;

    filePath: string;

    contentType: string;

    status: LibraryImportCandidateStatus;

    parsedSignals?: ParsedSignalBag | null;

    matches: LibraryImportMatch[];

    matchCount: number;

    notes?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export type PagedLibraryImportCandidates = PagedList<LibraryImportCandidate>;

export interface LibraryImportCandidateQueryParams {
    rootFolderId?: string;

    status?: LibraryImportCandidateStatus | LibraryImportCandidateStatus[];

    hasMatches?: boolean;

    pageNumber?: number;

    pageSize?: number;

    sortBy?: string;

    sortDirection?: 0 | 1 | 2;
}

export const libraryImportCandidateQueryKeys = {
    all: ['import/candidates'] as const,
    list: (params?: LibraryImportCandidateQueryParams) => ['import/candidates', 'list', params ?? {}] as const,
    detail: (id: string) => ['import/candidates', 'detail', id] as const,
};

export function getParsedSignalValue(signals: ParsedSignalBag | null | undefined, key: string): string {
    const values = signals?.[key];
    if (!Array.isArray(values)) {
        return '';
    }

    return values.find((value) => value.trim().length > 0)?.trim() ?? '';
}

export function getParsedSignalNumber(signals: ParsedSignalBag | null | undefined, key: string): number | null {
    const value = getParsedSignalValue(signals, key);
    if (!/^\d+$/.test(value)) {
        return null;
    }

    return Number(value);
}

async function fetchLibraryImportCandidates(
    params?: LibraryImportCandidateQueryParams,
): Promise<PagedLibraryImportCandidates> {
    const url = buildUrl('/import/candidates', params);
    return apiGet<PagedLibraryImportCandidates>(url);
}

export function useLibraryImportCandidates(params?: LibraryImportCandidateQueryParams) {
    useInvalidateQueriesOnEvents(
        ['library.scan.completed', 'library.importCandidate.updated'],
        [libraryImportCandidateQueryKeys.list(params)],
        { debounceMs: 300, exact: true, maxWaitMs: 1200, owner: 'library-import-candidates' },
    );

    const query = useQuery({
        queryKey: libraryImportCandidateQueryKeys.list(params),
        queryFn: () => fetchLibraryImportCandidates(params),
    });

    const items: LibraryImportCandidate[] = query.data?.items ?? [];
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
