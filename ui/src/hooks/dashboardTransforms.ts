import { extractContentFields, type ApiContentItemWithMetadata } from '@/lib/contentMapping';
import { formatElapsedDuration, formatRelativeTime } from '@/lib/formatTime';
import { humanizeJobType } from '@/lib/jobExecution';
import { toLocalDateKey } from '@/lib/dateFormatting';
import { parseCalendarDate } from '@/lib/dateParsing';
import { extractTitle } from '@/lib/metadata';
import {
    mapFulfillmentStatus,
    type ApiContentHierarchyItem,
    type ApiContentStats,
    type FulfillmentStatus,
    type MonitoringMode,
} from '@/types/content';
import { normalizeHealthStatus } from '@/types/healthStatus';
import type { PagedList } from '@/types/paging';
import type { ApiDownload, DownloadStatusValue } from './useDownloads';
import type { IndexerStatusValue } from './useIndexers';
import type { ApiJobExecution, JobExecutionStatusValue } from './useJobs';

export type IntegrationStatus = IndexerStatusValue;
export type DownloadStatus = DownloadStatusValue;
export type JobExecutionStatus = JobExecutionStatusValue;

export interface HealthCounts {
    healthy: number;
    unhealthy: number;
    degraded: number;
    stabilizing: number;
    unknown: number;
    stale: number;
}

export interface DownloadCounts {
    downloading: number;
    queued: number;
    paused: number;
    stalled: number;
}

export interface ActiveDownload {
    id: string;
    contentId?: string;
    parentId?: string;
    title: string;
    progress: number;
    state: 'downloading' | 'queued' | 'paused' | 'stalled';
    size?: string;
    eta?: string;
}

export interface RecentJob {
    id: string;
    name: string;
    status: 'completed' | 'running' | 'failed' | 'cancelled';
    duration?: string;
    timestamp: string;
}

export interface ContentCounts {
    total: number;
    missing: number;
    upgradable: number;
    fulfilled: number;
    fulfilledExternally: number;
    partial: number;
}

export interface RecentContentItem {
    id: string;
    parentId?: string;
    title: string;
    addedAt: string;
    fulfillment: FulfillmentStatus;
    monitoring: MonitoringMode | null;
    effectiveMonitoring: MonitoringMode;
}

export type { ApiContentItemWithMetadata as ApiContentItemWithDate };

export type PagedContentWithDates = PagedList<ApiContentItemWithMetadata>;

export interface UpcomingContentItem {
    id: string;
    contentId: string;
    parentId?: string;
    title: string;
    subtitle?: string;
    date: string;
    posterUrl?: string;
    contentType: string;
    level: string;
    levelLabel?: string;
    fulfillment: FulfillmentStatus;
    monitoring: MonitoringMode | null;
    effectiveMonitoring: MonitoringMode;
}

function accumulateStatus(counts: HealthCounts, status: string | null | undefined): void {
    switch (normalizeHealthStatus(status)) {
        case 'Healthy':
            counts.healthy++;
            break;
        case 'Unhealthy':
            counts.unhealthy++;
            break;
        case 'Degraded':
            counts.degraded++;
            break;
        case 'Stabilizing':
            counts.stabilizing++;
            break;
        case 'Stale':
            counts.stale++;
            break;
        case 'Unknown':
        default:
            counts.unknown++;
            break;
    }
}

export function calculateHealthCounts<T extends { enabled: boolean; status: string | null | undefined }>(
    items: T[],
): HealthCounts {
    const counts: HealthCounts = { healthy: 0, unhealthy: 0, degraded: 0, stabilizing: 0, unknown: 0, stale: 0 };

    for (const item of items) {
        if (!item.enabled) {
            continue;
        }
        accumulateStatus(counts, item.status);
    }

    return counts;
}

