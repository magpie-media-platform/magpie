import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
        if (!params || Object.keys(params).length === 0) {
            return path;
        }
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        return `${path}?${searchParams.toString()}`;
    }),
    isApiError: vi.fn(() => false),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { useDownloadClients, downloadClientQueryKeys } from './useDownloadClients';
import { useApiKeys, apiKeyQueryKeys } from './useApiKeys';
import { useBackups, backupQueryKeys } from './useBackups';
import { useIndexers, indexerQueryKeys } from './useIndexers';
import { useUsers, userQueryKeys } from './useUsers';

const mockApiGet = vi.mocked(apiGet);
const mockApiPost = vi.mocked(apiPost);
const mockApiPatch = vi.mocked(apiPatch);
const mockApiDelete = vi.mocked(apiDelete);

function createTestClient() {
    return new QueryClient({
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
}

function createWrapper(queryClient: QueryClient) {
    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

beforeEach(() => {
    vi.clearAllMocks();
});

afterEach(() => {
    vi.restoreAllMocks();
});

describe('cache invalidation', () => {
    describe('useDownloadClients', () => {
        const mockClients = [
            {
                id: 'c1',
                providerType: 'sabnzbd',
                name: 'SABnzbd',
                enabled: true,
                status: 1,
                createdAt: '2024-01-01T00:00:00Z',
            },
        ];

        it('should invalidate list query after create', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockClients);
            mockApiPost.mockResolvedValue({
                id: 'c2',
                providerType: 'test',
                name: 'New',
                enabled: true,
                status: 0,
                createdAt: '2024-01-02T00:00:00Z',
            });

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createClient({ providerType: 'test', name: 'New' });
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: downloadClientQueryKeys.list() });
        });

        it('should invalidate list query after update', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockClients);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateClient('c1', { name: { value: 'Updated' } });
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: downloadClientQueryKeys.list() });
        });

        it('should invalidate list query after delete', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockClients);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteClient('c1');
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: downloadClientQueryKeys.list() });
        });

        it('should invalidate list query after enable', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockClients);
            mockApiPost.mockResolvedValue({ ...mockClients[0], enabled: true });

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleClientEnabled('c1', true);
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: downloadClientQueryKeys.list() });
        });

        it('should invalidate list query after disable', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockClients);
            mockApiPost.mockResolvedValue({ ...mockClients[0], enabled: false });

            const { result } = renderHook(() => useDownloadClients(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleClientEnabled('c1', false);
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: downloadClientQueryKeys.list() });
        });
    });

    describe('useApiKeys', () => {
        const mockKeys = [
            {
                id: 'k1',
                name: 'Key 1',
                prefix: 'mp_',
                enabled: true,
                createdAt: '2024-01-01T00:00:00Z',
                lastUsedAt: null,
                expiresAt: null,
            },
        ];

        it('should invalidate all api-keys after create', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockKeys);
            mockApiPost.mockResolvedValue({ ...mockKeys[0], id: 'k2', key: 'mp_secret' });

            const { result } = renderHook(() => useApiKeys(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createApiKey({ name: 'New Key' });
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: apiKeyQueryKeys.all });
        });

        it('should invalidate all api-keys after delete', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockKeys);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useApiKeys(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteApiKey('k1');
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: apiKeyQueryKeys.all });
        });

        it('should invalidate all api-keys after enable', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockKeys);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useApiKeys(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleEnabled('k1', true);
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: apiKeyQueryKeys.all });
        });

        it('should invalidate all api-keys after disable', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockKeys);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useApiKeys(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleEnabled('k1', false);
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: apiKeyQueryKeys.all });
        });
    });

    describe('useBackups', () => {
        const mockBackups = [
            {
                id: '20240115_150000',
                name: 'Scheduled 20240115_150000',
                createdAt: '2024-01-15T15:00:00Z',
                sizeBytes: 1024,
            },
        ];

        it('should invalidate list after create', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockBackups);
            mockApiPost.mockResolvedValue({ id: '20240116_120000' });

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createBackup({});
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: backupQueryKeys.list() });
        });

        it('should invalidate list after delete', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockBackups);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteBackup('20240115_150000');
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: backupQueryKeys.list() });
        });
    });

    describe('useIndexers', () => {
        const mockIndexers = [
            {
                id: 'i1',
                providerType: 'newznab',
                name: 'NZBgeek',
                enabled: true,
                status: 1,
                createdAt: '2024-01-01T00:00:00Z',
            },
        ];
        const mockFeeds = [
            {
                id: 'f1',
                indexerId: 'i1',
                providerType: 'newznab',
                name: 'Main',
                enabled: true,
                createdAt: '2024-01-01T00:00:00Z',
            },
        ];

        it('should invalidate indexer list after create indexer', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/indexers') {
                    return Promise.resolve(mockIndexers);
                }
                if (url === '/indexer-feeds') {
                    return Promise.resolve(mockFeeds);
                }
                return Promise.resolve([]);
            });
            mockApiPost.mockResolvedValue({
                id: 'i2',
                providerType: 'torznab',
                name: 'New',
                enabled: true,
                status: 0,
                createdAt: '2024-01-02T00:00:00Z',
            });

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createIndexer({ providerType: 'torznab', name: 'New' });
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: indexerQueryKeys.list() });
        });

        it('should invalidate indexer list after update indexer', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/indexers') {
                    return Promise.resolve(mockIndexers);
                }
                if (url === '/indexer-feeds') {
                    return Promise.resolve(mockFeeds);
                }
                return Promise.resolve([]);
            });
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateIndexer('i1', { name: 'Updated' });
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: indexerQueryKeys.list() });
        });

        it('should invalidate indexer list after delete indexer', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/indexers') {
                    return Promise.resolve(mockIndexers);
                }
                if (url === '/indexer-feeds') {
                    return Promise.resolve(mockFeeds);
                }
                return Promise.resolve([]);
            });
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteIndexer('i1');
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: indexerQueryKeys.list() });
        });

        it('should invalidate feeds query after enable feed', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/indexers') {
                    return Promise.resolve(mockIndexers);
                }
                if (url === '/indexer-feeds') {
                    return Promise.resolve(mockFeeds);
                }
                return Promise.resolve([]);
            });
            mockApiPost.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleFeedEnabled('f1', true);
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: indexerQueryKeys.feeds() });
        });

        it('should invalidate feeds query after disable feed', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/indexers') {
                    return Promise.resolve(mockIndexers);
                }
                if (url === '/indexer-feeds') {
                    return Promise.resolve(mockFeeds);
                }
                return Promise.resolve([]);
            });
            mockApiPost.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleFeedEnabled('f1', false);
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: indexerQueryKeys.feeds() });
        });

        it('should invalidate feeds query after create feed', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/indexers') {
                    return Promise.resolve(mockIndexers);
                }
                if (url === '/indexer-feeds') {
                    return Promise.resolve(mockFeeds);
                }
                return Promise.resolve([]);
            });
            mockApiPost.mockResolvedValue({
                id: 'f2',
                indexerId: 'i1',
                providerType: 'newznab',
                name: 'New Feed',
                enabled: true,
                createdAt: '2024-01-02T00:00:00Z',
            });

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createFeed({ providerType: 'newznab', name: 'New Feed' });
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: indexerQueryKeys.feeds() });
        });

        it('should invalidate feeds query after update feed', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/indexers') {
                    return Promise.resolve(mockIndexers);
                }
                if (url === '/indexer-feeds') {
                    return Promise.resolve(mockFeeds);
                }
                return Promise.resolve([]);
            });
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateFeed('f1', { name: 'Updated Feed' });
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: indexerQueryKeys.feeds() });
        });

        it('should invalidate feeds query after delete feed', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/indexers') {
                    return Promise.resolve(mockIndexers);
                }
                if (url === '/indexer-feeds') {
                    return Promise.resolve(mockFeeds);
                }
                return Promise.resolve([]);
            });
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useIndexers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteFeed('f1');
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: indexerQueryKeys.feeds() });
        });
    });

    describe('useUsers', () => {
        const mockUsers = {
            items: [
                {
                    id: 'u1',
                    username: 'admin',
                    providerType: 'local',
                    role: 'admin',
                    enabled: true,
                    createdAt: '2024-01-01T00:00:00Z',
                },
            ],
            pageNumber: 1,
            pageSize: 25,
            totalItems: 1,
        };

        it('should invalidate all users after create', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockUsers);
            mockApiPost.mockResolvedValue({
                id: 'u2',
                username: 'newuser',
                providerType: 'local',
                role: 'viewer',
                enabled: true,
                createdAt: '2024-01-02T00:00:00Z',
            });

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createUser({ username: 'newuser', password: 'pass', role: 0, enabled: true });
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: userQueryKeys.all });
        });

        it('should invalidate all users after delete', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockUsers);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteUser('u1');
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: userQueryKeys.all });
        });

        it('should invalidate all users after enable', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockUsers);
            mockApiPost.mockResolvedValue({ ...mockUsers.items[0], enabled: true });

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleUserEnabled('u1', true);
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: userQueryKeys.all });
        });

        it('should invalidate all users after disable', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockUsers);
            mockApiPost.mockResolvedValue({ ...mockUsers.items[0], enabled: false });

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleUserEnabled('u1', false);
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: userQueryKeys.all });
        });

        it('should invalidate all users after update', async () => {
            const queryClient = createTestClient();
            const spy = vi.spyOn(queryClient, 'invalidateQueries');
            mockApiGet.mockResolvedValue(mockUsers);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateUser('u1', { role: 2 });
            });

            expect(spy).toHaveBeenCalledWith({ queryKey: userQueryKeys.all });
        });
    });
});
