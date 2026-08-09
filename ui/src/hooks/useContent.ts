import { useEffect, useMemo, useState } from 'react';
import { useQuery, useQueries, useMutation, useQueryClient, type QueryKey } from '@tanstack/react-query';

import { type ApiTag, tagQueryKeys } from '@/hooks/useTags';
import { createMetadataInspectorHooks, relatedContentSubjectRefs } from '@/hooks/metadataInspectorHooks';
import { contentReleaseQueryKeys } from '@/hooks/useContentReleases';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { fetchRootFolders, rootFolderQueryKeys } from '@/hooks/useRootFolders';
import { fetchQualityTierLists, qualityTierListQueryKeys } from '@/hooks/useQualityTierLists';
import { apiGet, apiPost, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { executeImmediateJobRequest, executeJobRequest, type JobActionDisposition } from '@/lib/jobActions';
import { magpieEventNames } from '@/lib/magpieEventNames';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { useJobAction, type JobActionLifecycleOptions } from '@/hooks/useJobAction';
import {
    type ApiContentHierarchyItem,
    type ContentDetailItem,
    type PagedContentHierarchyItems,
    type ContentQueryParams,
    mapToContentListItem,
    mapToContentDetailItem,
} from '@/types/content';
import {
    type AcquisitionEligibility,
    type BulkContentDeleteRequest,
    type BulkContentPatchRequest,
    type BulkResult,
    type ContentListResult,
    type ContentRelationship,
    type EffectiveQualityTierList,
    type EffectiveRootFolder,
    type MediaServerDeepLink,
    type MetadataInspectorResponse,
    type PendingRefreshResponse,
    type SearchContentReleasesParams,
    type SetContentRootFolderVariables,
    type SetQualityTierListVariables,
    type UpdateContentRequest,
} from '@/types/contentMetadata';

export const contentQueryKeys = {
    all: ['content'] as const,
    list: (params?: ContentQueryParams) =>
        params ? (['content', 'list', params] as const) : (['content', 'list'] as const),
    detail: (id: string) => ['content', 'detail', id] as const,
    tags: () => ['content', 'tags'] as const,
    mediaServerLinks: (id: string) => ['content', 'media-server-links', id] as const,
    metadataInspector: (id: string) => ['content', 'metadata-inspector', id] as const,
    effectiveRootFolder: (id: string) => ['content', 'effective-root-folder', id] as const,
    effectiveQualityTierList: (id: string) => ['content', 'effective-quality-tier-list', id] as const,
    acquisitionStatus: (id: string) => ['content', 'acquisition-status', id] as const,
    pendingRefresh: (id: string) => ['content', 'pending-refresh', id] as const,
};

const CONTENT_CHANGE_EVENTS = [
    magpieEventNames.contentHierarchyItemCreated,
    magpieEventNames.contentHierarchyItemDeleted,
    magpieEventNames.contentHierarchyItemUpdated,
    'content.fulfillment.changed',
    magpieEventNames.metadataRefreshed,
    magpieEventNames.downloadImported,
    'library.scan.completed',
] as const;

const SCOPED_CONTENT_CHANGE_EVENTS = CONTENT_CHANGE_EVENTS.filter((name) => name !== 'library.scan.completed');

function matchesScope(
    context: Record<string, string>,
    params: ContentQueryParams | undefined,
    prefix: '' | 'previous.',
): boolean {
    const value = (field: string): string | undefined => context[`content.impact.${prefix}${field}`];
    return (
        (!params?.contentType || value('contentType') === params.contentType) &&
        (!params?.level || value('level') === params.level) &&
        (!params?.parentId || value('parentId') === params.parentId) &&
        (!params?.topLevelOnly || value('topLevel') === 'true') &&
        (!params?.rootFolderId || value('rootFolderId') === params.rootFolderId) &&
        (params?.monitoring === undefined || value('monitoring') === String(params.monitoring)) &&
        (!params?.fulfillment || value('fulfillment')?.toLowerCase() === params.fulfillment.toLowerCase())
    );
}

const ACQUISITION_CHANGE_EVENTS = [
    ...CONTENT_CHANGE_EVENTS,
    'download.created',
    'download.updated',
    'download.deleted',
    magpieEventNames.downloadCompleted,
    magpieEventNames.downloadFailed,
    'download.stalled',
    'releaseSearch.completed',
    'releaseGrab.rejected',
] as const;

function useContentQueryEvents(
    queryKey: QueryKey,
    eventName: readonly string[] = CONTENT_CHANGE_EVENTS,
    options?: {
        debounceMs?: number;
        enabled?: boolean;
        entityIds?: readonly string[];
        failOpenEventNames?: readonly string[];
        maxWaitMs?: number;
        predicate?: (event: { context: Record<string, string> }) => boolean;
        unprovableMembership?: boolean;
    },
): void {
    const enabledEventNames = options?.enabled === false ? [] : eventName;
    const scopedEventNames = enabledEventNames.filter((name) => name !== 'library.scan.completed');
    useInvalidateQueriesOnEvents(scopedEventNames, [queryKey], {
        debounceMs: options?.debounceMs,
        entityIds: options?.entityIds,
        exact: true,
        failOpenEventNames: options?.failOpenEventNames,
        maxWaitMs: options?.maxWaitMs,
        owner: 'content-query',
        predicate: options?.predicate,
        unprovableMembership: options?.unprovableMembership,
    });
    useInvalidateQueriesOnEvents(
        enabledEventNames.includes('library.scan.completed') ? ['library.scan.completed'] : [],
        [queryKey],
        {
            debounceMs: options?.debounceMs,
            exact: true,
            maxWaitMs: options?.maxWaitMs,
            owner: 'content-query',
        },
    );
}

async function fetchAcquisitionStatus(id: string): Promise<AcquisitionEligibility> {
    return apiGet<AcquisitionEligibility>(`/content/${id}/acquisition-status`);
}

export function useAcquisitionStatus(id: string | undefined, options?: { enabled?: boolean }) {
    useContentQueryEvents(contentQueryKeys.acquisitionStatus(id ?? ''), ACQUISITION_CHANGE_EVENTS, {
        enabled: (options?.enabled ?? true) && !!id,
        entityIds: id ? [id] : [],
    });
    return useQuery({
        queryKey: contentQueryKeys.acquisitionStatus(id ?? ''),
        queryFn: () => fetchAcquisitionStatus(id!),
        enabled: (options?.enabled ?? true) && !!id,
        staleTime: POLLING_INTERVALS.slow,
    });
}

async function fetchEffectiveRootFolder(
    queryClient: ReturnType<typeof useQueryClient>,
    id: string,
): Promise<EffectiveRootFolder> {
    const [item, rootFolders] = await Promise.all([
        queryClient.fetchQuery({
            queryKey: contentQueryKeys.detail(id),
            queryFn: () => fetchContentById(id),
            staleTime: 0,
        }),
        queryClient.fetchQuery({
            queryKey: rootFolderQueryKeys.list(),
            queryFn: fetchRootFolders,
            staleTime: 0,
        }),
    ]);
    const rootFolderId = item.effectiveRootFolderId;
    const rootFolder = rootFolders.find((folder) => folder.id === rootFolderId);

    if (!rootFolderId || rootFolderId === '00000000-0000-0000-0000-000000000000') {
        return {
            rootFolderId: null,
            source: 'None',
            resolvingHierarchyItemId: null,
            isOverriddenOnItem: false,
        };
    }

    if (item.rootFolderId) {
        return {
            rootFolderId,
            rootFolderName: rootFolder?.name,
            rootFolderPath: rootFolder?.path,
            source: 'Override',
            resolvingHierarchyItemId: item.id,
            isOverriddenOnItem: true,
        };
    }

    let parentId = item.parentId;
    while (parentId) {
        const parent = await queryClient.fetchQuery({
            queryKey: contentQueryKeys.detail(parentId),
            queryFn: () => fetchContentById(parentId!),
            staleTime: 0,
        });
        if (parent.rootFolderId) {
            return {
                rootFolderId,
                rootFolderName: rootFolder?.name,
                rootFolderPath: rootFolder?.path,
                source: 'Inherited',
                resolvingHierarchyItemId: parent.id,
                isOverriddenOnItem: false,
            };
        }
        parentId = parent.parentId;
    }

    return {
        rootFolderId,
        rootFolderName: rootFolder?.name,
        rootFolderPath: rootFolder?.path,
        source: 'ContentTypeDefault',
        resolvingHierarchyItemId: null,
        isOverriddenOnItem: false,
    };
}

export function useEffectiveRootFolder(id: string | undefined, options?: { enabled?: boolean }) {
    useContentQueryEvents(contentQueryKeys.effectiveRootFolder(id ?? ''), CONTENT_CHANGE_EVENTS, {
        enabled: (options?.enabled ?? true) && !!id,
        entityIds: id ? [id] : [],
    });
    useContentQueryEvents(
        contentQueryKeys.effectiveRootFolder(id ?? ''),
        ['library.rootFolder.created', 'library.rootFolder.deleted', 'library.rootFolder.updated'],
        { enabled: (options?.enabled ?? true) && !!id },
    );
    return useQuery({
        queryKey: contentQueryKeys.effectiveRootFolder(id ?? ''),
        queryFn: ({ client }) => fetchEffectiveRootFolder(client, id!),
        enabled: (options?.enabled ?? true) && !!id,
        staleTime: POLLING_INTERVALS.slow,
    });
}

export function useSetContentRootFolder() {
    const queryClient = useQueryClient();
    return useMutation<void, Error, SetContentRootFolderVariables>({
        mutationFn: ({ contentHierarchyItemId, rootFolderId }) =>
            apiPatch<void>(`/content/${contentHierarchyItemId}`, { rootFolderId: { value: rootFolderId } }),
        onSuccess: (_, variables) => {
            void queryClient.invalidateQueries({
                queryKey: contentQueryKeys.effectiveRootFolder(variables.contentHierarchyItemId),
            });
            void queryClient.invalidateQueries({
                queryKey: contentQueryKeys.detail(variables.contentHierarchyItemId),
            });
            void queryClient.invalidateQueries({
                queryKey: contentQueryKeys.mediaServerLinks(variables.contentHierarchyItemId),
            });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
        },
    });
}

interface PreviewContentPathVariables {
    contentHierarchyItemId: string;
    path: string | null;
}

type ApplyContentPathVariables = PreviewContentPathVariables;

export function useApplyContentPath() {
    const queryClient = useQueryClient();
    return useMutation<void, Error, ApplyContentPathVariables>({
        mutationFn: ({ contentHierarchyItemId, path }) =>
            apiPatch<void>(`/content/${contentHierarchyItemId}`, {
                path: { value: path },
            }),
        onSuccess: (_, variables) => {
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(variables.contentHierarchyItemId) });
            void queryClient.invalidateQueries({
                queryKey: contentQueryKeys.mediaServerLinks(variables.contentHierarchyItemId),
            });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
        },
    });
}

