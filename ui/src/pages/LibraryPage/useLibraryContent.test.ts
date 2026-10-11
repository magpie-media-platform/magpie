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

    it('hides cached unrestricted rows while a saved filter is unresolved', () => {
        mockUseContentList.mockReturnValue({
            data: { items: [{ id: 'unfiltered-row' }], totalItems: 1 },
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });

        const { result } = renderHook(() =>
            useLibraryContent({
                contentTypeSlug: undefined,
                currentContentType: null,
                routeContentTypeSlug: undefined,
                selectedLevel: null,
                libraryTypesError: null,
                isRestoringFilter: true,
                debouncedFilterQuery: '',
                filterQuery: '',
                sortField: 'title',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        expect(mockUseContentList).toHaveBeenLastCalledWith(expect.anything(), {
            enabled: false,
            keepPreviousData: true,
        });
        expect(result.current.contentItems).toEqual([]);
        expect(result.current.pageRowIds).toEqual([]);
        expect(result.current.totalItems).toBe(0);
    });

    it('useLibraryContent_ReturnsNoPageRowIds_WhenPreviousRowsShowForANewFilter', () => {
        // Arrange
        mockUseContentList.mockReturnValue({
            data: { items: [{ id: 'previous-row' }], totalItems: 1 },
            isLoading: false,
            isPlaceholderData: true,
            error: null,
            refetch: vi.fn(),
        });

        // Act
        const { result } = renderHook(() =>
            useLibraryContent({
                contentTypeSlug: undefined,
                currentContentType: null,
                routeContentTypeSlug: undefined,
                selectedLevel: null,
                libraryTypesError: null,
                debouncedFilterQuery: '',
                filterQuery: '',
                sortField: 'title',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        // Assert
        expect(result.current.isPlaceholderData).toBe(true);
        expect(result.current.pageRowIds).toEqual([]);
        expect(result.current.contentItems).toHaveLength(1);
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
                filterQuery: '',
                sortField: 'name',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        expect(result.current.bulkFilter).toBe('ParentId null');
    });

    it('queries and selects matching items at every level when the all-levels scope is chosen', () => {
        const { result } = renderHook(() =>
            useLibraryContent({
                contentTypeSlug: undefined,
                currentContentType: null,
                routeContentTypeSlug: undefined,
                selectedLevel: null,
                allLevels: true,
                libraryTypesError: null,
                debouncedFilterQuery: 'title contains "dark"',
                filterQuery: 'title contains "dark"',
                sortField: 'title',
                sortDir: 'asc',
                pageIndex: 1,
            }),
        );

        expect(mockUseContentList).toHaveBeenLastCalledWith(
            {
                pageNumber: 2,
                pageSize: 50,
                filter: 'title contains "dark"',
                sortBy: 'Name',
                sortDirection: 'Ascending',
            },
            { enabled: true, keepPreviousData: true },
        );
        expect(result.current.bulkFilter).toBe('(title contains "dark")');
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
                filterQuery: '',
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
            { enabled: true, keepPreviousData: true },
        );
    });

    it('sends fulfilled-date sorting to the server', () => {
        renderHook(() =>
            useLibraryContent({
                contentTypeSlug: undefined,
                currentContentType: null,
                routeContentTypeSlug: undefined,
                selectedLevel: null,
                libraryTypesError: null,
                debouncedFilterQuery: '',
                filterQuery: '',
                sortField: 'fulfilledDate',
                sortDir: 'desc',
                pageIndex: 0,
            }),
        );

        expect(mockUseContentList).toHaveBeenLastCalledWith(
            expect.objectContaining({ sortBy: 'FulfilledAt', sortDirection: 'Descending' }),
            { enabled: true, keepPreviousData: true },
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
                filterQuery: '',
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
                debouncedFilterQuery: 'fulfillment = Missing',
                filterQuery: 'fulfillment = Missing',
                sortField: 'name',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        expect(result.current.bulkFilter).toBe('ParentId null and ContentType = "tv" and (fulfillment = Missing)');
        expect(mockUseContentList.mock.calls.at(-1)?.[0].fulfillment).toBeUndefined();
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
                filterQuery: '  Name contains "wire" or Monitoring = true  ',
                sortField: 'name',
                sortDir: 'asc',
                pageIndex: 0,
            }),
        );

        expect(result.current.bulkFilter).toBe(
            'ParentId null and ContentType = "tv" and (Name contains "wire" or Monitoring = true)',
        );
    });

    it('useLibraryContent_UsesCurrentFilterForBulkScope_WhenRequestFilterIsSettling', () => {
        // Arrange
        const args = {
            contentTypeSlug: 'tv',
            currentContentType: { id: 'tv', name: 'TV', pluralName: 'TV', slug: 'tv', levels: [] },
            routeContentTypeSlug: 'tv',
            selectedLevel: null,
            libraryTypesError: null,
            debouncedFilterQuery: 'Name contains "older"',
            filterQuery: 'Name contains "latest"',
            sortField: 'name',
            sortDir: 'asc',
            pageIndex: 0,
        };

        // Act
        const { result } = renderHook(() => useLibraryContent(args));

        // Assert
        expect(mockUseContentList).toHaveBeenLastCalledWith(
            expect.objectContaining({ filter: 'Name contains "older"' }),
            { enabled: true, keepPreviousData: true },
        );
        expect(result.current.bulkFilter).toBe('ParentId null and ContentType = "tv" and (Name contains "latest")');
    });
});
