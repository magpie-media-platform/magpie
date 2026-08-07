import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';

import { apiGet, apiPatch, apiDelete, apiPost, buildUrl } from '@/lib/api';
import { executeImmediateJobRequest, type JobActionDisposition } from '@/lib/jobActions';
import { sortedContributions } from '@/lib/metadata';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import { useJobAction, type JobActionLifecycleOptions } from '@/hooks/useJobAction';
import type { EventStreamPayload } from '@/hooks/useEventStream';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { PagedList } from '@/types/paging';
import { tagQueryKeys } from '@/hooks/useTags';

interface ApiExternalId {
    id: string;
    value: string;
    source?: string | null;
    url?: string | null;
    ordinal: number;
}

interface ApiGenre {
    id: string;
    name: string;
    ordinal: number;
}

interface ApiContentRating {
    id: string;
    body: string;
    rating: string;
    ordinal: number;
}

interface ApiAward {
    id: string;
    body: string;
    name: string;
    category?: string | null;
    status?: string | null;
    year?: number | null;
    ordinal: number;
}

interface ApiRelationship {
    id: string;
    kind: string;
    qualifier?: string | null;
    relatedMetadataContainerId?: string | null;
    ordinal: number;
}

interface ApiCustomMetadataValue {
    key: string;
    value?: string | null;
}

interface ApiMetadataContribution {
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
    birthDate?: string | null;
    deathDate?: string | null;
    established?: string | null;
    dissolved?: string | null;
    images?: ApiMetadataImage[] | null;
    externalIds?: ApiExternalId[] | null;
    genres?: ApiGenre[] | null;
    contentRatings?: ApiContentRating[] | null;
    awards?: ApiAward[] | null;
    relationships?: ApiRelationship[] | null;
    references?: ApiReference[] | null;
    customMetadata?: ApiCustomMetadataValue[] | null;
}

interface ApiReference {
    ordinal: number;
    kind: string;
    source: string;
    url: string;
    displayName?: string | null;
}

interface ApiMetadataImage {
    id?: string | null;
    type: string;
    url: string;
    ordinal?: number | null;
    isPrimary?: boolean | null;
    width?: number | null;
    height?: number | null;
}

export interface ApiMetadataContainer {
    id: string;
    createdAt: string;
    contributions: ApiMetadataContribution[];
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
}

interface StringSetPatch {
    add?: string[];
    remove?: string[];
}

interface SubjectTagsPatch {
    id: string;
    tags?: StringSetPatch;
}

export interface SubjectQueryParams {
    kind?: string;
    tag?: string;
    filter?: string;
    pageNumber?: number;
    pageSize?: number;
    sortBy?: string;
    sortDirection?: 'Ascending' | 'Descending';
}

export const subjectQueryKeys = {
    all: ['subjects'] as const,
    list: (params?: SubjectQueryParams) => ['subjects', 'list', params ?? {}] as const,
    detail: (id: string) => ['subjects', 'detail', id] as const,
};

const SUBJECT_CHANGE_EVENTS = ['subject.created', 'subject.deleted', 'subject.updated'] as const;
const SUBJECT_METADATA_JOB_EVENTS = ['job.execution.completed'] as const;

function isSubjectMetadataRefresh(event: { context: Record<string, string> }): boolean {
    return event.context['execution.jobType'] === 'SubjectMetadataRefreshItem';
}

function useSubjectEvents(
    queryKey: readonly unknown[],
    enabled = true,
    includeContent = false,
    entityIds?: readonly string[],
    predicate?: (event: EventStreamPayload) => boolean,
): void {
    useInvalidateQueriesOnEvents(
        enabled
            ? includeContent
                ? [
                      ...SUBJECT_CHANGE_EVENTS,
                      'content.hierarchyItem.created',
                      'content.hierarchyItem.deleted',
                      'content.hierarchyItem.updated',
                      'content.fulfillment.changed',
                      'download.imported',
                  ]
                : [...SUBJECT_CHANGE_EVENTS]
            : [],
        [queryKey],
        {
            entityIds,
            exact: true,
            owner: 'subjects-query',
            predicate,
        },
    );
}

export { extractLanguage, extractStatus } from '@/lib/metadata';