export function mapDownloadState(status: DownloadStatus): 'downloading' | 'queued' | 'paused' | 'stalled' | null {
    switch (status) {
        case 'Downloading':
            return 'downloading';
        case 'Grabbed':
        case 'Queued':
            return 'queued';
        case 'Paused':
            return 'paused';
        case 'Stalled':
            return 'stalled';
        case 'Failed':
        case 'Faulted':
        case 'Downloaded':
        case 'Importing':
        case 'Imported':
        case 'Cancelled':
            return null;
    }
}

export function mapDownloadStatusCounts(statusCounts: Partial<Record<DownloadStatusValue, number>>): DownloadCounts {
    return {
        downloading: statusCounts.Downloading ?? 0,
        queued: (statusCounts.Grabbed ?? 0) + (statusCounts.Queued ?? 0),
        paused: statusCounts.Paused ?? 0,
        stalled: statusCounts.Stalled ?? 0,
    };
}

export function mapActiveDownloads(downloads: ApiDownload[], limit: number): ActiveDownload[] {
    return downloads
        .filter((d) => mapDownloadState(d.status) !== null)
        .slice(0, limit)
        .map((d) => ({
            id: d.id,
            ...(d.contentHierarchyItemId ? { contentId: d.contentHierarchyItemId } : {}),
            ...(d.contentHierarchyParentId ? { parentId: d.contentHierarchyParentId } : {}),
            title: d.contentMetadataTitle?.trim() || d.contentHierarchyName?.trim() || d.title,
            progress: d.progress ?? 0,
            state: mapDownloadState(d.status) || 'queued',
        }));
}

export function mapJobStatus(status: JobExecutionStatus): RecentJob['status'] {
    switch (status) {
        case 'Completed':
            return 'completed';
        case 'Running':
        case 'Queued':
        case 'CancellationRequested':
            return 'running';
        case 'Cancelled':
            return 'cancelled';
        case 'Failed':
        default:
            return 'failed';
    }
}

export function mapRecentJobs(jobs: ApiJobExecution[], limit: number): RecentJob[] {
    return jobs.slice(0, limit).map((j) => ({
        id: j.id,
        name: humanizeJobType(j.jobType),
        status: mapJobStatus(j.status),
        duration: formatElapsedDuration(j.startedAt, j.completedAt),
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
        partial: stats.partial,
    };
}

export function mapRecentContent(items: ApiContentHierarchyItem[], limit: number): RecentContentItem[] {
    return items.slice(0, limit).map((item) => ({
        id: item.id,
        ...(item.parentId ? { parentId: item.parentId } : {}),
        title: extractTitle(item.metadataContainer) ?? item.name,
        addedAt: formatRelativeTime(item.createdAt),
        fulfillment: item.contentItem ? mapFulfillmentStatus(item.contentItem.fulfillment) : 'missing',
        monitoring: item.monitoring,
        effectiveMonitoring: item.effectiveMonitoring,
    }));
}

export function mapUpcomingContent(items: ApiContentItemWithMetadata[]): UpcomingContentItem[] {
    const today = toLocalDateKey(new Date());

    const result: UpcomingContentItem[] = [];
    for (const item of items) {
        const fields = extractContentFields(item);
        if (!fields.date) {
            continue;
        }
        const parsed = parseCalendarDate(fields.date);
        if (isNaN(parsed.getTime())) {
            continue;
        }
        if (toLocalDateKey(parsed) < today) {
            continue;
        }
        result.push({
            id: `upcoming-${item.id}`,
            contentId: item.id,
            ...(item.parentId ? { parentId: item.parentId } : {}),
            title: fields.title,
            subtitle: fields.subtitle,
            date: fields.date,
            posterUrl: fields.posterUrl,
            contentType: fields.contentType,
            level: fields.level,
            levelLabel: fields.levelLabel,
            fulfillment: fields.fulfillment,
            monitoring: item.monitoring,
            effectiveMonitoring: item.effectiveMonitoring,
        });
    }
    return result;
}
