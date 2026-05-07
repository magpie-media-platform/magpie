import { describe, it, expect, vi, afterEach } from 'vitest';

import {
    calculateHealthCounts,
    calculateIndexerHealthCounts,
    mapDownloadState,
    calculateDownloadCounts,
    mapActiveDownloads,
    mapJobStatus,
    formatDuration,
    mapRecentJobs,
    calculateContentCounts,
    mapRecentContent,
    mapUpcomingContent,
    type DashboardIndexer,
    type IntegrationStatus,
} from './dashboardTransforms';
import type { ApiDownload, DownloadStatusValue } from './useDownloads';
import type { ApiJobExecution, JobExecutionStatusValue } from './useJobs';
import type { ApiContentHierarchyItem, ApiContentStats } from '@/types/content';
import type { ApiContentItemWithMetadata } from '@/lib/contentMapping';

function makeHealthItem(status: IntegrationStatus, enabled = true) {
    return { enabled, status };
}

function makeDownload(overrides: Partial<ApiDownload> = {}): ApiDownload {
    return {
        id: 'dl-1',
        downloadClientId: 'dc-1',
        externalId: 'ext-1',
        title: 'Test Download',
        status: 2 as DownloadStatusValue,
        createdAt: '2025-01-01T00:00:00Z',
        ...overrides,
    };
}

function makeJobExecution(overrides: Partial<ApiJobExecution> = {}): ApiJobExecution {
    return {
        id: 'job-1',
        jobType: 'RefreshMetadata',
        scope: 0,
        status: 3 as JobExecutionStatusValue,
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
        monitoring: 0,
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
        monitoring: 0,
        createdAt: '2025-01-01T00:00:00Z',
        ...overrides,
    };
}

describe('calculateHealthCounts', () => {
    it('returns all zeros for empty array', () => {
        expect(calculateHealthCounts([])).toEqual({ healthy: 0, unhealthy: 0, unknown: 0, stale: 0 });
    });

    it('counts healthy items', () => {
        const items = [makeHealthItem(1), makeHealthItem(1)];
        expect(calculateHealthCounts(items)).toEqual({ healthy: 2, unhealthy: 0, unknown: 0, stale: 0 });
    });

    it('counts unhealthy items', () => {
        const items = [makeHealthItem(2)];
        expect(calculateHealthCounts(items)).toEqual({ healthy: 0, unhealthy: 1, unknown: 0, stale: 0 });
    });

    it('counts stale items', () => {
        const items = [makeHealthItem(3)];
        expect(calculateHealthCounts(items)).toEqual({ healthy: 0, unhealthy: 0, unknown: 0, stale: 1 });
    });

    it('counts unknown status (0) as unknown', () => {
        const items = [makeHealthItem(0)];
        expect(calculateHealthCounts(items)).toEqual({ healthy: 0, unhealthy: 0, unknown: 1, stale: 0 });
    });

    it('skips disabled items', () => {
        const items = [makeHealthItem(1, false), makeHealthItem(2, false)];
        expect(calculateHealthCounts(items)).toEqual({ healthy: 0, unhealthy: 0, unknown: 0, stale: 0 });
    });

    it('counts mixed statuses correctly', () => {
        const items = [
            makeHealthItem(1),
            makeHealthItem(2),
            makeHealthItem(3),
            makeHealthItem(0),
            makeHealthItem(1, false),
        ];
        expect(calculateHealthCounts(items)).toEqual({ healthy: 1, unhealthy: 1, unknown: 1, stale: 1 });
    });
});

