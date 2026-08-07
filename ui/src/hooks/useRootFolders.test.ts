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
import { useRootFolders, type ApiRootFolder } from './useRootFolders';

const mockApiFolders: ApiRootFolder[] = [
    {
        id: 'folder-1',
        name: 'Movies',
        path: '/media/movies',
        contentType: 'ct-movies',
        isDefault: true,
        defaultMonitoring: true,
        upgradeFileHandling: 'Replace',
        defaults: { movie: { qualityTierListId: 'quality-default' } },
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'folder-2',
        name: 'TV Shows',
        path: '/media/tv',
        contentType: 'ct-shows',
        isDefault: true,
        defaultMonitoring: true,
        upgradeFileHandling: 'Replace',
        defaults: { series: { qualityTierListId: 'quality-default' } },
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'folder-3',
        name: 'Music',
        path: '/media/music',
        contentType: 'ct-music',
        isDefault: false,
        defaultMonitoring: true,
        upgradeFileHandling: 'Replace',
        defaults: { album: { qualityTierListId: 'quality-default' } },
        createdAt: '2024-01-03T00:00:00Z',
        modifiedAt: '2024-01-10T00:00:00Z',
        modifiedByUserId: 'user-123',
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

describe('useRootFolders', () => {
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

    describe('fetching folders', () => {
        it('should fetch root folders from the API', async () => {
            mockApiGet.mockResolvedValue(mockApiFolders);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/root-folders');
        });

        it('should return folders from API', async () => {
            mockApiGet.mockResolvedValue(mockApiFolders);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.folders).toHaveLength(3);
            expect(result.current.folders[0].name).toBe('Movies');
            expect(result.current.folders[1].name).toBe('TV Shows');
            expect(result.current.folders[2].name).toBe('Music');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.folders).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns nothing', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.folders).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('creating folders', () => {
        it('should create a folder via POST /root-folders', async () => {
            mockApiGet.mockResolvedValue(mockApiFolders);
            mockApiPost.mockResolvedValue({
                id: 'folder-4',
                name: 'Books',
                path: '/media/books',
                contentType: 'ct-books',
                isDefault: false,
                defaultMonitoring: true,
                upgradeFileHandling: 'Replace',
                defaults: { book: { qualityTierListId: 'quality-default' } },
                createdAt: '2024-01-04T00:00:00Z',
            });

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createFolder({
                    name: 'Books',
                    path: '/media/books',
                    contentType: 'ct-books',
                    defaultMonitoring: true,
                    upgradeFileHandling: 'Replace',
                    defaults: { book: { qualityTierListId: 'quality-default' } },
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/root-folders', {
                name: 'Books',
                path: '/media/books',
                contentType: 'ct-books',
                defaultMonitoring: true,
                upgradeFileHandling: 'Replace',
                defaults: { book: { qualityTierListId: 'quality-default' } },
            });
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue(mockApiFolders);

            let resolvePost: (value: ApiRootFolder) => void;
            const postPromise = new Promise<ApiRootFolder>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.createFolder({
                    name: 'Books',
                    path: '/media/books',
                    contentType: 'ct-books',
                    defaultMonitoring: true,
                    upgradeFileHandling: 'Replace',
                    defaults: { book: { qualityTierListId: 'quality-default' } },
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(true);
            });

            act(() => {
                resolvePost!({
                    id: 'folder-4',
                    name: 'Books',
                    path: '/media/books',
                    contentType: 'ct-books',
                    isDefault: false,
                    defaultMonitoring: true,
                    upgradeFileHandling: 'Replace',
                    defaults: { book: { qualityTierListId: 'quality-default' } },
                    createdAt: '2024-01-04T00:00:00Z',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(false);
            });
        });
    });

    describe('updating folders', () => {
        it('should update a folder via PATCH /root-folders/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiFolders);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateFolder('folder-1', {
                    name: 'Movies Updated',
                    path: '/media/movies-new',
                    contentType: 'ct-movies',
                    defaultMonitoring: { value: true },
                    upgradeFileHandling: 'Replace',
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/root-folders/folder-1', {
                name: 'Movies Updated',
                path: '/media/movies-new',
                contentType: 'ct-movies',
                defaultMonitoring: { value: true },
                upgradeFileHandling: 'Replace',
            });
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockApiFolders);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.updateFolder('folder-1', {
                    name: 'Updated',
                    path: '/updated',
                    contentType: 'ct-movies',
                    defaultMonitoring: { value: true },
                    upgradeFileHandling: 'Replace',
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

    describe('deleting folders', () => {
        it('should delete a folder via DELETE /root-folders/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiFolders);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteFolder('folder-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/root-folders/folder-1');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiFolders);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.deleteFolder('folder-1');
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

    describe('folder data transformation', () => {
        it('should preserve all API fields', async () => {
            mockApiGet.mockResolvedValue([mockApiFolders[2]]);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const folder = result.current.folders[0];
            expect(folder.id).toBe('folder-3');
            expect(folder.name).toBe('Music');
            expect(folder.path).toBe('/media/music');
            expect(folder.contentType).toBe('ct-music');
            expect(folder.createdAt).toBe('2024-01-03T00:00:00Z');
            expect(folder.modifiedAt).toBe('2024-01-10T00:00:00Z');
            expect(folder.modifiedByUserId).toBe('user-123');
        });

        it('should handle null optional fields', async () => {
            mockApiGet.mockResolvedValue([mockApiFolders[0]]);

            const { result } = renderHook(() => useRootFolders(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const folder = result.current.folders[0];
            expect(folder.modifiedAt).toBeUndefined();
            expect(folder.modifiedByUserId).toBeUndefined();
        });
    });

    describe('query keys', () => {
        it('should export query keys for external use', async () => {
            const { rootFolderQueryKeys } = await import('./useRootFolders');

            expect(rootFolderQueryKeys.all).toEqual(['root-folders']);
            expect(rootFolderQueryKeys.list()).toEqual(['root-folders', 'list']);
            expect(rootFolderQueryKeys.detail('folder-1')).toEqual(['root-folders', 'detail', 'folder-1']);
        });
    });
});
