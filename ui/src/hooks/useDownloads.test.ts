import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider, type Query } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiDelete: vi.fn(),
    buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
        if (!params) {
            return path;
        }
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        const queryString = searchParams.toString();
        return queryString ? `${path}?${queryString}` : path;
    }),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

vi.mock('@/hooks/useInvalidateQueriesOnEvents', () => ({
    useInvalidateQueriesOnEvents: vi.fn(),
}));

import { apiGet, apiPost, apiDelete } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import {
    useDownloads,
    useDownload,
    useDownloadStatusCounts,
    useDownloadStatusTotal,
    isTerminalDownloadStatus,
    terminalDownloadStatuses,
    downloadQueryKeys,
    type ApiDownload,
    type DownloadStatusValue,
    type PagedDownloads,
} from './useDownloads';

const mockApiDownloads: ApiDownload[] = [
    {
        id: 'download-1',
        downloadClientId: 'client-1',
        externalId: 'sab_123',
        title: 'Movie.2024.1080p.BluRay.x264',
        status: 'Downloading',
        createdAt: '2024-01-15T10:00:00Z',
    },
    {
        id: 'download-2',
        downloadClientId: 'client-2',
        externalId: 'qbt_456',
        title: 'Show.S01E01.720p.WEB.x264',
        status: 'Queued',
        createdAt: '2024-01-15T10:05:00Z',
    },
    {
        id: 'download-3',
        downloadClientId: 'client-1',
        externalId: 'sab_789',
        title: 'Another.Movie.2023.2160p.UHD',
        status: 'Downloaded',
        outputPath: '/media/movies/Another Movie (2023)/Another.Movie.2023.2160p.UHD.mkv',
        dateCompleted: '2024-01-15T08:30:00Z',
        createdAt: '2024-01-14T22:00:00Z',
    },
];

const mockPagedResponse: PagedDownloads = {
    items: mockApiDownloads,
    pageNumber: 1,
    pageSize: 25,
    totalItems: 3,
};

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
            mutations: {
                retry: false,
            },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

function createWrapperWithClient() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
            mutations: {
                retry: false,
            },
        },
    });

    const Wrapper = ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);

    return { queryClient, Wrapper };
}

function getRefetchIntervalForDownload(queryClient: QueryClient, id: string) {
    const cache = queryClient.getQueryCache();
    const query = cache.find({ queryKey: downloadQueryKeys.detail(id) });
    expect(query, `query for ${id} should be in cache`).toBeDefined();
    const observer = query!.observers[0];
    expect(observer, `query for ${id} should have an observer`).toBeDefined();
    const refetchInterval = observer.options.refetchInterval as
        | number
        | false
        | ((query: Query<ApiDownload, Error>) => number | false | undefined);
    return refetchInterval;
}

