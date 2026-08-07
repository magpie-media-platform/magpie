import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { useAllApiKeys, useApiKeys, useUserApiKeys } from './useApiKeys';
import type { ApiKeyItem, ApiKeyCreatedItem } from '@/types';

const mockApiKeys: ApiKeyItem[] = [
    {
        id: 'key-1',
        userId: 'user-1',
        name: 'Test Key 1',
        keyPrefix: 'mgp_abc',
        enabled: true,
        expiresAt: null,
        lastUsedAt: '2024-01-15T10:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'key-2',
        userId: 'user-1',
        name: 'Test Key 2',
        keyPrefix: 'mgp_def',
        enabled: false,
        expiresAt: '2024-06-01T00:00:00Z',
        lastUsedAt: null,
        createdAt: '2024-02-01T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: '2024-03-01T00:00:00Z',
        modifiedByUserId: 'user-1',
    },
];

const mockCreatedKey: ApiKeyCreatedItem = {
    key: {
        id: 'key-3',
        userId: 'user-1',
        name: 'New Key',
        keyPrefix: 'mgp_xyz',
        enabled: true,
        expiresAt: null,
        lastUsedAt: null,
        createdAt: '2024-03-01T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
    plaintextKey: 'mgp_xyz_full_plaintext_key_value',
};

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

describe('useApiKeys', () => {
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

    it('should fetch API keys from /api-keys/my', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);

        const { result } = renderHook(() => useApiKeys(), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/api-keys/my');
        expect(result.current.apiKeys).toEqual(mockApiKeys);
        expect(result.current.error).toBeNull();
    });

    it('should return empty array when API returns empty', async () => {
        mockApiGet.mockResolvedValue([]);

        const { result } = renderHook(() => useApiKeys(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.apiKeys).toEqual([]);
    });

    it('should handle fetch error', async () => {
        mockApiGet.mockRejectedValue(new Error('Network error'));

        const { result } = renderHook(() => useApiKeys(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.error).toBeInstanceOf(Error);
        expect(result.current.error?.message).toBe('Network error');
    });

    it('creates an API key at the root route and returns its raw key', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);
        mockApiPost.mockResolvedValue(mockCreatedKey);

        const { result } = renderHook(() => useApiKeys(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        let created: ApiKeyCreatedItem | undefined;
        await act(async () => {
            created = await result.current.createApiKey({ name: 'New Key' });
        });

        expect(mockApiPost).toHaveBeenCalledWith('/api-keys', { name: 'New Key' });
        expect(created).toEqual(mockCreatedKey);
        expect(created?.plaintextKey).toBe('mgp_xyz_full_plaintext_key_value');
    });

    it('should delete an API key via DELETE /api-keys/{id}', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);
        mockApiDelete.mockResolvedValue(undefined);

        const { result } = renderHook(() => useApiKeys(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await act(async () => {
            await result.current.deleteApiKey('key-1');
        });

        expect(mockApiDelete).toHaveBeenCalledWith('/api-keys/key-1');
    });

    it('should rotate an API key via POST /api-keys/my/{id}/rotate', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);
        mockApiPost.mockResolvedValue(mockCreatedKey);

        const { result } = renderHook(() => useApiKeys(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        let rotated: ApiKeyCreatedItem | undefined;
        await act(async () => {
            rotated = await result.current.rotateApiKey('key-1');
        });

        expect(mockApiPost).toHaveBeenCalledWith('/api-keys/my/key-1/rotate');
        expect(rotated).toEqual(mockCreatedKey);
    });

    it('should enable an API key via PATCH /api-keys/{id}', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useApiKeys(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await act(async () => {
            await result.current.toggleEnabled('key-2', true);
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/api-keys/key-2', { enabled: { value: true } });
        expect(mockApiPost).not.toHaveBeenCalled();
    });

    it('should disable an API key via PATCH /api-keys/{id}', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useApiKeys(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await act(async () => {
            await result.current.toggleEnabled('key-1', false);
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/api-keys/key-1', { enabled: { value: false } });
        expect(mockApiPost).not.toHaveBeenCalled();
    });

    it('should reject with the server error when the toggle patch fails', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);
        mockApiPatch.mockRejectedValue(new Error('Key belongs to another user'));

        const { result } = renderHook(() => useApiKeys(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        let caught: unknown;
        await act(async () => {
            caught = await result.current.toggleEnabled('key-1', false).catch((error: unknown) => error);
        });

        expect((caught as Error).message).toBe('Key belongs to another user');
    });
});

describe('useUserApiKeys', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch API keys for a specific user', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);

        const { result } = renderHook(() => useUserApiKeys('user-2'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/users/user-2/api-keys');
        expect(result.current.apiKeys).toEqual(mockApiKeys);
    });

    it('should not fetch when userId is null', async () => {
        const { result } = renderHook(() => useUserApiKeys(null), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(false);
        expect(result.current.apiKeys).toEqual([]);
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('should delete another user API key via DELETE /api-keys/{id}', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);
        mockApiDelete.mockResolvedValue(undefined);

        const { result } = renderHook(() => useUserApiKeys('user-2'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await act(async () => {
            await result.current.deleteApiKey({ userId: 'user-2', id: 'key-1' });
        });

        expect(mockApiDelete).toHaveBeenCalledWith('/api-keys/key-1');
    });
});

describe('useAllApiKeys', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch all API keys from the root route', async () => {
        mockApiGet.mockResolvedValue(mockApiKeys);

        const { result } = renderHook(() => useAllApiKeys(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/api-keys');
        expect(result.current.apiKeys).toEqual(mockApiKeys);
    });
});
