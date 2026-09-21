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
import { useRemotePathMappings, type ApiRemotePathMapping } from './useRemotePathMappings';

const mockApiMappings: ApiRemotePathMapping[] = [
    {
        id: 'mapping-1',
        host: 'downloadbox.example.test',
        remotePath: 'E:\\Transmission',
        localPath: '\\\\downloadbox.example.test\\share\\Downloads',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'mapping-2',
        host: 'seedbox.example.com',
        remotePath: '/downloads/complete',
        localPath: '/mnt/seedbox/complete',
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'mapping-3',
        host: 'nas.local',
        remotePath: '/volume1/torrents',
        localPath: '/media/torrents',
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

describe('useRemotePathMappings', () => {
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

    describe('fetching mappings', () => {
        it('should fetch remote path mappings from the API', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/remote-path-mappings');
        });

        it('should return mappings from API', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.mappings).toHaveLength(3);
            expect(result.current.mappings[0].host).toBe('downloadbox.example.test');
            expect(result.current.mappings[1].host).toBe('seedbox.example.com');
            expect(result.current.mappings[2].host).toBe('nas.local');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.mappings).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns nothing', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.mappings).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('creating mappings', () => {
        it('should create a mapping via POST /remote-path-mappings', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);
            mockApiPost.mockResolvedValue({
                id: 'mapping-4',
                host: 'new.local',
                remotePath: '/downloads',
                localPath: '/media/downloads',
                createdAt: '2024-01-04T00:00:00Z',
            });

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createMapping({
                    host: 'new.local',
                    remotePath: '/downloads',
                    localPath: '/media/downloads',
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/remote-path-mappings', {
                host: 'new.local',
                remotePath: '/downloads',
                localPath: '/media/downloads',
            });
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);

            let resolvePost: (value: ApiRemotePathMapping) => void;
            const postPromise = new Promise<ApiRemotePathMapping>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.createMapping({
                    host: 'new.local',
                    remotePath: '/downloads',
                    localPath: '/media/downloads',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(true);
            });

            act(() => {
                resolvePost!({
                    id: 'mapping-4',
                    host: 'new.local',
                    remotePath: '/downloads',
                    localPath: '/media/downloads',
                    createdAt: '2024-01-04T00:00:00Z',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(false);
            });
        });
    });

    describe('updating mappings', () => {
        it('should update a mapping via PATCH /remote-path-mappings/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateMapping('mapping-1', {
                    host: { value: 'renamed.local' },
                    remotePath: { value: 'F:\\Transmission' },
                    localPath: { value: '\\\\renamed.local\\f$\\Transmission' },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/remote-path-mappings/mapping-1', {
                host: { value: 'renamed.local' },
                remotePath: { value: 'F:\\Transmission' },
                localPath: { value: '\\\\renamed.local\\f$\\Transmission' },
            });
        });

        it('should support partial updates', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateMapping('mapping-1', {
                    localPath: { value: '/new/local/path' },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/remote-path-mappings/mapping-1', {
                localPath: { value: '/new/local/path' },
            });
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.updateMapping('mapping-1', {
                    host: { value: 'updated.local' },
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

    describe('testing local paths', () => {
        it('should test a local path via POST /remote-path-mappings/test', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);
            mockApiPost.mockResolvedValue({
                success: true,
                message: 'Local path is reachable and readable.',
            });

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.testLocalPath({
                    localPath: '/media/downloads',
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/remote-path-mappings/test', {
                localPath: '/media/downloads',
            });
        });

        it('should set isTestingLocalPath while testing', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);

            let resolvePost: (value: { success: boolean; message: string }) => void;
            const postPromise = new Promise<{ success: boolean; message: string }>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.testLocalPath({
                    localPath: '/media/downloads',
                });
            });

            await waitFor(() => {
                expect(result.current.isTestingLocalPath).toBe(true);
            });

            act(() => {
                resolvePost!({
                    success: true,
                    message: 'Local path is reachable and readable.',
                });
            });

            await waitFor(() => {
                expect(result.current.isTestingLocalPath).toBe(false);
            });
        });
    });

    describe('deleting mappings', () => {
        it('should delete a mapping via DELETE /remote-path-mappings/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteMapping('mapping-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/remote-path-mappings/mapping-1');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiMappings);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.deleteMapping('mapping-1');
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

    describe('mapping data transformation', () => {
        it('should preserve all API fields', async () => {
            mockApiGet.mockResolvedValue([mockApiMappings[2]]);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const mapping = result.current.mappings[0];
            expect(mapping.id).toBe('mapping-3');
            expect(mapping.host).toBe('nas.local');
            expect(mapping.remotePath).toBe('/volume1/torrents');
            expect(mapping.localPath).toBe('/media/torrents');
            expect(mapping.createdAt).toBe('2024-01-03T00:00:00Z');
            expect(mapping.modifiedAt).toBe('2024-01-10T00:00:00Z');
            expect(mapping.modifiedByUserId).toBe('user-123');
        });

        it('should handle null optional fields', async () => {
            mockApiGet.mockResolvedValue([mockApiMappings[0]]);

            const { result } = renderHook(() => useRemotePathMappings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const mapping = result.current.mappings[0];
            expect(mapping.modifiedAt).toBeUndefined();
            expect(mapping.modifiedByUserId).toBeUndefined();
        });
    });

    describe('query keys', () => {
        it('should export query keys for external use', async () => {
            const { remotePathMappingQueryKeys } = await import('./useRemotePathMappings');
            expect(remotePathMappingQueryKeys.list()).toEqual(['remote-path-mappings', 'list']);
        });
    });
});
