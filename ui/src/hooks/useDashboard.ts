import { useQuery, useQueryClient } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet, buildUrl } from '@/lib/api';
import { toLocalDateKey } from '@/lib/dateFormatting';
import { parseUtcInstant } from '@/lib/dateParsing';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import type { ApiContentHierarchyItem, ApiContentStats, PagedContentHierarchyItems } from '@/types/content';
import { ACTIVE_DOWNLOAD_STATUSES } from '@/types/downloads';
import type { PagedList } from '@/types/paging';
import type { ApiDownloadClient } from './useDownloadClients';
import { downloadEvents, useDownloadStatusCounts, type ApiDownload } from './useDownloads';
import type { ApiMediaServer } from './useMediaServers';

import {
    calculateHealthCounts,
    mapDownloadStatusCounts,
    mapActiveDownloads,
    calculateContentCounts,
    mapRecentContent,
    mapRecentlyFulfilled,
    mapUpcomingContent,
    thisWeekStart,
    type HealthCounts,
    type DownloadCounts,
    type ActiveDownload,
    type ContentCounts,
    type RecentContentItem,
    type RecentlyFulfilled,
    type PagedContentWithDates,
    type UpcomingContentItem,
} from './dashboardTransforms';

export type {
    HealthCounts,
    DownloadCounts,
    ActiveDownload,
    ContentCounts,
    RecentContentItem,
    RecentlyFulfilled,
    UpcomingContentItem,
};
export type { IntegrationStatus, DownloadStatus } from './dashboardTransforms';

const FULFILLED_FILTER = 'fulfillment in (Fulfilled, Upgradable)';

export const dashboardQueryKeys = {
    downloadClients: ['dashboard', 'download-clients'] as const,
    mediaServers: ['dashboard', 'media-servers'] as const,
    downloads: ['dashboard', 'downloads'] as const,
    contentStats: ['dashboard', 'content-stats'] as const,
    recentContent: ['dashboard', 'recent-content'] as const,
    recentlyFulfilled: ['dashboard', 'recently-fulfilled'] as const,
    upcomingContent: (today: string) => ['dashboard', 'upcoming-content', today] as const,
};

async function fetchDownloadClients(): Promise<ApiDownloadClient[]> {
    return apiGet<ApiDownloadClient[]>('/download-clients');
}

async function fetchMediaServers(): Promise<ApiMediaServer[]> {
    return apiGet<ApiMediaServer[]>('/media-servers');
}

async function fetchDownloads(): Promise<PagedList<ApiDownload>> {
    return apiGet<PagedList<ApiDownload>>(
        buildUrl('/downloads', {
            statuses: ACTIVE_DOWNLOAD_STATUSES,
            pageNumber: 1,
            pageSize: 10,
            sortBy: 'CreatedAt',
            sortDirection: 'Descending',
        }),
    );
}

async function fetchContentStats(): Promise<ApiContentStats> {
    return apiGet<ApiContentStats>('/content/stats');
}

async function fetchRecentContent(): Promise<PagedContentHierarchyItems> {
    return apiGet<PagedContentHierarchyItems>(
        buildUrl('/content', {
            pageSize: 10,
            sortBy: 'createdAt',
            sortDirection: 'Descending',
        }),
    );
}

async function fetchRecentlyFulfilled(): Promise<ApiContentHierarchyItem[]> {
    const weekStart = thisWeekStart(Date.now());
    const items = new Map<string, ApiContentHierarchyItem>();
    for (let pageNumber = 1; ; pageNumber += 1) {
        const page = await apiGet<PagedContentHierarchyItems>(
            buildUrl('/content', {
                filter: FULFILLED_FILTER,
                pageNumber,
                pageSize: 200,
                sortBy: 'FulfilledAt',
                sortDirection: 'Descending',
            }),
        );
        for (const item of page.items) {
            items.set(item.id, item);
        }
        const oldest = page.items[page.items.length - 1]?.contentItem?.fulfilledAt;
        if (items.size >= page.totalItems || !oldest || parseUtcInstant(oldest).getTime() < weekStart) {
            return [...items.values()];
        }
    }
}

async function fetchUpcomingContent(today: string): Promise<PagedContentWithDates> {
    return apiGet<PagedContentWithDates>(
        buildUrl('/content', {
            filter: `publishDate >= "${today}"`,
            pageSize: 5,
            sortBy: 'publishDate',
            sortDirection: 'Ascending',
        }),
    );
}