async function fetchEffectiveQualityTierList(
    queryClient: ReturnType<typeof useQueryClient>,
    id: string,
): Promise<EffectiveQualityTierList> {
    const [item, tierLists] = await Promise.all([
        queryClient.fetchQuery({
            queryKey: contentQueryKeys.detail(id),
            queryFn: () => fetchContentById(id),
            staleTime: 0,
        }),
        queryClient.fetchQuery({
            queryKey: qualityTierListQueryKeys.list(),
            queryFn: () => fetchQualityTierLists(),
            staleTime: 0,
        }),
    ]);
    const tierListId = item.effectiveQualityTierListId;
    const tierList = tierLists.find((list) => list.id === tierListId);

    if (!tierListId || tierListId === '00000000-0000-0000-0000-000000000000') {
        return {
            source: 'None',
            resolvingHierarchyItemId: null,
            resolvingHierarchyItemName: null,
            ownQualityTierListId: item.qualityTierListId ?? null,
        };
    }

    if (item.qualityTierListId) {
        return {
            qualityTierListId: tierListId,
            qualityTierListName: tierList?.name,
            source: 'ExplicitOnItem',
            resolvingHierarchyItemId: item.id,
            resolvingHierarchyItemName: item.name,
            ownQualityTierListId: item.qualityTierListId,
        };
    }

    let parentId = item.parentId;
    while (parentId) {
        const parent = await queryClient.fetchQuery({
            queryKey: contentQueryKeys.detail(parentId),
            queryFn: () => fetchContentById(parentId!),
            staleTime: 0,
        });
        if (parent.qualityTierListId) {
            return {
                qualityTierListId: tierListId,
                qualityTierListName: tierList?.name,
                source: 'Inherited',
                resolvingHierarchyItemId: parent.id,
                resolvingHierarchyItemName: parent.name,
                ownQualityTierListId: null,
            };
        }
        parentId = parent.parentId;
    }

    return {
        qualityTierListId: tierListId,
        qualityTierListName: tierList?.name,
        source: 'ContentTypeDefaultRootFolder',
        resolvingHierarchyItemId: null,
        resolvingHierarchyItemName: null,
        ownQualityTierListId: null,
    };
}

