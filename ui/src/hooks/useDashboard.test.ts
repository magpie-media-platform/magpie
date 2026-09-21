import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});

vi.mock('@/hooks/useInvalidateQueriesOnEvents', () => ({
    useInvalidateQueriesOnEvents: vi.fn(),
}));

import { apiGet } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import {
    useDownloadClientHealth,
    useMediaServerHealth,
    useActiveDownloads,
    useRecentJobs,
    useContentCounts,
} from './useDashboard';

const mockDownloadClients = [
    {
        id: '1',
        name: 'SABnzbd',
        providerType: 'sabnzbd',
        enabled: true,
        status: 'Healthy',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: '2',
        name: 'Downloadclient A',
        providerType: 'downloadclient-a',
        enabled: true,
        status: 'Unknown',
        createdAt: '2024-01-01T00:00:00Z',
    },
];

const mockMediaServers = [
    {
        id: '1',
        name: 'Plex',
        providerType: 'plex',
        enabled: true,
        status: 'Healthy',
        createdAt: '2024-01-01T00:00:00Z',
    },
];

const mockDownloads = {
    items: [
        {
            id: 'dl-1',
            downloadClientId: '1',
            externalId: 'ext1',
            title: 'Movie 1',
            status: 'Downloading',
            createdAt: '2024-01-01T00:00:00Z',
        },
        {
            id: 'dl-2',
            downloadClientId: '1',
            externalId: 'ext2',
            title: 'Movie 2',
            status: 'Queued',
            createdAt: '2024-01-01T00:00:00Z',
        },
        {
            id: 'dl-3',
            downloadClientId: '2',
            externalId: 'ext3',
            title: 'Show S01E01',
            status: 'Stalled',
            createdAt: '2024-01-01T00:00:00Z',
        },
        {
            id: 'dl-4',
            downloadClientId: '2',
            externalId: 'ext4',
            title: 'Show S01E02',
            status: 'Paused',
            createdAt: '2024-01-01T00:00:00Z',
        },
        {
            id: 'dl-5',
            downloadClientId: '2',
            externalId: 'ext5',
            title: 'Show S01E03',
            status: 'Grabbed',
            createdAt: '2024-01-01T00:00:00Z',
        },
    ],
    pageNumber: 1,
    pageSize: 10,
    totalItems: 5,
};

const mockDownloadStatusTotals: Record<string, number> = {
    Grabbed: 2,
    Queued: 3,
    Downloading: 4,
    Paused: 5,
    Stalled: 6,
};

const mockJobs = {
    items: [
        {
            id: 'job-1',
            jobType: 'ContentMetadataRefreshItem',
            scope: 'Content',
            status: 'Completed',
            startedAt: '2024-01-01T10:00:00Z',
            completedAt: '2024-01-01T10:02:15Z',
            createdAt: '2024-01-01T10:00:00Z',
        },
        {
            id: 'job-2',
            jobType: 'SearchMissing',
            scope: 'Content',
            status: 'Running',
            startedAt: '2024-01-01T10:05:00Z',
            createdAt: '2024-01-01T10:05:00Z',
        },
        {
            id: 'job-3',
            jobType: 'DownloadImportItem',
            scope: 'Content',
            status: 'Failed',
            startedAt: '2024-01-01T09:00:00Z',
            completedAt: '2024-01-01T09:00:12Z',
            createdAt: '2024-01-01T09:00:00Z',
        },
    ],
    pageNumber: 1,
    pageSize: 10,
    totalItems: 3,
};

