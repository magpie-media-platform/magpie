import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query';

import { type ApiTag } from '@/hooks/useTags';
import { apiGet, apiPost, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import {
    type ApiContentHierarchyItem,
    type ContentListItem,
    type PagedContentHierarchyItems,
    type ContentQueryParams,
    mapToContentListItem,
    mapToContentDetailItem,
} from '@/types/content';

export const contentQueryKeys = {
    all: ['content'] as const,
    list: (params?: ContentQueryParams) =>
        params ? (['content', 'list', params] as const) : (['content', 'list'] as const),
    detail: (id: string) => ['content', 'detail', id] as const,
    children: (id: string, page?: number, pageSize?: number) =>
        ['content', 'children', id, { page, pageSize }] as const,
    tags: () => ['content', 'tags'] as const,
};

async function fetchContentList(params?: ContentQueryParams): Promise<PagedContentHierarchyItems> {
    const url = buildUrl('/content', params);
    return apiGet<PagedContentHierarchyItems>(url);
}

async function fetchContentById(id: string): Promise<ApiContentHierarchyItem> {
    return apiGet<ApiContentHierarchyItem>(`/content/${id}`);
}

async function fetchContentChildren(id: string, page = 1, pageSize = 25): Promise<PagedContentHierarchyItems> {
    const url = buildUrl(`/content/${id}/children`, { page, pageSize });
    return apiGet<PagedContentHierarchyItems>(url);
}

async function fetchTags(): Promise<ApiTag[]> {
    return apiGet<ApiTag[]>('/tags');
}

async function refreshContent(id: string): Promise<string> {
    return apiPost<string>(`/content/${id}/refresh`);
}

async function searchContentReleases(id: string): Promise<string> {
    return apiPost<string>(`/content/${id}/search`);
}

export interface ContentListResult {
    items: ContentListItem[];
    pageNumber: number;
    pageSize: number;
    totalItems: number;
}

export function useContentList(params?: ContentQueryParams, options?: { enabled?: boolean }) {
    return useQuery({
        queryKey: contentQueryKeys.list(params),
        queryFn: () => fetchContentList(params),
        select: (data): ContentListResult => ({
            items: Array.isArray(data?.items) ? data.items.map(mapToContentListItem) : [],
            pageNumber: data?.pageNumber ?? 1,
            pageSize: data?.pageSize ?? 25,
            totalItems: data?.totalItems ?? 0,
        }),
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
        enabled: options?.enabled ?? true,
    });
}

export function useContentDetail(id: string | undefined, options?: { enabled?: boolean }) {
    return useQuery({
        queryKey: contentQueryKeys.detail(id ?? ''),
        queryFn: () => fetchContentById(id!),
        select: mapToContentDetailItem,
        enabled: (options?.enabled ?? true) && !!id,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}

export function useContentHierarchy(
    activeId: string | undefined,
    parentId: string | undefined,
    rootId: string | undefined,
) {
    const queries = useQueries({
        queries: [
            {
                queryKey: contentQueryKeys.detail(activeId ?? ''),
                queryFn: () => fetchContentById(activeId!),
                select: mapToContentDetailItem,
                enabled: !!activeId,
                refetchInterval: POLLING_INTERVALS.slow,
                staleTime: POLLING_INTERVALS.slow / 2,
            },
            {
                queryKey: contentQueryKeys.detail(parentId ?? ''),
                queryFn: () => fetchContentById(parentId!),
                select: mapToContentDetailItem,
                enabled: !!parentId,
                refetchInterval: POLLING_INTERVALS.slow,
                staleTime: POLLING_INTERVALS.slow / 2,
            },
            {
                queryKey: contentQueryKeys.detail(rootId ?? ''),
                queryFn: () => fetchContentById(rootId!),
                select: mapToContentDetailItem,
                enabled: !!rootId,
                refetchInterval: POLLING_INTERVALS.slow,
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

export function useContentChildren(
    parentId: string | undefined,
    page = 1,
    pageSize = 25,
    options?: { enabled?: boolean },
) {
    return useQuery({
        queryKey: contentQueryKeys.children(parentId ?? '', page, pageSize),
        queryFn: () => fetchContentChildren(parentId!, page, pageSize),
        select: (data): ContentListResult => ({
            items: Array.isArray(data?.items) ? data.items.map(mapToContentListItem) : [],
            pageNumber: data?.pageNumber ?? 1,
            pageSize: data?.pageSize ?? 25,
            totalItems: data?.totalItems ?? 0,
        }),
        enabled: (options?.enabled ?? true) && !!parentId,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}

export function useContentTags() {
    return useQuery({
        queryKey: contentQueryKeys.tags(),
        queryFn: fetchTags,
        staleTime: POLLING_INTERVALS.slow,
    });
}

export function useRefreshContent() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: refreshContent,
        onSuccess: (_data, id) => {
            queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(id) });
            queryClient.invalidateQueries({ queryKey: contentQueryKeys.list() });
        },
    });
}

export function useSearchContentReleases() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: searchContentReleases,
        onSuccess: (_data, id) => {
            queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(id) });
            queryClient.invalidateQueries({ queryKey: contentQueryKeys.list() });
        },
    });
}

export interface UpdateContentRequest {
    id: string;
    monitoring?: number;
    qualityTierListId?: string | null;
    rootFolderId?: string | null;
    tagsToAdd?: string[];
    tagsToRemove?: string[];
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
            queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(request.id) });
            queryClient.invalidateQueries({ queryKey: contentQueryKeys.list() });
        },
    });
}

export function useDeleteContent() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: deleteContent,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: contentQueryKeys.list() });
        },
    });
}

export interface BulkUpdateContentRequest {
    ids: string[];
    monitoring?: number;
    qualityTierListId?: string | null;
    rootFolderId?: string | null;
    tagsToAdd?: string[];
    tagsToRemove?: string[];
}

export interface BulkUpdateResult {
    total: number;
    succeeded: number;
    failed: number;
    failedIds: string[];
}

export class BulkUpdateError extends Error {
    readonly result: BulkUpdateResult;

    constructor(result: BulkUpdateResult) {
        super(`Bulk update failed: ${result.failed} of ${result.total} items could not be updated`);
        this.name = 'BulkUpdateError';
        this.result = result;
    }
}

async function bulkUpdateContent(request: BulkUpdateContentRequest): Promise<BulkUpdateResult> {
    const { ids, monitoring, qualityTierListId, rootFolderId, tagsToAdd, tagsToRemove } = request;

    if (ids.length === 0) {
        return { total: 0, succeeded: 0, failed: 0, failedIds: [] };
    }

    const failedIds: string[] = [];
    const batchSize = 5;

    for (let i = 0; i < ids.length; i += batchSize) {
        const batch = ids.slice(i, i + batchSize);

        const results = await Promise.allSettled(
            batch.map((id) =>
                updateContent({
                    id,
                    monitoring,
                    qualityTierListId,
                    rootFolderId,
                    tagsToAdd,
                    tagsToRemove,
                }),
            ),
        );

        for (let j = 0; j < results.length; j++) {
            if (results[j].status === 'rejected') {
                failedIds.push(batch[j]);
            }
        }
    }

    const result: BulkUpdateResult = {
        total: ids.length,
        succeeded: ids.length - failedIds.length,
        failed: failedIds.length,
        failedIds,
    };

    if (failedIds.length === ids.length) {
        throw new BulkUpdateError(result);
    }

    return result;
}

export function useBulkUpdateContent() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: bulkUpdateContent,
        onSettled: () => {
            queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
        },
    });
}