describe('useDownloads', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('fails open on download creation for a filtered list', async () => {
        mockApiGet.mockResolvedValue(mockPagedResponse);

        const { result } = renderHook(() => useDownloads({ status: 'Downloading' }), {
            wrapper: createWrapper(),
        });
        await waitFor(() => expect(result.current.isLoading).toBe(false));
        const listOptions = vi
            .mocked(useInvalidateQueriesOnEvents)
            .mock.calls.find(([, , options]) => options.owner === 'downloads-query')?.[2];

        expect(listOptions?.failOpenEventNames).toEqual(['download.created']);
        expect(listOptions?.unprovableMembership).toBe(true);
    });

    it('fails open on download creation for client-filtered status counts', async () => {
        mockApiGet.mockResolvedValue(mockPagedResponse);

        const { result } = renderHook(() => useDownloadStatusCounts(['Downloading'], 'client-1'), {
            wrapper: createWrapper(),
        });
        await waitFor(() => expect(result.current.isLoading).toBe(false));
        const countOptions = vi
            .mocked(useInvalidateQueriesOnEvents)
            .mock.calls.find(([, , options]) => options.owner === 'downloads:status-counts')?.[2];

        expect(countOptions?.failOpenEventNames).toEqual(['download.created']);
        expect(countOptions?.unprovableMembership).toBe(true);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching downloads', () => {
        it('should fetch downloads from the API', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/downloads');
        });

        it('should return downloads from API', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.downloads).toHaveLength(3);
            expect(result.current.downloads[0].title).toBe('Movie.2024.1080p.BluRay.x264');
            expect(result.current.downloads[1].title).toBe('Show.S01E01.720p.WEB.x264');
            expect(result.current.downloads[2].title).toBe('Another.Movie.2023.2160p.UHD');
        });

        it('should return pagination info', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.pagination).toEqual({
                pageNumber: 1,
                pageSize: 25,
                totalItems: 3,
            });
        });

        it('should pass query parameters to the API', async () => {
            mockApiGet.mockResolvedValue({ ...mockPagedResponse, items: [mockApiDownloads[0]] });

            const { result } = renderHook(() => useDownloads({ status: 'Downloading', downloadClientId: 'client-1' }), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/downloads?status=Downloading&downloadClientId=client-1');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.downloads).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns empty list', async () => {
            mockApiGet.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 25, totalItems: 0 });

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.downloads).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('removing downloads', () => {
        it('should remove a download via DELETE /downloads/{id}', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.removeDownload('download-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/downloads/download-1');
        });

        it('should set isRemoving while removing', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.removeDownload('download-1');
            });

            await waitFor(() => {
                expect(result.current.isRemoving).toBe(true);
            });

            act(() => {
                resolveDelete!();
            });

            await waitFor(() => {
                expect(result.current.isRemoving).toBe(false);
            });
        });
    });

    describe('bulk downloads', () => {
        it.each([
            ['bulkForgetDownloads', '/downloads/bulk/delete'],
            ['bulkRemoveDownloads', '/downloads/bulk/remove'],
            ['bulkPauseDownloads', '/downloads/bulk/pause'],
            ['bulkResumeDownloads', '/downloads/bulk/resume'],
            ['bulkStopDownloads', '/downloads/bulk/stop'],
        ] as const)('posts %s with its filtered selection body', async (action, route) => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPost.mockResolvedValue({ count: 2, error: null });

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            await act(async () => {
                await result.current[action]({
                    filter: { status: 'Downloading', downloadClientId: 'client-1' },
                    excludeIds: ['download-2'],
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith(route, {
                filter: {
                    status: 'Downloading',
                    downloadClientId: 'client-1',
                    excludeIds: ['download-2'],
                },
            });
            expect(mockApiDelete).not.toHaveBeenCalled();
        });
    });

    describe('cancelling downloads', () => {
        it('should cancel a download via POST /downloads/{id}/cancel', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPost.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.cancelDownload('download-1');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/downloads/download-1/cancel');
        });

        it('should set isCancelling while cancelling', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolveCancel: () => void;
            const cancelPromise = new Promise<void>((resolve) => {
                resolveCancel = resolve;
            });
            mockApiPost.mockReturnValue(cancelPromise);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.cancelDownload('download-1');
            });

            await waitFor(() => {
                expect(result.current.isCancelling).toBe(true);
            });

            act(() => {
                resolveCancel!();
            });

            await waitFor(() => {
                expect(result.current.isCancelling).toBe(false);
            });
        });
    });

    describe('pausing downloads', () => {
        it('should pause a download via POST /downloads/{id}/pause', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPost.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.pauseDownload('download-1');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/downloads/download-1/pause');
        });

        it('should set isPausing while pausing', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolvePause: () => void;
            const pausePromise = new Promise<void>((resolve) => {
                resolvePause = resolve;
            });
            mockApiPost.mockReturnValue(pausePromise);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.pauseDownload('download-1');
            });

            await waitFor(() => {
                expect(result.current.isPausing).toBe(true);
            });

            act(() => {
                resolvePause!();
            });

            await waitFor(() => {
                expect(result.current.isPausing).toBe(false);
            });
        });
    });

    describe('resuming downloads', () => {
        it('should resume a download via POST /downloads/{id}/resume', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPost.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.resumeDownload('download-2');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/downloads/download-2/resume');
        });

        it('should set isResuming while resuming', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolveResume: () => void;
            const resumePromise = new Promise<void>((resolve) => {
                resolveResume = resolve;
            });
            mockApiPost.mockReturnValue(resumePromise);

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.resumeDownload('download-2');
            });

            await waitFor(() => {
                expect(result.current.isResuming).toBe(true);
            });

            act(() => {
                resolveResume!();
            });

            await waitFor(() => {
                expect(result.current.isResuming).toBe(false);
            });
        });
    });

    describe('download data transformation', () => {
        it('should preserve all API fields', async () => {
            mockApiGet.mockResolvedValue({ ...mockPagedResponse, items: [mockApiDownloads[2]] });

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const download = result.current.downloads[0];
            expect(download.id).toBe('download-3');
            expect(download.downloadClientId).toBe('client-1');
            expect(download.externalId).toBe('sab_789');
            expect(download.title).toBe('Another.Movie.2023.2160p.UHD');
            expect(download.status).toBe('Downloaded');
            expect(download.outputPath).toBe('/media/movies/Another Movie (2023)/Another.Movie.2023.2160p.UHD.mkv');
            expect(download.dateCompleted).toBe('2024-01-15T08:30:00Z');
            expect(download.createdAt).toBe('2024-01-14T22:00:00Z');
        });

        it('should handle null outputPath', async () => {
            mockApiGet.mockResolvedValue({ ...mockPagedResponse, items: [mockApiDownloads[0]] });

            const { result } = renderHook(() => useDownloads(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.downloads[0].outputPath).toBeUndefined();
        });
    });

    describe('status labels and variants', () => {
        it('should export status labels for all values', async () => {
            const { downloadStatusLabels } = await import('./useDownloads');

            expect(downloadStatusLabels.Grabbed).toBe('Grabbed');
            expect(downloadStatusLabels.Queued).toBe('Queued');
            expect(downloadStatusLabels.Downloading).toBe('Downloading');
            expect(downloadStatusLabels.Paused).toBe('Paused');
            expect(downloadStatusLabels.Stalled).toBe('Stalled');
            expect(downloadStatusLabels.Downloaded).toBe('Downloaded');
            expect(downloadStatusLabels.Importing).toBe('Importing');
            expect(downloadStatusLabels.Imported).toBe('Imported');
            expect(downloadStatusLabels.Failed).toBe('Failed');
            expect(downloadStatusLabels.Cancelled).toBe('Cancelled');
        });

        it('should export status variants for all values', async () => {
            const { downloadStatusVariants } = await import('./useDownloads');

            expect(downloadStatusVariants.Grabbed).toBe('grabbed');
            expect(downloadStatusVariants.Queued).toBe('queued');
            expect(downloadStatusVariants.Downloading).toBe('downloading');
            expect(downloadStatusVariants.Paused).toBe('paused');
            expect(downloadStatusVariants.Stalled).toBe('stalled');
            expect(downloadStatusVariants.Downloaded).toBe('downloaded');
            expect(downloadStatusVariants.Importing).toBe('importing');
            expect(downloadStatusVariants.Imported).toBe('imported');
            expect(downloadStatusVariants.Failed).toBe('failed');
            expect(downloadStatusVariants.Cancelled).toBe('cancelled');
        });
    });
});