const mockContentStats = {
    total: 100,
    missing: 25,
    upgradable: 10,
    fulfilled: 60,
    fulfilledExternally: 5,
    partial: 8,
};

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('useDashboard hooks', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/download-clients') {
                return Promise.resolve(mockDownloadClients);
            }
            if (url === '/media-servers') {
                return Promise.resolve(mockMediaServers);
            }
            if (url.startsWith('/downloads')) {
                const status = new URLSearchParams(url.split('?')[1]).get('status');
                if (status) {
                    return Promise.resolve({
                        items: [],
                        pageNumber: 1,
                        pageSize: 1,
                        totalItems: mockDownloadStatusTotals[status] ?? 0,
                    });
                }
                return Promise.resolve(mockDownloads);
            }
            if (url.startsWith('/jobs')) {
                return Promise.resolve(mockJobs);
            }
            if (url === '/content/stats') {
                return Promise.resolve(mockContentStats);
            }
            return Promise.reject(new Error(`Unexpected URL: ${url}`));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('useDownloadClientHealth', () => {
        it('returns health counts for download clients', async () => {
            const { result } = renderHook(() => useDownloadClientHealth(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.data).toEqual({
                healthy: 1,
                unhealthy: 0,
                degraded: 0,
                stabilizing: 0,
                unknown: 1,
                stale: 0,
            });
        });
    });

    describe('useMediaServerHealth', () => {
        it('returns health counts for media servers', async () => {
            const { result } = renderHook(() => useMediaServerHealth(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.data).toEqual({
                healthy: 1,
                unhealthy: 0,
                degraded: 0,
                stabilizing: 0,
                unknown: 0,
                stale: 0,
            });
        });
    });

    describe('useActiveDownloads', () => {
        it('requests active downloads before pagination and returns whole-queue counts', async () => {
            const { result } = renderHook(() => useActiveDownloads(10), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.data?.counts).toEqual({
                downloading: 4,
                queued: 5,
                paused: 5,
                stalled: 6,
            });

            expect(result.current.data?.items).toHaveLength(5);
            expect(result.current.data?.items.map((item) => item.state)).toEqual([
                'downloading',
                'queued',
                'stalled',
                'paused',
                'queued',
            ]);
            expect(mockApiGet).toHaveBeenCalledWith(
                '/downloads?statuses=Grabbed&statuses=Queued&statuses=Downloading&statuses=Paused&statuses=Stalled&pageNumber=1&pageSize=10&sortBy=CreatedAt&sortDirection=Descending',
            );
        });

        it('invalidates the item page when an uncached download changes active status', () => {
            renderHook(() => useActiveDownloads(10), { wrapper: createWrapper() });

            const invalidationCalls = vi.mocked(useInvalidateQueriesOnEvents).mock.calls;
            const dashboardInvalidation = invalidationCalls.find(
                ([, queryKeys]) => queryKeys[0]?.join(':') === 'dashboard:downloads',
            );
            const countInvalidation = invalidationCalls.find(
                ([, queryKeys]) => queryKeys[0]?.slice(0, 2).join(':') === 'downloads:count',
            );
            const predicate = dashboardInvalidation?.[2]?.predicate;
            const countPredicate = countInvalidation?.[2]?.predicate;
            const statusChangedEvent = {
                name: 'download.updated',
                context: {
                    'download.id': 'not-in-page',
                    'download.changedFields': 'Status',
                },
            };

            expect(predicate?.(statusChangedEvent)).toBe(true);
            expect(countPredicate?.(statusChangedEvent)).toBe(true);
            expect(dashboardInvalidation?.[2]?.owner).toBe('dashboard:downloads');
            expect(countInvalidation?.[2]?.owner).toBe('downloads:status-counts');
            expect(
                predicate?.({
                    name: 'download.updated',
                    context: {
                        'download.id': 'not-in-page',
                        'download.changedFields': 'Progress',
                    },
                }),
            ).toBe(false);
        });
    });

    describe('useRecentJobs', () => {
        it('returns recent jobs with limit', async () => {
            const { result } = renderHook(() => useRecentJobs(2), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.data).toHaveLength(2);
            expect(result.current.data?.[0].name).toBe('Content Metadata Refresh Item');
        });
    });

    describe('useContentCounts', () => {
        it('reads aggregate counts from /content/stats', async () => {
            const { result } = renderHook(() => useContentCounts(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/content/stats');
            expect(result.current.data).toEqual({
                total: 100,
                missing: 25,
                upgradable: 10,
                fulfilled: 60,
                fulfilledExternally: 5,
                partial: 8,
            });
        });
    });
});
