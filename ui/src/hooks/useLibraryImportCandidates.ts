import type { PagedList } from '@/types/paging';

export type LibraryImportCandidateStatus =
    | 'Pending'
    | 'Failed'
    | 'Invalidated'
    | 'Queued'
    | 'Importing'
    | 'MatchingPending'
    | 'Rejected';

export interface LibraryImportMatch {
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

    contentHierarchyItemId?: string | null;

    notes?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export type PagedLibraryImportCandidates = PagedList<LibraryImportCandidate>;

export const libraryImportCandidateQueryKeys = {
    all: ['import/candidates'] as const,
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
