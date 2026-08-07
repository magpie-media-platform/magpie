import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';

import { useLibraryContent } from './useLibraryContent';

const mockUseContentList = vi.fn();

vi.mock('@/hooks/useContent', () => ({
    useContentList: (params: unknown, options: unknown) => mockUseContentList(params, options),
    contentQueryKeys: { all: ['content'] },
}));

vi.mock('@tanstack/react-query', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@tanstack/react-query')>();
    return { ...actual, useQueryClient: () => ({ invalidateQueries: vi.fn() }) };
});

describe('useLibraryContent bulk filter', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseContentList.mockReturnValue({
            data: { items: [], pageNumber: 1, pageSize: 25, totalItems: 0 },
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });
    });

    it('restricts the selection to top-level rows when no scope is chosen', () => {
        const { result } = renderHook(() =>
            useLibraryContent({
                contentTypeSlug: undefined,
                currentContentType: null,
                routeContentTypeSlug: undefined,
                selectedLevel: null,
                libraryTypesError: null,
                debouncedFilterQuery: '',
                fulfillment: '',
                sortField: 'name',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        expect(result.current.bulkFilter).toBe('ParentId null');
    });

    it('queries a top-level library route by content type instead of parent id', () => {
        renderHook(() =>
            useLibraryContent({
                contentTypeSlug: 'widget',
                currentContentType: {
                    id: 'widget',
                    name: 'Widget',
                    pluralName: 'Widgets',
                    slug: 'widget',
                    levels: [],
                },
                routeContentTypeSlug: 'widget',
                selectedLevel: null,
                libraryTypesError: null,
                debouncedFilterQuery: '',
                fulfillment: '',
                sortField: 'title',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        expect(mockUseContentList).toHaveBeenLastCalledWith(
            {
                pageNumber: 1,
                pageSize: 50,
                topLevelOnly: true,
                contentType: 'widget',
                sortBy: 'Name',
                sortDirection: 'Ascending',
            },
            { enabled: true },
        );
    });

    it('expresses the content type and level scope as magpieql clauses', () => {
        const { result } = renderHook(() =>
            useLibraryContent({
                contentTypeSlug: 'kind-x',
                currentContentType: null,
                routeContentTypeSlug: 'kind-x',
                selectedLevel: {
                    slug: 'kind-x-scene',
                    displayName: 'Scene',
                    parentSlug: null,
                    standardFactFields: [],
                },
                libraryTypesError: null,
                debouncedFilterQuery: '',
                fulfillment: '',
                sortField: 'name',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        expect(result.current.bulkFilter).toBe('ParentId null and ContentType = "kind-x" and Level = "kind-x-scene"');
    });

    it('appends the fulfillment status as a bare enum identifier', () => {
        const { result } = renderHook(() =>
            useLibraryContent({
                contentTypeSlug: 'tv',
                currentContentType: null,
                routeContentTypeSlug: 'tv',
                selectedLevel: null,
                libraryTypesError: null,
                debouncedFilterQuery: '',
                fulfillment: 'Missing',
                sortField: 'name',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        expect(result.current.bulkFilter).toBe('ParentId null and ContentType = "tv" and Fulfillment = Missing');
    });

    it('wraps the typed filter query so its operators cannot escape the scope', () => {
        const { result } = renderHook(() =>
            useLibraryContent({
                contentTypeSlug: 'tv',
                currentContentType: null,
                routeContentTypeSlug: 'tv',
                selectedLevel: null,
                libraryTypesError: null,
                debouncedFilterQuery: '  Name contains "wire" or Monitoring = true  ',
                fulfillment: '',
                sortField: 'name',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        expect(result.current.bulkFilter).toBe(
            'ParentId null and ContentType = "tv" and (Name contains "wire" or Monitoring = true)',
        );
    });
});
