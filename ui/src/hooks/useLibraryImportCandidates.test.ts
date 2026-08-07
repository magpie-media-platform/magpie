import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('./useEventStream', () => ({
    useEventStream: vi.fn(),
    useGlobalEventStreamConnection: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
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
}));

import { apiGet } from '@/lib/api';
import { useEventStream } from './useEventStream';
import {
    useLibraryImportCandidates,
    libraryImportCandidateQueryKeys,
    type PagedLibraryImportCandidates,
} from './useLibraryImportCandidates';

const mockedUseEventStream = vi.mocked(useEventStream);

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

const emptyPage: PagedLibraryImportCandidates = {
    items: [],
    pageNumber: 1,
    pageSize: 25,
    totalItems: 0,
};

describe('useLibraryImportCandidates', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('fetches with no filters when no params supplied', async () => {
        mockApiGet.mockResolvedValue(emptyPage);

        const { result } = renderHook(() => useLibraryImportCandidates(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/import/candidates');
    });

    it('forwards the root folder and pagination params to the URL', async () => {
        mockApiGet.mockResolvedValue(emptyPage);

        const { result } = renderHook(
            () =>
                useLibraryImportCandidates({
                    rootFolderId: 'folder-1',
                    pageNumber: 2,
                    pageSize: 25,
                }),
            { wrapper: createWrapper() },
        );

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/import/candidates?rootFolderId=folder-1&pageNumber=2&pageSize=25');
    });

    it('exposes paginated items and totals', async () => {
        mockApiGet.mockResolvedValue({
            items: [
                {
                    id: 'm-1',
                    rootFolderId: 'rf-1',
                    scanRunId: 'sr-1',
                    filePath: '/media/a.mkv',
                    contentType: 'movie',
                    status: 'Pending' as const,
                    matches: [],
                    matchCount: 0,
                    createdAt: '2024-01-01T00:00:00Z',
                },
            ],
            pageNumber: 1,
            pageSize: 25,
            totalItems: 1,
        });

        const { result } = renderHook(() => useLibraryImportCandidates(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.items).toHaveLength(1);
        expect(result.current.items[0].id).toBe('m-1');
        expect(result.current.pagination).toEqual({
            pageNumber: 1,
            pageSize: 25,
            totalItems: 1,
        });
    });

    it('surfaces query errors', async () => {
        mockApiGet.mockRejectedValue(new Error('boom'));

        const { result } = renderHook(() => useLibraryImportCandidates(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.isError).toBe(true);
        expect(result.current.error).toBeInstanceOf(Error);
    });

    it('coalesces a burst of scan candidate events into a single refetch', async () => {
        mockApiGet.mockResolvedValue(emptyPage);
        const { result } = renderHook(() => useLibraryImportCandidates(), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        vi.useFakeTimers();
        mockApiGet.mockClear();
        const handler = mockedUseEventStream.mock.calls[0]?.[0];
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
        vi.useRealTimers();
    });

    it('exposes stable query keys', () => {
        expect(libraryImportCandidateQueryKeys.all).toEqual(['import/candidates']);
        expect(libraryImportCandidateQueryKeys.list({ rootFolderId: 'rf-1' })).toEqual([
            'import/candidates',
            'list',
            { rootFolderId: 'rf-1' },
        ]);
        expect(libraryImportCandidateQueryKeys.detail('m-1')).toEqual(['import/candidates', 'detail', 'm-1']);
    });
});