export function useEffectiveQualityTierList(id: string | undefined, options?: { enabled?: boolean }) {
    useContentQueryEvents(contentQueryKeys.effectiveQualityTierList(id ?? ''), CONTENT_CHANGE_EVENTS, {
        enabled: (options?.enabled ?? true) && !!id,
        entityIds: id ? [id] : [],
    });
    useContentQueryEvents(
        contentQueryKeys.effectiveQualityTierList(id ?? ''),
        ['qualityTierList.created', 'qualityTierList.deleted', 'qualityTierList.updated'],
        { enabled: (options?.enabled ?? true) && !!id },
    );
    return useQuery({
        queryKey: contentQueryKeys.effectiveQualityTierList(id ?? ''),
        queryFn: ({ client }) => fetchEffectiveQualityTierList(client, id!),
        enabled: (options?.enabled ?? true) && !!id,
        staleTime: POLLING_INTERVALS.slow,
    });
}

export function useSetQualityTierList() {
    const queryClient = useQueryClient();
    return useMutation<void, Error, SetQualityTierListVariables>({
        mutationFn: ({ contentHierarchyItemId, qualityTierListId }) =>
            apiPatch<void>(`/content/${contentHierarchyItemId}`, { qualityTierListId: { value: qualityTierListId } }),
        onSuccess: (_, variables) => {
            void queryClient.invalidateQueries({
                queryKey: contentQueryKeys.effectiveQualityTierList(variables.contentHierarchyItemId),
            });
            void queryClient.invalidateQueries({
                queryKey: contentQueryKeys.detail(variables.contentHierarchyItemId),
            });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
        },
    });
}

