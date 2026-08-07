import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

import {
    calculateHealthCounts,
    mapDownloadState,
    calculateDownloadCounts,
    mapDownloadStatusCounts,
    mapActiveDownloads,
    mapJobStatus,
    mapRecentJobs,
    calculateContentCounts,
    mapRecentContent,
    mapUpcomingContent,
} from './dashboardTransforms';
import type { ApiDownload, DownloadStatusValue } from './useDownloads';
import type { ApiJobExecution, JobExecutionStatusValue } from './useJobs';
import type { ApiContentHierarchyItem, ApiContentStats } from '@/types/content';
import type { ApiContentItemWithMetadata } from '@/lib/contentMapping';

function makeHealthItem(status: string, enabled = true) {
    return { enabled, status };
}

function makeDownload(overrides: Partial<ApiDownload> = {}): ApiDownload {
    return {
        id: 'dl-1',
        downloadClientId: 'dc-1',
        externalId: 'ext-1',
        title: 'Test Download',
        status: 'Downloading' as DownloadStatusValue,
        createdAt: '2025-01-01T00:00:00Z',
        ...overrides,
    };
}

function makeJobExecution(overrides: Partial<ApiJobExecution> = {}): ApiJobExecution {
    return {
        id: 'job-1',
        jobType: 'ContentMetadataRefreshItem',
        scope: 'System',
        status: 'Completed' as JobExecutionStatusValue,
        createdAt: '2025-01-01T00:00:00Z',
        ...overrides,
    };
}

function makeHierarchyItem(overrides: Partial<ApiContentHierarchyItem> = {}): ApiContentHierarchyItem {
    return {
        id: 'hi-1',
        metadataContainerId: 'mc-1',
        contentType: 'episodes',
        level: 'series',
        slug: 'test-item',
        name: 'Test Item',
        monitoring: null,
        effectiveMonitoring: true,
        createdAt: '2025-01-01T00:00:00Z',
        ...overrides,
    };
}

function makeContentItemWithMetadata(overrides: Partial<ApiContentItemWithMetadata> = {}): ApiContentItemWithMetadata {
    return {
        id: 'ci-1',
        metadataContainerId: 'mc-1',
        contentType: 'episodes',
        level: 'series',
        slug: 'test-item',
        name: 'Test Item',
        monitoring: null,
        effectiveMonitoring: true,
        createdAt: '2025-01-01T00:00:00Z',
        ...overrides,
    };
}

describe('calculateHealthCounts', () => {
    it('returns all zeros for empty array', () => {
        expect(calculateHealthCounts([])).toEqual({
            healthy: 0,
            unhealthy: 0,
            degraded: 0,
            stabilizing: 0,
            unknown: 0,
            stale: 0,
        });
    });

    it('counts healthy items', () => {
        const items = [makeHealthItem('Healthy'), makeHealthItem('Healthy')];
        expect(calculateHealthCounts(items)).toEqual({
            healthy: 2,
            unhealthy: 0,
            degraded: 0,
            stabilizing: 0,
            unknown: 0,
            stale: 0,
        });
    });

    it('counts unhealthy items', () => {
        const items = [makeHealthItem('Unhealthy')];
        expect(calculateHealthCounts(items)).toEqual({
            healthy: 0,
            unhealthy: 1,
            degraded: 0,
            stabilizing: 0,
            unknown: 0,
            stale: 0,
        });
    });

    it('counts degraded items', () => {
        const items = [makeHealthItem('Degraded')];
        expect(calculateHealthCounts(items)).toEqual({
            healthy: 0,
            unhealthy: 0,
            degraded: 1,
            stabilizing: 0,
            unknown: 0,
            stale: 0,
        });
    });

    it('counts stabilizing items', () => {
        const items = [makeHealthItem('Stabilizing')];
        expect(calculateHealthCounts(items)).toEqual({
            healthy: 0,
            unhealthy: 0,
            degraded: 0,
            stabilizing: 1,
            unknown: 0,
            stale: 0,
        });
    });

    it('counts stale items', () => {
        const items = [makeHealthItem('Stale')];
        expect(calculateHealthCounts(items)).toEqual({
            healthy: 0,
            unhealthy: 0,
            degraded: 0,
            stabilizing: 0,
            unknown: 0,
            stale: 1,
        });
    });

    it('counts unknown status as unknown', () => {
        const items = [makeHealthItem('Unknown')];
        expect(calculateHealthCounts(items)).toEqual({
            healthy: 0,
            unhealthy: 0,
            degraded: 0,
            stabilizing: 0,
            unknown: 1,
            stale: 0,
        });
    });

    it('skips disabled items', () => {
        const items = [makeHealthItem('Healthy', false), makeHealthItem('Unhealthy', false)];
        expect(calculateHealthCounts(items)).toEqual({
            healthy: 0,
            unhealthy: 0,
            degraded: 0,
            stabilizing: 0,
            unknown: 0,
            stale: 0,
        });
    });

    it('counts mixed statuses correctly', () => {
        const items = [
            makeHealthItem('Healthy'),
            makeHealthItem('Unhealthy'),
            makeHealthItem('Degraded'),
            makeHealthItem('Stabilizing'),
            makeHealthItem('Stale'),
            makeHealthItem('Unknown'),
            makeHealthItem('Healthy', false),
        ];
        expect(calculateHealthCounts(items)).toEqual({
            healthy: 1,
            unhealthy: 1,
            degraded: 1,
            stabilizing: 1,
            unknown: 1,
            stale: 1,
        });
    });

    it('normalizes lowercase and mixed-case status values before counting', () => {
        const items = [
            makeHealthItem('healthy'),
            makeHealthItem('unhealthy'),
            makeHealthItem('degraded'),
            makeHealthItem('dEgRaDeD'),
            makeHealthItem('stabilizing'),
            makeHealthItem('stale'),
            makeHealthItem('unknown'),
            makeHealthItem('not-a-status'),
        ];

        expect(calculateHealthCounts(items)).toEqual({
            healthy: 1,
            unhealthy: 1,
            degraded: 2,
            stabilizing: 1,
            unknown: 2,
            stale: 1,
        });
    });
});

