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

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { useWishlists, type ApiWishlist } from './useWishlists';

const mockApiWishlists: ApiWishlist[] = [
    {
        id: 'wl-1',
        name: 'My Movies',
        contentFilterId: 'filter-1',
        qualityTierListId: 'qtl-1',
        ownerUserId: 'user-1',
        ownerLock: false,
        enabled: true,
        lastSyncedAt: '2024-01-15T10:30:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        contentFilter: { id: 'filter-1', name: 'Action Movies' },
        qualityTierList: { id: 'qtl-1', name: 'HD' },
    },
    {
        id: 'wl-2',
        name: 'Sci-Fi Collection',
        contentFilterId: 'filter-2',
        qualityTierListId: null,
        ownerUserId: 'user-1',
        ownerLock: true,
        enabled: true,
        lastSyncedAt: null,
        createdAt: '2024-01-02T00:00:00Z',
        contentFilter: { id: 'filter-2', name: 'Sci-Fi Filter' },
        qualityTierList: null,
    },
    {
        id: 'wl-3',
        name: 'Family Favorites',
        contentFilterId: 'filter-3',
        qualityTierListId: 'qtl-2',
        ownerUserId: 'user-2',
        ownerLock: false,
        enabled: false,
        lastSyncedAt: '2024-01-14T18:00:00Z',
        createdAt: '2024-01-03T00:00:00Z',
        contentFilter: { id: 'filter-3', name: 'Family Friendly' },
        qualityTierList: { id: 'qtl-2', name: '4K Ultra HD' },
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

describe('useWishlists', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);
    const mockApiPatch = vi.mocked(apiPatch);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching wishlists', () => {
        it('should fetch wishlists from the API', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/wishlists');
        });

        it('should return wishlists from API', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.wishlists).toHaveLength(3);
            expect(result.current.wishlists[0].name).toBe('My Movies');
            expect(result.current.wishlists[1].name).toBe('Sci-Fi Collection');
            expect(result.current.wishlists[2].name).toBe('Family Favorites');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.wishlists).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns nothing', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.wishlists).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('creating wishlists', () => {
        it('should create a wishlist via POST /wishlists', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);
            mockApiPost.mockResolvedValue({
                id: 'wl-4',
                name: 'New Wishlist',
                contentFilterId: 'filter-1',
                qualityTierListId: null,
                ownerUserId: 'user-1',
                ownerLock: false,
                enabled: true,
                createdAt: '2024-01-04T00:00:00Z',
            });

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createWishlist({
                    name: 'New Wishlist',
                    contentFilterId: 'filter-1',
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/wishlists', {
                name: 'New Wishlist',
                contentFilterId: 'filter-1',
            });
        });

        it('passes enabled when creating a wishlist', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);
            mockApiPost.mockResolvedValue({
                id: 'wl-4',
                name: 'New Wishlist',
                contentFilterId: 'filter-1',
                qualityTierListId: null,
                ownerUserId: 'user-1',
                ownerLock: false,
                enabled: false,
                createdAt: '2024-01-04T00:00:00Z',
            });

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createWishlist({
                    name: 'New Wishlist',
                    contentFilterId: 'filter-1',
                    enabled: false,
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/wishlists', {
                name: 'New Wishlist',
                contentFilterId: 'filter-1',
                enabled: false,
            });
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);

            let resolvePost: (value: ApiWishlist) => void;
            const postPromise = new Promise<ApiWishlist>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.createWishlist({
                    name: 'New Wishlist',
                    contentFilterId: 'filter-1',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(true);
            });

            act(() => {
                resolvePost!({
                    id: 'wl-4',
                    name: 'New Wishlist',
                    contentFilterId: 'filter-1',
                    ownerUserId: 'user-1',
                    ownerLock: false,
                    enabled: true,
                    createdAt: '2024-01-04T00:00:00Z',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(false);
            });
        });
    });

    describe('updating wishlists', () => {
        it('should update own wishlist via PATCH /wishlists/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateWishlist('wl-1', {
                    name: { value: 'Updated Movies' },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/wishlists/wl-1', {
                name: { value: 'Updated Movies' },
            });
        });

        it('should update shared wishlist via PATCH /wishlists/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateWishlist('wl-3', {
                    name: { value: 'Updated Family' },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/wishlists/wl-3', {
                name: { value: 'Updated Family' },
            });
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.updateWishlist('wl-1', {
                    name: { value: 'Updated' },
                });
            });

            await waitFor(() => {
                expect(result.current.isUpdating).toBe(true);
            });

            act(() => {
                resolvePatch!();
            });

            await waitFor(() => {
                expect(result.current.isUpdating).toBe(false);
            });
        });
    });

    describe('locking/unlocking wishlists', () => {
        it('should lock a wishlist by patching ownerLock', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleLock('wl-1', true);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/wishlists/wl-1', { ownerLock: { value: true } });
        });

        it('should unlock a wishlist by patching ownerLock', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleLock('wl-2', false);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/wishlists/wl-2', { ownerLock: { value: false } });
        });

        it('should set isLocking while toggling lock', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.toggleLock('wl-1', true);
            });

            await waitFor(() => {
                expect(result.current.isLocking).toBe(true);
            });

            act(() => {
                resolvePatch!();
            });

            await waitFor(() => {
                expect(result.current.isLocking).toBe(false);
            });
        });
    });

    describe('deleting wishlists', () => {
        it('should delete own wishlist via DELETE /wishlists/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteWishlist('wl-1', false);
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/wishlists/wl-1');
        });

        it('should append deleteFilter query param when cascading filter delete', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteWishlist('wl-1', true);
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/wishlists/wl-1?deleteFilter=true');
        });

        it('should delete a visible wishlist via DELETE /wishlists/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteWishlist('wl-3', false);
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/wishlists/wl-3');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiWishlists);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.deleteWishlist('wl-1', false);
            });

            await waitFor(() => {
                expect(result.current.isDeleting).toBe(true);
            });

            act(() => {
                resolveDelete!();
            });

            await waitFor(() => {
                expect(result.current.isDeleting).toBe(false);
            });
        });
    });

    describe('wishlist data transformation', () => {
        it('should preserve all API fields', async () => {
            mockApiGet.mockResolvedValue([mockApiWishlists[0]]);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const wishlist = result.current.wishlists[0];
            expect(wishlist.id).toBe('wl-1');
            expect(wishlist.name).toBe('My Movies');
            expect(wishlist.contentFilterId).toBe('filter-1');
            expect(wishlist.qualityTierListId).toBe('qtl-1');
            expect(wishlist.ownerUserId).toBe('user-1');
            expect(wishlist.ownerLock).toBe(false);
            expect(wishlist.enabled).toBe(true);
            expect(wishlist.lastSyncedAt).toBe('2024-01-15T10:30:00Z');
            expect(wishlist.createdAt).toBe('2024-01-01T00:00:00Z');
            expect(wishlist.contentFilter).toEqual({ id: 'filter-1', name: 'Action Movies' });
            expect(wishlist.qualityTierList).toEqual({ id: 'qtl-1', name: 'HD' });
        });

        it('should handle null qualityTierListId', async () => {
            mockApiGet.mockResolvedValue([mockApiWishlists[1]]);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.wishlists[0].qualityTierListId).toBeNull();
            expect(result.current.wishlists[0].qualityTierList).toBeNull();
        });

        it('should handle null lastSyncedAt', async () => {
            mockApiGet.mockResolvedValue([mockApiWishlists[1]]);

            const { result } = renderHook(() => useWishlists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.wishlists[0].lastSyncedAt).toBeNull();
        });
    });
});
