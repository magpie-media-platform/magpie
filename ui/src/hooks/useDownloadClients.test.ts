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
import { useDownloadClients, type ApiDownloadClient } from './useDownloadClients';

const mockApiClients: ApiDownloadClient[] = [
    {
        id: 'client-1',
        providerType: 'sabnzbd',
        name: 'SABnzbd',
        enabled: true,
        status: 'Healthy',
        lastChecked: '2024-01-15T10:30:00Z',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'client-2',
        providerType: 'downloadclient-a',
        name: 'Downloadclient A',
        enabled: true,
        status: 'Unhealthy',
        lastChecked: '2024-01-15T10:25:00Z',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'client-3',
        providerType: 'transmission',
        name: 'Transmission',
        enabled: false,
        status: 'Unknown',
        lastChecked: null,
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
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

describe('useDownloadClients', () => {
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

    describe('fetching clients', () => {
        it('should fetch download clients from the API', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/download-clients');
        });

        it('should return clients from API', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.clients).toHaveLength(3);
            expect(result.current.clients[0].name).toBe('SABnzbd');
            expect(result.current.clients[1].name).toBe('Downloadclient A');
            expect(result.current.clients[2].name).toBe('Transmission');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.clients).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns nothing', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.clients).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('toggling client enabled state', () => {
        it('should enable a client via PATCH /download-clients/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleClientEnabled('client-3', true);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/download-clients/client-3', {
                enabled: { value: true },
            });
        });

        it('should disable a client via PATCH /download-clients/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleClientEnabled('client-1', false);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/download-clients/client-1', {
                enabled: { value: false },
            });
        });

        it('should set isToggling while toggling', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.toggleClientEnabled('client-1', false);
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

    describe('creating clients', () => {
        it('should create a client via POST /download-clients', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);
            mockApiPost.mockResolvedValue({
                id: 'client-4',
                providerType: 'deluge',
                name: 'New Deluge',
                enabled: true,
                status: 'Unknown',
                createdAt: '2024-01-04T00:00:00Z',
            });

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createClient({
                    providerType: 'deluge',
                    name: 'New Deluge',
                    enabled: true,
                    settings: { host: 'localhost', port: '8112' },
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/download-clients', {
                providerType: 'deluge',
                name: 'New Deluge',
                enabled: true,
                settings: { host: 'localhost', port: '8112' },
            });
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);

            let resolvePost: (value: ApiDownloadClient) => void;
            const postPromise = new Promise<ApiDownloadClient>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.createClient({
                    providerType: 'deluge',
                    name: 'New Deluge',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(true);
            });

            act(() => {
                resolvePost!({
                    id: 'client-4',
                    providerType: 'deluge',
                    name: 'New Deluge',
                    enabled: true,
                    status: 'Unknown',
                    cleanupDeleteFiles: true,
                    keepFailedOnClient: false,
                    createdAt: '2024-01-04T00:00:00Z',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(false);
            });
        });
    });

    describe('updating clients', () => {
        it('should update a client via PATCH /download-clients/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateClient('client-1', {
                    name: { value: 'SABnzbd Updated' },
                    enabled: { value: false },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/download-clients/client-1', {
                name: { value: 'SABnzbd Updated' },
                enabled: { value: false },
            });
        });

        it('should send settings as DictionaryPatch with add/remove/update', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateClient('client-1', {
                    name: { value: 'SABnzbd Updated' },
                    settings: {
                        add: { apiKey: 'abc123' },
                        update: { host: '192.168.1.100' },
                        remove: ['port'],
                    },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/download-clients/client-1', {
                name: { value: 'SABnzbd Updated' },
                settings: {
                    add: { apiKey: 'abc123' },
                    update: { host: '192.168.1.100' },
                    remove: ['port'],
                },
            });
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.updateClient('client-1', {
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

    describe('deleting clients', () => {
        it('should delete a client via DELETE /download-clients/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteClient('client-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/download-clients/client-1');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiClients);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.deleteClient('client-1');
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

    describe('client data transformation', () => {
        it('should preserve all API fields', async () => {
            mockApiGet.mockResolvedValue([mockApiClients[0]]);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const client = result.current.clients[0];
            expect(client.id).toBe('client-1');
            expect(client.name).toBe('SABnzbd');
            expect(client.providerType).toBe('sabnzbd');
            expect(client.enabled).toBe(true);
            expect(client.status).toBe('Healthy');
            expect(client.lastChecked).toBe('2024-01-15T10:30:00Z');
            expect(client.createdAt).toBe('2024-01-01T00:00:00Z');
        });

        it('should handle null lastChecked', async () => {
            mockApiGet.mockResolvedValue([mockApiClients[2]]);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.clients[0].lastChecked).toBeNull();
        });
    });

    describe('status labels and variants', () => {
        it('should export status labels for all values', async () => {
            const { downloadClientStatusLabels } = await import('./useDownloadClients');

            expect(downloadClientStatusLabels.Unknown).toBe('Unknown');
            expect(downloadClientStatusLabels.Healthy).toBe('Healthy');
            expect(downloadClientStatusLabels.Unhealthy).toBe('Unhealthy');
            expect(downloadClientStatusLabels.Stale).toBe('Stale');
        });

        it('should export status variants for all values', async () => {
            const { downloadClientStatusVariants } = await import('./useDownloadClients');

            expect(downloadClientStatusVariants.Unknown).toBe('unknown');
            expect(downloadClientStatusVariants.Healthy).toBe('healthy');
            expect(downloadClientStatusVariants.Unhealthy).toBe('unhealthy');
            expect(downloadClientStatusVariants.Stale).toBe('stale');
        });
    });
});