function extractScalar(
    container: ApiMetadataContainer | null | undefined,
    pick: (contribution: ApiMetadataContribution) => string | null | undefined,
): string | undefined {
    if (!container?.contributions?.length) {
        return undefined;
    }
    for (const contribution of sortedContributions(container)) {
        const value = pick(contribution);
        if (value) {
            return value;
        }
    }
    return undefined;
}

export function extractBirthDate(container?: ApiMetadataContainer | null): string | undefined {
    return extractScalar(container, (c) => c.birthDate);
}

export function extractDeathDate(container?: ApiMetadataContainer | null): string | undefined {
    return extractScalar(container, (c) => c.deathDate);
}

export function extractEstablished(container?: ApiMetadataContainer | null): string | undefined {
    return extractScalar(container, (c) => c.established);
}

export function extractDissolved(container?: ApiMetadataContainer | null): string | undefined {
    return extractScalar(container, (c) => c.dissolved);
}

interface ExtractedCustomValue {
    key: string;
    value: string;
}

export function extractCustomValues(container?: ApiMetadataContainer | null): ExtractedCustomValue[] {
    if (!container?.contributions?.length) {
        return [];
    }

    const sorted = sortedContributions(container);
    const out: ExtractedCustomValue[] = [];
    const seen = new Set<string>();

    for (const contribution of sorted) {
        if (!contribution.customMetadata?.length) continue;
        for (const entry of contribution.customMetadata) {
            const key = entry.key?.trim();
            const value = entry.value?.trim();
            if (!key || !value) continue;
            const dedupeKey = key.toLowerCase();
            if (seen.has(dedupeKey)) continue;
            seen.add(dedupeKey);
            out.push({ key, value });
        }
    }

    return out;
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
                    source: extId.source ?? 'unknown',
                    id: extId.value,
                    url: extId.url ?? undefined,
                });
            }
        }
    }

    return externalIds;
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

interface ExtractedImage {
    id: string;
    imageId?: string;
    url: string;
    type: string;
    isPrimary: boolean;
    width?: number;
    height?: number;
}

export function extractImages(container?: ApiMetadataContainer | null): ExtractedImage[] {
    if (!container?.contributions?.length) {
        return [];
    }

    const sorted = sortedContributions(container);
    const images: ExtractedImage[] = [];

    for (const contribution of sorted) {
        if (!contribution.images?.length) continue;
        for (const img of contribution.images) {
            images.push({
                id: `${contribution.id}-${img.url}`,
                imageId: img.id ?? undefined,
                url: img.url,
                type: img.type,
                isPrimary: img.isPrimary ?? false,
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

async function fetchSubjects(params?: SubjectQueryParams): Promise<PagedList<ApiSubject>> {
    const url = buildUrl('/subjects', params);
    return apiGet<PagedList<ApiSubject>>(url);
}

async function fetchSubjectById(id: string): Promise<ApiSubject> {
    return apiGet<ApiSubject>(`/subjects/${id}`);
}

async function patchSubject(id: string, data: Omit<SubjectTagsPatch, 'id'>): Promise<void> {
    return apiPatch(`/subjects/${id}`, data);
}

async function deleteSubject(id: string): Promise<void> {
    return apiDelete(`/subjects/${id}`);
}

export function useSubjects(params?: SubjectQueryParams) {
    const queryClient = useQueryClient();
    const hasFilters = Boolean(params?.filter || params?.kind || params?.tag);
    const subjectMatchesList = (event: EventStreamPayload): boolean => {
        const current = queryClient.getQueryData<PagedList<ApiSubject>>(subjectQueryKeys.list(params));
        if (current?.items.some((subject) => subject.id === event.context['subject.id'])) {
            return true;
        }
        if (SUBJECT_CHANGE_EVENTS.some((eventName) => eventName === event.name)) {
            return true;
        }
        if (!hasFilters) {
            return true;
        }
        if (params?.filter) {
            return false;
        }
        return (
            (!params?.kind || event.context['subject.kind'] === params.kind) &&
            (!params?.tag || event.context['subject.tags']?.split(',').includes(params.tag))
        );
    };
    useSubjectEvents(subjectQueryKeys.list(params), true, false, undefined, subjectMatchesList);
    useInvalidateQueriesOnEvents(SUBJECT_METADATA_JOB_EVENTS, [subjectQueryKeys.list(params)], {
        debounceMs: 150,
        exact: true,
        owner: 'subjects:list',
        predicate: (event) => isSubjectMetadataRefresh(event) && subjectMatchesList(event),
    });

    const subjectsQuery = useQuery({
        queryKey: subjectQueryKeys.list(params),
        queryFn: () => fetchSubjects(params),
        staleTime: POLLING_INTERVALS.slow / 2,
        placeholderData: keepPreviousData,
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, data }: { id: string; data: Omit<SubjectTagsPatch, 'id'> }) => patchSubject(id, data),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: subjectQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteSubject,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: subjectQueryKeys.all });
        },
    });

    const page = subjectsQuery.data;
    const subjects: ApiSubject[] = page?.items ?? [];
    const totalItems = page?.totalItems ?? 0;
    const pageNumber = page?.pageNumber ?? params?.pageNumber ?? 1;
    const pageSize = page?.pageSize ?? params?.pageSize ?? subjects.length;

    const updateSubjectTags = async (id: string, data: Omit<SubjectTagsPatch, 'id'>): Promise<void> => {
        await patchMutation.mutateAsync({ id, data });
    };

    return {
        subjects,
        totalItems,
        pageNumber,
        pageSize,
        isLoading: subjectsQuery.isLoading,
        error: queryErrorOrNull(subjectsQuery),

        updateSubjectTags,
        isUpdating: patchMutation.isPending,

        deleteSubject: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        refetch: subjectsQuery.refetch,
    };
}

