import { useMutation } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';

export interface ProviderMetadataCandidate {
    providerType: string;

    externalIdSource: string;

    externalId: string;

    contentType: string;

    level?: string | null;

    title?: string | null;

    year?: number | null;

    releaseDate?: string | null;

    overview?: string | null;

    language?: string | null;

    thumbnailUrl?: string | null;

    inLibrary?: boolean;
}

export interface ProviderMetadataCandidateSearchResult {
    candidates: ProviderMetadataCandidate[];

    pageItemCount: number;

    excludedCount: number;

    pageInfo: ProviderSearchPageInfo;

    warnings?: string[];
}

interface ProviderSearchPageInfo {
    requestedPage: number;

    hasMore: boolean;
}

export interface ProviderMetadataCandidateSearchResponse {
    matches: Array<{
        candidate: Omit<ProviderMetadataCandidate, 'inLibrary'>;

        inLibrary: boolean;
    }>;

    pageItemCount: number;

    existsInLibraryCount: number;

    pageInfo: ProviderSearchPageInfo;

    warnings?: string[];
}

interface ProviderMetadataCandidateSearchRequest {
    query: string;

    contentType?: string;

    page?: number;
}

export function normalizeProviderMetadataCandidateSearchResult(
    response: ProviderMetadataCandidateSearchResponse,
): ProviderMetadataCandidateSearchResult {
    return {
        candidates: response.matches.map((match) => ({
            ...match.candidate,
            inLibrary: match.inLibrary,
        })),
        pageItemCount: response.pageItemCount,
        excludedCount: response.existsInLibraryCount,
        pageInfo: response.pageInfo,
        warnings: response.warnings,
    };
}

async function searchProviderMetadataCandidates(
    request: ProviderMetadataCandidateSearchRequest,
): Promise<ProviderMetadataCandidateSearchResult> {
    const url = buildUrl('/content-search', {
        query: request.query,
        contentType: request.contentType,
        page: request.page,
    });
    const response = await apiGet<ProviderMetadataCandidateSearchResponse>(url);
    return normalizeProviderMetadataCandidateSearchResult(response);
}

export function useProviderMetadataCandidateSearch() {
    return useMutation({
        mutationFn: searchProviderMetadataCandidates,
    });
}
