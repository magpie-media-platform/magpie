import { useMemo } from 'react';

import { useQuery } from '@tanstack/react-query';

import { useAuth } from '@/auth';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type {
    LibraryLevelDefinition,
    LibraryMetadataFieldDefinition,
    LibraryStandardMetadataFieldDefinition,
    LibraryTypeDefinition,
} from '@/types/library';

export type {
    LibraryLevelDefinition,
    LibraryMetadataFieldDefinition,
    LibraryMetadataValueKind,
    LibraryStandardMetadataFieldDefinition,
    LibraryTypeDefinition,
} from '@/types/library';

const METADATA_FIELD_FLAGS: Record<string, number> = {
    none: 0x0,
    title: 0x1,
    originaltitle: 0x2,
    sorttitle: 0x4,
    language: 0x8,
    overview: 0x10,
    publishdate: 0x20,
    duration: 0x40,
    productionstatus: 0x80,
    established: 0x100,
    dissolved: 0x200,
    birthdate: 0x400,
    deathdate: 0x800,
    externalids: 0x1000,
    references: 0x2000,
    relationships: 0x4000,
    images: 0x8000,
    previews: 0x10000,
    alternatetexts: 0x20000,
    contentratings: 0x40000,
    awards: 0x80000,
    genres: 0x100000,
    sequencenumber: 0x200000,
};

const ALL_METADATA_FIELDS = Object.values(METADATA_FIELD_FLAGS).reduce((mask, field) => mask | field, 0);

export const STANDARD_METADATA_FIELDS: readonly LibraryStandardMetadataFieldDefinition[] = [
    { field: 'Title', key: 'title', displayName: 'Title', dataType: 'string', valueKind: 'text', editable: true },
    {
        field: 'OriginalTitle',
        key: 'originalTitle',
        displayName: 'Original title',
        dataType: 'string',
        valueKind: 'text',
        editable: true,
    },
    {
        field: 'SortTitle',
        key: 'sortTitle',
        displayName: 'Sort title',
        dataType: 'string',
        valueKind: 'text',
        editable: true,
    },
    {
        field: 'Language',
        key: 'language',
        displayName: 'Language',
        dataType: 'string',
        valueKind: 'text',
        editable: true,
    },
    {
        field: 'Overview',
        key: 'overview',
        displayName: 'Overview',
        dataType: 'string',
        valueKind: 'text',
        editable: true,
    },
    {
        field: 'PublishDate',
        key: 'publishDate',
        displayName: 'Publish date',
        dataType: 'date',
        valueKind: 'date',
        editable: true,
    },
    {
        field: 'Duration',
        key: 'duration',
        displayName: 'Duration',
        dataType: 'duration',
        format: 'duration',
        valueKind: 'duration',
        editable: true,
    },
    {
        field: 'ProductionStatus',
        key: 'productionStatus',
        displayName: 'Production status',
        dataType: 'string',
        valueKind: 'text',
        editable: true,
    },
    {
        field: 'Established',
        key: 'established',
        displayName: 'Established',
        dataType: 'date',
        valueKind: 'date',
        editable: true,
    },
    {
        field: 'Dissolved',
        key: 'dissolved',
        displayName: 'Dissolved',
        dataType: 'date',
        valueKind: 'date',
        editable: true,
    },
    {
        field: 'BirthDate',
        key: 'birthDate',
        displayName: 'Birth date',
        dataType: 'date',
        valueKind: 'date',
        editable: true,
    },
    {
        field: 'DeathDate',
        key: 'deathDate',
        displayName: 'Death date',
        dataType: 'date',
        valueKind: 'date',
        editable: true,
    },
    {
        field: 'ExternalIds',
        key: 'externalIds',
        displayName: 'External IDs',
        dataType: 'collection',
        valueKind: 'collection',
        editable: false,
    },
    {
        field: 'References',
        key: 'references',
        displayName: 'References',
        dataType: 'collection',
        valueKind: 'collection',
        editable: false,
    },
    {
        field: 'Relationships',
        key: 'relationships',
        displayName: 'Relationships',
        dataType: 'collection',
        valueKind: 'collection',
        editable: false,
    },
    {
        field: 'Images',
        key: 'images',
        displayName: 'Images',
        dataType: 'collection',
        valueKind: 'collection',
        editable: false,
    },
    {
        field: 'Previews',
        key: 'previews',
        displayName: 'Previews',
        dataType: 'collection',
        valueKind: 'collection',
        editable: false,
    },
    {
        field: 'AlternateTexts',
        key: 'alternateTexts',
        displayName: 'Alternate texts',
        dataType: 'collection',
        valueKind: 'collection',
        editable: false,
    },
    {
        field: 'ContentRatings',
        key: 'contentRatings',
        displayName: 'Content ratings',
        dataType: 'collection',
        valueKind: 'collection',
        editable: false,
    },
    {
        field: 'Awards',
        key: 'awards',
        displayName: 'Awards',
        dataType: 'collection',
        valueKind: 'collection',
        editable: false,
    },
    {
        field: 'Genres',
        key: 'genres',
        displayName: 'Genres',
        dataType: 'collection',
        valueKind: 'collection',
        editable: false,
    },
    {
        field: 'SequenceNumber',
        key: 'sequenceNumber',
        displayName: 'Sequence number',
        dataType: 'number',
        valueKind: 'number',
        editable: true,
    },
];

