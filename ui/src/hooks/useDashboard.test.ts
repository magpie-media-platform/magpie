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

import { apiGet } from '@/lib/api';
import {
    useIndexerHealth,
    useDownloadClientHealth,
    useMediaServerHealth,
    useActiveDownloads,
    useRecentJobs,
    useContentCounts,
} from './useDashboard';

const mockIndexers = [
    {
        id: '1',
        name: 'Indexer 1',
        providerType: 'newznab',
        connections: [{ id: 'c1', enabled: true, status: 1 }],
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: '2',
        name: 'Indexer 2',
        providerType: 'torznab',
        connections: [{ id: 'c2', enabled: true, status: 1 }],
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: '3',
        name: 'Indexer 3',
        providerType: 'newznab',
        connections: [{ id: 'c3', enabled: true, status: 2 }],
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: '4',
        name: 'Indexer 4',
        providerType: 'torznab',
        connections: [{ id: 'c4', enabled: false, status: 1 }],
        createdAt: '2024-01-01T00:00:00Z',
    },
];

const mockDownloadClients = [
    { id: '1', name: 'SABnzbd', providerType: 'sabnzbd', enabled: true, status: 1, createdAt: '2024-01-01T00:00:00Z' },
    {
        id: '2',
        name: 'qBittorrent',
        providerType: 'qbittorrent',
        enabled: true,
        status: 0,
        createdAt: '2024-01-01T00:00:00Z',
    },
];

const mockMediaServers = [
    { id: '1', name: 'Plex', providerType: 'plex', enabled: true, status: 1, createdAt: '2024-01-01T00:00:00Z' },
];

const mockDownloads = {
    items: [
        {
            id: 'dl-1',
            downloadClientId: '1',
            externalId: 'ext1',
            title: 'Movie 1',
            status: 2,
            createdAt: '2024-01-01T00:00:00Z',
        },
        {
            id: 'dl-2',
            downloadClientId: '1',
            externalId: 'ext2',
            title: 'Movie 2',
            status: 1,
            createdAt: '2024-01-01T00:00:00Z',
        },
        {
            id: 'dl-3',
            downloadClientId: '2',
            externalId: 'ext3',
            title: 'Show S01E01',
            status: 4,
            createdAt: '2024-01-01T00:00:00Z',
        },
        {
            id: 'dl-4',
            downloadClientId: '2',
            externalId: 'ext4',
            title: 'Show S01E02',
            status: 5,
            createdAt: '2024-01-01T00:00:00Z',
        },
    ],
    pageNumber: 1,
    pageSize: 10,
    totalItems: 4,
};

const mockJobs = {
    items: [
        {
            id: 'job-1',
            jobType: 'RefreshMetadata',
            scope: 2,
            status: 3,
            startedAt: '2024-01-01T10:00:00Z',
            completedAt: '2024-01-01T10:02:15Z',
            createdAt: '2024-01-01T10:00:00Z',
        },
        {
            id: 'job-2',
            jobType: 'SearchMissing',
            scope: 2,
            status: 2,
            startedAt: '2024-01-01T10:05:00Z',
            createdAt: '2024-01-01T10:05:00Z',
        },
        {
            id: 'job-3',
            jobType: 'ImportDownloaded',
            scope: 2,
            status: 4,
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
            if (url === '/indexers') {
                return Promise.resolve(mockIndexers);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockDownloadClients);
            }
            if (url === '/media-servers') {
                return Promise.resolve(mockMediaServers);
            }
            if (url.startsWith('/downloads')) {
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

    describe('useIndexerHealth', () => {
        it('returns health counts for indexers', async () => {
            const { result } = renderHook(() => useIndexerHealth(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.data).toEqual({
                healthy: 2,
                unhealthy: 1,
                unknown: 0,
                stale: 0,
            });
        });
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
                unknown: 0,
                stale: 0,
            });
        });
    });

    describe('useActiveDownloads', () => {
        it('returns download counts and items', async () => {
            const { result } = renderHook(() => useActiveDownloads(3), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.data?.counts).toEqual({
                downloading: 1,
                queued: 1,
                stalled: 1,
            });

            expect(result.current.data?.items).toHaveLength(3);
        });
    });

    describe('useRecentJobs', () => {
        it('returns recent jobs with limit', async () => {
            const { result } = renderHook(() => useRecentJobs(2), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.data).toHaveLength(2);
            expect(result.current.data?.[0].name).toBe('Refresh Metadata');
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
            });
        });
    });
});