describe('mapDownloadState', () => {
    it('maps Downloading to downloading', () => {
        expect(mapDownloadState('Downloading')).toBe('downloading');
    });

    it('maps Grabbed to queued', () => {
        expect(mapDownloadState('Grabbed')).toBe('queued');
    });

    it('maps Queued to queued', () => {
        expect(mapDownloadState('Queued')).toBe('queued');
    });

    it('maps Stalled to stalled', () => {
        expect(mapDownloadState('Stalled')).toBe('stalled');
    });

    it('maps Paused to paused', () => {
        expect(mapDownloadState('Paused')).toBe('paused');
    });

    it('maps Downloaded to null', () => {
        expect(mapDownloadState('Downloaded')).toBeNull();
    });
});

describe('calculateDownloadCounts', () => {
    it('returns all zeros for empty array', () => {
        expect(calculateDownloadCounts([])).toEqual({ downloading: 0, queued: 0, paused: 0, stalled: 0 });
    });

    it('counts download states correctly', () => {
        const downloads = [
            makeDownload({ id: 'd1', status: 'Downloading' }),
            makeDownload({ id: 'd2', status: 'Grabbed' }),
            makeDownload({ id: 'd3', status: 'Queued' }),
            makeDownload({ id: 'd4', status: 'Stalled' }),
            makeDownload({ id: 'd5', status: 'Paused' }),
        ];
        expect(calculateDownloadCounts(downloads)).toEqual({ downloading: 1, queued: 2, paused: 1, stalled: 1 });
    });
});

describe('mapDownloadStatusCounts', () => {
    it('maps whole-queue status totals to dashboard states', () => {
        expect(
            mapDownloadStatusCounts({
                Grabbed: 2,
                Queued: 3,
                Downloading: 4,
                Paused: 5,
                Stalled: 6,
            }),
        ).toEqual({ downloading: 4, queued: 5, paused: 5, stalled: 6 });
    });
});

