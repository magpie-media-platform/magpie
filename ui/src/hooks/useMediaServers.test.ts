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
import { useMediaServers, type ApiMediaServer } from './useMediaServers';

const mockApiServers: ApiMediaServer[] = [
    {
        id: 'server-1',
        providerType: 'plex',
        name: 'Plex Media Server',
        enabled: true,
        status: 'Healthy',
        lastChecked: '2024-01-15T10:30:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'server-2',
        providerType: 'mediaserver-a',
        name: 'Mediaserver A',
        enabled: true,
        status: 'Unhealthy',
        lastChecked: '2024-01-15T10:25:00Z',
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'server-3',
        providerType: 'emby',
        name: 'Emby (Disabled)',
        enabled: false,
        status: 'Unknown',
        lastChecked: null,
        createdAt: '2024-01-03T00:00:00Z',
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

describe('useMediaServers', () => {
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

    describe('fetching servers', () => {
        it('should fetch media servers from the API', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/media-servers');
        });

        it('should return servers from API', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.servers).toHaveLength(3);
            expect(result.current.servers[0].name).toBe('Plex Media Server');
            expect(result.current.servers[1].name).toBe('Mediaserver A');
            expect(result.current.servers[2].name).toBe('Emby (Disabled)');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.servers).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns nothing', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.servers).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('toggling server enabled state', () => {
        it('should enable a server via PATCH /media-servers/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleServerEnabled('server-3', true);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/media-servers/server-3', {
                enabled: { value: true },
            });
        });

        it('should disable a server via PATCH /media-servers/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleServerEnabled('server-1', false);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/media-servers/server-1', {
                enabled: { value: false },
            });
        });

        it('should set isToggling while toggling', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.toggleServerEnabled('server-1', false);
            });

            await waitFor(() => {
                expect(result.current.isToggling).toBe(true);
            });

            act(() => {
                resolvePatch!();
            });

            await waitFor(() => {
                expect(result.current.isToggling).toBe(false);
            });
        });
    });

    describe('creating servers', () => {
        it('should create a server via POST /media-servers', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);
            mockApiPost.mockResolvedValue({
                id: 'server-4',
                providerType: 'kodi',
                name: 'New Kodi',
                enabled: true,
                status: 'Unknown',
                createdAt: '2024-01-04T00:00:00Z',
            });

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createServer({
                    providerType: 'kodi',
                    name: 'New Kodi',
                    enabled: true,
                    settings: { host: '192.168.1.100', port: '8080' },
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/media-servers', {
                providerType: 'kodi',
                name: 'New Kodi',
                enabled: true,
                settings: { host: '192.168.1.100', port: '8080' },
            });
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);

            let resolvePost: (value: ApiMediaServer) => void;
            const postPromise = new Promise<ApiMediaServer>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.createServer({
                    providerType: 'kodi',
                    name: 'New Kodi',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(true);
            });

            act(() => {
                resolvePost!({
                    id: 'server-4',
                    providerType: 'kodi',
                    name: 'New Kodi',
                    enabled: true,
                    status: 'Unknown',
                    createdAt: '2024-01-04T00:00:00Z',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(false);
            });
        });
    });

    describe('updating servers', () => {
        it('should update a server via PATCH /media-servers/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateServer('server-1', {
                    name: { value: 'Plex Updated' },
                    enabled: { value: false },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/media-servers/server-1', {
                name: { value: 'Plex Updated' },
                enabled: { value: false },
            });
        });

        it('should send settings as DictionaryPatch with add/remove/update', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateServer('server-1', {
                    name: { value: 'Plex Updated' },
                    settings: {
                        add: { apiKey: 'abc123' },
                        update: { host: '192.168.1.100' },
                        remove: ['port'],
                    },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/media-servers/server-1', {
                name: { value: 'Plex Updated' },
                settings: {
                    add: { apiKey: 'abc123' },
                    update: { host: '192.168.1.100' },
                    remove: ['port'],
                },
            });
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.updateServer('server-1', {
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

    describe('deleting servers', () => {
        it('should delete a server via DELETE /media-servers/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteServer('server-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/media-servers/server-1');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiServers);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.deleteServer('server-1');
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

    describe('server data transformation', () => {
        it('should preserve all API fields', async () => {
            mockApiGet.mockResolvedValue([mockApiServers[0]]);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const server = result.current.servers[0];
            expect(server.id).toBe('server-1');
            expect(server.name).toBe('Plex Media Server');
            expect(server.providerType).toBe('plex');
            expect(server.enabled).toBe(true);
            expect(server.status).toBe('Healthy');
            expect(server.lastChecked).toBe('2024-01-15T10:30:00Z');
            expect(server.createdAt).toBe('2024-01-01T00:00:00Z');
        });

        it('should handle null lastChecked', async () => {
            mockApiGet.mockResolvedValue([mockApiServers[2]]);

            const { result } = renderHook(() => useMediaServers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.servers[0].lastChecked).toBeNull();
        });
    });

    describe('status labels and variants', () => {
        it('should export status labels for all values', async () => {
            const { mediaServerStatusLabels } = await import('./useMediaServers');

            expect(mediaServerStatusLabels.Unknown).toBe('Unknown');
            expect(mediaServerStatusLabels.Healthy).toBe('Healthy');
            expect(mediaServerStatusLabels.Unhealthy).toBe('Unhealthy');
            expect(mediaServerStatusLabels.Stale).toBe('Stale');
        });

        it('should export status variants for all values', async () => {
            const { mediaServerStatusVariants } = await import('./useMediaServers');

            expect(mediaServerStatusVariants.Unknown).toBe('unknown');
            expect(mediaServerStatusVariants.Healthy).toBe('healthy');
            expect(mediaServerStatusVariants.Unhealthy).toBe('unhealthy');
            expect(mediaServerStatusVariants.Stale).toBe('stale');
        });
    });
});
