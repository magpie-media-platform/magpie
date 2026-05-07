import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { sortedContributions } from '@/lib/metadata';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export interface ApiExternalId {
    id: string;
    value: string;
    source?: string | null;
    ordinal: number;
}

export interface ApiGenre {
    id: string;
    name: string;
    ordinal: number;
}

export interface ApiContentRating {
    id: string;
    body: string;
    rating: string;
    ordinal: number;
}

export interface ApiAward {
    id: string;
    body: string;
    name: string;
    category?: string | null;
    status?: string | null;
    year?: number | null;
    ordinal: number;
}

export interface ApiRelationship {
    id: string;
    kind: string;
    qualifier?: string | null;
    relatedMetadataContainerId?: string | null;
    ordinal: number;
}

export interface ApiMetadataContribution {
    id: string;
    ordinal: number;
    locked: boolean;
    providerType?: string | null;
    source?: string | null;
    title?: string | null;
    originalTitle?: string | null;
    sortTitle?: string | null;
    language?: string | null;
    overview?: string | null;
    publishDate?: string | null;
    productionStatus?: string | null;
    images?: ApiMetadataImage[] | null;
    externalIds?: ApiExternalId[] | null;
    genres?: ApiGenre[] | null;
    contentRatings?: ApiContentRating[] | null;
    awards?: ApiAward[] | null;
    relationships?: ApiRelationship[] | null;
}

export interface ApiMetadataImage {
    type: string;
    url: string;
    width?: number | null;
    height?: number | null;
}

export interface ApiMetadataContainer {
    id: string;
    createdAt: string;
    contributions: ApiMetadataContribution[];
}

interface ApiMaterializedMetadata {
    title?: string | null;
    originalTitle?: string | null;
    sortTitle?: string | null;
    overview?: string | null;
    releaseDate?: string | null;
    endDate?: string | null;
    runtime?: number | null;
    status?: string | null;
    images?: ApiMetadataImage[] | null;
    externalIds?: { source: string; externalId: string; url?: string | null }[] | null;
    genres?: { name: string }[] | null;
    contentRatings?: { rating: string; region?: string | null }[] | null;
}

export interface ApiSubject {
    id: string;

    metadataContainerId: string;

    kind: string;

    dateLastRefreshed?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    metadataContainer?: ApiMetadataContainer | null;

    metadata?: ApiMaterializedMetadata | null;
}

function normalizeSubjectMetadata(subject: ApiSubject): ApiSubject {
    if (subject.metadataContainer || !subject.metadata) {
        return subject;
    }

    const m = subject.metadata;
    const contribution: ApiMetadataContribution = {
        id: 'materialized',
        ordinal: 0,
        locked: false,
        title: m.title,
        originalTitle: m.originalTitle,
        sortTitle: m.sortTitle,
        overview: m.overview,
        publishDate: m.releaseDate,
        productionStatus: m.status,
        images: m.images ?? null,
        externalIds:
            m.externalIds?.map((e, i) => ({
                id: `ext-${i}`,
                value: e.externalId,
                source: e.source,
                ordinal: i,
            })) ?? null,
        genres:
            m.genres?.map((g, i) => ({
                id: `genre-${i}`,
                name: g.name,
                ordinal: i,
            })) ?? null,
        contentRatings:
            m.contentRatings?.map((cr, i) => ({
                id: `cr-${i}`,
                body: cr.region ?? '',
                rating: cr.rating,
                ordinal: i,
            })) ?? null,
    };

    return {
        ...subject,
        metadataContainer: {
            id: subject.metadataContainerId,
            createdAt: subject.createdAt,
            contributions: [contribution],
        },
    };
}

export interface StringSetPatch {
    add?: string[];
    remove?: string[];
}

export interface SubjectTagsPatch {
    id: string;
    tags?: StringSetPatch;
}

export interface SubjectQueryParams {
    kind?: string;
    tag?: string;
}