interface ApiContentLevelDefinition {
    slug: string;
    displayName: string;
    parentSlug?: string | null;
    iconName?: string | null;
    supportedMetadataFields?: number | string | null;
    customMetadataFields?: unknown[] | null;
    standardFactFields?: unknown[] | null;
    acceptedFileTypes?: string[] | null;
    effectiveAcceptedMimeTypes?: string[] | null;
    acceptedSidecarTypes?: string[] | null;
    subfolder?: string | null;
}

interface ApiContentTypeDefinition {
    slug: string;
    displayName: string;
    pluralDisplayName?: string | null;
    iconName?: string | null;
    levelDefinitions: ApiContentLevelDefinition[];
}

interface ApiSubjectKindDefinition {
    slug: string;
    displayName: string | null;
    pluralDisplayName?: string | null;
    iconName?: string | null;
    customMetadataFields: unknown[];
    supportedMetadataFields?: number | string | null;
}

export const libraryTypesQueryKeys = {
    all: ['libraryTypes'] as const,
    contentTypes: () => ['libraryTypes', 'content-types'] as const,
    subjectTypes: () => ['libraryTypes', 'subject-types'] as const,
};

const LIBRARY_TYPE_CATALOG_EVENTS = [
    'extension.installed',
    'extension.enabled',
    'extension.disabled',
    'extension.uninstalled',
    'extension.upgraded',
] as const;

const LIBRARY_COUNT_EVENTS = [
    'content.hierarchyItem.created',
    'content.hierarchyItem.updated',
    'content.hierarchyItem.deleted',
    'subject.created',
    'subject.deleted',
    'library.scan.completed',
] as const;

async function fetchContentTypes(): Promise<ApiContentTypeDefinition[]> {
    return apiGet<ApiContentTypeDefinition[]>('/content-types');
}

async function fetchSubjectTypes(): Promise<ApiSubjectKindDefinition[]> {
    return apiGet<ApiSubjectKindDefinition[]>('/subject-types');
}

function normalizeMetadataFields(value: number | string | null | undefined): number {
    if (typeof value === 'number' && Number.isFinite(value)) {
        return value;
    }
    if (typeof value === 'string') {
        const parsed = Number(value);
        if (Number.isFinite(parsed)) {
            return parsed;
        }
        if (value.trim().replace(/\s+/g, '').toLowerCase() === 'all') {
            return ALL_METADATA_FIELDS;
        }
        const fields = value
            .split(',')
            .map((part) => part.trim().replace(/\s+/g, '').toLowerCase())
            .filter(Boolean);
        if (fields.length > 0) {
            return fields.reduce((mask, field) => mask | (METADATA_FIELD_FLAGS[field] ?? 0), 0);
        }
    }
    return ALL_METADATA_FIELDS;
}

function mergeMetadataFields(levels: LibraryLevelDefinition[]): number {
    if (levels.length === 0) {
        return ALL_METADATA_FIELDS;
    }
    return levels.reduce((fields, level) => fields | normalizeMetadataFields(level.supportedMetadataFields), 0);
}

function standardMetadataFieldsFromMask(mask: number): LibraryStandardMetadataFieldDefinition[] {
    return STANDARD_METADATA_FIELDS.filter((field) => {
        const flag = METADATA_FIELD_FLAGS[field.field.toLowerCase()];
        return flag !== undefined && (mask & flag) !== 0;
    });
}

function mergeStandardMetadataFields(levels: LibraryLevelDefinition[]): LibraryStandardMetadataFieldDefinition[] {
    if (levels.length === 0) {
        return standardMetadataFieldsFromMask(ALL_METADATA_FIELDS);
    }
    const seen = new Set<string>();
    const fields: LibraryStandardMetadataFieldDefinition[] = [];
    for (const level of levels) {
        for (const field of level.standardMetadataFields ?? []) {
            if (seen.has(field.field)) {
                continue;
            }
            seen.add(field.field);
            fields.push(field);
        }
    }
    return fields;
}

