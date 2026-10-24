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
    useContentCounts,
    useRecentlyFulfilled,
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
            });
        });
    });

    describe('useRecentlyFulfilled', () => {
        beforeEach(() => {
            vi.stubEnv('TZ', 'UTC');
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(new Date('2025-01-08T12:00:00Z'));
        });

        afterEach(() => {
            vi.useRealTimers();
            vi.unstubAllEnvs();
        });

        it('UseRecentlyFulfilled_ReadsTheNextPage_WhenThePageEndsInsideTheWeek', async () => {
            // Arrange
            mockApiGet
                .mockResolvedValueOnce({
                    items: [
                        {
                            id: 'h1',
                            name: 'Item One',
                            createdAt: '2024-06-01T00:00:00Z',
                            contentItem: { fulfillment: 'Fulfilled', fulfilledAt: '2025-01-08T11:00:00Z' },
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 200,
                    totalItems: 10,
                })
                .mockResolvedValueOnce({
                    items: [
                        {
                            id: 'h2',
                            name: 'Item Two',
                            createdAt: '2024-06-01T00:00:00Z',
                            contentItem: { fulfillment: 'Upgradable', fulfilledAt: '2025-01-03T00:00:00Z' },
                        },
                        {
                            id: 'h3',
                            name: 'Item Three',
                            createdAt: '2024-06-01T00:00:00Z',
                            contentItem: { fulfillment: 'Fulfilled', fulfilledAt: '2024-12-20T00:00:00Z' },
                        },
                    ],
                    pageNumber: 2,
                    pageSize: 200,
                    totalItems: 10,
                });

            // Act
            const { result } = renderHook(() => useRecentlyFulfilled(5), { wrapper: createWrapper() });

            // Assert
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });
            expect(mockApiGet.mock.calls).toEqual([
                [
                    '/content?filter=fulfillment+in+%28Fulfilled%2C+Upgradable%29&pageNumber=1&pageSize=200&sortBy=FulfilledAt&sortDirection=Descending',
                ],
                [
                    '/content?filter=fulfillment+in+%28Fulfilled%2C+Upgradable%29&pageNumber=2&pageSize=200&sortBy=FulfilledAt&sortDirection=Descending',
                ],
            ]);
            expect(result.current.data?.today).toBe(1);
            expect(result.current.data?.thisWeek).toBe(2);
            expect(result.current.data?.items.map((item) => item.title)).toEqual([
                'Item One',
                'Item Two',
                'Item Three',
            ]);
        });

        it('UseRecentlyFulfilled_ReadsOnePage_WhenThePageHoldsEveryItem', async () => {
            // Arrange
            mockApiGet.mockResolvedValueOnce({
                items: [
                    {
                        id: 'h1',
                        name: 'Item One',
                        createdAt: '2024-06-01T00:00:00Z',
                        contentItem: { fulfillment: 'Fulfilled', fulfilledAt: '2025-01-08T11:00:00Z' },
                    },
                ],
                pageNumber: 1,
                pageSize: 200,
                totalItems: 1,
            });

            // Act
            const { result } = renderHook(() => useRecentlyFulfilled(5), { wrapper: createWrapper() });

            // Assert
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });
            expect(mockApiGet).toHaveBeenCalledTimes(1);
            expect(result.current.data?.thisWeek).toBe(1);
        });

        it('UseRecentlyFulfilled_CountsEachItemOnce_WhenTheNextPageRepeatsAnItem', async () => {
            // Arrange
            mockApiGet
                .mockResolvedValueOnce({
                    items: [
                        {
                            id: 'h1',
                            name: 'Item One',
                            createdAt: '2024-06-01T00:00:00Z',
                            contentItem: { fulfillment: 'Fulfilled', fulfilledAt: '2025-01-08T11:00:00Z' },
                        },
                        {
                            id: 'h2',
                            name: 'Item Two',
                            createdAt: '2024-06-01T00:00:00Z',
                            contentItem: { fulfillment: 'Fulfilled', fulfilledAt: '2025-01-07T00:00:00Z' },
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 200,
                    totalItems: 10,
                })
                .mockResolvedValueOnce({
                    items: [
                        {
                            id: 'h2',
                            name: 'Item Two',
                            createdAt: '2024-06-01T00:00:00Z',
                            contentItem: { fulfillment: 'Fulfilled', fulfilledAt: '2025-01-07T00:00:00Z' },
                        },
                        {
                            id: 'h3',
                            name: 'Item Three',
                            createdAt: '2024-06-01T00:00:00Z',
                            contentItem: { fulfillment: 'Fulfilled', fulfilledAt: '2024-12-20T00:00:00Z' },
                        },
                    ],
                    pageNumber: 2,
                    pageSize: 200,
                    totalItems: 10,
                });

            // Act
            const { result } = renderHook(() => useRecentlyFulfilled(5), { wrapper: createWrapper() });

            // Assert
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });
            expect(result.current.data?.thisWeek).toBe(2);
            expect(result.current.data?.items.map((item) => item.title)).toEqual([
                'Item One',
                'Item Two',
                'Item Three',
            ]);
        });

        it('UseRecentlyFulfilled_RecountsToday_WhenTheLocalDayChanges', async () => {
            // Arrange
            mockApiGet.mockResolvedValue({
                items: [
                    {
                        id: 'h1',
                        name: 'Item One',
                        createdAt: '2024-06-01T00:00:00Z',
                        contentItem: { fulfillment: 'Fulfilled', fulfilledAt: '2025-01-08T11:00:00Z' },
                    },
                ],
                pageNumber: 1,
                pageSize: 200,
                totalItems: 1,
            });
            const { result, rerender } = renderHook(() => useRecentlyFulfilled(5), { wrapper: createWrapper() });
            await waitFor(() => {
                expect(result.current.data?.today).toBe(1);
            });

            // Act
            vi.setSystemTime(new Date('2025-01-09T01:00:00Z'));
            rerender();

            // Assert
            await waitFor(() => {
                expect(result.current.data?.today).toBe(0);
            });
            expect(result.current.data?.thisWeek).toBe(1);
        });

        it('UseRecentlyFulfilled_StopsReading_WhenThePageEndsWithoutAFulfilledDate', async () => {
            // Arrange
            mockApiGet.mockResolvedValueOnce({
                items: [
                    {
                        id: 'h1',
                        name: 'Item One',
                        createdAt: '2024-06-01T00:00:00Z',
                        contentItem: { fulfillment: 'Fulfilled', fulfilledAt: '2025-01-08T11:00:00Z' },
                    },
                    {
                        id: 'h2',
                        name: 'Item Two',
                        createdAt: '2024-06-01T00:00:00Z',
                        contentItem: { fulfillment: 'Fulfilled', fulfilledAt: null },
                    },
                ],
                pageNumber: 1,
                pageSize: 200,
                totalItems: 10,
            });

            // Act
            const { result } = renderHook(() => useRecentlyFulfilled(5), { wrapper: createWrapper() });

            // Assert
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });
            expect(mockApiGet).toHaveBeenCalledTimes(1);
            expect(result.current.data?.thisWeek).toBe(1);
        });
    });
});
