import { useQuery } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import type { ApiContentStats, PagedContentHierarchyItems } from '@/types/content';
import type { PagedList } from '@/types/paging';
import type { ApiDownloadClient } from './useDownloadClients';
import type { ApiDownload } from './useDownloads';
import type { ApiJobExecution } from './useJobs';
import type { ApiMediaServer } from './useMediaServers';

import {
    calculateHealthCounts,
    calculateIndexerHealthCounts,
    calculateDownloadCounts,
    mapActiveDownloads,
    mapRecentJobs,
    calculateContentCounts,
    mapRecentContent,
    mapUpcomingContent,
    type DashboardIndexer,
    type HealthCounts,
    type DownloadCounts,
    type ActiveDownload,
    type RecentJob,
    type ContentCounts,
    type RecentContentItem,
    type PagedContentWithDates,
    type UpcomingContentItem,
} from './dashboardTransforms';

export type {
    HealthCounts,
    DownloadCounts,
    ActiveDownload,
    RecentJob,
    ContentCounts,
    RecentContentItem,
    UpcomingContentItem,
};
export type { IntegrationStatus, DownloadStatus, JobExecutionStatus } from './dashboardTransforms';

export const dashboardQueryKeys = {
    all: ['dashboard'] as const,
    indexers: ['dashboard', 'indexers'] as const,
    downloadClients: ['dashboard', 'download-clients'] as const,
    mediaServers: ['dashboard', 'media-servers'] as const,
    downloads: ['dashboard', 'downloads'] as const,
    jobs: ['dashboard', 'jobs'] as const,
    contentStats: ['dashboard', 'content-stats'] as const,
    recentContent: ['dashboard', 'recent-content'] as const,
    upcomingContent: ['dashboard', 'upcoming-content'] as const,
};

async function fetchIndexers(): Promise<DashboardIndexer[]> {
    return apiGet<DashboardIndexer[]>('/indexers');
}

async function fetchDownloadClients(): Promise<ApiDownloadClient[]> {
    return apiGet<ApiDownloadClient[]>('/download-clients');
}

async function fetchMediaServers(): Promise<ApiMediaServer[]> {
    return apiGet<ApiMediaServer[]>('/media-servers');
}

async function fetchDownloads(): Promise<PagedList<ApiDownload>> {
    return apiGet<PagedList<ApiDownload>>('/downloads?pageSize=10');
}

async function fetchJobExecutions(): Promise<PagedList<ApiJobExecution>> {
    return apiGet<PagedList<ApiJobExecution>>('/jobs?pageSize=10&sortBy=createdAt&sortDirection=2');
}

async function fetchContentStats(): Promise<ApiContentStats> {
    return apiGet<ApiContentStats>('/content/stats');
}

async function fetchRecentContent(): Promise<PagedContentHierarchyItems> {
    return apiGet<PagedContentHierarchyItems>(
        buildUrl('/content', {
            pageSize: 10,
            sortBy: 'createdAt',
            sortDirection: 2,
        }),
    );
}

async function fetchUpcomingContent(): Promise<PagedContentWithDates> {
    const today = new Date().toISOString().split('T')[0];
    return apiGet<PagedContentWithDates>(
        buildUrl('/content', {
            filter: `publishDate >= "${today}"`,
            pageSize: 5,
            sortBy: 'publishDate',
            sortDirection: 1,
        }),
    );
}

export function useIndexerHealth() {
    return useQuery({
        queryKey: dashboardQueryKeys.indexers,
        queryFn: fetchIndexers,
        select: calculateIndexerHealthCounts,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}

export function useDownloadClientHealth() {
    return useQuery({
        queryKey: dashboardQueryKeys.downloadClients,
        queryFn: fetchDownloadClients,
        select: calculateHealthCounts,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}

export function useMediaServerHealth() {
    return useQuery({
        queryKey: dashboardQueryKeys.mediaServers,
        queryFn: fetchMediaServers,
        select: calculateHealthCounts,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}

export function useActiveDownloads(limit = 5) {
    return useQuery({
        queryKey: dashboardQueryKeys.downloads,
        queryFn: fetchDownloads,
        select: (data) => ({
            counts: calculateDownloadCounts(data.items),
            items: mapActiveDownloads(data.items, limit),
        }),
        refetchInterval: POLLING_INTERVALS.fast,
        staleTime: POLLING_INTERVALS.fast / 2,
    });
}

export function useRecentJobs(limit = 5) {
    return useQuery({
        queryKey: dashboardQueryKeys.jobs,
        queryFn: fetchJobExecutions,
        select: (data) => mapRecentJobs(data.items, limit),
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}

export function useContentCounts() {
    return useQuery({
        queryKey: dashboardQueryKeys.contentStats,
        queryFn: fetchContentStats,
        select: calculateContentCounts,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}

export function useRecentContent(limit = 3) {
    return useQuery({
        queryKey: dashboardQueryKeys.recentContent,
        queryFn: fetchRecentContent,
        select: (data) => mapRecentContent(data.items, limit),
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}

export function useUpcomingContent() {
    return useQuery({
        queryKey: dashboardQueryKeys.upcomingContent,
        queryFn: fetchUpcomingContent,
        select: (data) => mapUpcomingContent(data.items),
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });
}
