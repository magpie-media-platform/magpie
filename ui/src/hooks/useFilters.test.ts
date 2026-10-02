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

vi.mock('@/lib/magpieQl', () => ({
    formatMagpieQlCriteria: vi.fn(),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { formatMagpieQlCriteria } from '@/lib/magpieQl';
import {
    useContentFilters,
    useFormattedCriteria,
    useCreateContentFilter,
    useUpdateContentFilter,
    useDeleteContentFilter,
    type ContentFilter,
} from './useFilters';

const mockSharedFilters: ContentFilter[] = [
    {
        id: 'shared-1',
        ownerUserId: null,
        name: 'Action Movies',
        criteria: { contentHierarchyItem: { and: [{ genre: 'Action' }] } },
        ownerLock: null,
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'shared-2',
        ownerUserId: null,
        name: 'Comedy Shows',
        criteria: { contentHierarchyItem: { and: [{ genre: 'Comedy' }, { kind: 'Series' }] } },
        ownerLock: null,
        createdAt: '2024-01-02T00:00:00Z',
        createdByUserId: null,
        modifiedAt: '2024-01-15T10:30:00Z',
        modifiedByUserId: null,
    },
];

const mockMyFilters: ContentFilter[] = [
    {
        id: 'my-1',
        ownerUserId: 'user-1',
        name: 'My Favorites',
        criteria: { contentHierarchyItem: { and: [{ rating: { gte: 8.0 } }] } },
        ownerLock: true,
        createdAt: '2024-01-03T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
];

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

describe('useContentFilters', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockFormatMagpieQlCriteria = vi.mocked(formatMagpieQlCriteria);

    beforeEach(() => {
        vi.clearAllMocks();

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve(mockSharedFilters);
            }
            if (url === '/content-filters/my') {
                return Promise.resolve(mockMyFilters);
            }
            return Promise.reject(new Error(`Unexpected URL: ${url}`));
        });

        mockFormatMagpieQlCriteria.mockResolvedValue({ query: '' });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch and combine shared and personal filters', async () => {
        const { result } = renderHook(() => useContentFilters(), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.filters).toHaveLength(3);
        expect(result.current.error).toBeNull();
    });

    it('should transform filters to UI format', async () => {
        const { result } = renderHook(() => useContentFilters(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        const sharedFilter = result.current.filters.find((f) => f.id === 'shared-1');
        expect(sharedFilter).toBeDefined();
        expect(sharedFilter?.name).toBe('Action Movies');
        expect(sharedFilter?.ownerId).toBeNull();
        expect(sharedFilter?.ownerLock).toBe(false);
        expect(sharedFilter?.criteriaCount).toBe(1);

        const myFilter = result.current.filters.find((f) => f.id === 'my-1');
        expect(myFilter).toBeDefined();
        expect(myFilter?.name).toBe('My Favorites');
        expect(myFilter?.ownerId).toBe('user-1');
        expect(myFilter?.ownerLock).toBe(true);
    });

    it('should return empty array when both endpoints fail', async () => {
        mockApiGet.mockRejectedValue(new Error('Network error'));

        const { result } = renderHook(() => useContentFilters(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.filters).toHaveLength(0);
        expect(result.current.error).toBeInstanceOf(Error);
    });

    it('should call correct API endpoints', async () => {
        renderHook(() => useContentFilters(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/content-filters');
            expect(mockApiGet).toHaveBeenCalledWith('/content-filters/my');
        });
    });

    it('keeps saved filters loading until their MQL is available', async () => {
        let complete!: (value: { query: string }) => void;
        mockFormatMagpieQlCriteria.mockReturnValue(
            new Promise((resolve) => {
                complete = resolve;
            }),
        );
        const { result } = renderHook(() => useContentFilters(), { wrapper: createWrapper() });
        await waitFor(() => expect(mockFormatMagpieQlCriteria).toHaveBeenCalled());
        expect(result.current.isLoading).toBe(true);
        await act(async () => complete({ query: 'fulfillment = Missing and monitoring = true' }));
        await waitFor(() => expect(result.current.isLoading).toBe(false));
        expect(result.current.filters[0].query).toBe('fulfillment = Missing and monitoring = true');
    });

    it('should format filter queries via backend', async () => {
        mockFormatMagpieQlCriteria.mockResolvedValueOnce({ query: 'genre = Action' });
        mockFormatMagpieQlCriteria.mockResolvedValueOnce({ query: 'genre = Comedy and kind = Series' });
        mockFormatMagpieQlCriteria.mockResolvedValueOnce({ query: 'rating >= 8.0' });

        const { result } = renderHook(() => useContentFilters(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await waitFor(() => {
            const actionFilter = result.current.filters.find((f) => f.id === 'shared-1');
            expect(actionFilter?.query).toBe('genre = Action');
        });

        expect(mockFormatMagpieQlCriteria).toHaveBeenCalledTimes(3);
        expect(mockFormatMagpieQlCriteria).toHaveBeenCalledWith(
            { contentHierarchyItem: { and: [{ genre: 'Action' }] } },
            'content',
        );
    });

    it('surfaces format errors through the hook error state', async () => {
        mockFormatMagpieQlCriteria.mockRejectedValue(new Error('Format failed'));

        const { result } = renderHook(() => useContentFilters(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await waitFor(() => {
            expect(result.current.error?.message).toBe('Format failed');
        });

        expect(result.current.filters).toHaveLength(3);
        const filter = result.current.filters.find((f) => f.id === 'shared-1');
        expect(filter?.query).toBe('');
        expect(filter?.queryError?.message).toBe('Format failed');
        expect(result.current.listError).toBeNull();
    });
});

describe('useFormattedCriteria', () => {
    const mockFormatMagpieQlCriteria = vi.mocked(formatMagpieQlCriteria);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('formats criteria through React Query', async () => {
        const criteria: ContentFilter['criteria'] = {
            contentHierarchyItem: { and: [{ genre: 'Action' }] },
        };
        mockFormatMagpieQlCriteria.mockResolvedValue({ query: 'genre = Action' });

        const { result } = renderHook(() => useFormattedCriteria(criteria), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.data).toBe('genre = Action');
        });

        expect(result.current.error).toBeNull();
        expect(mockFormatMagpieQlCriteria).toHaveBeenCalledWith(criteria, 'content');
    });

    it('does not format empty criteria', () => {
        const { result } = renderHook(() => useFormattedCriteria({}), {
            wrapper: createWrapper(),
        });

        expect(result.current.data).toBeUndefined();
        expect(result.current.error).toBeNull();
        expect(mockFormatMagpieQlCriteria).not.toHaveBeenCalled();
    });

    it('surfaces formatter errors through query state', async () => {
        mockFormatMagpieQlCriteria.mockRejectedValue(new Error('Format failed'));

        const { result } = renderHook(
            () => useFormattedCriteria({ contentHierarchyItem: { and: [{ genre: 'Action' }] } }),
            { wrapper: createWrapper() },
        );

        await waitFor(() => {
            expect(result.current.error?.message).toBe('Format failed');
        });

        expect(result.current.data).toBeUndefined();
    });
});

describe('useCreateContentFilter', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should create a filter', async () => {
        mockApiPost.mockResolvedValue({ id: 'new-1', name: 'New Filter' });

        const { result } = renderHook(() => useCreateContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({
                name: 'New Filter',
                criteria: {},
            });
        });

        expect(mockApiPost).toHaveBeenCalledWith('/content-filters', {
            name: 'New Filter',
            criteria: {},
        });
    });
});

describe('useUpdateContentFilter', () => {
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should update a filter name', async () => {
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useUpdateContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({
                id: 'shared-1',
                name: { value: 'Updated Name' },
            });
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/content-filters/shared-1', {
            name: { value: 'Updated Name' },
        });
    });

    it('should update a filter owner lock', async () => {
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useUpdateContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({
                id: 'my-1',
                ownerLock: { value: true },
            });
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/content-filters/my-1', {
            ownerLock: { value: true },
        });
    });
});