function normalizeFieldDefinition(value: unknown): LibraryMetadataFieldDefinition | null {
    if (!value || typeof value !== 'object') {
        return null;
    }
    const source = value as Record<string, unknown>;
    if (typeof source.key !== 'string' || source.key.trim() === '') {
        return null;
    }
    return {
        key: source.key,
        displayName: typeof source.displayName === 'string' ? source.displayName : null,
        dataType: typeof source.dataType === 'string' ? source.dataType : null,
        format: typeof source.format === 'string' ? source.format : null,
    };
}

function normalizeFieldDefinitions(values: unknown[] | null | undefined): LibraryMetadataFieldDefinition[] {
    return (values ?? [])
        .map(normalizeFieldDefinition)
        .filter((value): value is LibraryMetadataFieldDefinition => !!value);
}

export function getStandardMetadataField(
    definition:
        | Pick<LibraryTypeDefinition, 'standardMetadataFields'>
        | Pick<LibraryLevelDefinition, 'standardMetadataFields'>
        | null
        | undefined,
    field: string,
): LibraryStandardMetadataFieldDefinition | undefined {
    return definition?.standardMetadataFields?.find((f) => f.field.toLowerCase() === field.toLowerCase());
}

function mapContentTypes(types: ApiContentTypeDefinition[]): LibraryTypeDefinition[] {
    return types.map((t) => {
        const levels = (t.levelDefinitions ?? []).map((l) => {
            const supportedMetadataFields = normalizeMetadataFields(l.supportedMetadataFields);
            return {
                slug: l.slug,
                displayName: l.displayName,
                parentSlug: l.parentSlug ?? null,
                iconName: l.iconName ?? null,
                supportedMetadataFields,
                customMetadataFields: normalizeFieldDefinitions(l.customMetadataFields),
                standardMetadataFields: standardMetadataFieldsFromMask(supportedMetadataFields),
                standardFactFields: normalizeFieldDefinitions(l.standardFactFields),
                acceptedFileTypes: [...(l.acceptedFileTypes ?? [])],
                effectiveAcceptedMimeTypes: [...(l.effectiveAcceptedMimeTypes ?? [])],
                acceptedSidecarTypes: [...(l.acceptedSidecarTypes ?? [])],
                subfolder: l.subfolder == null ? null : l.subfolder.trim(),
            };
        });
        return {
            id: t.slug,
            name: t.displayName,
            pluralName: t.pluralDisplayName ?? t.displayName ?? t.slug,
            slug: t.slug,
            iconName: t.iconName ?? null,
            levels,
            supportedMetadataFields: mergeMetadataFields(levels),
            customMetadataFields: levels.flatMap((l) => l.customMetadataFields ?? []),
            standardMetadataFields: mergeStandardMetadataFields(levels),
        };
    });
}

function mapSubjectTypes(types: ApiSubjectKindDefinition[]): LibraryTypeDefinition[] {
    return types.map((t) => {
        const supportedMetadataFields = normalizeMetadataFields(t.supportedMetadataFields);
        return {
            id: t.slug,
            name: t.displayName ?? t.slug,
            pluralName: t.pluralDisplayName ?? t.displayName ?? t.slug,
            slug: t.slug,
            iconName: t.iconName ?? null,
            levels: [],
            supportedMetadataFields,
            customMetadataFields: normalizeFieldDefinitions(t.customMetadataFields),
            standardMetadataFields: standardMetadataFieldsFromMask(supportedMetadataFields),
        };
    });
}

export function getRootLevels(type: LibraryTypeDefinition | null | undefined): LibraryLevelDefinition[] {
    if (!type) {
        return [];
    }
    return type.levels.filter((l) => !l.parentSlug);
}

