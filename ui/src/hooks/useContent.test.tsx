import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import {
    useContentList,
    useContentDetail,
    useContentHierarchy,
    usePendingRefresh,
    useContentTags,
    useRefreshContent,
    useSearchContentReleases,
    useUpdateContent,
    useBulkDeleteContent,
    useBulkPatchContent,
    useContentRelationships,
    useMetadataInspector,
    usePatchMetadataInspector,
    useReorderMetadataContributions,
    useEffectiveRootFolder,
    useEffectiveQualityTierList,
    useSetQualityTierList,
    contentQueryKeys,
} from './useContent';
import { contentReleaseQueryKeys } from './useContentReleases';
import { useEventStream } from './useEventStream';
import { useRootFolders } from './useRootFolders';
import { useQualityTierLists } from './useQualityTierLists';
import { tagQueryKeys } from './useTags';
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

vi.mock('./useEventStream', () => ({
    useEventStream: vi.fn(),
}));

const mockedUseEventStream = vi.mocked(useEventStream);

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
            expect(contentQueryKeys.list({ pageNumber: 1 })).toEqual(['content', 'list', { pageNumber: 1 }]);
            expect(contentQueryKeys.detail('123')).toEqual(['content', 'detail', '123']);
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
                    monitoring: 'Monitored',
                    effectiveMonitoring: 'Monitored',
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
                        fulfillment: 'Fulfilled',
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

            expect(api.apiGet).toHaveBeenCalledWith('/content', { signal: expect.any(AbortSignal) });
            expect(result.current.data).toEqual({
                items: [
                    {
                        id: '11111111-1111-1111-1111-111111111111',
                        title: 'Breaking Bad',
                        posterUrl: 'https://example.com/poster.jpg',
                        level: 'show',
                        levelLabel: 'Show',
                        contentType: 'episodes',
                        monitoring: 'Monitored',
                        effectiveMonitoring: 'Monitored',
                        fulfillment: 'fulfilled',
                        importPending: false,
                        year: 2008,
                        publishDate: '2008-01-20T00:00:00Z',
                        dateAdded: '2024-01-01T00:00:00Z',
                        lastSearched: undefined,
                        acquisitionStatus: undefined,
                        previews: [],
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
                        monitoring: true,
                        filter: 'breaking',
                        pageNumber: 2,
                        pageSize: 50,
                    }),
                { wrapper: createWrapper(queryClient) },
            );

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiGet).toHaveBeenCalledWith(
                '/content?contentType=episodes&monitoring=true&filter=breaking&pageNumber=2&pageSize=50',
                { signal: expect.any(AbortSignal) },
            );
        });

        it('useContentList_KeepsLatestResults_WhenOlderRequestResolvesLast', async () => {
            // Arrange
            let resolveOlder!: (value: typeof mockPagedResponse) => void;
            let resolveLatest!: (value: typeof mockPagedResponse) => void;
            const olderResponse = new Promise<typeof mockPagedResponse>((resolve) => {
                resolveOlder = resolve;
            });
            const latestResponse = new Promise<typeof mockPagedResponse>((resolve) => {
                resolveLatest = resolve;
            });
            vi.mocked(api.apiGet).mockImplementation((url: string) =>
                url.includes('filter=older') ? olderResponse : latestResponse,
            );

            // Act
            const { result, rerender } = renderHook(({ filter }) => useContentList({ filter }), {
                initialProps: { filter: 'older' },
                wrapper: createWrapper(queryClient),
            });
            await waitFor(() =>
                expect(api.apiGet).toHaveBeenCalledWith('/content?filter=older', { signal: expect.any(AbortSignal) }),
            );
            rerender({ filter: 'latest' });
            await waitFor(() =>
                expect(api.apiGet).toHaveBeenCalledWith('/content?filter=latest', { signal: expect.any(AbortSignal) }),
            );

            await act(async () => {
                resolveLatest({
                    ...mockPagedResponse,
                    items: [
                        {
                            ...mockPagedResponse.items[0],
                            metadataContainer: {
                                ...mockPagedResponse.items[0].metadataContainer,
                                contributions: [
                                    {
                                        ...mockPagedResponse.items[0].metadataContainer.contributions[0],
                                        title: 'Latest title',
                                    },
                                ],
                            },
                        },
                    ],
                });
            });
            await waitFor(() => expect(result.current.data?.items[0]?.title).toBe('Latest title'));

            await act(async () => {
                resolveOlder({
                    ...mockPagedResponse,
                    items: [
                        {
                            ...mockPagedResponse.items[0],
                            metadataContainer: {
                                ...mockPagedResponse.items[0].metadataContainer,
                                contributions: [
                                    {
                                        ...mockPagedResponse.items[0].metadataContainer.contributions[0],
                                        title: 'Older title',
                                    },
                                ],
                            },
                        },
                    ],
                });
            });

            // Assert
            expect(result.current.data?.items[0]?.title).toBe('Latest title');
        });

        it('refetches a filtered list exactly once when matching content is created before metadata exists', async () => {
            const newContentId = '44444444-4444-4444-4444-444444444444';
            const emptyResponse = { items: [], pageNumber: 1, pageSize: 25, totalItems: 0 };
            const matchingResponse = {
                ...mockPagedResponse,
                items: [
                    {
                        ...mockPagedResponse.items[0],
                        id: newContentId,
                        contentType: 'movies',
                        level: 'movie',
                    },
                ],
            };
            let listRequestCount = 0;
            vi.mocked(api.apiGet).mockImplementation(async (url: string) => {
                if (url === '/content?contentType=movies') {
                    listRequestCount += 1;
                    return listRequestCount === 1 ? emptyResponse : matchingResponse;
                }
                if (url === `/content/${newContentId}`) {
                    return { id: newContentId };
                }
                throw new Error(`Unexpected request: ${url}`);
            });

            const { result } = renderHook(() => useContentList({ contentType: 'movies' }), {
                wrapper: createWrapper(queryClient),
            });
            await waitFor(() => expect(result.current.isSuccess).toBe(true));
            const contentEventHandler = mockedUseEventStream.mock.calls.find(
                ([, options]) =>
                    Array.isArray(options?.eventName) && options.eventName.includes('content.hierarchyItem.created'),
            )?.[0];

            act(() => {
                contentEventHandler?.({
                    name: 'content.hierarchyItem.created',
                    context: { 'content.hierarchyItemId': newContentId },
                });
            });

            await waitFor(() => expect(result.current.data?.items[0]?.id).toBe(newContentId), { timeout: 2000 });
            expect(listRequestCount).toBe(2);
            expect(api.apiGet).not.toHaveBeenCalledWith(`/content/${newContentId}`);
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
            monitoring: 'Monitored',
            effectiveMonitoring: 'Monitored',
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
                fulfillment: 'Fulfilled',
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
                level: 'movie',
                levelLabel: 'Movie',
                monitoring: 'Monitored',
                effectiveMonitoring: 'Monitored',
                fulfillment: 'fulfilled',
                importPending: false,
                year: 2023,
                publishDate: '2023-07-21T00:00:00Z',
                overview: 'The story of J. Robert Oppenheimer.',
                parentId: undefined,
                path: '/movies/Oppenheimer',
                dateAdded: '2024-01-01T00:00:00Z',
                lastSearched: '2024-01-15T00:00:00Z',
                lastRefreshed: undefined,
                externalIds: [],
                references: [],
                genres: [],
                factValues: {},
                sequenceNumber: undefined,
                customValues: [],
                previews: [],
                images: [
                    {
                        id: '0-https://example.com/poster.jpg',
                        url: 'https://example.com/poster.jpg',
                        type: 'poster',
                        width: undefined,
                        height: undefined,
                        isPrimary: undefined,
                    },
                ],
                mediaFiles: undefined,
                qualityTierListId: undefined,
                contentType: 'movies',
                rootFolderId: undefined,
                contentItemId: '33333333-3333-3333-3333-333333333333',
                acquisitionStatus: undefined,
            });
        });

        it('coalesces canonical dependency reads across query owners', async () => {
            vi.mocked(api.apiGet).mockImplementation((path: string) => {
                if (path === `/content/${mockContentItem.id}`) {
                    return Promise.resolve(mockContentItem);
                }
                if (path === '/root-folders' || path === '/quality-tier-lists') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error(`Unexpected path: ${path}`));
            });

            const { result } = renderHook(
                () => ({
                    hierarchy: useContentHierarchy(mockContentItem.id, undefined, undefined),
                    rootFolder: useEffectiveRootFolder(mockContentItem.id),
                    qualityTierList: useEffectiveQualityTierList(mockContentItem.id),
                    rootFolders: useRootFolders(),
                    qualityTierLists: useQualityTierLists(),
                }),
                { wrapper: createWrapper(queryClient) },
            );

            await waitFor(() => {
                expect(result.current.hierarchy.content.isSuccess).toBe(true);
                expect(result.current.rootFolder.isSuccess).toBe(true);
                expect(result.current.qualityTierList.isSuccess).toBe(true);
                expect(result.current.rootFolders.isLoading).toBe(false);
                expect(result.current.qualityTierLists.isLoading).toBe(false);
            });

            expect(
                vi.mocked(api.apiGet).mock.calls.filter(([path]) => path === `/content/${mockContentItem.id}`),
            ).toHaveLength(1);
            expect(vi.mocked(api.apiGet).mock.calls.filter(([path]) => path === '/root-folders')).toHaveLength(1);
            expect(vi.mocked(api.apiGet).mock.calls.filter(([path]) => path === '/quality-tier-lists')).toHaveLength(1);

            vi.mocked(api.apiGet).mockClear();
            let resolveContentRefresh: ((value: typeof mockContentItem) => void) | undefined;
            vi.mocked(api.apiGet).mockImplementation((path: string) => {
                if (path === `/content/${mockContentItem.id}`) {
                    return new Promise<typeof mockContentItem>((resolve) => {
                        resolveContentRefresh = resolve;
                    });
                }
                if (path === '/root-folders' || path === '/quality-tier-lists') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error(`Unexpected path: ${path}`));
            });
            const event = {
                name: 'content.hierarchyItem.updated',
                context: { 'content.hierarchyItemId': mockContentItem.id },
            };

            act(() => {
                for (const [handler, options] of mockedUseEventStream.mock.calls) {
                    const eventNames = options?.eventName;
                    if (Array.isArray(eventNames) && eventNames.includes(event.name)) {
                        handler(event);
                    }
                }
            });
            await act(() => new Promise((resolve) => setTimeout(resolve, 250)));

            expect(
                vi.mocked(api.apiGet).mock.calls.filter(([path]) => path === `/content/${mockContentItem.id}`),
            ).toHaveLength(1);
            resolveContentRefresh?.(mockContentItem);
        });

        it('keeps each ancestor query on its own id when refetched after an inheritance walk', async () => {
            // The app keeps walked ancestor queries cached; with gcTime 0 they are rebuilt and the refetch boundary is lost.
            queryClient.setQueryDefaults(contentQueryKeys.all, { gcTime: 300_000 });
            const track = {
                ...mockContentItem,
                id: 'track-1',
                name: 'Breed',
                parentId: 'album-1',
                effectiveRootFolderId: 'root-1',
                effectiveQualityTierListId: 'tiers-1',
            };
            const album = { ...mockContentItem, id: 'album-1', name: 'Nevermind', parentId: 'artist-1' };
            const artist = {
                ...mockContentItem,
                id: 'artist-1',
                name: 'Nirvana',
                rootFolderId: 'root-1',
                qualityTierListId: 'tiers-1',
            };
            vi.mocked(api.apiGet).mockImplementation((path: string) => {
                const item = { '/content/track-1': track, '/content/album-1': album, '/content/artist-1': artist }[
                    path
                ];
                if (item) {
                    return Promise.resolve(item);
                }
                if (path === '/root-folders' || path === '/quality-tier-lists') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error(`Unexpected path: ${path}`));
            });

            const { result } = renderHook(
                () => ({
                    rootFolder: useEffectiveRootFolder('track-1'),
                    qualityTierList: useEffectiveQualityTierList('track-1'),
                }),
                { wrapper: createWrapper(queryClient) },
            );
            await waitFor(() => {
                expect(result.current.rootFolder.data?.resolvingHierarchyItemId).toBe('artist-1');
                expect(result.current.qualityTierList.data?.resolvingHierarchyItemId).toBe('artist-1');
            });

            vi.mocked(api.apiGet).mockClear();
            await act(() => queryClient.refetchQueries({ queryKey: ['content', 'detail'] }));

            expect(
                vi
                    .mocked(api.apiGet)
                    .mock.calls.map(([path]) => path)
                    .sort(),
            ).toEqual(['/content/album-1', '/content/artist-1', '/content/track-1']);
            expect(queryClient.getQueryData(contentQueryKeys.detail('album-1'))).toMatchObject({
                id: 'album-1',
                name: 'Nevermind',
            });
            expect(queryClient.getQueryData(contentQueryKeys.detail('artist-1'))).toMatchObject({
                id: 'artist-1',
                name: 'Nirvana',
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

        it('reads the pending-refresh flag from the content detail payload', async () => {
            // Arrange
            vi.mocked(api.apiGet).mockResolvedValue({ ...mockContentItem, pendingMetadataRefresh: true });

            // Act
            const { result } = renderHook(() => usePendingRefresh('11111111-1111-1111-1111-111111111111'), {
                wrapper: createWrapper(queryClient),
            });
            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            // Assert
            expect(api.apiGet).toHaveBeenCalledWith('/content/11111111-1111-1111-1111-111111111111');
            expect(result.current.data).toBe(true);
        });

        it('reports no pending refresh when the payload clears the flag', async () => {
            // Arrange
            vi.mocked(api.apiGet).mockResolvedValue({ ...mockContentItem, pendingMetadataRefresh: false });

            // Act
            const { result } = renderHook(() => usePendingRefresh('11111111-1111-1111-1111-111111111111'), {
                wrapper: createWrapper(queryClient),
            });
            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            // Assert
            expect(result.current.data).toBe(false);
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
        it('starts a refresh metadata job', async () => {
            vi.mocked(api.apiPost).mockResolvedValue('22222222-2222-2222-2222-222222222222');

            const { result } = renderHook(() => useRefreshContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate('11111111-1111-1111-1111-111111111111');

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPost).toHaveBeenCalledWith('/jobs/execute-immediate', {
                jobType: 'ContentMetadataRefreshItem',
                parameters: {
                    contentHierarchyItemId: '11111111-1111-1111-1111-111111111111',
                },
            });
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
        it('searches releases for the content item', async () => {
            vi.mocked(api.apiPost).mockResolvedValue('33333333-3333-3333-3333-333333333333');

            const { result } = renderHook(() => useSearchContentReleases(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({ id: '11111111-1111-1111-1111-111111111111' });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPost).toHaveBeenCalledWith('/jobs/execute-immediate', {
                jobType: 'ReleaseSearchItem',
                parameters: {
                    contentHierarchyItemId: '11111111-1111-1111-1111-111111111111',
                    force: 'true',
                },
            });
        });

        it('enqueues the search as a queued job in queue mode', async () => {
            vi.mocked(api.apiPost).mockResolvedValue('33333333-3333-3333-3333-333333333333');

            const { result } = renderHook(() => useSearchContentReleases(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({ id: '11111111-1111-1111-1111-111111111111', mode: 'Queue' });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPost).toHaveBeenCalledWith('/jobs/enqueue', {
                jobType: 'ReleaseSearchItem',
                parameters: {
                    contentHierarchyItemId: '11111111-1111-1111-1111-111111111111',
                    force: 'true',
                },
                mode: 'Queue',
            });
        });

        it('handles errors', async () => {
            const error = new Error('Search failed');
            vi.mocked(api.apiPost).mockRejectedValue(error);

            const { result } = renderHook(() => useSearchContentReleases(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({ id: '11111111-1111-1111-1111-111111111111' });

            await waitFor(() => expect(result.current.isError).toBe(true));
            expect(result.current.error).toBe(error);
        });

        it('invalidates content and release queries on success', async () => {
            vi.mocked(api.apiPost).mockResolvedValue('33333333-3333-3333-3333-333333333333');
            const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

            const { result } = renderHook(() => useSearchContentReleases(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({ id: '11111111-1111-1111-1111-111111111111' });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            const calls = invalidateSpy.mock.calls.map((c) => c[0]?.queryKey);
            expect(calls).toEqual(
                expect.arrayContaining([
                    contentQueryKeys.detail('11111111-1111-1111-1111-111111111111'),
                    contentQueryKeys.list(),
                    contentReleaseQueryKeys.forContent('11111111-1111-1111-1111-111111111111'),
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
                monitoring: 'Monitored',
                qualityTierListId: 'tier-1',
                rootFolderId: 'folder-1',
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledWith('/content/11111111-1111-1111-1111-111111111111', {
                monitoring: { value: 'Monitored' },
                qualityTierListId: { value: 'tier-1' },
                rootFolderId: { value: 'folder-1' },
            });
        });

        it('invalidates the tag-by-entity and tags-all keys so an added chip appears immediately', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue(undefined);
            const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

            const { result } = renderHook(() => useUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                id: '11111111-1111-1111-1111-111111111111',
                tagsToAdd: ['favourite'],
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            const calls = invalidateSpy.mock.calls.map((c) => c[0]?.queryKey);
            expect(calls).toEqual(
                expect.arrayContaining([
                    contentQueryKeys.detail('11111111-1111-1111-1111-111111111111'),
                    contentQueryKeys.list(),
                    tagQueryKeys.byEntity('11111111-1111-1111-1111-111111111111'),
                    tagQueryKeys.all,
                ]),
            );
        });

        it('sends partial updates with ScalarPatch wrapper', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue(undefined);

            const { result } = renderHook(() => useUpdateContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                id: '11111111-1111-1111-1111-111111111111',
                monitoring: 'Unmonitored',
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledWith('/content/11111111-1111-1111-1111-111111111111', {
                monitoring: { value: 'Unmonitored' },
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
                monitoring: 'Monitored',
            });

            await waitFor(() => expect(result.current.isError).toBe(true));
            expect(result.current.error).toBe(error);
        });
    });
    describe('useBulkPatchContent', () => {
        it('PATCHes the consolidated bulk route with explicit ids and invalidates the content list', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue({ count: 2, error: null });
            const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');

            const { result } = renderHook(() => useBulkPatchContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                ids: ['11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222'],
                patch: { monitoring: { value: 'Unmonitored' } },
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                ids: ['11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222'],
                patch: { monitoring: { value: 'Unmonitored' } },
            });
            expect(result.current.data).toEqual({ count: 2, error: null });
            expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: contentQueryKeys.all });
        });

        it('PATCHes the consolidated bulk route with a magpieql filter for select-across-pages', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue({ count: 250, error: null });

            const { result } = renderHook(() => useBulkPatchContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                filter: 'ParentId null and ContentType = "kind-x"',
                patch: { tags: { add: ['Favorites'] } },
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                filter: 'ParentId null and ContentType = "kind-x"',
                patch: { tags: { add: ['Favorites'] } },
            });
        });
    });

    describe('useBulkDeleteContent', () => {
        it('posts the bulk delete request and invalidates the content list', async () => {
            vi.mocked(api.apiPost).mockResolvedValue({ count: 2, error: null });
            const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');

            const { result } = renderHook(() => useBulkDeleteContent(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({
                ids: ['11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222'],
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPost).toHaveBeenCalledWith('/content/bulk/delete', {
                ids: ['11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222'],
            });
            expect(result.current.data).toEqual({ count: 2, error: null });
            expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: contentQueryKeys.all });
        });
    });

    describe('usePatchMetadataInspector', () => {
        const contentId = '11111111-1111-1111-1111-111111111111';

        it('PATCHes the metadata route and invalidates the inspector after the 204 response', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue(undefined);
            const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');

            const { result } = renderHook(() => usePatchMetadataInspector(contentId), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({ title: { value: 'Operator Title' }, genres: { add: ['Action'] } });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledWith(`/content/${contentId}/metadata`, {
                title: { value: 'Operator Title' },
                genres: { add: ['Action'] },
            });
            expect(invalidateQueries).toHaveBeenCalledWith({
                queryKey: contentQueryKeys.metadataInspector(contentId),
            });
            expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: contentQueryKeys.detail(contentId) });
        });
    });

    describe('useReorderMetadataContributions', () => {
        const contentId = '11111111-1111-1111-1111-111111111111';

        it('PATCHes the complete provider order and invalidates materialized metadata', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue(undefined);
            const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');
            const { result } = renderHook(() => useReorderMetadataContributions(contentId), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate(['contrib-b', 'contrib-a']);

            await waitFor(() => expect(result.current.isSuccess).toBe(true));
            expect(api.apiPatch).toHaveBeenCalledWith(`/content/${contentId}/metadata`, {
                contributionIds: ['contrib-b', 'contrib-a'],
            });
            expect(invalidateQueries).toHaveBeenCalledWith({
                queryKey: contentQueryKeys.metadataInspector(contentId),
            });
            expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: contentQueryKeys.detail(contentId) });
        });
    });

    describe('useMetadataInspector', () => {
        const contentId = '11111111-1111-1111-1111-111111111111';
        const containerA = '44444444-4444-4444-4444-444444444444';
        const containerB = '55555555-5555-5555-5555-555555555555';

        it('resolves distinct materialized and contribution subjects through the contextual relatedContentId query', async () => {
            const materialized = {
                containerId: '22222222-2222-2222-2222-222222222222',
                title: 'The Matrix',
                relationships: [
                    {
                        id: '66666666-6666-6666-6666-666666666666',
                        kind: 'cast',
                        relatedMetadataContainerId: containerA,
                    },
                ],
            };
            const contributions = [
                {
                    id: '33333333-3333-3333-3333-333333333333',
                    ordinal: 0,
                    providerType: 'provider-c',
                    title: 'The Matrix',
                    relationships: [
                        {
                            id: '77777777-7777-7777-7777-777777777777',
                            kind: 'director',
                            relatedMetadataContainerId: containerB,
                        },
                        {
                            id: '88888888-8888-8888-8888-888888888888',
                            kind: 'writer',
                            relatedMetadataContainerId: containerA,
                        },
                    ],
                },
            ];
            const subjects = {
                [containerA]: {
                    subjectId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                    name: 'Keanu Reeves',
                    imageUrl: null,
                },
                [containerB]: {
                    subjectId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                    name: 'Lana Wachowski',
                    imageUrl: null,
                },
            };
            vi.mocked(api.apiGet).mockImplementation(async (url) => {
                if (url === `/content/${contentId}/metadata`) {
                    return materialized;
                }
                if (url === `/content/${contentId}/metadata/contributions`) {
                    return contributions;
                }
                if (url === `/subjects?relatedContentId=${contentId}&pageNumber=1&pageSize=100`) {
                    return {
                        items: [
                            {
                                id: subjects[containerA].subjectId,
                                metadataContainerId: containerA,
                                kind: 'person',
                                createdAt: '2026-01-01T00:00:00Z',
                                metadataContainer: {
                                    id: containerA,
                                    createdAt: '2026-01-01T00:00:00Z',
                                    contributions: [{ id: 'c1', ordinal: 0, locked: false, title: 'Keanu Reeves' }],
                                },
                            },
                            {
                                id: subjects[containerB].subjectId,
                                metadataContainerId: containerB,
                                kind: 'person',
                                createdAt: '2026-01-01T00:00:00Z',
                                metadataContainer: {
                                    id: containerB,
                                    createdAt: '2026-01-01T00:00:00Z',
                                    contributions: [{ id: 'c2', ordinal: 0, locked: false, title: 'Lana Wachowski' }],
                                },
                            },
                        ],
                        pageNumber: 1,
                        pageSize: 100,
                        totalItems: 2,
                    };
                }
                throw new Error(`Unexpected URL: ${url}`);
            });

            const { result } = renderHook(() => useMetadataInspector(contentId), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiGet).toHaveBeenCalledWith(`/content/${contentId}/metadata`);
            expect(api.apiGet).toHaveBeenCalledWith(`/content/${contentId}/metadata/contributions`);
            expect(api.apiGet).toHaveBeenCalledWith(
                `/subjects?relatedContentId=${contentId}&pageNumber=1&pageSize=100`,
            );
            expect(api.apiGet).not.toHaveBeenCalledWith('/subjects?pageNumber=1&pageSize=500');
            const subjectCalls = vi
                .mocked(api.apiGet)
                .mock.calls.filter(([url]) => typeof url === 'string' && url.startsWith('/subjects?'));
            expect(subjectCalls).toHaveLength(1);
            expect(result.current.data).toEqual({ materialized, contributions, subjects, subjectsTruncated: false });
        });

        it('continues paging the contextual result until a referenced subject is resolved', async () => {
            vi.mocked(api.apiGet).mockImplementation(async (url) => {
                if (url === `/content/${contentId}/metadata`) {
                    return {
                        containerId: '22222222-2222-2222-2222-222222222222',
                        relationships: [
                            {
                                id: '66666666-6666-6666-6666-666666666666',
                                kind: 'cast',
                                relatedMetadataContainerId: containerB,
                            },
                        ],
                    };
                }
                if (url === `/content/${contentId}/metadata/contributions`) {
                    return [];
                }
                if (url === `/subjects?relatedContentId=${contentId}&pageNumber=1&pageSize=100`) {
                    return {
                        items: [
                            {
                                id: '77777777-7777-7777-7777-777777777777',
                                metadataContainerId: containerA,
                                kind: 'person',
                                createdAt: '2026-01-01T00:00:00Z',
                            },
                        ],
                        pageNumber: 1,
                        pageSize: 100,
                        totalItems: 2,
                    };
                }
                if (url === `/subjects?relatedContentId=${contentId}&pageNumber=2&pageSize=100`) {
                    return {
                        items: [
                            {
                                id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                                metadataContainerId: containerB,
                                kind: 'person',
                                createdAt: '2026-01-01T00:00:00Z',
                                metadataContainer: {
                                    id: containerB,
                                    createdAt: '2026-01-01T00:00:00Z',
                                    contributions: [{ id: 'c1', ordinal: 0, locked: false, title: 'Lana Wachowski' }],
                                },
                            },
                        ],
                        pageNumber: 2,
                        pageSize: 100,
                        totalItems: 2,
                    };
                }
                throw new Error(`Unexpected URL: ${url}`);
            });

            const { result } = renderHook(() => useMetadataInspector(contentId), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiGet).toHaveBeenCalledWith(
                `/subjects?relatedContentId=${contentId}&pageNumber=1&pageSize=100`,
            );
            expect(api.apiGet).toHaveBeenCalledWith(
                `/subjects?relatedContentId=${contentId}&pageNumber=2&pageSize=100`,
            );
            expect(result.current.data?.subjects[containerB]).toEqual({
                subjectId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                name: 'Lana Wachowski',
                imageUrl: null,
            });
        });

        it('does not fall back to an unfiltered scan when a relationship target is missing', async () => {
            const missingContainer = '99999999-9999-9999-9999-999999999999';
            vi.mocked(api.apiGet).mockImplementation(async (url) => {
                if (url === `/content/${contentId}/metadata`) {
                    return {
                        containerId: '22222222-2222-2222-2222-222222222222',
                        relationships: [
                            {
                                id: '66666666-6666-6666-6666-666666666666',
                                kind: 'cast',
                                relatedMetadataContainerId: missingContainer,
                            },
                        ],
                    };
                }
                if (url === `/content/${contentId}/metadata/contributions`) {
                    return [];
                }
                if (url === `/subjects?relatedContentId=${contentId}&pageNumber=1&pageSize=100`) {
                    return {
                        items: [
                            {
                                id: '77777777-7777-7777-7777-777777777777',
                                metadataContainerId: containerA,
                                kind: 'person',
                                createdAt: '2026-01-01T00:00:00Z',
                            },
                        ],
                        pageNumber: 1,
                        pageSize: 100,
                        totalItems: 1,
                    };
                }
                throw new Error(`Unexpected URL: ${url}`);
            });

            const { result } = renderHook(() => useMetadataInspector(contentId), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            const subjectCalls = vi
                .mocked(api.apiGet)
                .mock.calls.filter(([url]) => typeof url === 'string' && url.startsWith('/subjects?'));
            expect(subjectCalls).toEqual([[`/subjects?relatedContentId=${contentId}&pageNumber=1&pageSize=100`]]);
            expect(result.current.data?.subjects).toEqual({});
        });
    });

    describe('useContentRelationships', () => {
        const contentId = '11111111-1111-1111-1111-111111111111';

        it('derives one resolved relationship per credit when providers repeat it', async () => {
            const relatedContainerId = '22222222-2222-2222-2222-222222222222';
            vi.mocked(api.apiGet).mockImplementation(async (url) => {
                if (url === `/content/${contentId}/metadata`) {
                    return {
                        containerId: '66666666-6666-6666-6666-666666666666',
                        title: 'The Matrix',
                        relationships: [
                            {
                                id: '88888888-8888-8888-8888-888888888888',
                                kind: 'cast',
                                qualifier: 'Neo',
                                ordinal: 3,
                                relatedMetadataContainerId: relatedContainerId,
                            },
                        ],
                    };
                }
                if (url === `/content/${contentId}/metadata/contributions`) {
                    return [
                        {
                            id: '77777777-7777-7777-7777-777777777777',
                            ordinal: 0,
                            providerType: 'provider-c',
                            relationships: [
                                {
                                    id: '88888888-8888-8888-8888-888888888888',
                                    kind: 'cast',
                                    qualifier: 'Neo',
                                    ordinal: 3,
                                    relatedMetadataContainerId: relatedContainerId,
                                },
                            ],
                        },
                        {
                            id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                            ordinal: 1,
                            providerType: 'provider-d',
                            relationships: [
                                {
                                    id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                                    kind: 'cast',
                                    qualifier: 'Neo',
                                    ordinal: 3,
                                    relatedMetadataContainerId: relatedContainerId,
                                },
                            ],
                        },
                    ];
                }
                if (url.startsWith('/subjects?')) {
                    return {
                        items: [
                            {
                                id: '33333333-3333-3333-3333-333333333333',
                                metadataContainerId: relatedContainerId,
                                kind: 'person',
                                createdAt: '2026-01-01T00:00:00Z',
                                metadataContainer: {
                                    id: relatedContainerId,
                                    createdAt: '2026-01-01T00:00:00Z',
                                    contributions: [
                                        {
                                            id: '99999999-9999-9999-9999-999999999999',
                                            ordinal: 0,
                                            locked: false,
                                            title: 'Keanu Reeves',
                                            images: [{ type: 'profile', url: 'https://example.com/keanu.jpg' }],
                                        },
                                    ],
                                },
                            },
                        ],
                        pageNumber: 1,
                        pageSize: 100,
                        totalItems: 1,
                    };
                }
                throw new Error(`Unexpected URL: ${url}`);
            });

            const { result } = renderHook(() => useContentRelationships(contentId), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(api.apiGet).toHaveBeenCalledWith(`/content/${contentId}/metadata`);
            expect(api.apiGet).toHaveBeenCalledWith(`/content/${contentId}/metadata/contributions`);
            expect(api.apiGet).toHaveBeenCalledWith(
                `/subjects?relatedContentId=${contentId}&pageNumber=1&pageSize=100`,
            );
            expect(result.current.data).toEqual([
                {
                    id: '88888888-8888-8888-8888-888888888888',
                    kind: 'cast',
                    qualifier: 'Neo',
                    ordinal: 3,
                    subjectId: '33333333-3333-3333-3333-333333333333',
                    name: 'Keanu Reeves',
                    imageUrl: 'https://example.com/keanu.jpg',
                },
            ]);
        });
    });

    describe('useEffectiveRootFolder', () => {
        const itemId = '11111111-1111-1111-1111-111111111111';
        const parentId = '22222222-2222-2222-2222-222222222222';

        it('terminates the ancestor walk when the parent chain cycles', async () => {
            vi.mocked(api.apiGet).mockImplementation((path: string) => {
                if (path === `/content/${itemId}`) {
                    return Promise.resolve({
                        id: itemId,
                        parentId,
                        rootFolderId: null,
                        effectiveRootFolderId: 'rf-1',
                        name: 'Child',
                    });
                }
                if (path === `/content/${parentId}`) {
                    return Promise.resolve({
                        id: parentId,
                        parentId: itemId,
                        rootFolderId: null,
                        effectiveRootFolderId: 'rf-1',
                        name: 'Parent',
                    });
                }
                if (path === '/root-folders') {
                    return Promise.resolve([{ id: 'rf-1', name: 'Media', path: '/media' }]);
                }
                return Promise.reject(new Error(`unexpected path ${path}`));
            });

            const { result } = renderHook(() => useEffectiveRootFolder(itemId), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(result.current.data).toEqual({
                rootFolderId: 'rf-1',
                rootFolderName: 'Media',
                rootFolderPath: '/media',
                source: 'ContentTypeDefault',
                resolvingHierarchyItemId: null,
                isOverriddenOnItem: false,
            });
            expect(vi.mocked(api.apiGet).mock.calls.filter(([path]) => path === `/content/${parentId}`)).toHaveLength(
                1,
            );
        });

        it('stops the ancestor walk at the depth cap when the parent chain never ends', async () => {
            vi.mocked(api.apiGet).mockImplementation((path: string) => {
                if (path === '/root-folders') {
                    return Promise.resolve([{ id: 'rf-1', name: 'Media', path: '/media' }]);
                }
                const match = /^\/content\/ancestor-(\d+)$/.exec(path);
                if (match) {
                    return Promise.resolve({
                        id: `ancestor-${match[1]}`,
                        parentId: `ancestor-${Number(match[1]) + 1}`,
                        rootFolderId: null,
                        effectiveRootFolderId: 'rf-1',
                        name: `Ancestor ${match[1]}`,
                    });
                }
                return Promise.reject(new Error(`unexpected path ${path}`));
            });

            const { result } = renderHook(() => useEffectiveRootFolder('ancestor-0'), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(result.current.data?.source).toBe('ContentTypeDefault');
            expect(
                vi.mocked(api.apiGet).mock.calls.filter(([path]) => path.startsWith('/content/ancestor-')),
            ).toHaveLength(33);
        });
    });

    describe('useEffectiveQualityTierList', () => {
        const itemId = '11111111-1111-1111-1111-111111111111';

        it('resolves the inherited tier name from the item effective field and the tier lists', async () => {
            vi.mocked(api.apiGet).mockImplementation((path: string) => {
                if (path === `/content/${itemId}`) {
                    return Promise.resolve({
                        id: itemId,
                        parentId: null,
                        qualityTierListId: null,
                        effectiveQualityTierListId: 'tier-video',
                        name: 'The Matrix',
                    });
                }
                if (path === '/quality-tier-lists') {
                    return Promise.resolve([{ id: 'tier-video', name: 'Video' }]);
                }
                return Promise.reject(new Error(`unexpected path ${path}`));
            });

            const { result } = renderHook(() => useEffectiveQualityTierList(itemId), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(result.current.data).toEqual({
                qualityTierListId: 'tier-video',
                qualityTierListName: 'Video',
                source: 'ContentTypeDefaultRootFolder',
                resolvingHierarchyItemId: null,
                resolvingHierarchyItemName: null,
                ownQualityTierListId: null,
            });
        });

        it('reports an explicit override set on the item', async () => {
            vi.mocked(api.apiGet).mockImplementation((path: string) => {
                if (path === `/content/${itemId}`) {
                    return Promise.resolve({
                        id: itemId,
                        parentId: null,
                        qualityTierListId: 'tier-video',
                        effectiveQualityTierListId: 'tier-video',
                        name: 'The Matrix',
                    });
                }
                if (path === '/quality-tier-lists') {
                    return Promise.resolve([{ id: 'tier-video', name: 'Video' }]);
                }
                return Promise.reject(new Error(`unexpected path ${path}`));
            });

            const { result } = renderHook(() => useEffectiveQualityTierList(itemId), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(result.current.data).toEqual({
                qualityTierListId: 'tier-video',
                qualityTierListName: 'Video',
                source: 'ExplicitOnItem',
                resolvingHierarchyItemId: itemId,
                resolvingHierarchyItemName: 'The Matrix',
                ownQualityTierListId: 'tier-video',
            });
        });
    });

    describe('useSetQualityTierList', () => {
        const itemId = '11111111-1111-1111-1111-111111111111';

        it('patches the content item with the ScalarPatch-wrapped tier list id', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue(undefined);

            const { result } = renderHook(() => useSetQualityTierList(), {
                wrapper: createWrapper(queryClient),
            });

            result.current.mutate({ contentHierarchyItemId: itemId, qualityTierListId: 'tier-video' });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(api.apiPatch).toHaveBeenCalledWith(`/content/${itemId}`, {
                qualityTierListId: { value: 'tier-video' },
            });
        });
    });
});