describe('mapActiveDownloads', () => {
    it('returns empty array for empty input', () => {
        expect(mapActiveDownloads([], 5)).toEqual([]);
    });

    it('includes paused downloads and filters out completed downloads', () => {
        const downloads = [
            makeDownload({ id: 'd1', status: 'Paused' }),
            makeDownload({ id: 'd2', status: 'Downloaded' }),
        ];
        expect(mapActiveDownloads(downloads, 5)).toEqual([
            {
                id: 'd1',
                title: 'Test Download',
                progress: 0,
                state: 'paused',
            },
        ]);
    });

    it('maps active downloads with correct shape', () => {
        const downloads = [
            makeDownload({
                id: 'd1',
                title: 'My File',
                contentMetadataTitle: 'Pilot',
                contentHierarchyItemId: 'episode-1',
                contentHierarchyParentId: 'season-1',
                status: 'Downloading',
            }),
        ];
        const result = mapActiveDownloads(downloads, 5);

        expect(result).toHaveLength(1);
        expect(result[0]).toEqual({
            id: 'd1',
            contentId: 'episode-1',
            parentId: 'season-1',
            title: 'Pilot',
            progress: 0,
            state: 'downloading',
        });
    });

    it('respects limit parameter', () => {
        const downloads = [
            makeDownload({ id: 'd1', status: 'Downloading' }),
            makeDownload({ id: 'd2', status: 'Grabbed' }),
            makeDownload({ id: 'd3', status: 'Stalled' }),
        ];
        const result = mapActiveDownloads(downloads, 2);
        expect(result).toHaveLength(2);
    });

    it('carries live progress through to the dashboard widget', () => {
        const downloads = [
            makeDownload({ id: 'd1', status: 'Downloading', progress: 88 }),
            makeDownload({ id: 'd2', status: 'Stalled', progress: 26 }),
        ];
        const result = mapActiveDownloads(downloads, 5);
        expect(result.map((d) => d.progress)).toEqual([88, 26]);
    });

    it('falls back to 0 when progress is null', () => {
        const downloads = [makeDownload({ id: 'd1', status: 'Downloading', progress: null })];
        expect(mapActiveDownloads(downloads, 5)[0].progress).toBe(0);
    });
});

describe('mapJobStatus', () => {
    it('maps Completed to completed', () => {
        expect(mapJobStatus('Completed')).toBe('completed');
    });

    it('maps Queued to running', () => {
        expect(mapJobStatus('Queued')).toBe('running');
    });

    it('maps Running to running', () => {
        expect(mapJobStatus('Running')).toBe('running');
    });

    it('maps CancellationRequested to running', () => {
        expect(mapJobStatus('CancellationRequested')).toBe('running');
    });

    it('maps Failed to failed', () => {
        expect(mapJobStatus('Failed')).toBe('failed');
    });

    it('maps Cancelled to failed', () => {
        expect(mapJobStatus('Cancelled')).toBe('failed');
    });
});

describe('mapRecentJobs', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('returns empty array for empty input', () => {
        expect(mapRecentJobs([], 5)).toEqual([]);
    });

    it('maps job executions with humanized names', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2025-01-01T00:05:00Z'));

        const jobs = [
            makeJobExecution({
                id: 'j1',
                jobType: 'ContentMetadataRefreshItem',
                status: 'Completed',
                startedAt: '2025-01-01T00:00:00Z',
                completedAt: '2025-01-01T00:00:02Z',
            }),
        ];

        const result = mapRecentJobs(jobs, 5);
        expect(result).toHaveLength(1);
        expect(result[0].name).toBe('Content Metadata Refresh Item');
        expect(result[0].status).toBe('completed');
        expect(result[0].duration).toBe('2s');
    });

    it('respects limit parameter', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2025-01-01T01:00:00Z'));

        const jobs = [makeJobExecution({ id: 'j1' }), makeJobExecution({ id: 'j2' }), makeJobExecution({ id: 'j3' })];

        expect(mapRecentJobs(jobs, 2)).toHaveLength(2);
    });

    it('uses createdAt for timestamp when startedAt is missing', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2025-01-01T00:05:00Z'));

        const jobs = [makeJobExecution({ id: 'j1', startedAt: null, createdAt: '2025-01-01T00:00:00Z' })];
        const result = mapRecentJobs(jobs, 5);
        expect(result[0].timestamp).toBe('5m ago');
    });
});

describe('calculateContentCounts', () => {
    it('passes through aggregate counts from the stats endpoint', () => {
        const stats: ApiContentStats = {
            total: 100,
            missing: 25,
            upgradable: 10,
            fulfilled: 60,
            fulfilledExternally: 5,
            partial: 8,
        };

        expect(calculateContentCounts(stats)).toEqual({
            total: 100,
            missing: 25,
            upgradable: 10,
            fulfilled: 60,
            fulfilledExternally: 5,
            partial: 8,
        });
    });

    it('handles an empty library', () => {
        const stats: ApiContentStats = {
            total: 0,
            missing: 0,
            upgradable: 0,
            fulfilled: 0,
            fulfilledExternally: 0,
            partial: 0,
        };

        expect(calculateContentCounts(stats)).toEqual({
            total: 0,
            missing: 0,
            upgradable: 0,
            fulfilled: 0,
            fulfilledExternally: 0,
            partial: 0,
        });
    });
});

