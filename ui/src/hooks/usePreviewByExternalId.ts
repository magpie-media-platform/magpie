import { useMutation } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import type { ContentPreviewClip } from '@/types/content';
import type { TemporalPrecision } from '@/types/contentMetadata';

export interface PreviewByExternalIdRequest {
    providerType: string;

    externalIdKey: string;

    externalId: string;

    contentType: string;

    levelSlug?: string;
}

interface ContentPreviewImage {
    type: string;
    url: string;
    language?: string | null;
}

interface ContentPreviewRating {
    body: string;
    rating: string;
}

interface ContentPreviewExternalId {
    source?: string | null;
    value: string;
}

export interface ContentPreviewRelatedSubject {
    name: string;
    kind: string;
    relationshipKind: string;
    relationshipQualifier?: string | null;
    ordinal: number;
    imageUrl?: string | null;
}

export interface ContentPreview {
    providerType: string;
    contentType: string;
    levelSlug: string;
    title?: string | null;
    originalTitle?: string | null;
    overview?: string | null;
    releaseDate?: string | null;
    releaseDatePrecision?: TemporalPrecision | null;
    year?: number | null;
    duration?: string | null;
    language?: string | null;
    productionStatus?: string | null;
    genres: string[];
    contentRatings: ContentPreviewRating[];
    images: ContentPreviewImage[];
    previews: ContentPreviewClip[];
    externalIds: ContentPreviewExternalId[];
    relatedSubjects: ContentPreviewRelatedSubject[];
}

async function fetchPreviewByExternalId(request: PreviewByExternalIdRequest): Promise<ContentPreview> {
    return apiPost<ContentPreview>('/content/preview-by-external-id', request);
}

export function usePreviewByExternalId() {
    return useMutation({
        mutationFn: fetchPreviewByExternalId,
    });
}