describe('calculateIndexerHealthCounts', () => {
    it('returns all zeros for empty array', () => {
        expect(calculateIndexerHealthCounts([])).toEqual({ healthy: 0, unhealthy: 0, unknown: 0, stale: 0 });
    });

    it('skips indexers with no connections', () => {
        const indexers: DashboardIndexer[] = [{ connections: [] }, {}];
        expect(calculateIndexerHealthCounts(indexers)).toEqual({ healthy: 0, unhealthy: 0, unknown: 0, stale: 0 });
    });

    it('counts connection statuses across indexers', () => {
        const indexers: DashboardIndexer[] = [
            {
                connections: [
                    { id: 'c1', indexerId: 'i1', name: 'A', enabled: true, status: 1, createdAt: '' },
                    { id: 'c2', indexerId: 'i1', name: 'B', enabled: true, status: 2, createdAt: '' },
                ],
            },
            {
                connections: [
                    { id: 'c3', indexerId: 'i2', name: 'C', enabled: false, status: 1, createdAt: '' },
                    { id: 'c4', indexerId: 'i2', name: 'D', enabled: true, status: 3, createdAt: '' },
                ],
            },
        ];
        expect(calculateIndexerHealthCounts(indexers)).toEqual({ healthy: 1, unhealthy: 1, unknown: 0, stale: 1 });
    });
});

describe('mapDownloadState', () => {
    it('maps status 2 to downloading', () => {
        expect(mapDownloadState(2)).toBe('downloading');
    });

    it('maps status 0 to queued', () => {
        expect(mapDownloadState(0)).toBe('queued');
    });

    it('maps status 1 to queued', () => {
        expect(mapDownloadState(1)).toBe('queued');
    });

    it('maps status 4 to stalled', () => {
        expect(mapDownloadState(4)).toBe('stalled');
    });

    it('maps status 3 (completed) to null', () => {
        expect(mapDownloadState(3)).toBeNull();
    });

    it('maps status 5 to null', () => {
        expect(mapDownloadState(5)).toBeNull();
    });
});

describe('calculateDownloadCounts', () => {
    it('returns all zeros for empty array', () => {
        expect(calculateDownloadCounts([])).toEqual({ downloading: 0, queued: 0, stalled: 0 });
    });

    it('counts download states correctly', () => {
        const downloads = [
            makeDownload({ id: 'd1', status: 2 }),
            makeDownload({ id: 'd2', status: 0 }),
            makeDownload({ id: 'd3', status: 1 }),
            makeDownload({ id: 'd4', status: 4 }),
            makeDownload({ id: 'd5', status: 3 }),
        ];
        expect(calculateDownloadCounts(downloads)).toEqual({ downloading: 1, queued: 2, stalled: 1 });
    });
});

describe('mapActiveDownloads', () => {
    it('returns empty array for empty input', () => {
        expect(mapActiveDownloads([], 5)).toEqual([]);
    });

    it('filters out completed downloads', () => {
        const downloads = [makeDownload({ id: 'd1', status: 3 }), makeDownload({ id: 'd2', status: 5 })];
        expect(mapActiveDownloads(downloads, 5)).toEqual([]);
    });

    it('maps active downloads with correct shape', () => {
        const downloads = [makeDownload({ id: 'd1', title: 'My File', status: 2 })];
        const result = mapActiveDownloads(downloads, 5);

        expect(result).toHaveLength(1);
        expect(result[0]).toEqual({
            id: 'd1',
            title: 'My File',
            progress: 0,
            state: 'downloading',
        });
    });

    it('respects limit parameter', () => {
        const downloads = [
            makeDownload({ id: 'd1', status: 2 }),
            makeDownload({ id: 'd2', status: 0 }),
            makeDownload({ id: 'd3', status: 4 }),
        ];
        const result = mapActiveDownloads(downloads, 2);
        expect(result).toHaveLength(2);
    });
});

describe('mapJobStatus', () => {
    it('maps status 3 to completed', () => {
        expect(mapJobStatus(3)).toBe('completed');
    });

    it('maps status 1 to running', () => {
        expect(mapJobStatus(1)).toBe('running');
    });

    it('maps status 2 to running', () => {
        expect(mapJobStatus(2)).toBe('running');
    });

    it('maps status 6 to running', () => {
        expect(mapJobStatus(6)).toBe('running');
    });

    it('maps status 4 to failed', () => {
        expect(mapJobStatus(4)).toBe('failed');
    });

    it('maps status 5 to failed', () => {
        expect(mapJobStatus(5)).toBe('failed');
    });

    it('maps status 0 to failed (default)', () => {
        expect(mapJobStatus(0)).toBe('failed');
    });
});