export function useLibraryTypes() {
    const { isAuthenticated } = useAuth();
    useInvalidateQueriesOnEvents(
        isAuthenticated ? LIBRARY_TYPE_CATALOG_EVENTS : [],
        [libraryTypesQueryKeys.contentTypes(), libraryTypesQueryKeys.subjectTypes()],
        { exact: true, owner: 'library-type-catalog' },
    );

    const contentTypesQuery = useQuery({
        queryKey: libraryTypesQueryKeys.contentTypes(),
        queryFn: fetchContentTypes,
        staleTime: POLLING_INTERVALS.slow,
        gcTime: 5 * 60 * 1000,
        enabled: isAuthenticated,
    });

    const subjectTypesQuery = useQuery({
        queryKey: libraryTypesQueryKeys.subjectTypes(),
        queryFn: fetchSubjectTypes,
        staleTime: POLLING_INTERVALS.slow,
        gcTime: 5 * 60 * 1000,
        enabled: isAuthenticated,
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

function buildIconNameBySlug(types: LibraryTypeDefinition[]): Record<string, string | undefined> {
    const map: Record<string, string | undefined> = {};
    for (const type of types) {
        map[type.slug.toLowerCase()] = type.iconName ?? undefined;
        for (const level of type.levels) {
            map[level.slug.toLowerCase()] = level.iconName ?? undefined;
        }
    }
    return map;
}

export function useTypeIconName() {
    const { contentTypes, subjectTypes } = useLibraryTypes();

    const contentIconNameBySlug = useMemo(() => buildIconNameBySlug(contentTypes), [contentTypes]);
    const subjectIconNameBySlug = useMemo(() => buildIconNameBySlug(subjectTypes), [subjectTypes]);

    return useMemo(
        () =>
            (slug: string | null | undefined, options?: { subject?: boolean }): string | undefined => {
                const key = slug?.trim().toLowerCase();
                if (!key) {
                    return undefined;
                }

                return options?.subject ? subjectIconNameBySlug[key] : contentIconNameBySlug[key];
            },
        [contentIconNameBySlug, subjectIconNameBySlug],
    );
}

export type LibraryCounts = Record<string, number | undefined>;

interface PagedCountResponse {
    totalItems: number;
}

async function fetchContentTypeCount(slug: string): Promise<number> {
    const url = buildUrl('/content', { contentType: slug, topLevelOnly: true, pageSize: 1 });
    const data = await apiGet<PagedCountResponse>(url);
    return data.totalItems;
}

async function fetchSubjectTypeCount(slug: string): Promise<number> {
    const url = buildUrl('/subjects', { kind: slug, pageSize: 1 });
    const data = await apiGet<PagedCountResponse>(url);
    return data.totalItems;
}

export const libraryCountsQueryKeys = {
    counts: (contentSlugs: string[], subjectSlugs: string[]) =>
        ['libraryTypes', 'counts', contentSlugs, subjectSlugs] as const,
};

async function fetchContentLevelCount(contentType: string, levelSlug: string): Promise<number> {
    const url = buildUrl('/content', {
        contentType,
        level: levelSlug,
        topLevelOnly: true,
        pageSize: 1,
    });
    const data = await apiGet<PagedCountResponse>(url);
    return data.totalItems;
}

const contentLevelCountsQueryKeys = {
    counts: (contentType: string, levelSlugs: string[]) =>
        ['libraryTypes', 'level-counts', contentType, levelSlugs] as const,
};

export function useContentLevelCounts(contentType: string, levels: LibraryLevelDefinition[]) {
    const levelSlugs = levels.map((l) => l.slug);
    useInvalidateQueriesOnEvents(
        contentType && levels.length > 0 ? LIBRARY_COUNT_EVENTS : [],
        [contentLevelCountsQueryKeys.counts(contentType, levelSlugs)],
        { debounceMs: 300, exact: true, maxWaitMs: 1200, owner: 'library-counts:levels' },
    );

    const countsQuery = useQuery({
        queryKey: contentLevelCountsQueryKeys.counts(contentType, levelSlugs),
        queryFn: async () => {
            const results = await Promise.all(
                levelSlugs.map((slug) =>
                    fetchContentLevelCount(contentType, slug).then((count) => [slug, count] as const),
                ),
            );

            const counts: LibraryCounts = {};
            for (const [slug, count] of results) {
                counts[slug] = count;
            }
            return counts;
        },
        enabled: contentType.length > 0 && levels.length > 0,
        staleTime: POLLING_INTERVALS.slow,
        gcTime: 5 * 60 * 1000,
    });

    return {
        counts: countsQuery.data ?? {},
    };
}

export function useLibraryCounts(contentTypes: LibraryTypeDefinition[], subjectTypes: LibraryTypeDefinition[]) {
    const contentSlugs = contentTypes.map((t) => t.slug);
    const subjectSlugs = subjectTypes.map((t) => t.slug);
    useInvalidateQueriesOnEvents(
        contentTypes.length > 0 || subjectTypes.length > 0 ? LIBRARY_COUNT_EVENTS : [],
        [libraryCountsQueryKeys.counts(contentSlugs, subjectSlugs)],
        { debounceMs: 300, exact: true, maxWaitMs: 1200, owner: 'library-counts:types' },
    );

    const countsQuery = useQuery({
        queryKey: libraryCountsQueryKeys.counts(contentSlugs, subjectSlugs),
        queryFn: async () => {
            const [contentResults, subjectResults] = await Promise.all([
                Promise.all(contentSlugs.map((slug) => fetchContentTypeCount(slug).then((n) => [slug, n] as const))),
                Promise.all(subjectSlugs.map((slug) => fetchSubjectTypeCount(slug).then((n) => [slug, n] as const))),
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
