import { useMemo } from 'react';

import { useQuery } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export interface LibraryTypeDefinition {
    id: string;

    name: string;

    slug: string;
}

interface ApiContentTypeDefinition {
    slug: string;
    displayName: string;
    levelDefinitions: unknown[];
}

interface ApiSubjectKindDefinition {
    slug: string;
    displayName: string | null;
    customMetadataFields: unknown[];
}

export const libraryTypesQueryKeys = {
    all: ['libraryTypes'] as const,
    contentTypes: () => ['libraryTypes', 'content-types'] as const,
    subjectTypes: () => ['libraryTypes', 'subject-types'] as const,
};

async function fetchContentTypes(): Promise<ApiContentTypeDefinition[]> {
    return apiGet<ApiContentTypeDefinition[]>('/content-types');
}

async function fetchSubjectTypes(): Promise<ApiSubjectKindDefinition[]> {
    return apiGet<ApiSubjectKindDefinition[]>('/subject-types');
}

function mapContentTypes(types: ApiContentTypeDefinition[]): LibraryTypeDefinition[] {
    return types.map((t) => ({
        id: t.slug,
        name: t.displayName,
        slug: t.slug,
    }));
}

function mapSubjectTypes(types: ApiSubjectKindDefinition[]): LibraryTypeDefinition[] {
    return types.map((t) => ({
        id: t.slug,
        name: t.displayName ?? t.slug,
        slug: t.slug,
    }));
}

export function useLibraryTypes() {
    const contentTypesQuery = useQuery({
        queryKey: libraryTypesQueryKeys.contentTypes(),
        queryFn: fetchContentTypes,
        staleTime: POLLING_INTERVALS.slow,
        gcTime: 5 * 60 * 1000,
    });

    const subjectTypesQuery = useQuery({
        queryKey: libraryTypesQueryKeys.subjectTypes(),
        queryFn: fetchSubjectTypes,
        staleTime: POLLING_INTERVALS.slow,
        gcTime: 5 * 60 * 1000,
    });

    const contentTypes = useMemo(
        () => (contentTypesQuery.data ? mapContentTypes(contentTypesQuery.data) : []),
        [contentTypesQuery.data],
    );

    const subjectTypes = useMemo(
        () => (subjectTypesQuery.data ? mapSubjectTypes(subjectTypesQuery.data) : []),
        [subjectTypesQuery.data],
    );

    return {
        contentTypes,

        subjectTypes,

        isLoading: contentTypesQuery.isLoading || subjectTypesQuery.isLoading,

        error: queryErrorOrNull({ error: contentTypesQuery.error ?? subjectTypesQuery.error }),

        refetch: async () => {
            await Promise.all([contentTypesQuery.refetch(), subjectTypesQuery.refetch()]);
        },
    };
}

export type LibraryCounts = Record<string, number | undefined>;

interface PagedCountResponse {
    totalItems: number;
}

async function fetchContentTypeCount(slug: string): Promise<number> {
    const url = buildUrl('/content', { contentType: slug, pageSize: 1 });
    const data = await apiGet<PagedCountResponse>(url);
    return data.totalItems;
}

async function fetchSubjectTypeCount(slug: string): Promise<number> {
    const url = buildUrl('/subjects', { kind: slug });
    const data = await apiGet<unknown[]>(url);
    return data.length;
}

export const libraryCountsQueryKeys = {
    counts: (contentSlugs: string[], subjectSlugs: string[]) =>
        ['libraryTypes', 'counts', contentSlugs, subjectSlugs] as const,
};

export function useLibraryCounts(contentTypes: LibraryTypeDefinition[], subjectTypes: LibraryTypeDefinition[]) {
    const contentSlugs = contentTypes.map((t) => t.slug);
    const subjectSlugs = subjectTypes.map((t) => t.slug);

    const countsQuery = useQuery({
        queryKey: libraryCountsQueryKeys.counts(contentSlugs, subjectSlugs),
        queryFn: async () => {
            const [contentResults, subjectResults] = await Promise.all([
                Promise.all(contentTypes.map((t) => fetchContentTypeCount(t.slug).then((n) => [t.slug, n] as const))),
                Promise.all(subjectTypes.map((t) => fetchSubjectTypeCount(t.slug).then((n) => [t.slug, n] as const))),
            ]);

            const counts: LibraryCounts = {};

            for (const [slug, count] of contentResults) {
                counts[slug] = count;
            }

            for (const [slug, count] of subjectResults) {
                counts[slug] = count;
            }

            return counts;
        },
        enabled: contentTypes.length > 0 || subjectTypes.length > 0,
        staleTime: POLLING_INTERVALS.slow,
        gcTime: 5 * 60 * 1000,
    });

    return { counts: countsQuery.data ?? {} };
}