describe('formatDuration', () => {
    it('returns undefined when startedAt is null', () => {
        expect(formatDuration(null, '2025-01-01T00:01:00Z')).toBeUndefined();
    });

    it('returns undefined when completedAt is null', () => {
        expect(formatDuration('2025-01-01T00:00:00Z', null)).toBeUndefined();
    });

    it('returns undefined when both are null', () => {
        expect(formatDuration(null, null)).toBeUndefined();
    });

    it('formats sub-second durations in milliseconds', () => {
        expect(formatDuration('2025-01-01T00:00:00.000Z', '2025-01-01T00:00:00.500Z')).toBe('500ms');
    });

    it('formats durations under a minute in seconds', () => {
        expect(formatDuration('2025-01-01T00:00:00Z', '2025-01-01T00:00:45Z')).toBe('45s');
    });

    it('formats durations over a minute with minutes and seconds', () => {
        expect(formatDuration('2025-01-01T00:00:00Z', '2025-01-01T00:02:30Z')).toBe('2m 30s');
    });

    it('formats exact minutes without seconds', () => {
        expect(formatDuration('2025-01-01T00:00:00Z', '2025-01-01T00:05:00Z')).toBe('5m');
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
                jobType: 'RefreshMetadata',
                status: 3,
                startedAt: '2025-01-01T00:00:00Z',
                completedAt: '2025-01-01T00:00:02Z',
            }),
        ];

        const result = mapRecentJobs(jobs, 5);
        expect(result).toHaveLength(1);
        expect(result[0].name).toBe('Refresh Metadata');
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
        };

        expect(calculateContentCounts(stats)).toEqual({
            total: 100,
            missing: 25,
            upgradable: 10,
            fulfilled: 60,
            fulfilledExternally: 5,
        });
    });

    it('handles an empty library', () => {
        const stats: ApiContentStats = {
            total: 0,
            missing: 0,
            upgradable: 0,
            fulfilled: 0,
            fulfilledExternally: 0,
        };

        expect(calculateContentCounts(stats)).toEqual({
            total: 0,
            missing: 0,
            upgradable: 0,
            fulfilled: 0,
            fulfilledExternally: 0,
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
                name: 'My Show',
                createdAt: '2025-01-01T00:00:00Z',
                contentItem: { id: 'c1', contentHierarchyItemId: 'h1', fulfillment: 2, createdAt: '' },
            }),
        ];

        const result = mapRecentContent(items, 5);
        expect(result).toHaveLength(1);
        expect(result[0].id).toBe('h1');
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
    it('returns empty array for empty input', () => {
        expect(mapUpcomingContent([])).toEqual([]);
    });

    it('maps content items to upcoming items', () => {
        const items = [
            makeContentItemWithMetadata({
                id: 'ci-1',
                name: 'Upcoming Episode',
                contentType: 'episodes',
            }),
        ];

        const result = mapUpcomingContent(items);
        expect(result).toHaveLength(1);
        expect(result[0].id).toBe('upcoming-ci-1');
        expect(result[0].contentId).toBe('ci-1');
        expect(result[0].title).toBe('Upcoming Episode');
        expect(result[0].kind).toBe('episode');
        expect(result[0].fulfillment).toBe('missing');
    });

    it('uses metadata title when available', () => {
        const items = [
            makeContentItemWithMetadata({
                name: 'Fallback',
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [{ ordinal: 0, title: 'From Metadata' }],
                },
            }),
        ];

        const result = mapUpcomingContent(items);
        expect(result[0].title).toBe('From Metadata');
    });

    it('uses publish date from metadata as airDate', () => {
        const items = [
            makeContentItemWithMetadata({
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [{ ordinal: 0, publishDate: '2025-06-15' }],
                },
            }),
        ];

        const result = mapUpcomingContent(items);
        expect(result[0].airDate).toBe('2025-06-15');
    });

    it('falls back to current date when no publish date', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2025-06-01T12:00:00Z'));

        const items = [makeContentItemWithMetadata()];
        const result = mapUpcomingContent(items);
        expect(result[0].airDate).toBe('2025-06-01T12:00:00.000Z');
    });
});