export const subjectQueryKeys = {
    all: ['subjects'] as const,
    list: (params?: SubjectQueryParams) => ['subjects', 'list', params ?? {}] as const,
    detail: (id: string) => ['subjects', 'detail', id] as const,
};

export function extractLanguage(container?: ApiMetadataContainer | null): string | undefined {
    if (!container?.contributions?.length) {
        return undefined;
    }

    const sorted = sortedContributions(container);
    for (const contribution of sorted) {
        if (contribution.language) {
            return contribution.language;
        }
    }
    return undefined;
}

export function extractStatus(container?: ApiMetadataContainer | null): string | undefined {
    if (!container?.contributions?.length) {
        return undefined;
    }

    const sorted = sortedContributions(container);
    for (const contribution of sorted) {
        if (contribution.productionStatus) {
            return contribution.productionStatus;
        }
    }
    return undefined;
}

export function extractGenres(container?: ApiMetadataContainer | null): string[] {
    if (!container?.contributions?.length) {
        return [];
    }

    const sorted = sortedContributions(container);
    const genres: string[] = [];
    const seen = new Set<string>();

    for (const contribution of sorted) {
        if (!contribution.genres?.length) continue;
        const sortedGenres = [...contribution.genres].sort((a, b) => a.ordinal - b.ordinal);
        for (const genre of sortedGenres) {
            if (!seen.has(genre.name)) {
                seen.add(genre.name);
                genres.push(genre.name);
            }
        }
    }

    return genres;
}

export function extractContentRating(container?: ApiMetadataContainer | null): string | undefined {
    if (!container?.contributions?.length) {
        return undefined;
    }

    const sorted = sortedContributions(container);
    for (const contribution of sorted) {
        if (!contribution.contentRatings?.length) continue;
        const sortedRatings = [...contribution.contentRatings].sort((a, b) => a.ordinal - b.ordinal);
        if (sortedRatings[0]?.rating) {
            return sortedRatings[0].rating;
        }
    }
    return undefined;
}

export function extractExternalIds(
    container?: ApiMetadataContainer | null,
): { source: string; id: string; url?: string }[] {
    if (!container?.contributions?.length) {
        return [];
    }

    const sorted = sortedContributions(container);
    const externalIds: { source: string; id: string; url?: string }[] = [];
    const seen = new Set<string>();

    for (const contribution of sorted) {
        if (!contribution.externalIds?.length) continue;
        const sortedIds = [...contribution.externalIds].sort((a, b) => a.ordinal - b.ordinal);
        for (const extId of sortedIds) {
            const key = `${extId.source ?? 'unknown'}:${extId.value}`;
            if (!seen.has(key)) {
                seen.add(key);
                externalIds.push({
                    source: extId.source ?? 'Unknown',
                    id: extId.value,
                    url: buildExternalIdUrl(extId.source, extId.value),
                });
            }
        }
    }

    return externalIds;
}

function buildExternalIdUrl(source?: string | null, value?: string): string | undefined {
    if (!source || !value) return undefined;

    const sourceLower = source.toLowerCase();
    switch (sourceLower) {
        case 'imdb':
            return `https://www.imdb.com/title/${value}/`;
        case 'tmdb':
            return `https://www.themoviedb.org/tv/${value}`;
        case 'tvdb':
        case 'thetvdb':
            return `https://thetvdb.com/?id=${value}&tab=series`;
        default:
            return undefined;
    }
}

export function extractAwards(container?: ApiMetadataContainer | null): string[] {
    if (!container?.contributions?.length) {
        return [];
    }

    const sorted = sortedContributions(container);
    const awards: string[] = [];

    for (const contribution of sorted) {
        if (!contribution.awards?.length) continue;
        const sortedAwards = [...contribution.awards].sort((a, b) => a.ordinal - b.ordinal);
        for (const award of sortedAwards) {
            const parts = [award.body];
            if (award.category) {
                parts.push(award.category);
            } else if (award.name) {
                parts.push(award.name);
            }
            if (award.year) {
                parts.push(`(${award.year})`);
            }
            if (award.status) {
                parts.push(`[${award.status}]`);
            }
            awards.push(parts.join(' '));
        }
    }

    return awards;
}

