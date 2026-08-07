import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiPatch, apiPost, apiDelete } from '@/lib/api';
import { countConfiguredIndexerInstances, useIndexers, type ApiIndexer, type ApiIndexerFeed } from './useIndexers';

const mockApiIndexers: ApiIndexer[] = [
    {
        id: 'idx-1',
        providerType: 'newznab',
        name: 'NZBgeek',
        description: 'NZBgeek Usenet indexer',
        availability: 'SemiPrivate',
        categories: ['5000', '5010'],
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'idx-2',
        providerType: 'torznab',
        name: 'Jackett',
        description: 'Jackett proxy indexer',
        availability: 'Public',
        createdAt: '2024-01-02T00:00:00Z',
    },
];

const mockApiFeeds: ApiIndexerFeed[] = [
    {
        id: 'feed-1',
        providerType: 'prowlarr',
        name: 'Prowlarr Sync',
        description: 'Automatic indexer sync from Prowlarr',
        enabled: true,
        lastSync: '2024-01-15T09:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'feed-2',
        providerType: 'jackett',
        name: 'Jackett Definitions',
        description: null,
        enabled: false,
        lastSync: null,
        createdAt: '2024-01-02T00:00:00Z',
    },
];

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

describe('countConfiguredIndexerInstances', () => {
    it('sums configured connection counts and ignores catalog-only indexers', () => {
        expect(
            countConfiguredIndexerInstances([
                { ...mockApiIndexers[0], connectionCount: 0 },
                { ...mockApiIndexers[1], connectionCount: 2 },
            ]),
        ).toBe(2);
    });
});

describe('useIndexers', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPatch = vi.mocked(apiPatch);
    const mockApiPost = vi.mocked(apiPost);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching indexers', () => {
        it('should fetch indexers from the API', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve(mockApiIndexers);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve(mockApiFeeds);
                }
                return Promise.reject(new Error('Unknown path'));
            });

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/indexers');
            expect(mockApiGet).toHaveBeenCalledWith('/indexer-feeds');
        });

        it('should return indexers from API', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve(mockApiIndexers);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve(mockApiFeeds);
                }
                return Promise.reject(new Error('Unknown path'));
            });

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.indexers).toHaveLength(2);
            expect(result.current.indexers[0].name).toBe('NZBgeek');
            expect(result.current.indexers[1].name).toBe('Jackett');
        });

        it('should transform feeds to UI format', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve([]);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve(mockApiFeeds);
                }
                return Promise.reject(new Error('Unknown path'));
            });

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.feeds).toHaveLength(2);

            const prowlarrFeed = result.current.feeds.find((f) => f.id === 'feed-1');
            expect(prowlarrFeed).toBeDefined();
            expect(prowlarrFeed?.name).toBe('Prowlarr Sync');
            expect(prowlarrFeed?.provider).toBe('prowlarr');
            expect(prowlarrFeed?.enabled).toBe(true);
            expect(prowlarrFeed?.description).toBe('Automatic indexer sync from Prowlarr');
        });

        it('should return empty arrays on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.indexers).toHaveLength(0);
            expect(result.current.feeds).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty arrays when API returns nothing', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve([]);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error('Unknown path'));
            });

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.indexers).toHaveLength(0);
            expect(result.current.feeds).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('deleting indexers', () => {
        it('should delete an indexer', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve(mockApiIndexers);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error('Unknown path'));
            });
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteIndexer('idx-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/indexers/idx-1');
        });

        it('should set isDeletingIndexer while deleting', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve(mockApiIndexers);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error('Unknown path'));
            });

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.deleteIndexer('idx-1');
            });

            await waitFor(() => {
                expect(result.current.isDeletingIndexer).toBe(true);
            });

            act(() => {
                resolveDelete!();
            });

            await waitFor(() => {
                expect(result.current.isDeletingIndexer).toBe(false);
            });
        });
    });

    describe('feed operations', () => {
        it('should enable a feed', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve([]);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve(mockApiFeeds);
                }
                return Promise.reject(new Error('Unknown path'));
            });
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleFeedEnabled('feed-2', true);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/indexer-feeds/feed-2', { enabled: true });
        });

        it('should disable a feed', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve([]);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve(mockApiFeeds);
                }
                return Promise.reject(new Error('Unknown path'));
            });
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleFeedEnabled('feed-1', false);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/indexer-feeds/feed-1', { enabled: false });
        });

        it('should sync a feed and invalidate the feeds query', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve([]);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve(mockApiFeeds);
                }
                return Promise.reject(new Error('Unknown path'));
            });
            mockApiPost.mockResolvedValue(undefined);

            const queryClient = new QueryClient({
                defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
            });
            const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
            const wrapper = ({ children }: { children: React.ReactNode }) =>
                createElement(QueryClientProvider, { client: queryClient }, children);

            const { result } = renderHook(() => useIndexers(), { wrapper });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.syncFeed('feed-1');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/jobs/execute-immediate', {
                jobType: 'IndexerFeedSyncItem',
                parameters: { feedId: 'feed-1' },
            });
            expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['indexers', 'feeds'] });
        });

        it('should delete a feed', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve([]);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve(mockApiFeeds);
                }
                return Promise.reject(new Error('Unknown path'));
            });
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteFeed('feed-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/indexer-feeds/feed-1');
        });
    });

    describe('feed transformation', () => {
        it('should handle null description', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve([]);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve([mockApiFeeds[1]]);
                }
                return Promise.reject(new Error('Unknown path'));
            });

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.feeds[0].description).toBeNull();
        });

        it('should handle undefined settings', async () => {
            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return Promise.resolve([]);
                }
                if (path === '/indexer-feeds') {
                    return Promise.resolve([
                        {
                            id: 'feed-3',
                            providerType: 'test',
                            name: 'Test Feed',
                            enabled: true,
                            createdAt: '2024-01-01T00:00:00Z',
                        },
                    ]);
                }
                return Promise.reject(new Error('Unknown path'));
            });

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.feeds[0].settings).toEqual({});
        });
    });

    describe('loading states', () => {
        it('should track individual loading states', async () => {
            let resolveIndexers: (value: ApiIndexer[]) => void;
            let resolveFeeds: (value: ApiIndexerFeed[]) => void;

            mockApiGet.mockImplementation((path: string) => {
                if (path === '/indexers') {
                    return new Promise<ApiIndexer[]>((resolve) => {
                        resolveIndexers = resolve;
                    });
                }
                if (path === '/indexer-feeds') {
                    return new Promise<ApiIndexerFeed[]>((resolve) => {
                        resolveFeeds = resolve;
                    });
                }
                return Promise.reject(new Error('Unknown path'));
            });

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);
            expect(result.current.isLoadingIndexers).toBe(true);
            expect(result.current.isLoadingFeeds).toBe(true);

            act(() => {
                resolveIndexers!(mockApiIndexers);
            });

            await waitFor(() => {
                expect(result.current.isLoadingIndexers).toBe(false);
            });

            expect(result.current.isLoading).toBe(true);
            expect(result.current.isLoadingFeeds).toBe(true);

            act(() => {
                resolveFeeds!(mockApiFeeds);
            });

            await waitFor(() => {
                expect(result.current.isLoadingFeeds).toBe(false);
            });

            expect(result.current.isLoading).toBe(false);
        });
    });
});