async function fetchContentList(params?: ContentQueryParams): Promise<PagedContentHierarchyItems> {
    const url = buildUrl('/content', params);
    return apiGet<PagedContentHierarchyItems>(url);
}

async function fetchContentById(id: string): Promise<ApiContentHierarchyItem> {
    return apiGet<ApiContentHierarchyItem>(`/content/${id}`);
}

async function fetchTags(): Promise<ApiTag[]> {
    return apiGet<ApiTag[]>('/tags');
}

async function refreshContent(id: string): Promise<JobActionDisposition> {
    return executeImmediateJobRequest({
        jobType: 'ContentMetadataRefreshItem',
        parameters: { contentHierarchyItemId: id },
    });
}

async function rescanContentMediaInfo(id: string): Promise<JobActionDisposition> {
    return executeImmediateJobRequest({
        jobType: 'MediaInfoExtractScan',
        parameters: { contentHierarchyItemId: id },
    });
}

async function searchContentReleases({ id, mode }: SearchContentReleasesParams): Promise<JobActionDisposition> {
    return executeJobRequest({
        jobType: 'ReleaseSearchItem',
        parameters: { contentHierarchyItemId: id, force: 'true' },
        mode: mode ?? 'Immediate',
    });
}

export function useContentList(params?: ContentQueryParams, options?: { enabled?: boolean }) {
    const queryClient = useQueryClient();
    const hasFilters = Boolean(
        params?.contentType ||
        params?.filter ||
        params?.fulfillment ||
        params?.level ||
        params?.monitoring !== undefined ||
        params?.parentId ||
        params?.rootFolderId ||
        params?.topLevelOnly,
    );
    useContentQueryEvents(contentQueryKeys.list(params), CONTENT_CHANGE_EVENTS, {
        debounceMs: 250,
        enabled: options?.enabled ?? true,
        entityIds: params?.parentId ? [params.parentId] : undefined,
        failOpenEventNames: hasFilters ? ['content.hierarchyItem.created'] : undefined,
        maxWaitMs: 1000,
        predicate: (event) => {
            const current = queryClient.getQueryData<PagedContentHierarchyItems>(contentQueryKeys.list(params));
            if (current?.items.some((item) => item.id === event.context['content.hierarchyItemId'])) {
                return true;
            }
            if (!hasFilters) {
                return true;
            }
            if (params?.filter) {
                return false;
            }
            return matchesScope(event.context, params, '') || matchesScope(event.context, params, 'previous.');
        },
        unprovableMembership: hasFilters,
    });
    return useQuery({
        queryKey: contentQueryKeys.list(params),
        queryFn: () => fetchContentList(params),
        select: (data): ContentListResult => ({
            items: Array.isArray(data?.items) ? data.items.map(mapToContentListItem) : [],
            pageNumber: data?.pageNumber ?? 1,
            pageSize: data?.pageSize ?? 25,
            totalItems: data?.totalItems ?? 0,
        }),
        staleTime: POLLING_INTERVALS.slow / 2,
        enabled: options?.enabled ?? true,
    });
}