describe('countCriteria (via useContentFilters)', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockFormatMagpieQlCriteria = vi.mocked(formatMagpieQlCriteria);

    beforeEach(() => {
        vi.clearAllMocks();
        mockFormatMagpieQlCriteria.mockResolvedValue({ query: '' });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    function makeFilter(id: string, criteria: ContentFilter['criteria']): ContentFilter {
        return {
            id,
            ownerUserId: null,
            name: id,
            criteria,
            ownerLock: null,
            createdAt: '2024-01-01T00:00:00Z',
            createdByUserId: null,
            modifiedAt: null,
            modifiedByUserId: null,
        };
    }

    it('counts top-level `and` clauses', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve([makeFilter('a', { contentHierarchyItem: { and: [{ x: 1 }, { y: 2 }] } })]);
            }
            return Promise.resolve([]);
        });

        const { result } = renderHook(() => useContentFilters(), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        const f = result.current.filters.find((x) => x.id === 'a');
        expect(f?.criteriaCount).toBe(2);
    });

    it('counts top-level `or` clauses (not just `and`)', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve([
                    makeFilter('b', {
                        contentHierarchyItem: { or: [{ x: 1 }, { y: 2 }, { z: 3 }] },
                    } as unknown as ContentFilter['criteria']),
                ]);
            }
            return Promise.resolve([]);
        });

        const { result } = renderHook(() => useContentFilters(), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        const f = result.current.filters.find((x) => x.id === 'b');
        expect(f?.criteriaCount).toBe(3);
    });

    it('counts top-level `not` (single child) as 1', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve([
                    makeFilter('c', {
                        contentHierarchyItem: { not: { x: 1 } },
                    } as unknown as ContentFilter['criteria']),
                ]);
            }
            return Promise.resolve([]);
        });

        const { result } = renderHook(() => useContentFilters(), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        const f = result.current.filters.find((x) => x.id === 'c');
        expect(f?.criteriaCount).toBe(1);
    });

    it('returns 0 for an empty filter', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve([makeFilter('d', {})]);
            }
            return Promise.resolve([]);
        });

        const { result } = renderHook(() => useContentFilters(), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        const f = result.current.filters.find((x) => x.id === 'd');
        expect(f?.criteriaCount).toBe(0);
    });
});

