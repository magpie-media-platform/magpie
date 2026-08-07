import { useMutation } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import {
    normalizeProviderMetadataCandidateSearchResult,
    type ProviderMetadataCandidateSearchResponse,
    type ProviderMetadataCandidateSearchResult,
} from '@/hooks/useProviderMetadataCandidateSearch';

interface SavedProviderMetadataCandidateSearchRequest {
    query: string;

    contentType?: string;

    page?: number;
}

async function searchSavedProviderMetadataCandidates(
    request: SavedProviderMetadataCandidateSearchRequest,
): Promise<ProviderMetadataCandidateSearchResult> {
    const url = buildUrl('/content-search', {
        query: request.query,
        contentType: request.contentType,
        page: request.page,
    });
    const response = await apiGet<ProviderMetadataCandidateSearchResponse>(url);
    return normalizeProviderMetadataCandidateSearchResult(response);
}

export function useSavedProviderMetadataCandidateSearch() {
    return useMutation({
        mutationFn: searchSavedProviderMetadataCandidates,
    });
}