export function useContentDetail(id: string | undefined, options?: { enabled?: boolean }) {
    useContentQueryEvents(contentQueryKeys.detail(id ?? ''), CONTENT_CHANGE_EVENTS, {
        enabled: (options?.enabled ?? true) && !!id,
        entityIds: id ? [id] : [],
    });
    return useQuery({
        queryKey: contentQueryKeys.detail(id ?? ''),
        queryFn: () => fetchContentById(id!),
        select: mapToContentDetailItem,
        enabled: (options?.enabled ?? true) && !!id,
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}

export function useContentHierarchy(
    activeId: string | undefined,
    parentId: string | undefined,
    rootId: string | undefined,
) {
    const hierarchyIds = [...new Set([activeId, parentId, rootId].filter((id): id is string => !!id))];
    useInvalidateQueriesOnEvents(SCOPED_CONTENT_CHANGE_EVENTS, hierarchyIds.map(contentQueryKeys.detail), {
        entityIds: hierarchyIds,
        exact: true,
        owner: 'content-hierarchy',
    });
    useInvalidateQueriesOnEvents(
        hierarchyIds.length > 0 ? ['library.scan.completed'] : [],
        hierarchyIds.map(contentQueryKeys.detail),
        { exact: true, owner: 'content-hierarchy' },
    );
    const queries = useQueries({
        queries: [
            {
                queryKey: contentQueryKeys.detail(activeId ?? ''),
                queryFn: () => fetchContentById(activeId!),
                select: mapToContentDetailItem,
                enabled: !!activeId,
                staleTime: POLLING_INTERVALS.slow / 2,
            },
            {
                queryKey: contentQueryKeys.detail(parentId ?? ''),
                queryFn: () => fetchContentById(parentId!),
                select: mapToContentDetailItem,
                enabled: !!parentId,
                staleTime: POLLING_INTERVALS.slow / 2,
            },
            {
                queryKey: contentQueryKeys.detail(rootId ?? ''),
                queryFn: () => fetchContentById(rootId!),
                select: mapToContentDetailItem,
                enabled: !!rootId,
                staleTime: POLLING_INTERVALS.slow / 2,
            },
        ],
    });

    return {
        content: queries[0],
        parent: queries[1],
        root: queries[2],
    };
}

interface ContentAncestorsResult {
    ancestors: ContentDetailItem[];
    isLoading: boolean;
}

export function useContentAncestors(leafParentId: string | undefined): ContentAncestorsResult {
    const [ids, setIds] = useState<string[]>([]);
    useInvalidateQueriesOnEvents(SCOPED_CONTENT_CHANGE_EVENTS, ids.map(contentQueryKeys.detail), {
        entityIds: ids,
        exact: true,
        owner: 'content-ancestors',
    });
    useInvalidateQueriesOnEvents(ids.length > 0 ? ['library.scan.completed'] : [], ids.map(contentQueryKeys.detail), {
        exact: true,
        owner: 'content-ancestors',
    });

    useEffect(() => {
        setIds(leafParentId ? [leafParentId] : []);
    }, [leafParentId]);

    const queries = useQueries({
        queries: ids.map((id) => ({
            queryKey: contentQueryKeys.detail(id),
            queryFn: () => fetchContentById(id),
            select: mapToContentDetailItem,
            enabled: !!id,
            staleTime: POLLING_INTERVALS.slow / 2,
        })),
    });

    const lastParentId = queries.length > 0 ? queries[queries.length - 1].data?.parentId : undefined;

    useEffect(() => {
        if (lastParentId) {
            setIds((prev) => (prev.includes(lastParentId) ? prev : [...prev, lastParentId]));
        }
    }, [lastParentId]);

    const ancestors = queries
        .map((query) => query.data)
        .filter((data): data is ContentDetailItem => !!data)
        .reverse();

    const isLoading = queries.some((q) => q.isLoading);

    return { ancestors, isLoading };
}

export function useContentTags() {
    useContentQueryEvents(contentQueryKeys.tags(), [
        'content.hierarchyItem.created',
        'content.hierarchyItem.deleted',
        'content.hierarchyItem.updated',
        'subject.created',
        'subject.deleted',
        'subject.updated',
        'wishlist.created',
        'wishlist.deleted',
        'wishlist.updated',
    ]);
    return useQuery({
        queryKey: contentQueryKeys.tags(),
        queryFn: fetchTags,
        staleTime: POLLING_INTERVALS.slow,
    });
}

async function fetchMediaServerLinks(id: string): Promise<MediaServerDeepLink[]> {
    return apiGet<MediaServerDeepLink[]>(`/content/${id}/media-server-links`);
}

function contentRelationshipsFromInspector(inspector: MetadataInspectorResponse | undefined): ContentRelationship[] {
    if (!inspector) {
        return [];
    }

    return (inspector.materialized.relationships ?? [])
        .map((relationship) => {
            const subject = relationship.relatedMetadataContainerId
                ? inspector.subjects[relationship.relatedMetadataContainerId]
                : undefined;
            return {
                id: relationship.id,
                kind: relationship.kind,
                qualifier: relationship.qualifier ?? null,
                ordinal: relationship.ordinal ?? 0,
                subjectId: subject?.subjectId ?? null,
                name: subject?.name ?? 'Unknown',
                imageUrl: subject?.imageUrl ?? null,
            };
        })
        .sort((a, b) => a.ordinal - b.ordinal || a.kind.localeCompare(b.kind));
}

async function fetchPendingRefresh(id: string): Promise<PendingRefreshResponse> {
    return apiGet<PendingRefreshResponse>(`/content/${id}/pending-refresh`);
}

export function usePendingRefresh(id: string | undefined, options?: { enabled?: boolean }) {
    const queryKey = contentQueryKeys.pendingRefresh(id ?? '');
    useInvalidateQueriesOnEvents(
        (options?.enabled ?? true) && id
            ? [
                  'job.execution.created',
                  'job.execution.queued',
                  'job.execution.started',
                  'job.execution.completed',
                  'job.execution.failed',
                  'job.execution.cancelled',
                  'metadata.refreshed',
              ]
            : [],
        [queryKey],
        {
            debounceMs: 150,
            entityIds: id ? [id] : [],
            exact: true,
            maxWaitMs: 600,
            owner: 'content-pending-refresh',
            predicate: (event) =>
                event.name === 'metadata.refreshed' ||
                event.context['execution.jobType'] === 'ContentMetadataRefreshItem',
        },
    );
    return useQuery({
        queryKey,
        queryFn: () => fetchPendingRefresh(id!),
        enabled: (options?.enabled ?? true) && !!id,
    });
}

export function useContentRelationships(id: string | undefined, options?: { enabled?: boolean }) {
    const inspector = useMetadataInspector(id, options);
    const data = useMemo(() => contentRelationshipsFromInspector(inspector.data), [inspector.data]);

    return {
        data,
        isLoading: inspector.isLoading,
        error: inspector.error,
    };
}

export function useMediaServerLinks(id: string | undefined, options?: { enabled?: boolean }) {
    useContentQueryEvents(contentQueryKeys.mediaServerLinks(id ?? ''), CONTENT_CHANGE_EVENTS, {
        enabled: (options?.enabled ?? true) && !!id,
        entityIds: id ? [id] : [],
    });
    useContentQueryEvents(
        contentQueryKeys.mediaServerLinks(id ?? ''),
        ['mediaServer.created', 'mediaServer.deleted', 'mediaServer.updated'],
        { enabled: (options?.enabled ?? true) && !!id },
    );
    return useQuery({
        queryKey: contentQueryKeys.mediaServerLinks(id ?? ''),
        queryFn: () => fetchMediaServerLinks(id!),
        enabled: (options?.enabled ?? true) && !!id,
        staleTime: POLLING_INTERVALS.slow,
    });
}

export function useRefreshContent(options?: JobActionLifecycleOptions<string>) {
    const queryClient = useQueryClient();

    return useJobAction<string>({
        mutationFn: refreshContent,
        onJobStarted: (disposition, id) => {
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.pendingRefresh(id), exact: true });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.list() });
            options?.onJobStarted?.(disposition, id);
        },
        onJobExecutionSettled: (execution, id, disposition) => {
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.pendingRefresh(id), exact: true });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(id), exact: true });
            options?.onJobExecutionSettled?.(execution, id, disposition);
        },
        trackExecution: options?.trackExecution ?? true,
    });
}

