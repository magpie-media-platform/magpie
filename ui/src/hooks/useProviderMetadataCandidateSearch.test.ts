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

    it('gets candidates with the saved query, content type, and page in the query string', async () => {
        const response: ProviderMetadataCandidateSearchResponse = {
            matches: [{ candidate, inLibrary: false }],
            pageItemCount: 1,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 2, hasMore: true },
        };
        const expected: ProviderMetadataCandidateSearchResult = {
            candidates: [{ ...candidate, inLibrary: false }],
            pageItemCount: 1,
            excludedCount: 0,
            pageInfo: { requestedPage: 2, hasMore: true },
        };
        mockApiGet.mockResolvedValue(response);
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });

        let candidateSearchResult: ProviderMetadataCandidateSearchResult | undefined;
        await act(async () => {
            candidateSearchResult = await result.current.mutateAsync({
                query: 'genre has "Science Fiction"',
                contentType: 'movies',
                page: 2,
            });
        });

        expect(mockApiGet).toHaveBeenCalledTimes(1);
        expect(mockApiGet).toHaveBeenCalledWith(
            '/content-search?query=genre+has+%22Science+Fiction%22&contentType=movies&page=2',
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

    it('omits absent content type and page from the query string', async () => {
        mockApiGet.mockResolvedValue({
            matches: [],
            pageItemCount: 0,
            existsInLibraryCount: 0,
            pageInfo: { requestedPage: 1, hasMore: false },
        });
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useProviderMetadataCandidateSearch(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({ query: 'title contains "Matrix"' });
        });

        expect(mockApiGet).toHaveBeenCalledWith('/content-search?query=title+contains+%22Matrix%22');
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
});
