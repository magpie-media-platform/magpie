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
import { useWishlistDiscovered } from './useWishlistDiscovered';

const WISHLIST_ID = 'wl-1';

function mockResponse() {
    return {
        page: {
            items: [
                {
                    discoveredId: 'discovered-1',
                    source: 'provider-h',
                    externalId: '1234',
                    title: 'Die Hard',
                    discoveredAt: '2024-01-01T00:00:00Z',
                    linkId: 'ci-1',
                    posterUrl: 'https://example.test/p.jpg',
                    publishDate: '1988-07-15T00:00:00Z',
                    addedAt: '2024-01-02T00:00:00Z',
                    status: 'Found' as const,
                },
            ],
            pageNumber: 1,
            pageSize: 25,
            totalItems: 1,
        },
        counts: {
            totalDiscovered: 5,
            inLibrary: 3,
            notInLibrary: 2,
            missing: 1,
        },
    };
}

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

describe('useWishlistDiscovered', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('maps the page rows and counts from the response envelope', async () => {
        mockApiGet.mockResolvedValue(mockResponse());

        const { result } = renderHook(() => useWishlistDiscovered(WISHLIST_ID, { pageNumber: 1, pageSize: 25 }), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.rows).toHaveLength(1);
        expect(result.current.rows[0].linkId).toBe('ci-1');
        expect(result.current.pagination?.totalItems).toBe(1);
        expect(result.current.rows[0].discoveredId).toBe('discovered-1');
        expect(result.current.counts).toEqual({
            totalDiscovered: 5,
            inLibrary: 3,
            notInLibrary: 2,
            missing: 1,
        });
    });

    it('omits default filters and maps sort/status/inLibrary/search to query params', async () => {
        mockApiGet.mockResolvedValue(mockResponse());

        const { result } = renderHook(
            () =>
                useWishlistDiscovered(WISHLIST_ID, {
                    pageNumber: 2,
                    pageSize: 25,
                    inLibrary: 'NotInLibrary',
                    status: 'Missing',
                    sort: 'PublishDate',
                    sortDescending: true,
                    search: 'hard',
                }),
            { wrapper: createWrapper() },
        );

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        const url = mockApiGet.mock.calls[0][0] as string;
        expect(url).toContain('pageNumber=2');
        expect(url).toContain('pageSize=25');
        expect(url).toContain('inLibrary=false');
        expect(url).toContain('status=Missing');
        expect(url).toContain('sort=PublishDate');
        expect(url).toContain('sortDescending=true');
        expect(url).toContain('search=hard');
    });

    it('drops "all" filters and an empty search from the query string', async () => {
        mockApiGet.mockResolvedValue(mockResponse());

        const { result } = renderHook(
            () =>
                useWishlistDiscovered(WISHLIST_ID, {
                    pageNumber: 1,
                    pageSize: 25,
                    inLibrary: 'All',
                    status: 'All',
                    sort: 'Discovered',
                    search: '   ',
                }),
            { wrapper: createWrapper() },
        );

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        const url = mockApiGet.mock.calls[0][0] as string;
        expect(url).not.toContain('inLibrary=');
        expect(url).not.toContain('status=');
        expect(url).not.toContain('search=');
        expect(url).toContain('sort=Discovered');
    });

    it('does not fetch when wishlistId is undefined', async () => {
        const { result } = renderHook(() => useWishlistDiscovered(undefined), {
            wrapper: createWrapper(),
        });

        expect(result.current.rows).toEqual([]);
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('requests the backend discovered endpoint', async () => {
        mockApiGet.mockResolvedValue(mockResponse());

        const { result } = renderHook(() => useWishlistDiscovered(WISHLIST_ID), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith(`/wishlists/${WISHLIST_ID}/discovered`);
    });

    it('fails open on new content for a filtered discovered list', async () => {
        mockApiGet.mockResolvedValue(mockResponse());

        const { result } = renderHook(
            () => useWishlistDiscovered(WISHLIST_ID, { inLibrary: 'NotInLibrary', status: 'Missing' }),
            { wrapper: createWrapper() },
        );
        await waitFor(() => expect(result.current.isLoading).toBe(false));
        const contentOptions = vi
            .mocked(useInvalidateQueriesOnEvents)
            .mock.calls.filter((events) => events[0].includes('content.hierarchyItem.created'))
            .at(-1)?.[2];

        expect(contentOptions?.failOpenEventNames).toEqual(['content.hierarchyItem.created']);
        expect(contentOptions?.entityIds).toEqual(['ci-1']);
        expect(
            contentOptions?.predicate?.({
                name: 'content.hierarchyItem.updated',
                context: { 'content.hierarchyItemId': 'content-other' },
            }),
        ).toBe(false);
    });
});