describe('useDownload', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch a single download by ID', async () => {
        mockApiGet.mockResolvedValue(mockApiDownloads[0]);

        const { result } = renderHook(() => useDownload('download-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/downloads/download-1');
        expect(result.current.download?.id).toBe('download-1');
        expect(result.current.download?.title).toBe('Movie.2024.1080p.BluRay.x264');
    });

    it('should not fetch when disabled', async () => {
        const { result } = renderHook(() => useDownload('download-1', false), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).not.toHaveBeenCalled();
        });

        expect(result.current.download).toBeNull();
    });

    it('should not fetch when ID is empty', async () => {
        const { result } = renderHook(() => useDownload(''), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).not.toHaveBeenCalled();
        });

        expect(result.current.download).toBeNull();
    });

    it('should return error on failure', async () => {
        mockApiGet.mockRejectedValue(new Error('Not found'));

        const { result } = renderHook(() => useDownload('download-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.download).toBeNull();
        expect(result.current.error).toBeInstanceOf(Error);
    });

    describe('polling gating', () => {
        it('exposes the terminal-status set drawn from the DownloadStatus enum', () => {
            expect(Array.from(terminalDownloadStatuses).sort()).toEqual(['Cancelled', 'Failed', 'Imported']);
        });

        it.each<[string, DownloadStatusValue, boolean]>([
            ['Grabbed', 'Grabbed', false],
            ['Queued', 'Queued', false],
            ['Downloading', 'Downloading', false],
            ['Paused', 'Paused', false],
            ['Stalled', 'Stalled', false],
            ['Downloaded', 'Downloaded', false],
            ['Imported', 'Imported', true],
            ['Failed', 'Failed', true],
            ['Cancelled', 'Cancelled', true],
        ])('isTerminalDownloadStatus(%s) === %s', (_label, status, expected) => {
            expect(isTerminalDownloadStatus(status)).toBe(expected);
        });

        it('isTerminalDownloadStatus(undefined) is false', () => {
            expect(isTerminalDownloadStatus(undefined)).toBe(false);
        });

        it('does not poll in-flight downloads', async () => {
            const inflight: ApiDownload = { ...mockApiDownloads[0], status: 'Downloading' };
            mockApiGet.mockResolvedValue(inflight);

            const { queryClient, Wrapper } = createWrapperWithClient();
            const { result } = renderHook(() => useDownload('download-1'), { wrapper: Wrapper });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const refetchInterval = getRefetchIntervalForDownload(queryClient, 'download-1');
            expect(refetchInterval).toBeUndefined();
        });

        it.each<[string, DownloadStatusValue]>([
            ['Imported', 'Imported'],
            ['Failed', 'Failed'],
        ])('does not poll terminal status %s', async (_label, status) => {
            const terminal: ApiDownload = { ...mockApiDownloads[0], status };
            mockApiGet.mockResolvedValue(terminal);

            const { queryClient, Wrapper } = createWrapperWithClient();
            const { result } = renderHook(() => useDownload('download-1'), { wrapper: Wrapper });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const refetchInterval = getRefetchIntervalForDownload(queryClient, 'download-1');
            expect(refetchInterval).toBeUndefined();
        });
    });
});