export function useUpdateSubjectTags() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: ({ id, tags }: { id: string; tags: StringSetPatch }) => patchSubject(id, { tags }),
        onSuccess: (_data, request) => {
            void queryClient.invalidateQueries({ queryKey: subjectQueryKeys.detail(request.id) });
            void queryClient.invalidateQueries({ queryKey: subjectQueryKeys.all });
            void queryClient.invalidateQueries({ queryKey: tagQueryKeys.byEntity(request.id) });
            void queryClient.invalidateQueries({ queryKey: tagQueryKeys.all });
        },
    });
}

export function useSubject(id: string, enabled = true) {
    useSubjectEvents(subjectQueryKeys.detail(id), enabled && Boolean(id), false, id ? [id] : []);
    useInvalidateQueriesOnEvents(enabled && id ? SUBJECT_METADATA_JOB_EVENTS : [], [subjectQueryKeys.detail(id)], {
        debounceMs: 150,
        entityIds: id ? [id] : [],
        exact: true,
        owner: 'subjects:detail',
        predicate: isSubjectMetadataRefresh,
    });
    const subjectQuery = useQuery({
        queryKey: subjectQueryKeys.detail(id),
        queryFn: () => fetchSubjectById(id),
        enabled: enabled && Boolean(id),
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    return {
        subject: subjectQuery.data ?? null,
        isLoading: subjectQuery.isLoading,
        error: queryErrorOrNull(subjectQuery),
        refetch: subjectQuery.refetch,
    };
}

async function refreshSubject(id: string): Promise<JobActionDisposition> {
    return executeImmediateJobRequest({
        jobType: 'SubjectMetadataRefreshItem',
        parameters: { subjectId: id },
    });
}

export function useRefreshSubject(options?: JobActionLifecycleOptions<string>) {
    const queryClient = useQueryClient();

    return useJobAction<string>({
        mutationFn: refreshSubject,
        onJobStarted: (disposition, id) => {
            void queryClient.invalidateQueries({ queryKey: subjectQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: subjectQueryKeys.all });
            options?.onJobStarted?.(disposition, id);
        },
        onJobExecutionSettled: options?.onJobExecutionSettled,
        trackExecution: options?.trackExecution,
    });
}

async function setPrimaryImage(subjectId: string, imageId: string): Promise<void> {
    await apiPost(`/subjects/${subjectId}/images/${imageId}/primary`);
}

export function useSetPrimaryImage(subjectId: string) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (imageId: string) => setPrimaryImage(subjectId, imageId),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: subjectQueryKeys.detail(subjectId) });
            void queryClient.invalidateQueries({ queryKey: subjectQueryKeys.all });
        },
    });
}
