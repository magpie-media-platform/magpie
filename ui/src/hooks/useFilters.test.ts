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

vi.mock('@/lib/filterQuery', () => ({
    formatFilterCriteria: vi.fn(),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { formatFilterCriteria } from '@/lib/filterQuery';
import {
    useContentFilters,
    useCreateContentFilter,
    useUpdateContentFilter,
    useDeleteContentFilter,
    contentFilterQueryKeys,
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
    const mockFormatFilterCriteria = vi.mocked(formatFilterCriteria);

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

        mockFormatFilterCriteria.mockResolvedValue({ query: '' });
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

    it('should format filter queries via backend', async () => {
        mockFormatFilterCriteria.mockResolvedValueOnce({ query: 'genre = Action' });
        mockFormatFilterCriteria.mockResolvedValueOnce({ query: 'genre = Comedy and kind = Series' });
        mockFormatFilterCriteria.mockResolvedValueOnce({ query: 'rating >= 8.0' });

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

        expect(mockFormatFilterCriteria).toHaveBeenCalledTimes(3);
        expect(mockFormatFilterCriteria).toHaveBeenCalledWith(
            { contentHierarchyItem: { and: [{ genre: 'Action' }] } },
            'content',
        );
    });

    it('should gracefully handle format errors', async () => {
        mockFormatFilterCriteria.mockResolvedValue({ query: '' });

        const { result } = renderHook(() => useContentFilters(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.filters).toHaveLength(3);
        const filter = result.current.filters.find((f) => f.id === 'shared-1');
        expect(filter?.query).toBe('');
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

    it('should create a shared filter', async () => {
        mockApiPost.mockResolvedValue({ id: 'new-1', name: 'New Filter' });

        const { result } = renderHook(() => useCreateContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({
                name: 'New Filter',
                criteria: {},
                isShared: true,
            });
        });

        expect(mockApiPost).toHaveBeenCalledWith('/content-filters', {
            name: 'New Filter',
            criteria: {},
        });
    });

    it('should create a personal filter', async () => {
        mockApiPost.mockResolvedValue({ id: 'new-2', name: 'My Filter' });

        const { result } = renderHook(() => useCreateContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({
                name: 'My Filter',
                criteria: {},
                isShared: false,
            });
        });

        expect(mockApiPost).toHaveBeenCalledWith('/content-filters/my', {
            name: 'My Filter',
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

    it('should update a shared filter', async () => {
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useUpdateContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({
                id: 'shared-1',
                isShared: true,
                name: { value: 'Updated Name' },
            });
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/content-filters/shared-1', {
            name: { value: 'Updated Name' },
        });
    });

    it('should update a personal filter', async () => {
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useUpdateContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({
                id: 'my-1',
                isShared: false,
                ownerLock: { value: true },
            });
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/content-filters/my/my-1', {
            ownerLock: { value: true },
        });
    });
});

describe('contentFilterQueryKeys.preview', () => {
    it('produces different cache keys for shared vs personal previews with same filterId/page/pageSize', () => {
        const sharedKey = contentFilterQueryKeys.preview('filter-1', true, 1, 10);
        const personalKey = contentFilterQueryKeys.preview('filter-1', false, 1, 10);

        expect(sharedKey).not.toEqual(personalKey);
        // Sanity: shared/personal distinction is reflected in the key.
        expect(JSON.stringify(sharedKey)).not.toEqual(JSON.stringify(personalKey));
    });

    it('matches itself for the same isShared/page/pageSize', () => {
        const a = contentFilterQueryKeys.preview('filter-1', true, 1, 10);
        const b = contentFilterQueryKeys.preview('filter-1', true, 1, 10);
        expect(a).toEqual(b);
    });
});

describe('countCriteria (via useContentFilters)', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockFormatFilterCriteria = vi.mocked(formatFilterCriteria);

    beforeEach(() => {
        vi.clearAllMocks();
        mockFormatFilterCriteria.mockResolvedValue({ query: '' });
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
                    // Cast through unknown -- the runtime shape allows `or` even if
                    // the strict TS type primarily models `and`.
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
    const mockFormatFilterCriteria = vi.mocked(formatFilterCriteria);

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

        // First call returns promiseA (stale), second returns promiseB (fresh).
        mockFormatFilterCriteria.mockReturnValueOnce(promiseA).mockReturnValueOnce(promiseB);

        // First render: shared returns the filter with initial criteria.
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

        // Wait for the first format call to be kicked off.
        await waitFor(() => {
            expect(mockFormatFilterCriteria).toHaveBeenCalledTimes(1);
        });

        // Mutate the filter in place and trigger a refetch so the criteria fingerprint changes.
        currentFilter = { ...currentFilter, criteria: updatedCriteria };

        await act(async () => {
            await result.current.refetch();
        });

        // The new criteria should kick off a second format call.
        await waitFor(() => {
            expect(mockFormatFilterCriteria).toHaveBeenCalledTimes(2);
        });

        // Resolve the newer (B) request first so it commits.
        await act(async () => {
            resolveB?.({ query: 'fresh-B' });
            await promiseB;
        });

        await waitFor(() => {
            const f = result.current.filters.find((x) => x.id === 'race-1');
            expect(f?.query).toBe('fresh-B');
        });

        // Now resolve the stale (A) request -- it must NOT overwrite 'fresh-B'.
        await act(async () => {
            resolveA?.({ query: 'stale-A' });
            await promiseA;
        });

        // Give the microtask queue a tick.
        await new Promise((r) => setTimeout(r, 0));

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

    it('should delete a shared filter', async () => {
        mockApiDelete.mockResolvedValue(undefined);

        const { result } = renderHook(() => useDeleteContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({
                id: 'shared-1',
                isShared: true,
            });
        });

        expect(mockApiDelete).toHaveBeenCalledWith('/content-filters/shared-1');
    });

    it('should delete a personal filter', async () => {
        mockApiDelete.mockResolvedValue(undefined);

        const { result } = renderHook(() => useDeleteContentFilter(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.mutateAsync({
                id: 'my-1',
                isShared: false,
            });
        });

        expect(mockApiDelete).toHaveBeenCalledWith('/content-filters/my/my-1');
    });
});
