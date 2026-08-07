import { useMutation } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import {
    normalizeProviderMetadataCandidateSearchResult,
    type ProviderMetadataCandidateSearchResponse,
    type ProviderMetadataCandidateSearchResult,
} from '@/hooks/useProviderMetadataCandidateSearch';
import type { ContentFilterCriteria } from '@/hooks/useFilters';
import { formatMagpieQlCriteria } from '@/lib/magpieQl';

interface WishlistPreviewRequest {
    criteria: ContentFilterCriteria;

    contentType?: string;

    page?: number;
}

async function previewWishlist(request: WishlistPreviewRequest): Promise<ProviderMetadataCandidateSearchResult> {
    const formatted = await formatMagpieQlCriteria(request.criteria, 'content');
    const url = buildUrl('/content-search', {
        query: formatted.query,
        contentType: request.contentType,
        page: request.page,
    });
    const response = await apiGet<ProviderMetadataCandidateSearchResponse>(url);
    return normalizeProviderMetadataCandidateSearchResult(response);
}

export function useWishlistPreview() {
    return useMutation({
        mutationFn: previewWishlist,
    });
}
