import { useQuery } from '@tanstack/react-query';

import { apiGet, buildUrl, ApiError } from '@/lib/api';
import { extractImageUrl, extractTitle } from '@/lib/metadata';
import type { ApiContentHierarchyItem } from '@/types/content';
import type { MaterializedMetadata } from '@/types/contentMetadata';
import type { PagedList } from '@/types/paging';
import type { ApiSubject } from './useSubjects';
import type { ContentPreview } from './usePreviewByExternalId';

export async function fetchLocalContentPreview(
    id: string,
    signal?: AbortSignal,
    identity: 'hierarchy' | 'item' = 'hierarchy',
): Promise<ContentPreview & { localContentId: string }> {
    const options = signal ? { signal } : undefined;
    if (identity === 'item') {
        const page = await apiGet<PagedList<ApiContentHierarchyItem>>(
            buildUrl('/content', { contentItemId: id, pageSize: 2 }),
            options,
        );
        const item = page.items[0];
        if (
            page.totalItems !== 1 ||
            page.items.length !== 1 ||
            item?.contentItem?.id.toLowerCase() !== id.toLowerCase()
        )
            throw new ApiError(404, 'This library item is no longer available.');
        return fetchLocalContentPreview(item.id, signal);
    }
    const [item, metadata] = await Promise.all([
        apiGet<ApiContentHierarchyItem>(`/content/${encodeURIComponent(id)}`, options),
        apiGet<MaterializedMetadata>(`/content/${encodeURIComponent(id)}/metadata`, options),
    ]);
    signal?.throwIfAborted();
    const subjects = new Map<string, ApiSubject>();
    const relationships = metadata.relationships ?? [];
    const remaining = new Set(
        relationships.flatMap((r) => (r.relatedMetadataContainerId ? [r.relatedMetadataContainerId] : [])),
    );
    for (let pageNumber = 1; remaining.size > 0; pageNumber += 1) {
        const page = await apiGet<PagedList<ApiSubject>>(
            buildUrl('/subjects', { relatedContentId: id, pageNumber, pageSize: 100 }),
            options,
        );
        for (const subject of page.items) {
            subjects.set(subject.metadataContainerId, subject);
            remaining.delete(subject.metadataContainerId);
        }
        if (page.items.length === 0 || pageNumber * 100 >= page.totalItems) break;
    }
    return {
        localContentId: item.id,
        providerType: '',
        contentType: item.contentType,
        levelSlug: item.level,
        title: metadata.title ?? item.name,
        originalTitle: metadata.originalTitle,
        overview: metadata.overview,
        releaseDate: metadata.publishDate,
        year: metadata.publishDate ? Number(metadata.publishDate.slice(0, 4)) || undefined : undefined,
        duration: metadata.duration,
        language: metadata.language,
        productionStatus: metadata.productionStatus,
        genres: (metadata.genres ?? []).map((genre) => genre.name),
        contentRatings: metadata.contentRatings ?? [],
        images: metadata.images ?? [],
        externalIds: metadata.externalIds ?? [],
        previews: (metadata.previews ?? []).map((preview) => ({
            ...preview,
            title: preview.title ?? undefined,
            mimeType: preview.mimeType ?? undefined,
            embedKind: preview.embedKind ?? undefined,
            language: preview.language ?? undefined,
        })),
        relatedSubjects: relationships
            .flatMap((relationship) => {
                const subject = subjects.get(relationship.relatedMetadataContainerId ?? '');
                return subject
                    ? [
                          {
                              name: extractTitle(subject.metadataContainer) ?? 'Unknown',
                              kind: subject.kind,
                              relationshipKind: relationship.kind,
                              relationshipQualifier: relationship.qualifier,
                              ordinal: relationship.ordinal ?? 0,
                              imageUrl: extractImageUrl(subject.metadataContainer),
                          },
                      ]
                    : [];
            })
            .sort((left, right) => left.ordinal - right.ordinal),
    };
}

export function useLocalContentPreview(id: string | undefined, identity: 'hierarchy' | 'item' = 'hierarchy') {
    return useQuery({
        queryKey: ['content', 'local-preview', id, identity],
        queryFn: ({ signal }) => fetchLocalContentPreview(id!, signal, identity),
        enabled: !!id,
        staleTime: 0,
        retry: false,
    });
}