describe('mapRecentContent', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('returns empty array for empty input', () => {
        expect(mapRecentContent([], 5)).toEqual([]);
    });

    it('maps hierarchy items to recent content', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2025-01-01T01:00:00Z'));

        const items = [
            makeHierarchyItem({
                id: 'h1',
                parentId: 'root-1',
                name: 'My Show',
                createdAt: '2025-01-01T00:00:00Z',
                contentItem: { id: 'c1', contentHierarchyItemId: 'h1', fulfillment: 'Fulfilled', createdAt: '' },
            }),
        ];

        const result = mapRecentContent(items, 5);
        expect(result).toHaveLength(1);
        expect(result[0].id).toBe('h1');
        expect(result[0].parentId).toBe('root-1');
        expect(result[0].title).toBe('My Show');
        expect(result[0].fulfillment).toBe('fulfilled');
        expect(result[0].addedAt).toBe('1h ago');
    });

    it('uses metadata title when available', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2025-01-01T01:00:00Z'));

        const items = [
            makeHierarchyItem({
                name: 'Fallback Name',
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [{ ordinal: 0, title: 'Metadata Title' }],
                },
            }),
        ];

        const result = mapRecentContent(items, 5);
        expect(result[0].title).toBe('Metadata Title');
    });

    it('sets fulfillment to missing when no contentItem', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2025-01-01T01:00:00Z'));

        const items = [makeHierarchyItem()];
        const result = mapRecentContent(items, 5);
        expect(result[0].fulfillment).toBe('missing');
    });

    it('respects limit parameter', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2025-01-01T01:00:00Z'));

        const items = [makeHierarchyItem({ id: '1' }), makeHierarchyItem({ id: '2' }), makeHierarchyItem({ id: '3' })];
        expect(mapRecentContent(items, 2)).toHaveLength(2);
    });
});

describe('mapUpcomingContent', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2025-06-01T12:00:00Z'));
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    function withPublishDate(date: string, overrides: Partial<ApiContentItemWithMetadata> = {}) {
        return makeContentItemWithMetadata({
            metadataContainer: { id: 'mc-1', contributions: [{ ordinal: 0, publishDate: date }] },
            ...overrides,
        });
    }

    it('returns empty array for empty input', () => {
        expect(mapUpcomingContent([])).toEqual([]);
    });

    it('maps future-dated content items to upcoming items', () => {
        const items = [
            withPublishDate('2025-06-15', {
                id: 'ci-1',
                parentId: 'season-1',
                name: 'Upcoming Episode',
                contentType: 'episodes',
            }),
        ];

        const result = mapUpcomingContent(items);
        expect(result).toHaveLength(1);
        expect(result[0].id).toBe('upcoming-ci-1');
        expect(result[0].contentId).toBe('ci-1');
        expect(result[0].parentId).toBe('season-1');
        expect(result[0].title).toBe('Upcoming Episode');
        expect(result[0].contentType).toBe('episodes');
        expect(result[0].fulfillment).toBe('missing');
    });

    it('uses metadata title when available', () => {
        const items = [
            makeContentItemWithMetadata({
                name: 'Fallback',
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [{ ordinal: 0, title: 'From Metadata', publishDate: '2025-06-15' }],
                },
            }),
        ];

        const result = mapUpcomingContent(items);
        expect(result[0].title).toBe('From Metadata');
    });

    it('uses publish date from metadata as the date', () => {
        const result = mapUpcomingContent([withPublishDate('2025-06-15')]);
        expect(result[0].date).toBe('2025-06-15');
    });

    it('drops items without a publish date', () => {
        const result = mapUpcomingContent([makeContentItemWithMetadata()]);
        expect(result).toEqual([]);
    });

    it('drops already-released items so they are not shown as upcoming', () => {
        const result = mapUpcomingContent([withPublishDate('2003-06-06')]);
        expect(result).toEqual([]);
    });

    it('keeps an item dated exactly today', () => {
        const result = mapUpcomingContent([withPublishDate('2025-06-01')]);
        expect(result).toHaveLength(1);
    });
});
