import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import {
    type ApiContentHierarchyItem,
    type ContentListItem,
    type ContentQueryParams,
    type PagedContentHierarchyItems,
    mapToContentListItem,
} from '@/types/content';

export const WANTED_PAGE_SIZE = 200;

const MAX_PAGES = 50;

const WANTED_CHANGE_EVENTS = [
    'content.hierarchyItem.created',
    'content.hierarchyItem.deleted',
    'content.hierarchyItem.updated',
    'content.fulfillment.changed',
    'metadata.refreshed',
    'download.created',
    'download.updated',
    'download.deleted',
    'download.completed',
    'download.failed',
    'download.imported',
    'download.stalled',
] as const;
const WANTED_FAIL_OPEN_EVENTS = ['content.hierarchyItem.created', 'download.created'] as const;

export const WANTED_CONTAINER_FILTER = 'Fulfillment in (Missing, Partial)';

interface WantedResult {
    items: ApiContentHierarchyItem[];
    totalItems: number;
    truncated: boolean;
}

interface UseWantedContentResult {
    items: ContentListItem[];

    upgradeItems: ContentListItem[];

    rawItems: ApiContentHierarchyItem[];

    isLoading: boolean;

    isFetching: boolean;

    isError: boolean;

    error: unknown;

    totalItems: number;

    isTruncated: boolean;

    refetch: () => Promise<void>;
}

export const wantedQueryKeys = {
    list: (parentId?: string) => ['wanted', 'list', { parentId: parentId ?? null }] as const,
    upgrades: (parentId?: string) => ['wanted', 'upgrades', { parentId: parentId ?? null }] as const,
    descendantCounts: ['wanted', 'descendant-counts'] as const,
    missingDescendants: (contentId: string) => ['wanted', 'descendant-counts', contentId] as const,
};

function wantedScope(parentId: string | undefined): Pick<ContentQueryParams, 'parentId' | 'topLevelOnly'> {
    return parentId ? { parentId } : { topLevelOnly: true };
}

async function fetchAllContent(params: ContentQueryParams): Promise<WantedResult> {
    const accumulated: ApiContentHierarchyItem[] = [];
    let totalItems = 0;

    for (let pageNumber = 1; pageNumber <= MAX_PAGES; pageNumber += 1) {
        const url = buildUrl('/content', { ...params, pageNumber, pageSize: WANTED_PAGE_SIZE });
        const result = await apiGet<PagedContentHierarchyItems>(url);
        totalItems = result.totalItems;
        accumulated.push(...result.items);

        if (result.items.length === 0 || accumulated.length >= totalItems) {
            break;
        }
    }

    return { items: accumulated, totalItems, truncated: accumulated.length < totalItems };
}

function dedupe(items: ApiContentHierarchyItem[]): ApiContentHierarchyItem[] {
    const seen = new Set<string>();
    return items.filter((item) => {
        if (seen.has(item.id)) {
            return false;
        }
        seen.add(item.id);
        return true;
    });
}

/** Loads every effectively monitored direct child through the public content query. */
export async function fetchWantedChildren(parentId: string): Promise<ContentListItem[]> {
    const result = await fetchAllContent({ parentId, monitoring: true });
    return dedupe(result.items).map(mapToContentListItem);
}

export function useWantedContent(parentId?: string): UseWantedContentResult {
    const wantedQuery = useQuery({
        queryKey: wantedQueryKeys.list(parentId),
        queryFn: () => fetchAllContent({ ...wantedScope(parentId), monitoring: true, filter: WANTED_CONTAINER_FILTER }),
        staleTime: POLLING_INTERVALS.slow / 2,
    });
    const upgradesQuery = useQuery({
        queryKey: wantedQueryKeys.upgrades(parentId),
        queryFn: () => fetchAllContent({ ...wantedScope(parentId), monitoring: true, fulfillment: 'Upgradable' }),
        staleTime: POLLING_INTERVALS.slow / 2,
    });
    const scopedEntityIds = useMemo(
        () => [
            ...(parentId ? [parentId] : []),
            ...(wantedQuery.data?.items.map((item) => item.id) ?? []),
            ...(upgradesQuery.data?.items.map((item) => item.id) ?? []),
        ],
        [parentId, wantedQuery.data?.items, upgradesQuery.data?.items],
    );
    const queryKeys = useMemo(
        () => [wantedQueryKeys.list(parentId), wantedQueryKeys.upgrades(parentId), wantedQueryKeys.descendantCounts],
        [parentId],
    );

    useInvalidateQueriesOnEvents(WANTED_CHANGE_EVENTS, queryKeys, {
        debounceMs: 300,
        entityIds: scopedEntityIds,
        exact: false,
        failOpenEventNames: WANTED_FAIL_OPEN_EVENTS,
        maxWaitMs: 1200,
        owner: 'wanted-content',
    });
    useInvalidateQueriesOnEvents(
        ['qualityTierList.created', 'qualityTierList.updated', 'qualityTierList.deleted'],
        [wantedQueryKeys.list(parentId), wantedQueryKeys.upgrades(parentId)],
        { debounceMs: 300, exact: true, maxWaitMs: 1200, owner: 'wanted-content' },
    );

    const rawItems = useMemo(() => dedupe(wantedQuery.data?.items ?? []), [wantedQuery.data?.items]);
    const items = useMemo(() => rawItems.map(mapToContentListItem), [rawItems]);
    const upgradeItems = useMemo(
        () => dedupe(upgradesQuery.data?.items ?? []).map(mapToContentListItem),
        [upgradesQuery.data?.items],
    );

    const totalItems = useMemo(
        () => new Set([...items, ...upgradeItems].map((item) => item.id)).size,
        [items, upgradeItems],
    );

    const refetch = async () => {
        await Promise.all([wantedQuery.refetch(), upgradesQuery.refetch()]);
    };

    return {
        items,
        upgradeItems,
        rawItems,
        isLoading: wantedQuery.isLoading || upgradesQuery.isLoading,
        isFetching: wantedQuery.isFetching || upgradesQuery.isFetching,
        isError: wantedQuery.isError || upgradesQuery.isError,
        error: wantedQuery.error ?? upgradesQuery.error ?? null,
        totalItems,
        isTruncated: (wantedQuery.data?.truncated ?? false) || (upgradesQuery.data?.truncated ?? false),
        refetch,
    };
}