export function useSearchContentReleases(options?: JobActionLifecycleOptions<SearchContentReleasesParams>) {
    const queryClient = useQueryClient();

    return useJobAction<SearchContentReleasesParams>({
        mutationFn: searchContentReleases,
        onJobStarted: (disposition, variables) => {
            const { id } = variables;
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.list() });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.acquisitionStatus(id) });
            void queryClient.invalidateQueries({ queryKey: contentReleaseQueryKeys.forContent(id) });
            options?.onJobStarted?.(disposition, variables);
        },
        onJobExecutionSettled: options?.onJobExecutionSettled,
        trackExecution: options?.trackExecution,
    });
}

export function useRescanContentMediaInfo(options?: JobActionLifecycleOptions<string>) {
    const queryClient = useQueryClient();

    return useJobAction<string>({
        mutationFn: rescanContentMediaInfo,
        onJobStarted: (disposition, id) => {
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.acquisitionStatus(id) });
            options?.onJobStarted?.(disposition, id);
        },
        onJobExecutionSettled: options?.onJobExecutionSettled,
        trackExecution: options?.trackExecution,
    });
}

async function updateContent(request: UpdateContentRequest): Promise<void> {
    const { id, monitoring, qualityTierListId, rootFolderId, tagsToAdd, tagsToRemove } = request;

    const body: Record<string, unknown> = {};

    if (monitoring !== undefined) {
        body.monitoring = { value: monitoring };
    }
    if (qualityTierListId !== undefined) {
        body.qualityTierListId = { value: qualityTierListId };
    }
    if (rootFolderId !== undefined) {
        body.rootFolderId = { value: rootFolderId };
    }

    if ((tagsToAdd && tagsToAdd.length > 0) || (tagsToRemove && tagsToRemove.length > 0)) {
        const tags: Record<string, string[]> = {};
        if (tagsToAdd && tagsToAdd.length > 0) {
            tags.add = tagsToAdd;
        }
        if (tagsToRemove && tagsToRemove.length > 0) {
            tags.remove = tagsToRemove;
        }
        body.tags = tags;
    }

    return apiPatch<void>(`/content/${id}`, body);
}

