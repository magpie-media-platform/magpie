import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import {
    useContentList,
    useContentDetail,
    useContentChildren,
    useContentTags,
    useRefreshContent,
    useSearchContentReleases,
    useUpdateContent,
    useDeleteContent,
    useBulkUpdateContent,
    contentQueryKeys,
    BulkUpdateError,
} from './useContent';
import * as api from '@/lib/api';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
        apiPatch: vi.fn(),
        apiDelete: vi.fn(),
    };
});

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
                staleTime: 0,
            },
        },
    });
}

function createWrapper(queryClient: QueryClient) {
    return function Wrapper({ children }: { children: ReactNode }) {
        return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
}

describe('useContent hooks', () => {
    let queryClient: QueryClient;

    beforeEach(() => {
        queryClient = createTestQueryClient();
        vi.clearAllMocks();
    });

    afterEach(() => {
        queryClient.clear();
    });

    describe('contentQueryKeys', () => {
        it('generates correct query keys', () => {
            expect(contentQueryKeys.all).toEqual(['content']);
            expect(contentQueryKeys.list()).toEqual(['content', 'list']);
            expect(contentQueryKeys.list({ page: 1 })).toEqual(['content', 'list', { page: 1 }]);
            expect(contentQueryKeys.detail('123')).toEqual(['content', 'detail', '123']);
            expect(contentQueryKeys.children('123')).toEqual([
                'content',
                'children',
                '123',
                { page: undefined, pageSize: undefined },
            ]);
            expect(contentQueryKeys.children('123', 1, 25)).toEqual([
                'content',
                'children',
                '123',
                { page: 1, pageSize: 25 },
            ]);
            expect(contentQueryKeys.tags()).toEqual(['content', 'tags']);
        });
    });

    describe('useContentList', () => {
        const mockPagedResponse = {
            items: [
                {
                    id: '11111111-1111-1111-1111-111111111111',
                    metadataContainerId: '22222222-2222-2222-2222-222222222222',
                    contentType: 'episodes',
                    level: 'show',
                    slug: 'breaking-bad',
                    name: 'Breaking Bad',
                    monitoring: 1,
                    createdAt: '2024-01-01T00:00:00Z',
                    metadataContainer: {
                        id: '22222222-2222-2222-2222-222222222222',
                        contributions: [
                            {
                                ordinal: 0,
                                title: 'Breaking Bad',
                                publishDate: '2008-01-20T00:00:00Z',
                                images: [{ type: 'poster', url: 'https://example.com/poster.jpg' }],
                            },
                        ],
                    },
                    contentItem: {
                        id: '33333333-3333-3333-3333-333333333333',
                        contentHierarchyItemId: '11111111-1111-1111-1111-111111111111',
                        fulfillment: 2,
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                },
            ],
            pageNumber: 1,
            pageSize: 25,
            totalItems: 1,
        };

        it('fetches content list and maps to UI model', async () => {
            vi.mocked(api.apiGet).mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useContentList(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiGet).toHaveBeenCalledWith('/content');
            expect(result.current.data).toEqual({
                items: [
                    {
                        id: '11111111-1111-1111-1111-111111111111',
                        title: 'Breaking Bad',
                        posterUrl: 'https://example.com/poster.jpg',
                        kind: 'episode',
                        monitoring: 'active',
                        fulfillment: 'fulfilled',
                        year: 2008,
                        dateAdded: '2024-01-01T00:00:00Z',
                        lastSearched: undefined,
                        tags: undefined,
                    },
                ],
                pageNumber: 1,
                pageSize: 25,
                totalItems: 1,
            });
        });

        it('applies query parameters to API request', async () => {
            vi.mocked(api.apiGet).mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(
                () =>
                    useContentList({
                        contentType: 'episodes',
                        monitoring: 1,
                        search: 'breaking',
                        page: 2,
                        pageSize: 50,
                    }),
                { wrapper: createWrapper(queryClient) },
            );

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiGet).toHaveBeenCalledWith(
                '/content?contentType=episodes&monitoring=1&search=breaking&page=2&pageSize=50',
            );
        });

        it('can be disabled', () => {
            const { result } = renderHook(() => useContentList(undefined, { enabled: false }), {
                wrapper: createWrapper(queryClient),
            });

            expect(result.current.isFetching).toBe(false);
            expect(api.apiGet).not.toHaveBeenCalled();
        });

        it('handles API errors', async () => {
            const error = new Error('Network error');
            vi.mocked(api.apiGet).mockRejectedValue(error);

            const { result } = renderHook(() => useContentList(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isError).toBe(true));
            expect(result.current.error).toBe(error);
        });
    });

    describe('useContentDetail', () => {
        const mockContentItem = {
            id: '11111111-1111-1111-1111-111111111111',
            metadataContainerId: '22222222-2222-2222-2222-222222222222',
            contentType: 'movies',
            level: 'movie',
            slug: 'oppenheimer',
            name: 'Oppenheimer',
            monitoring: 1,
            path: '/movies/Oppenheimer',
            createdAt: '2024-01-01T00:00:00Z',
            metadataContainer: {
                id: '22222222-2222-2222-2222-222222222222',
                contributions: [
                    {
                        ordinal: 0,
                        title: 'Oppenheimer',
                        overview: 'The story of J. Robert Oppenheimer.',
                        publishDate: '2023-07-21T00:00:00Z',
                        images: [{ type: 'poster', url: 'https://example.com/poster.jpg' }],
                    },
                ],
            },
            contentItem: {
                id: '33333333-3333-3333-3333-333333333333',
                contentHierarchyItemId: '11111111-1111-1111-1111-111111111111',
                fulfillment: 2,
                dateLastSearched: '2024-01-15T00:00:00Z',
                createdAt: '2024-01-01T00:00:00Z',
            },
        };

        it('fetches content detail and maps to UI model', async () => {
            vi.mocked(api.apiGet).mockResolvedValue(mockContentItem);

            const { result } = renderHook(() => useContentDetail('11111111-1111-1111-1111-111111111111'), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiGet).toHaveBeenCalledWith('/content/11111111-1111-1111-1111-111111111111');
            expect(result.current.data).toEqual({
                id: '11111111-1111-1111-1111-111111111111',
                title: 'Oppenheimer',
                posterUrl: 'https://example.com/poster.jpg',
                kind: 'movie',
                monitoring: 'active',
                fulfillment: 'fulfilled',
                year: 2023,
                overview: 'The story of J. Robert Oppenheimer.',
                subjectId: '22222222-2222-2222-2222-222222222222',
                subjectName: 'Oppenheimer',
                parentId: undefined,
                path: '/movies/Oppenheimer',
                dateAdded: '2024-01-01T00:00:00Z',
                lastSearched: '2024-01-15T00:00:00Z',
                lastRefreshed: undefined,
            });
        });

        it('does not fetch when id is undefined', () => {
            const { result } = renderHook(() => useContentDetail(undefined), { wrapper: createWrapper(queryClient) });

            expect(result.current.isFetching).toBe(false);
            expect(api.apiGet).not.toHaveBeenCalled();
        });

        it('handles missing content item', async () => {
            const mockWithoutContentItem = {
                ...mockContentItem,
                contentItem: null,
            };
            vi.mocked(api.apiGet).mockResolvedValue(mockWithoutContentItem);

            const { result } = renderHook(() => useContentDetail('11111111-1111-1111-1111-111111111111'), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(result.current.data?.fulfillment).toBe('missing');
        });
    });

    describe('useContentChildren', () => {
        const mockChildrenResponse = {
            items: [
                {
                    id: 'season-1',
                    metadataContainerId: 'meta-1',
                    contentType: 'episodes',
                    level: 'season',
                    slug: 'season-1',
                    name: 'Season 1',
                    monitoring: 1,
                    createdAt: '2024-01-01T00:00:00Z',
                    metadataContainer: {
                        id: 'meta-1',
                        contributions: [{ ordinal: 0, title: 'Season 1' }],
                    },
                    contentItem: {
                        id: 'content-1',
                        contentHierarchyItemId: 'season-1',
                        fulfillment: 2,
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                },
            ],
            pageNumber: 1,
            pageSize: 25,
            totalItems: 1,
        };

        it('fetches children and maps to UI model', async () => {
            vi.mocked(api.apiGet).mockResolvedValue(mockChildrenResponse);

            const { result } = renderHook(() => useContentChildren('parent-id'), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiGet).toHaveBeenCalledWith('/content/parent-id/children?page=1&pageSize=25');
            expect(result.current.data?.items).toHaveLength(1);
            expect(result.current.data?.items[0].title).toBe('Season 1');
        });

        it('supports pagination parameters', async () => {
            vi.mocked(api.apiGet).mockResolvedValue(mockChildrenResponse);

            const { result } = renderHook(() => useContentChildren('parent-id', 2, 50), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiGet).toHaveBeenCalledWith('/content/parent-id/children?page=2&pageSize=50');
        });

        it('does not fetch when parentId is undefined', () => {
            const { result } = renderHook(() => useContentChildren(undefined), { wrapper: createWrapper(queryClient) });

            expect(result.current.isFetching).toBe(false);
            expect(api.apiGet).not.toHaveBeenCalled();
        });
    });

    describe('useContentTags', () => {
        const mockTags = [
            { id: 'tag-1', name: 'Favorites', color: '#f7768e' },
            { id: 'tag-2', name: 'Watch Later', color: '#7aa2f7' },
        ];

        it('fetches tags from API', async () => {
            vi.mocked(api.apiGet).mockResolvedValue(mockTags);

            const { result } = renderHook(() => useContentTags(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiGet).toHaveBeenCalledWith('/tags');
            expect(result.current.data).toEqual(mockTags);
        });
    });

    describe('useRefreshContent', () => {
        it('calls POST to refresh endpoint', async () => {
            vi.mocked(api.apiPost).mockResolvedValue('22222222-2222-2222-2222-222222222222');

            const { result } = renderHook(() => useRefreshContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate('11111111-1111-1111-1111-111111111111');

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPost).toHaveBeenCalledWith('/content/11111111-1111-1111-1111-111111111111/refresh');
        });

        it('handles errors', async () => {
            const error = new Error('Refresh failed');
            vi.mocked(api.apiPost).mockRejectedValue(error);

            const { result } = renderHook(() => useRefreshContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate('11111111-1111-1111-1111-111111111111');

            await waitFor(() => expect(result.current.isError).toBe(true));
            expect(result.current.error).toBe(error);
        });

        it('invalidates both detail and list queries on success', async () => {
            vi.mocked(api.apiPost).mockResolvedValue('22222222-2222-2222-2222-222222222222');
            const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

            const { result } = renderHook(() => useRefreshContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate('11111111-1111-1111-1111-111111111111');

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            const calls = invalidateSpy.mock.calls.map((c) => c[0]?.queryKey);
            expect(calls).toEqual(
                expect.arrayContaining([
                    contentQueryKeys.detail('11111111-1111-1111-1111-111111111111'),
                    contentQueryKeys.list(),
                ]),
            );
        });
    });

    describe('useSearchContentReleases', () => {
        it('calls POST to search endpoint', async () => {
            vi.mocked(api.apiPost).mockResolvedValue('33333333-3333-3333-3333-333333333333');

            const { result } = renderHook(() => useSearchContentReleases(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate('11111111-1111-1111-1111-111111111111');

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPost).toHaveBeenCalledWith('/content/11111111-1111-1111-1111-111111111111/search');
        });

        it('handles errors', async () => {
            const error = new Error('Search failed');
            vi.mocked(api.apiPost).mockRejectedValue(error);

            const { result } = renderHook(() => useSearchContentReleases(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate('11111111-1111-1111-1111-111111111111');

            await waitFor(() => expect(result.current.isError).toBe(true));
            expect(result.current.error).toBe(error);
        });

        it('invalidates both detail and list queries on success', async () => {
            vi.mocked(api.apiPost).mockResolvedValue('33333333-3333-3333-3333-333333333333');
            const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

            const { result } = renderHook(() => useSearchContentReleases(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate('11111111-1111-1111-1111-111111111111');

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            const calls = invalidateSpy.mock.calls.map((c) => c[0]?.queryKey);
            expect(calls).toEqual(
                expect.arrayContaining([
                    contentQueryKeys.detail('11111111-1111-1111-1111-111111111111'),
                    contentQueryKeys.list(),
                ]),
            );
        });
    });

    describe('useUpdateContent', () => {
        it('calls PATCH to content endpoint with ScalarPatch body', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue(undefined);

            const { result } = renderHook(() => useUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                id: '11111111-1111-1111-1111-111111111111',
                monitoring: 1,
                qualityTierListId: 'tier-1',
                rootFolderId: 'folder-1',
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledWith('/content/11111111-1111-1111-1111-111111111111', {
                monitoring: { value: 1 },
                qualityTierListId: { value: 'tier-1' },
                rootFolderId: { value: 'folder-1' },
            });
        });

        it('sends partial updates with ScalarPatch wrapper', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue(undefined);

            const { result } = renderHook(() => useUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                id: '11111111-1111-1111-1111-111111111111',
                monitoring: 0,
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledWith('/content/11111111-1111-1111-1111-111111111111', {
                monitoring: { value: 0 },
            });
        });

        it('handles errors', async () => {
            const error = new Error('Update failed');
            vi.mocked(api.apiPatch).mockRejectedValue(error);

            const { result } = renderHook(() => useUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                id: '11111111-1111-1111-1111-111111111111',
                monitoring: 1,
            });

            await waitFor(() => expect(result.current.isError).toBe(true));
            expect(result.current.error).toBe(error);
        });
    });

    describe('useBulkUpdateContent', () => {
        it('updates all items and returns success result', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue(undefined);

            const { result } = renderHook(() => useBulkUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                ids: ['id-1', 'id-2', 'id-3'],
                monitoring: 1,
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledTimes(3);
            expect(result.current.data).toEqual({
                total: 3,
                succeeded: 3,
                failed: 0,
                failedIds: [],
            });
        });

        it('continues after partial failures and reports them', async () => {
            vi.mocked(api.apiPatch)
                .mockResolvedValueOnce(undefined)
                .mockRejectedValueOnce(new Error('fail'))
                .mockResolvedValueOnce(undefined);

            const { result } = renderHook(() => useBulkUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                ids: ['id-1', 'id-2', 'id-3'],
                monitoring: 1,
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledTimes(3);
            expect(result.current.data).toEqual({
                total: 3,
                succeeded: 2,
                failed: 1,
                failedIds: ['id-2'],
            });
        });

        it('processes all batches even if one batch has failures', async () => {
            vi.mocked(api.apiPatch)
                .mockResolvedValueOnce(undefined)
                .mockResolvedValueOnce(undefined)
                .mockRejectedValueOnce(new Error('fail'))
                .mockResolvedValueOnce(undefined)
                .mockResolvedValueOnce(undefined)
                .mockResolvedValueOnce(undefined)
                .mockResolvedValueOnce(undefined);

            const { result } = renderHook(() => useBulkUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                ids: ['id-1', 'id-2', 'id-3', 'id-4', 'id-5', 'id-6', 'id-7'],
                monitoring: 1,
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledTimes(7);
            expect(result.current.data).toEqual({
                total: 7,
                succeeded: 6,
                failed: 1,
                failedIds: ['id-3'],
            });
        });

        it('throws BulkUpdateError when all items fail', async () => {
            vi.mocked(api.apiPatch).mockRejectedValue(new Error('fail'));

            const { result } = renderHook(() => useBulkUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                ids: ['id-1', 'id-2'],
                monitoring: 1,
            });

            await waitFor(() => expect(result.current.isError).toBe(true));

            expect(result.current.error).toBeInstanceOf(BulkUpdateError);
            expect(result.current.error).toBeInstanceOf(Error);
            expect((result.current.error as BulkUpdateError).result).toEqual({
                total: 2,
                succeeded: 0,
                failed: 2,
                failedIds: ['id-1', 'id-2'],
            });
        });

        it('returns an empty no-op result when ids array is empty (does not throw)', async () => {
            const { result } = renderHook(() => useBulkUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                ids: [],
                monitoring: 1,
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).not.toHaveBeenCalled();
            expect(result.current.isError).toBe(false);
            expect(result.current.error).toBeNull();
            expect(result.current.data).toEqual({
                total: 0,
                succeeded: 0,
                failed: 0,
                failedIds: [],
            });
        });
    });

    describe('useDeleteContent', () => {
        it('calls DELETE to content endpoint', async () => {
            vi.mocked(api.apiDelete).mockResolvedValue(undefined);

            const { result } = renderHook(() => useDeleteContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate('11111111-1111-1111-1111-111111111111');

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiDelete).toHaveBeenCalledWith('/content/11111111-1111-1111-1111-111111111111');
        });

        it('handles errors', async () => {
            const error = new Error('Delete failed');
            vi.mocked(api.apiDelete).mockRejectedValue(error);

            const { result } = renderHook(() => useDeleteContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate('11111111-1111-1111-1111-111111111111');

            await waitFor(() => expect(result.current.isError).toBe(true));
            expect(result.current.error).toBe(error);
        });
    });
});
