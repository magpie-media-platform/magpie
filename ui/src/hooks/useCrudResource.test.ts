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
import { buildQueryKeys, useCrudResource } from './useCrudResource';

interface TestItem {
    id: string;
    name: string;
    enabled: boolean;
    status: number;
    createdAt: string;
}

interface TestCreate {
    name: string;
    enabled?: boolean;
}

interface TestPatch {
    name?: { value: string };
    enabled?: { value: boolean };
}

const queryKeys = buildQueryKeys('test-resources');

function useTestResource() {
    return useCrudResource<TestItem, TestCreate, TestPatch>({ basePath: '/test-resources' }, queryKeys);
}

const mockItems: TestItem[] = [
    { id: 'item-1', name: 'First', enabled: true, status: 1, createdAt: '2024-01-01T00:00:00Z' },
    { id: 'item-2', name: 'Second', enabled: false, status: 0, createdAt: '2024-01-02T00:00:00Z' },
];

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('useCrudResource', () => {
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

    describe('fetching', () => {
        it('should fetch items from the API', async () => {
            mockApiGet.mockResolvedValue(mockItems);

            const { result } = renderHook(() => useTestResource(), { wrapper: createWrapper() });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/test-resources');
            expect(result.current.items).toHaveLength(2);
            expect(result.current.items[0].name).toBe('First');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useTestResource(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.items).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns nothing', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useTestResource(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.items).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('creating', () => {
        it('should create an item via POST', async () => {
            mockApiGet.mockResolvedValue(mockItems);
            mockApiPost.mockResolvedValue({ id: 'item-3', name: 'New', enabled: true, status: 0, createdAt: '' });

            const { result } = renderHook(() => useTestResource(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.create({ name: 'New', enabled: true });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/test-resources', { name: 'New', enabled: true });
        });
    });

    describe('updating', () => {
        it('should update an item via PATCH', async () => {
            mockApiGet.mockResolvedValue(mockItems);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useTestResource(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.update('item-1', { name: { value: 'Updated' } });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/test-resources/item-1', { name: { value: 'Updated' } });
        });
    });

    describe('toggling enabled', () => {
        it('should enable via POST /{id}/enable', async () => {
            mockApiGet.mockResolvedValue(mockItems);
            mockApiPost.mockResolvedValue({ ...mockItems[1], enabled: true });

            const { result } = renderHook(() => useTestResource(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleEnabled('item-2', true);
            });

            expect(mockApiPost).toHaveBeenCalledWith('/test-resources/item-2/enable');
        });

        it('should disable via POST /{id}/disable', async () => {
            mockApiGet.mockResolvedValue(mockItems);
            mockApiPost.mockResolvedValue({ ...mockItems[0], enabled: false });

            const { result } = renderHook(() => useTestResource(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleEnabled('item-1', false);
            });

            expect(mockApiPost).toHaveBeenCalledWith('/test-resources/item-1/disable');
        });
    });

    describe('deleting', () => {
        it('should delete via DELETE /{id}', async () => {
            mockApiGet.mockResolvedValue(mockItems);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useTestResource(), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteItem('item-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/test-resources/item-1');
        });
    });
});

describe('buildQueryKeys', () => {
    it('should create query keys with given prefix', () => {
        const keys = buildQueryKeys('my-resource');

        expect(keys.all).toEqual(['my-resource']);
        expect(keys.list()).toEqual(['my-resource', 'list']);
        expect(keys.detail('abc')).toEqual(['my-resource', 'detail', 'abc']);
    });
});