async function deleteContent(id: string): Promise<void> {
    return apiDelete(`/content/${id}`);
}

export function useUpdateContent() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: updateContent,
        onSuccess: (_data, request) => {
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(request.id) });
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.list() });
            void queryClient.invalidateQueries({ queryKey: tagQueryKeys.byEntity(request.id) });
            void queryClient.invalidateQueries({ queryKey: tagQueryKeys.all });
        },
    });
}

const metadataInspectorHooks = createMetadataInspectorHooks({
    basePath: '/content',
    inspectorKey: (id) => contentQueryKeys.metadataInspector(id),
    detailKey: (id) => contentQueryKeys.detail(id),
    resolveSubjectRefs: relatedContentSubjectRefs,
});

export const useMetadataInspector = metadataInspectorHooks.useMetadataInspector;
export const usePatchMetadataInspector = metadataInspectorHooks.usePatchMetadataInspector;
export const useReorderMetadataContributions = metadataInspectorHooks.useReorderMetadataContributions;

export function useDeleteContent() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: deleteContent,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.list() });
        },
    });
}

async function bulkPatchContent(request: BulkContentPatchRequest): Promise<BulkResult> {
    return apiPatch<BulkResult>('/content/bulk', request);
}

async function bulkDeleteContent(request: BulkContentDeleteRequest): Promise<BulkResult> {
    return apiPost<BulkResult>('/content/bulk/delete', request);
}

export function useBulkPatchContent() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: bulkPatchContent,
        onSettled: () => {
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
        },
    });
}

export function useBulkDeleteContent() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: bulkDeleteContent,
        onSettled: () => {
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
        },
    });
}
