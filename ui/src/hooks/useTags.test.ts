import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
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
}));

import { apiGet } from '@/lib/api';
import { useTags, useTagsByEntity, type ApiTag } from './useTags';

const mockApiTags: ApiTag[] = [{ name: '4k' }, { name: 'anime' }, { name: 'movies' }];

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('useTags', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch tags from the API', async () => {
        mockApiGet.mockResolvedValue(mockApiTags);

        const { result } = renderHook(() => useTags(), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/tags');
    });

    it('fetches tags with a prefix parameter', async () => {
        mockApiGet.mockResolvedValue([{ name: 'archive' }]);
        const { result } = renderHook(() => useTags({ prefix: 'arc' }), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(mockApiGet).toHaveBeenCalledWith('/tags?prefix=arc');
        expect(result.current.tags).toEqual([{ name: 'archive' }]);
    });

    it('should transform API tags to UI format', async () => {
        mockApiGet.mockResolvedValue(mockApiTags);

        const { result } = renderHook(() => useTags(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.tags).toHaveLength(3);

        const tag4k = result.current.tags.find((t) => t.name === '4k');
        expect(tag4k).toBeDefined();
        expect(tag4k?.name).toBe('4k');

        const animeTag = result.current.tags.find((t) => t.name === 'anime');
        expect(animeTag).toBeDefined();
        expect(animeTag?.name).toBe('anime');

        const moviesTag = result.current.tags.find((t) => t.name === 'movies');
        expect(moviesTag).toBeDefined();
        expect(moviesTag?.name).toBe('movies');
    });

    it('should return empty array on error', async () => {
        mockApiGet.mockRejectedValue(new Error('Network error'));

        const { result } = renderHook(() => useTags(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.tags).toHaveLength(0);
        expect(result.current.error).toBeInstanceOf(Error);
    });

    it('keeps one array identity across renders when the query has no data', async () => {
        mockApiGet.mockRejectedValue(new Error('Network error'));

        const { result, rerender } = renderHook(() => useTags(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });
        const first = result.current.tags;
        rerender();

        expect(result.current.tags).toBe(first);
    });

    it('should return empty array when API returns nothing', async () => {
        mockApiGet.mockResolvedValue([]);

        const { result } = renderHook(() => useTags(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.tags).toHaveLength(0);
        expect(result.current.error).toBeNull();
    });
});

describe('useTagsByEntity', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch tags with entityId parameter', async () => {
        mockApiGet.mockResolvedValue([{ name: '4k' }]);

        const { result } = renderHook(() => useTagsByEntity('entity-123'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/tags?entityId=entity-123');
        expect(result.current.tags).toHaveLength(1);
    });

    it('should not fetch when entityId is empty', async () => {
        const { result } = renderHook(() => useTagsByEntity(''), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(false);
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('should not fetch when disabled', async () => {
        const { result } = renderHook(() => useTagsByEntity('entity-123', false), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(false);
        expect(mockApiGet).not.toHaveBeenCalled();
    });
});