export function extractImages(
    container?: ApiMetadataContainer | null,
): { id: string; url: string; type: string; width?: number; height?: number }[] {
    if (!container?.contributions?.length) {
        return [];
    }

    const sorted = sortedContributions(container);
    const images: { id: string; url: string; type: string; width?: number; height?: number }[] = [];

    for (const contribution of sorted) {
        if (!contribution.images?.length) continue;
        for (const img of contribution.images) {
            images.push({
                id: `${contribution.id}-${img.url}`,
                url: img.url,
                type: img.type,
                width: img.width ?? undefined,
                height: img.height ?? undefined,
            });
        }
    }

    return images;
}

export function extractRelationships(
    container?: ApiMetadataContainer | null,
): { id: string; kind: string; qualifier?: string; relatedMetadataContainerId?: string }[] {
    if (!container?.contributions?.length) {
        return [];
    }

    const sorted = sortedContributions(container);
    const relationships: {
        id: string;
        kind: string;
        qualifier?: string;
        relatedMetadataContainerId?: string;
    }[] = [];

    for (const contribution of sorted) {
        if (!contribution.relationships?.length) continue;
        const sortedRels = [...contribution.relationships].sort((a, b) => a.ordinal - b.ordinal);
        for (const rel of sortedRels) {
            relationships.push({
                id: rel.id,
                kind: rel.kind,
                qualifier: rel.qualifier ?? undefined,
                relatedMetadataContainerId: rel.relatedMetadataContainerId ?? undefined,
            });
        }
    }

    return relationships;
}

async function fetchSubjects(params?: SubjectQueryParams): Promise<ApiSubject[]> {
    const url = buildUrl('/subjects', params);
    return apiGet<ApiSubject[]>(url);
}

async function fetchSubjectById(id: string): Promise<ApiSubject> {
    const subject = await apiGet<ApiSubject>(`/subjects/${id}`);
    return normalizeSubjectMetadata(subject);
}

async function patchSubject(id: string, data: Omit<SubjectTagsPatch, 'id'>): Promise<void> {
    return apiPatch(`/subjects/${id}`, data);
}

async function deleteSubject(id: string): Promise<void> {
    return apiDelete(`/subjects/${id}`);
}

export function useSubjects(params?: SubjectQueryParams) {
    const queryClient = useQueryClient();

    const subjectsQuery = useQuery({
        queryKey: subjectQueryKeys.list(params),
        queryFn: () => fetchSubjects(params),
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, data }: { id: string; data: Omit<SubjectTagsPatch, 'id'> }) => patchSubject(id, data),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: subjectQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteSubject,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: subjectQueryKeys.all });
        },
    });

    const subjects: ApiSubject[] = subjectsQuery.data ?? [];

    const updateSubjectTags = async (id: string, data: Omit<SubjectTagsPatch, 'id'>): Promise<void> => {
        await patchMutation.mutateAsync({ id, data });
    };

    return {
        subjects,
        isLoading: subjectsQuery.isLoading,
        error: queryErrorOrNull(subjectsQuery),

        updateSubjectTags,
        isUpdating: patchMutation.isPending,

        deleteSubject: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        refetch: subjectsQuery.refetch,
    };
}

export function useSubject(id: string, enabled = true) {
    const subjectQuery = useQuery({
        queryKey: subjectQueryKeys.detail(id),
        queryFn: () => fetchSubjectById(id),
        enabled: enabled && Boolean(id),
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    return {
        subject: subjectQuery.data ?? null,
        isLoading: subjectQuery.isLoading,
        error: queryErrorOrNull(subjectQuery),
        refetch: subjectQuery.refetch,
    };
}