describe('useDownloadStatusTotal', () => {
    const mockApiGet = vi.mocked(apiGet);
    const activeStatuses: DownloadStatusValue[] = ['Grabbed', 'Queued', 'Downloading', 'Paused', 'Stalled'];

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('issues a single grouped request and returns the aggregate total', async () => {
        mockApiGet.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 1, totalItems: 6 });

        const { result } = renderHook(() => useDownloadStatusTotal(activeStatuses), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(result.current.total).toBe(6);
        const calls = mockApiGet.mock.calls.map((call) => String(call[0]));
        expect(calls).toHaveLength(1);
        expect(calls[0]).toContain('statuses=');
        expect(calls[0]).toContain('pageSize=1');
        expect(calls.some((url) => /[?&]status=/.test(url))).toBe(false);
    });

    it('subscribes one query key so a status event triggers one refetch, not one per status', () => {
        mockApiGet.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 1, totalItems: 3 });

        renderHook(() => useDownloadStatusTotal(activeStatuses), { wrapper: createWrapper() });

        const call = vi
            .mocked(useInvalidateQueriesOnEvents)
            .mock.calls.find(([, , options]) => options.owner === 'downloads-query');
        expect(call).toBeDefined();
        expect(call![1]).toHaveLength(1);
    });

    it('does not fetch when disabled', async () => {
        renderHook(() => useDownloadStatusTotal(activeStatuses, undefined, false), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(mockApiGet).not.toHaveBeenCalled());
    });

    it('scopes the count to the given download client', async () => {
        mockApiGet.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 1, totalItems: 2 });

        const { result } = renderHook(() => useDownloadStatusTotal(['Grabbed', 'Queued'], 'client-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(String(mockApiGet.mock.calls[0][0])).toContain('downloadClientId=client-1');
    });
});