describe('useContentFilters formatted-query race', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockFormatMagpieQlCriteria = vi.mocked(formatMagpieQlCriteria);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('drops a stale resolution when a newer fingerprint has overtaken it', async () => {
        const initialCriteria: ContentFilter['criteria'] = {
            contentHierarchyItem: { and: [{ v: 'A' }] },
        };
        const updatedCriteria: ContentFilter['criteria'] = {
            contentHierarchyItem: { and: [{ v: 'B' }] },
        };

        let resolveA: ((value: { query: string }) => void) | undefined;
        let resolveB: ((value: { query: string }) => void) | undefined;
        const promiseA = new Promise<{ query: string }>((r) => {
            resolveA = r;
        });
        const promiseB = new Promise<{ query: string }>((r) => {
            resolveB = r;
        });

        mockFormatMagpieQlCriteria.mockReturnValueOnce(promiseA).mockReturnValueOnce(promiseB);

        let currentFilter: ContentFilter = {
            id: 'race-1',
            ownerUserId: null,
            name: 'Race',
            criteria: initialCriteria,
            ownerLock: null,
            createdAt: '2024-01-01T00:00:00Z',
            createdByUserId: null,
            modifiedAt: null,
            modifiedByUserId: null,
        };

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/content-filters') {
                return Promise.resolve([currentFilter]);
            }
            return Promise.resolve([]);
        });

        const { result } = renderHook(() => useContentFilters(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(mockFormatMagpieQlCriteria).toHaveBeenCalledTimes(1);
        });

        currentFilter = { ...currentFilter, criteria: updatedCriteria };

        await act(async () => {
            await result.current.refetch();
        });

        await waitFor(() => {
            expect(mockFormatMagpieQlCriteria).toHaveBeenCalledTimes(2);
        });

        await act(async () => {
            resolveB?.({ query: 'fresh-B' });
            await promiseB;
        });

        await waitFor(() => {
            const f = result.current.filters.find((x) => x.id === 'race-1');
            expect(f?.query).toBe('fresh-B');
        });

        await act(async () => {
            resolveA?.({ query: 'stale-A' });
            await promiseA;
        });

        const f = result.current.filters.find((x) => x.id === 'race-1');
        expect(f?.query).toBe('fresh-B');
    });
});

describe('useDeleteContentFilter', () => {
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should delete a filter', async () => {
        mockApiDelete.mockResolvedValue(undefined);

        const { result } = renderHook(() => useDeleteContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({ id: 'shared-1' });
        });

        expect(mockApiDelete).toHaveBeenCalledWith('/content-filters/shared-1');
    });
});
