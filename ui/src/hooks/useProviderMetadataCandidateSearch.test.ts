import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

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
import { useProviderMetadataCandidateSearch } from './useProviderMetadataCandidateSearch';
import type {
    ProviderMetadataCandidate,
    ProviderMetadataCandidateSearchResponse,
    ProviderMetadataCandidateSearchResult,
} from './useProviderMetadataCandidateSearch';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    const Wrapper = ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
    return { Wrapper };
}

describe('useProviderMetadataCandidateSearch', () => {
    const mockApiGet = vi.mocked(apiGet);

    const candidate: ProviderMetadataCandidate = {
        providerType: 'provider-a',
        externalIdSource: 'provider-a.movie',
        externalId: '603',
        contentType: 'movies',
        title: 'The Matrix',
        year: 1999,
    };

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('Search_ReturnsCandidates_WhenSavedQueryIncludesContentTypeAndPage', async () => {
        // Arrange
        const response: ProviderMetadataCandidateSearchResponse = {
            matches: [{ candidate, inLibrary: false }],
            pageItemCount: 1,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 1, hasMore: true },
        };
        const expected: ProviderMetadataCandidateSearchResult = {
            candidates: [{ ...candidate, inLibrary: false }],
            pageItemCount: 1,
            excludedCount: 0,
            pageInfo: { requestedPage: 1, hasMore: true },
        };
        mockApiGet.mockResolvedValue(response);
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });

        // Act
        let candidateSearchResult: ProviderMetadataCandidateSearchResult | undefined;
        await act(async () => {
            candidateSearchResult = await result.current.mutateAsync({
                query: 'genre has "Science Fiction"',
                contentType: 'movies',
                page: 1,
            });
        });

        // Assert
        expect(mockApiGet).toHaveBeenCalledTimes(1);
        expect(mockApiGet).toHaveBeenCalledWith(
            '/content-search?query=genre+has+%22Science+Fiction%22&contentType=movies&page=1',
            expect.objectContaining({ signal: expect.any(AbortSignal) }),
        );
        expect(candidateSearchResult).toEqual(expected);
    });

    it('keeps two searches independent', async () => {
        // Arrange
        mockApiGet.mockResolvedValue({
            matches: [],
            pageItemCount: 0,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 1, hasMore: false },
        });
        const { Wrapper } = createWrapper();
        const { result } = renderHook(
            () => ({ first: useProviderMetadataCandidateSearch(), second: useProviderMetadataCandidateSearch() }),
            { wrapper: Wrapper },
        );
        // Act
        await act(async () => {
            await result.current.first.mutateAsync({ query: 'sample' });
        });
        // Assert
        await waitFor(() => expect(result.current.first.data?.candidates).toEqual([]));
        expect(result.current.second.data).toBeUndefined();
        expect(result.current.second.isIdle).toBe(true);
    });

    it('Search_OmitsOptionalParameters_WhenContentTypeAndPageAreAbsent', async () => {
        // Arrange
        mockApiGet.mockResolvedValue({
            matches: [],
            pageItemCount: 0,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 1, hasMore: false },
        });
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });

        // Act
        await act(async () => {
            await result.current.mutateAsync({ query: 'title contains "Matrix"' });
        });

        // Assert
        expect(mockApiGet).toHaveBeenCalledWith(
            '/content-search?query=title+contains+%22Matrix%22',
            expect.objectContaining({ signal: expect.any(AbortSignal) }),
        );
    });

    it('surfaces an API error to the caller', async () => {
        mockApiGet.mockRejectedValue(new Error('boom'));
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });

        await expect(
            act(async () => {
                await result.current.mutateAsync({ query: 'title contains "Matrix"', contentType: 'movies' });
            }),
        ).rejects.toThrow('boom');
    });

    it('reuses bookmarks for next, previous, and retry without restarting page one', async () => {
        // Arrange
        mockApiGet
            .mockResolvedValueOnce({
                matches: [{ candidate, inLibrary: false }],
                pageItemCount: 1,
                existsInLibraryCount: 0,
                pageInfo: { requestedPage: 1, hasMore: true },
                continuation: 'first',
                nextContinuation: 'second',
            })
            .mockRejectedValueOnce(new Error('response lost'))
            .mockResolvedValue({
                matches: [],
                pageItemCount: 0,
                existsInLibraryCount: 0,
                pageInfo: { requestedPage: 2, hasMore: false },
                continuation: 'second',
            });
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });

        // Act
        await act(async () => {
            await result.current.mutateAsync({ query: 'sample', page: 1 });
        });
        await expect(
            act(async () => {
                await result.current.mutateAsync({ query: 'sample', page: 2 });
            }),
        ).rejects.toThrow('response lost');
        await act(async () => {
            await result.current.mutateAsync({ query: 'sample', page: 2 });
        });
        await act(async () => {
            await result.current.mutateAsync({ query: 'sample', page: 1, resume: true });
        });
        await act(async () => {
            await result.current.mutateAsync({ query: 'sample', page: 1 });
        });

        // Assert
        expect(mockApiGet.mock.calls.map(([url]) => url)).toEqual([
            '/content-search?query=sample&page=1',
            '/content-search?query=sample&page=2&continuation=second',
            '/content-search?query=sample&page=2&continuation=second',
            '/content-search?query=sample&page=1&continuation=first',
            '/content-search?query=sample&page=1',
        ]);
    });

    it('retries page one when the first response never supplied a bookmark', async () => {
        // Arrange
        mockApiGet.mockRejectedValueOnce(new Error('response lost')).mockResolvedValue({
            matches: [],
            pageItemCount: 0,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 1, hasMore: false },
            continuation: 'first',
        });
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });

        // Act
        await expect(
            act(async () => {
                await result.current.mutateAsync({ query: 'sample', page: 1 });
            }),
        ).rejects.toThrow('response lost');
        await act(async () => {
            await result.current.mutateAsync({ query: 'sample', page: 1, resume: true });
        });

        // Assert
        expect(mockApiGet.mock.calls.map(([url]) => url)).toEqual([
            '/content-search?query=sample&page=1',
            '/content-search?query=sample&page=1',
        ]);
    });

    it('does not transfer bookmarks between tabs or after reset', async () => {
        // Arrange
        mockApiGet.mockResolvedValue({
            matches: [],
            pageItemCount: 0,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 1, hasMore: true },
            continuation: 'first',
            nextContinuation: 'second',
        });
        const { Wrapper } = createWrapper();
        const { result } = renderHook(
            () => ({
                first: useProviderMetadataCandidateSearch(),
                second: useProviderMetadataCandidateSearch(),
            }),
            { wrapper: Wrapper },
        );

        // Act
        await act(async () => {
            await result.current.first.mutateAsync({ query: 'sample' });
        });
        await expect(
            act(async () => {
                await result.current.second.mutateAsync({ query: 'sample', page: 2 });
            }),
        ).rejects.toThrow('another search');
        act(() => result.current.first.reset());
        await expect(
            act(async () => {
                await result.current.first.mutateAsync({ query: 'sample', page: 1, resume: true });
            }),
        ).rejects.toThrow('another search');

        // Assert
        expect(mockApiGet).toHaveBeenCalledTimes(1);
    });

    it('Cancel_IgnoresLateResult_WhenFreshSameQuerySearchStarts', async () => {
        // Arrange
        let finishOld!: (response: ProviderMetadataCandidateSearchResponse) => void;
        mockApiGet.mockReturnValueOnce(new Promise((resolve) => (finishOld = resolve))).mockResolvedValueOnce({
            matches: [{ candidate: { ...candidate, externalId: 'fresh' }, inLibrary: false }],
            pageItemCount: 1,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 1, hasMore: false },
        });
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });
        let oldRequest!: Promise<ProviderMetadataCandidateSearchResult>;

        // Act
        act(() => {
            oldRequest = result.current.mutateAsync({ query: 'sample', page: 1 });
        });
        await waitFor(() => expect(result.current.isPending).toBe(true));
        const oldSignal = mockApiGet.mock.calls[0][1]?.signal;
        expect(oldSignal).toBeInstanceOf(AbortSignal);
        act(() => expect(result.current.cancel()).toBeNull());
        expect(oldSignal?.aborted).toBe(true);
        await waitFor(() => expect(result.current.isPending).toBe(false));
        await act(async () => {
            await result.current.mutateAsync({ query: 'sample', page: 1 });
        });
        await act(async () => {
            finishOld({
                matches: [{ candidate: { ...candidate, externalId: 'stale' }, inLibrary: false }],
                pageItemCount: 1,
                existsInLibraryCount: 0,
                pageInfo: { requestedPage: 1, hasMore: true },
                nextContinuation: 'stale-next',
            });
            await expect(oldRequest).rejects.toMatchObject({ name: 'AbortError' });
        });

        // Assert
        expect(result.current.data?.candidates[0].externalId).toBe('fresh');
        expect(result.current.isSuccess).toBe(true);
        expect(mockApiGet.mock.calls.map(([url]) => url)).toEqual([
            '/content-search?query=sample&page=1',
            '/content-search?query=sample&page=1',
        ]);
    });

    it('Cancel_SkipsRequest_WhenSearchHasNotDispatched', async () => {
        // Arrange
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });
        let request!: Promise<ProviderMetadataCandidateSearchResult>;

        // Act
        act(() => {
            request = result.current.mutateAsync({ query: 'sample', page: 1 });
            result.current.cancel();
        });
        await act(async () => {
            await expect(request).rejects.toMatchObject({ name: 'AbortError' });
        });

        // Assert
        expect(mockApiGet).not.toHaveBeenCalled();
        expect(result.current.isPending).toBe(false);
        expect(result.current.data).toBeUndefined();
    });

    it('Unmount_AbortsRequest_WhenConsumerLeavesTree', async () => {
        // Arrange
        let finish!: (response: ProviderMetadataCandidateSearchResponse) => void;
        mockApiGet.mockReturnValueOnce(new Promise((resolve) => (finish = resolve)));
        const { Wrapper } = createWrapper();
        const { result, unmount } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });
        let request!: Promise<ProviderMetadataCandidateSearchResult>;
        act(() => {
            request = result.current.mutateAsync({ query: 'sample', page: 1 });
        });
        await waitFor(() => expect(mockApiGet).toHaveBeenCalledTimes(1));
        const signal = mockApiGet.mock.calls[0][1]?.signal;

        // Act
        unmount();
        await waitFor(() => expect(signal?.aborted).toBe(true));
        await act(async () => {
            finish({
                matches: [{ candidate, inLibrary: false }],
                pageItemCount: 1,
                existsInLibraryCount: 0,
                pageInfo: { requestedPage: 1, hasMore: false },
            });
            await expect(request).rejects.toMatchObject({ name: 'AbortError' });
        });

        // Assert
        expect(signal?.aborted).toBe(true);
    });

    it('Cancel_RetainsDeliveredPageAndBookmark_WhenContinuationIsPending', async () => {
        // Arrange
        let finishPageTwo!: (response: ProviderMetadataCandidateSearchResponse) => void;
        mockApiGet
            .mockResolvedValueOnce({
                matches: [{ candidate, inLibrary: false }],
                pageItemCount: 1,
                existsInLibraryCount: 0,
                pageInfo: { requestedPage: 1, hasMore: true },
                continuation: 'first',
                nextContinuation: 'second',
            })
            .mockReturnValueOnce(new Promise((resolve) => (finishPageTwo = resolve)))
            .mockResolvedValueOnce({
                matches: [{ candidate: { ...candidate, externalId: 'page-two' }, inLibrary: false }],
                pageItemCount: 1,
                existsInLibraryCount: 0,
                pageInfo: { requestedPage: 2, hasMore: false },
                continuation: 'second',
            });
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });
        await act(async () => {
            await result.current.mutateAsync({ query: 'sample', page: 1 });
        });
        let interrupted!: Promise<ProviderMetadataCandidateSearchResult>;

        // Act
        act(() => {
            interrupted = result.current.mutateAsync({ query: 'sample', page: 2, resume: true });
        });
        await waitFor(() => expect(result.current.isPending).toBe(true));
        const pageTwoSignal = mockApiGet.mock.calls[1][1]?.signal;
        act(() => expect(result.current.cancel()).toEqual({ query: 'sample', page: 1 }));
        expect(pageTwoSignal?.aborted).toBe(true);
        await waitFor(() => expect(result.current.isPending).toBe(false));
        expect(result.current.data?.candidates[0].externalId).toBe('603');
        await act(async () => {
            finishPageTwo({
                matches: [{ candidate: { ...candidate, externalId: 'late-page-two' }, inLibrary: false }],
                pageItemCount: 1,
                existsInLibraryCount: 0,
                pageInfo: { requestedPage: 2, hasMore: true },
                nextContinuation: 'wrong-third',
            });
            await expect(interrupted).rejects.toMatchObject({ name: 'AbortError' });
        });
        await act(async () => {
            await result.current.mutateAsync({ query: 'sample', page: 2, resume: true });
        });

        // Assert
        expect(result.current.data?.candidates[0].externalId).toBe('page-two');
        expect(mockApiGet.mock.calls.map(([url]) => url)).toEqual([
            '/content-search?query=sample&page=1',
            '/content-search?query=sample&page=2&continuation=second',
            '/content-search?query=sample&page=2&continuation=second',
        ]);
    });

    it('Cancel_PreservesOtherCaller_WhenCancelledRequestFailsLate', async () => {
        // Arrange
        let failFirst!: (error: Error) => void;
        let finishSecond!: (response: ProviderMetadataCandidateSearchResponse) => void;
        mockApiGet
            .mockReturnValueOnce(new Promise((_, reject) => (failFirst = reject)))
            .mockReturnValueOnce(new Promise((resolve) => (finishSecond = resolve)));
        const { Wrapper } = createWrapper();
        const { result } = renderHook(
            () => ({ first: useProviderMetadataCandidateSearch(), second: useProviderMetadataCandidateSearch() }),
            { wrapper: Wrapper },
        );
        let firstRequest!: Promise<ProviderMetadataCandidateSearchResult>;
        let secondRequest!: Promise<ProviderMetadataCandidateSearchResult>;
        act(() => {
            firstRequest = result.current.first.mutateAsync({ query: 'first', page: 1 });
            secondRequest = result.current.second.mutateAsync({ query: 'second', page: 1 });
        });
        await waitFor(() => expect(result.current.second.isPending).toBe(true));

        // Act
        act(() => {
            result.current.first.cancel();
        });
        const secondSignal = mockApiGet.mock.calls[1][1]?.signal;
        expect(secondSignal?.aborted).toBe(false);
        await act(async () => {
            failFirst(new Error('late failure'));
            await expect(firstRequest).rejects.toThrow('late failure');
            finishSecond({
                matches: [{ candidate, inLibrary: false }],
                pageItemCount: 1,
                existsInLibraryCount: 0,
                pageInfo: { requestedPage: 1, hasMore: false },
            });
            await secondRequest;
        });

        // Assert
        expect(result.current.first.isError).toBe(false);
        expect(result.current.second.data?.candidates[0].externalId).toBe('603');
    });
});
