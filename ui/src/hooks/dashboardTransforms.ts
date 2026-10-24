import { extractContentFields, type ApiContentItemWithMetadata } from '@/lib/contentMapping';
import { formatRelativeTime } from '@/lib/formatTime';
import { toLocalDateKey } from '@/lib/dateFormatting';
import { parseCalendarDate, parseUtcInstant } from '@/lib/dateParsing';
import { extractPublishDate, extractPublishDatePrecision, extractTitle } from '@/lib/metadata';
import {
    mapFulfillmentStatus,
    type ApiContentHierarchyItem,
    type ApiContentStats,
    type FulfillmentStatus,
    type MonitoringMode,
} from '@/types/content';
import { normalizeHealthStatus } from '@/types/healthStatus';
import type { TemporalPrecision } from '@/types/contentMetadata';
import type { PagedList } from '@/types/paging';
import type { ApiDownload, DownloadStatusValue } from './useDownloads';
import type { IndexerStatusValue } from './useIndexers';

export type IntegrationStatus = IndexerStatusValue;
export type DownloadStatus = DownloadStatusValue;

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

export interface ContentCounts {
    total: number;
    missing: number;
    upgradable: number;
    fulfilled: number;
}

export interface RecentContentItem {
    id: string;
    parentId?: string;
    title: string;
    timestamp: string;
    fulfillment: FulfillmentStatus;
    publishDate?: string;
    publishDatePrecision?: TemporalPrecision;
    effectiveMonitoring: MonitoringMode;
}

export interface RecentlyFulfilled {
    today: number;
    thisWeek: number;
    items: RecentContentItem[];
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
    datePrecision?: TemporalPrecision;
    posterUrl?: string;
    contentType: string;
    level: string;
    levelLabel?: string;
    fulfillment: FulfillmentStatus;
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

export function calculateContentCounts(stats: ApiContentStats): ContentCounts {
    return {
        total: stats.total,
        missing: stats.missing,
        upgradable: stats.upgradable,
        fulfilled: stats.fulfilled,
    };
}

function toRecentContentItem(item: ApiContentHierarchyItem, date: string | null | undefined): RecentContentItem {
    return {
        id: item.id,
        ...(item.parentId ? { parentId: item.parentId } : {}),
        title: extractTitle(item.metadataContainer) ?? item.name,
        timestamp: formatRelativeTime(date, 'Unknown'),
        fulfillment: item.contentItem ? mapFulfillmentStatus(item.contentItem.fulfillment) : 'missing',
        publishDate: extractPublishDate(item.metadataContainer),
        publishDatePrecision: extractPublishDatePrecision(item.metadataContainer),
        effectiveMonitoring: item.effectiveMonitoring,
    };
}

export function mapRecentContent(items: ApiContentHierarchyItem[], limit: number): RecentContentItem[] {
    return items.slice(0, limit).map((item) => toRecentContentItem(item, item.createdAt));
}

function localDayStart(now: number, daysAgo: number): number {
    const start = new Date(now);
    start.setDate(start.getDate() - daysAgo);
    return start.setHours(0, 0, 0, 0);
}

export function thisWeekStart(now: number): number {
    return localDayStart(now, 6);
}

export function mapRecentlyFulfilled(items: ApiContentHierarchyItem[], limit: number, now: number): RecentlyFulfilled {
    const todayStart = localDayStart(now, 0);
    const weekStart = thisWeekStart(now);
    const parentIds = new Set(items.map((item) => item.parentId));
    const lowest = items.filter((item) => !parentIds.has(item.id));
    const times = lowest.map((item) => parseUtcInstant(item.contentItem?.fulfilledAt ?? '').getTime());
    return {
        today: times.filter((time) => time >= todayStart).length,
        thisWeek: times.filter((time) => time >= weekStart).length,
        items: lowest.slice(0, limit).map((item) => toRecentContentItem(item, item.contentItem?.fulfilledAt)),
    };
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
            datePrecision: fields.datePrecision,
            posterUrl: fields.posterUrl,
            contentType: fields.contentType,
            level: fields.level,
            levelLabel: fields.levelLabel,
            fulfillment: fields.fulfillment,
            effectiveMonitoring: item.effectiveMonitoring,
        });
    }
    return result;
}
