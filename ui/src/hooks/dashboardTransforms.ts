import { extractContentFields, type ApiContentItemWithMetadata } from '@/lib/contentMapping';
import { formatRelativeTime } from '@/lib/formatTime';
import { extractTitle } from '@/lib/metadata';
import {
    mapFulfillmentStatus,
    type ApiContentHierarchyItem,
    type ApiContentStats,
    type FulfillmentStatus,
    type FulfillmentStatusValue,
    type ContentKind,
} from '@/types/content';
import type { PagedList } from '@/types/paging';
import type { ApiDownload, DownloadStatusValue } from './useDownloads';
import type { ApiIndexerConnection, IndexerStatusValue } from './useIndexers';
import type { ApiJobExecution, JobExecutionStatusValue } from './useJobs';

export type IntegrationStatus = IndexerStatusValue;
export type DownloadStatus = DownloadStatusValue;
export type JobExecutionStatus = JobExecutionStatusValue;

export interface DashboardIndexer {
    connections?: ApiIndexerConnection[];
}

export interface HealthCounts {
    healthy: number;
    unhealthy: number;
    unknown: number;
    stale: number;
}

export interface DownloadCounts {
    downloading: number;
    queued: number;
    stalled: number;
}

export interface ActiveDownload {
    id: string;
    title: string;
    progress: number;
    state: 'downloading' | 'queued' | 'stalled';
    size?: string;
    eta?: string;
}

export interface RecentJob {
    id: string;
    name: string;
    status: 'completed' | 'running' | 'failed';
    duration?: string;
    timestamp: string;
}

export interface ContentCounts {
    total: number;
    missing: number;
    upgradable: number;
    fulfilled: number;
    fulfilledExternally: number;
}

export interface RecentContentItem {
    id: string;
    title: string;
    addedAt: string;
    fulfillment: FulfillmentStatus;
}

export type { ApiContentItemWithMetadata as ApiContentItemWithDate };

export type PagedContentWithDates = PagedList<ApiContentItemWithMetadata>;

export interface UpcomingContentItem {
    id: string;
    contentId: string;
    title: string;
    subtitle?: string;
    airDate: string;
    posterUrl?: string;
    kind: ContentKind;
    fulfillment: FulfillmentStatus;
}

function accumulateStatus(counts: HealthCounts, status: IntegrationStatus): void {
    switch (status) {
        case 1:
            counts.healthy++;
            break;
        case 2:
            counts.unhealthy++;
            break;
        case 3:
            counts.stale++;
            break;
        case 0:
        default:
            counts.unknown++;
            break;
    }
}

export function calculateHealthCounts<T extends { enabled: boolean; status: IntegrationStatus }>(
    items: T[],
): HealthCounts {
    const counts: HealthCounts = { healthy: 0, unhealthy: 0, unknown: 0, stale: 0 };

    for (const item of items) {
        if (!item.enabled) {
            continue;
        }
        accumulateStatus(counts, item.status);
    }

    return counts;
}

export function calculateIndexerHealthCounts(indexers: DashboardIndexer[]): HealthCounts {
    const counts: HealthCounts = { healthy: 0, unhealthy: 0, unknown: 0, stale: 0 };

    for (const indexer of indexers) {
        if (!indexer.connections?.length) {
            continue;
        }
        for (const conn of indexer.connections) {
            if (!conn.enabled) {
                continue;
            }
            accumulateStatus(counts, conn.status);
        }
    }

    return counts;
}

export function mapDownloadState(status: DownloadStatus): 'downloading' | 'queued' | 'stalled' | null {
    switch (status) {
        case 2:
            return 'downloading';
        case 0:
        case 1:
            return 'queued';
        case 4:
            return 'stalled';
        default:
            return null;
    }
}

export function calculateDownloadCounts(downloads: ApiDownload[]): DownloadCounts {
    const counts: DownloadCounts = { downloading: 0, queued: 0, stalled: 0 };

    for (const download of downloads) {
        const state = mapDownloadState(download.status);
        if (state) {
            counts[state]++;
        }
    }

    return counts;
}

export function mapActiveDownloads(downloads: ApiDownload[], limit: number): ActiveDownload[] {
    return downloads
        .filter((d) => mapDownloadState(d.status) !== null)
        .slice(0, limit)
        .map((d) => ({
            id: d.id,
            title: d.title,
            progress: 0,
            state: mapDownloadState(d.status) || 'queued',
        }));
}

export function mapJobStatus(status: JobExecutionStatus): 'completed' | 'running' | 'failed' {
    switch (status) {
        case 3:
            return 'completed';
        case 2:
        case 1:
        case 6:
            return 'running';
        case 4:
        case 5:
        default:
            return 'failed';
    }
}

export function formatDuration(startedAt?: string | null, completedAt?: string | null): string | undefined {
    if (!startedAt || !completedAt) {
        return undefined;
    }

    const start = new Date(startedAt).getTime();
    const end = new Date(completedAt).getTime();
    const ms = end - start;

    if (ms < 1000) {
        return `${ms}ms`;
    }
    if (ms < 60000) {
        return `${Math.round(ms / 1000)}s`;
    }

    const minutes = Math.floor(ms / 60000);
    const seconds = Math.round((ms % 60000) / 1000);
    return seconds > 0 ? `${minutes}m ${seconds}s` : `${minutes}m`;
}

export function mapRecentJobs(jobs: ApiJobExecution[], limit: number): RecentJob[] {
    return jobs.slice(0, limit).map((j) => ({
        id: j.id,
        name: j.jobType.replace(/([A-Z])/g, ' $1').trim(),
        status: mapJobStatus(j.status),
        duration: formatDuration(j.startedAt, j.completedAt),
        timestamp: formatRelativeTime(j.startedAt || j.createdAt),
    }));
}

export function calculateContentCounts(stats: ApiContentStats): ContentCounts {
    return {
        total: stats.total,
        missing: stats.missing,
        upgradable: stats.upgradable,
        fulfilled: stats.fulfilled,
        fulfilledExternally: stats.fulfilledExternally,
    };
}

export function mapRecentContent(items: ApiContentHierarchyItem[], limit: number): RecentContentItem[] {
    return items.slice(0, limit).map((item) => ({
        id: item.id,
        title: extractTitle(item.metadataContainer) ?? item.name,
        addedAt: formatRelativeTime(item.createdAt),
        fulfillment: item.contentItem
            ? mapFulfillmentStatus(item.contentItem.fulfillment as FulfillmentStatusValue)
            : 'missing',
    }));
}

export function mapUpcomingContent(items: ApiContentItemWithMetadata[]): UpcomingContentItem[] {
    return items.map((item) => {
        const fields = extractContentFields(item);

        return {
            id: `upcoming-${item.id}`,
            contentId: item.id,
            title: fields.title,
            subtitle: fields.subtitle,
            airDate: fields.date ?? new Date().toISOString(),
            posterUrl: fields.posterUrl,
            kind: fields.kind,
            fulfillment: fields.fulfillment,
        };
    });
}