export function useDownloadClientHealth() {
    useInvalidateQueriesOnEvents(
        ['downloadClient.created', 'downloadClient.deleted', 'downloadClient.updated'],
        [dashboardQueryKeys.downloadClients],
        { exact: true },
    );
    useInvalidateQueriesOnEvents(['integration.health.changed'], [dashboardQueryKeys.downloadClients], {
        context: { 'integration.type': 'downloadClient' },
        debounceMs: 200,
        exact: true,
        maxWaitMs: 800,
    });
    return useQuery({
        queryKey: dashboardQueryKeys.downloadClients,
        queryFn: fetchDownloadClients,
        select: calculateHealthCounts,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}

export function useMediaServerHealth() {
    useInvalidateQueriesOnEvents(
        ['mediaServer.created', 'mediaServer.deleted', 'mediaServer.updated'],
        [dashboardQueryKeys.mediaServers],
        { exact: true },
    );
    useInvalidateQueriesOnEvents(['integration.health.changed'], [dashboardQueryKeys.mediaServers], {
        context: { 'integration.type': 'mediaServer' },
        debounceMs: 200,
        exact: true,
        maxWaitMs: 800,
    });
    return useQuery({
        queryKey: dashboardQueryKeys.mediaServers,
        queryFn: fetchMediaServers,
        select: calculateHealthCounts,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}

export function useActiveDownloads(limit = 5) {
    const queryClient = useQueryClient();
    const statusCounts = useDownloadStatusCounts(ACTIVE_DOWNLOAD_STATUSES);
    useInvalidateQueriesOnEvents([...downloadEvents], [dashboardQueryKeys.downloads], {
        debounceMs: 200,
        exact: true,
        maxWaitMs: 800,
        predicate: (event) => {
            if (event.name !== 'download.updated') {
                return true;
            }

            const changedFields = event.context['download.changedFields']?.split(',') ?? [];
            if (changedFields.some((field) => field.trim().toLowerCase() === 'status')) {
                return true;
            }

            const downloadId = event.context['download.id'];
            const current = queryClient.getQueryData<PagedList<ApiDownload>>(dashboardQueryKeys.downloads);
            return !!current?.items.some((download) => download.id === downloadId);
        },
    });
    useInvalidateQueriesOnEvents(
        ['content.hierarchyItem.updated', 'content.hierarchyItem.deleted', 'metadata.refreshed'],
        [dashboardQueryKeys.downloads],
        {
            debounceMs: 200,
            exact: true,
            maxWaitMs: 800,
            predicate: (event) => {
                const current = queryClient.getQueryData<PagedList<ApiDownload>>(dashboardQueryKeys.downloads);
                return !!current?.items.some(
                    (download) => download.contentHierarchyItemId === event.context['content.hierarchyItemId'],
                );
            },
        },
    );
    const downloadsQuery = useQuery({
        queryKey: dashboardQueryKeys.downloads,
        queryFn: fetchDownloads,
    });

    return {
        ...downloadsQuery,
        data: downloadsQuery.data
            ? {
                  counts: mapDownloadStatusCounts(statusCounts.counts),
                  items: mapActiveDownloads(downloadsQuery.data.items, limit),
              }
            : undefined,
        isLoading: downloadsQuery.isLoading || statusCounts.isLoading,
        error: downloadsQuery.error ?? statusCounts.error,
        refetch: async () => {
            const [result] = await Promise.all([downloadsQuery.refetch(), statusCounts.refetch()]);
            return result;
        },
    };
}

export function useContentCounts() {
    useInvalidateQueriesOnEvents(
        [
            'content.hierarchyItem.created',
            'content.hierarchyItem.updated',
            'content.hierarchyItem.deleted',
            'content.fulfillment.changed',
        ],
        [dashboardQueryKeys.contentStats],
        { debounceMs: 300, exact: true, maxWaitMs: 1200 },
    );
    return useQuery({
        queryKey: dashboardQueryKeys.contentStats,
        queryFn: fetchContentStats,
        select: calculateContentCounts,
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}

export function useRecentContent(limit = 3) {
    useInvalidateQueriesOnEvents(
        [
            'content.hierarchyItem.created',
            'content.hierarchyItem.updated',
            'content.hierarchyItem.deleted',
            'content.fulfillment.changed',
            'metadata.refreshed',
        ],
        [dashboardQueryKeys.recentContent],
        { debounceMs: 300, exact: true, maxWaitMs: 1200 },
    );
    return useQuery({
        queryKey: dashboardQueryKeys.recentContent,
        queryFn: fetchRecentContent,
        select: (data) => mapRecentContent(data.items, limit),
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}

export function useRecentlyFulfilled(limit = 5) {
    useInvalidateQueriesOnEvents(
        [
            'content.hierarchyItem.created',
            'content.hierarchyItem.updated',
            'content.hierarchyItem.deleted',
            'content.fulfillment.changed',
            'metadata.refreshed',
        ],
        [dashboardQueryKeys.recentlyFulfilled],
        { debounceMs: 300, exact: true, maxWaitMs: 1200 },
    );
    return useQuery({
        queryKey: dashboardQueryKeys.recentlyFulfilled,
        queryFn: fetchRecentlyFulfilled,
        select: (items) => mapRecentlyFulfilled(items, limit, Date.now()),
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}

export function useUpcomingContent() {
    const today = toLocalDateKey(new Date());
    useInvalidateQueriesOnEvents(
        [
            'content.hierarchyItem.created',
            'content.hierarchyItem.updated',
            'content.hierarchyItem.deleted',
            'content.fulfillment.changed',
            'metadata.refreshed',
        ],
        [dashboardQueryKeys.upcomingContent(today)],
        { debounceMs: 300, exact: true, maxWaitMs: 1200 },
    );
    return useQuery({
        queryKey: dashboardQueryKeys.upcomingContent(today),
        queryFn: () => fetchUpcomingContent(today),
        select: (data) => mapUpcomingContent(data.items),
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}
