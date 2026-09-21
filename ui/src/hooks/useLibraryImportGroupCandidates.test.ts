import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('./useEventStream', () => ({
    useEventStream: vi.fn(),
    useGlobalEventStreamConnection: vi.fn(),
}));

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return { ...actual, apiGet: vi.fn() };
});

import { apiGet } from '@/lib/api';
import { useEventStream } from './useEventStream';
import { useLibraryImportGroupCandidates } from './useLibraryImportGroupCandidates';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('useLibraryImportGroupCandidates', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('surfaces query errors', async () => {
        mockApiGet.mockRejectedValue(new Error('boom'));
        const { result } = renderHook(() => useLibraryImportGroupCandidates({ groupKey: '/media/group' }), {
            wrapper: createWrapper(),
        });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(result.current.isError).toBe(true);
        expect(result.current.error).toEqual(new Error('boom'));
    });

    it('coalesces a burst of candidate events into a single refetch', async () => {
        mockApiGet.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 500, totalItems: 0 });
        const { result } = renderHook(() => useLibraryImportGroupCandidates({ groupKey: '/media/group' }), {
            wrapper: createWrapper(),
        });
        await waitFor(() => expect(result.current.isLoading).toBe(false));
        expect(result.current.items).toEqual([]);

        vi.useFakeTimers();
        mockApiGet.mockClear();
        const handler = vi.mocked(useEventStream).mock.calls[0]?.[0];
        for (let index = 0; index < 100; index += 1) {
            handler?.({
                name: 'library.importCandidate.updated',
                context: { 'libraryImportCandidate.id': `candidate-${index}` },
            });
        }
        await act(async () => {
            await vi.advanceTimersByTimeAsync(300);
        });

        expect(mockApiGet).toHaveBeenCalledTimes(1);
    });

    it('does not fetch when params are null', async () => {
        const { result } = renderHook(() => useLibraryImportGroupCandidates(null), { wrapper: createWrapper() });

        await waitFor(() => expect(result.current.isLoading).toBe(false));
        expect(mockApiGet).not.toHaveBeenCalled();
        expect(result.current.items).toEqual([]);
    });

    it('fetches the group key and root folder scoped to the group', async () => {
        mockApiGet.mockResolvedValue({
            items: [
                {
                    id: 'c-1',
                    rootFolderId: 'rf-1',
                    scanRunId: 'sr-1',
                    filePath: '/media/tv/Bluey/c-1.mkv',
                    contentType: 'tv',
                    status: 'Pending' as const,
                    matches: [],
                    matchCount: 0,
                    createdAt: '2024-01-01T00:00:00Z',
                },
            ],
            pageNumber: 1,
            pageSize: 500,
            totalItems: 1,
        });

        const { result } = renderHook(
            () => useLibraryImportGroupCandidates({ groupKey: '/media/tv/Bluey', rootFolderId: 'folder-1' }),
            { wrapper: createWrapper() },
        );

        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(mockApiGet).toHaveBeenCalledWith(
            '/import/candidates?groupKey=%2Fmedia%2Ftv%2FBluey&rootFolderId=folder-1&pageNumber=1&pageSize=500',
        );
        expect(result.current.items).toHaveLength(1);
    });

    it('pages through the full result set when a group has more than one page of candidates', async () => {
        const fullFirstPage = Array.from({ length: 500 }, (_, index) => ({ id: `c-${index}` }));
        mockApiGet
            .mockResolvedValueOnce({ items: fullFirstPage, pageNumber: 1, pageSize: 500, totalItems: 501 })
            .mockResolvedValueOnce({ items: [{ id: 'c-500' }], pageNumber: 2, pageSize: 500, totalItems: 501 });

        const { result } = renderHook(() => useLibraryImportGroupCandidates({ groupKey: '/media/tv/Bluey' }), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(result.current.items).toHaveLength(501));
        expect(mockApiGet).toHaveBeenCalledTimes(2);
        expect(result.current.isTruncated).toBe(false);
    });

    it.each([
        [25000, false],
        [25001, true],
    ])('stops after fifty pages and reports truncation for %i candidates', async (totalItems, isTruncated) => {
        // Arrange
        mockApiGet.mockResolvedValue({ items: Array(500).fill({ id: 'candidate-1' }), totalItems });
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);

        // Act
        const { result } = renderHook(() => useLibraryImportGroupCandidates({ groupKey: '/media/group' }), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        // Assert
        expect(mockApiGet).toHaveBeenCalledTimes(50);
        expect(result.current.items).toHaveLength(25000);
        expect(result.current.isTruncated).toBe(isTruncated);
    });

    it('reports a short response as truncated when the total still includes missing candidates', async () => {
        // Arrange
        mockApiGet.mockResolvedValue({ items: [{ id: 'candidate-1' }], totalItems: 2 });
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);

        // Act
        const { result } = renderHook(() => useLibraryImportGroupCandidates({ groupKey: '/media/group' }), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        // Assert
        expect(mockApiGet).toHaveBeenCalledTimes(1);
        expect(result.current.items).toEqual([{ id: 'candidate-1' }]);
        expect(result.current.isTruncated).toBe(true);
    });
});
