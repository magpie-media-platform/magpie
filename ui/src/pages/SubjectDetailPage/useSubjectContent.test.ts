import { act, renderHook, waitFor } from '@testing-library/react';
import { createElement } from 'react';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useContentList } from '@/hooks/useContent';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import type { ContentQueryParams } from '@/types';

import { useSubjectContent } from './useSubjectContent';

vi.mock('@/hooks/useContent', () => ({ useContentList: vi.fn() }));
vi.mock('@/hooks/useLibraryTypes', () => ({ useLibraryTypes: vi.fn() }));
vi.mock('@/hooks/ui/useCardLayout', () => ({ useCardLayout: vi.fn() }));

const mockUseContentList = vi.mocked(useContentList);
const mockUseLibraryTypes = vi.mocked(useLibraryTypes);
const mockUseCardLayout = vi.mocked(useCardLayout);

function lastParams(): ContentQueryParams {
    return mockUseContentList.mock.calls.at(-1)![0] as ContentQueryParams;
}

const CONTAINER_ID = '3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c';
const SCOPE = `Relationships has (RelatedMetadataContainerId = "${CONTAINER_ID}")`;

describe('useSubjectContent', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseCardLayout.mockReturnValue(false);
        mockUseLibraryTypes.mockReturnValue({ contentTypes: [] } as unknown as ReturnType<typeof useLibraryTypes>);
        mockUseContentList.mockReturnValue({
            data: { items: [], pageNumber: 1, pageSize: 25, totalItems: 75 },
            isLoading: false,
            error: null,
        } as unknown as ReturnType<typeof useContentList>);
    });

    it('Pagination_ReplacesTheInvalidEntry_WhenTheResultHasFewerPages', async () => {
        // Arrange
        mockUseContentList.mockReturnValue({
            data: { items: [], pageNumber: 99, pageSize: 25, totalItems: 30 },
            isLoading: false,
            error: null,
        } as unknown as ReturnType<typeof useContentList>);
        const { result } = renderHook(
            () => ({ content: useSubjectContent(CONTAINER_ID), navigate: useNavigate(), location: useLocation() }),
            {
                wrapper: ({ children }) =>
                    createElement(
                        MemoryRouter,
                        { initialEntries: ['/previous', '/?tab=content&contentPage=99'] },
                        children,
                    ),
            },
        );

        // Assert
        expect(result.current.content.currentPage).toBe(2);

        // Act
        await act(() => result.current.navigate(-1));

        // Assert
        expect(result.current.location.pathname).toBe('/previous');

        // Act
        await act(() => result.current.navigate(1));

        // Assert
        expect(result.current.location.search).toBe('?tab=content&contentPage=2');
    });

    it('UrlState_RestoresFiltersSortAndPage_WhenOpeningASharedUrl', () => {
        // Arrange
        const { result } = renderHook(() => useSubjectContent(CONTAINER_ID), {
            wrapper: ({ children }) =>
                createElement(
                    MemoryRouter,
                    {
                        initialEntries: [
                            '/?contentFilter=title%20contains%20%22Breaking%22&contentSort=title:asc&contentPage=3',
                        ],
                    },
                    children,
                ),
        });

        // Act
        const params = lastParams();

        // Assert
        expect(params.filter).toBe(`${SCOPE} and (title contains "Breaking")`);
        expect(params.pageNumber).toBe(3);
        expect(params.sortBy).toBe('Name');
        expect(params.sortDirection).toBe('Ascending');
        expect(result.current.headerFilters.title).toBe('Breaking');
        expect(result.current.currentPage).toBe(3);
    });

    it('UrlState_RestoresThePreviousSortAndPage_WhenNavigatingBack', async () => {
        // Arrange
        const { result } = renderHook(
            () => ({ content: useSubjectContent(CONTAINER_ID), navigate: useNavigate(), location: useLocation() }),
            {
                wrapper: ({ children }) =>
                    createElement(
                        MemoryRouter,
                        { initialEntries: ['/?tab=content&contentSort=title:asc&contentPage=3'] },
                        children,
                    ),
            },
        );

        // Act
        act(() => result.current.content.onSortingChange([{ id: 'publishDate', desc: true }]));

        // Assert
        expect(result.current.location.search).toBe('?tab=content');
        expect(result.current.content.currentPage).toBe(1);

        // Act
        await act(() => result.current.navigate(-1));

        // Assert
        expect(result.current.content.sorting).toEqual([{ id: 'title', desc: false }]);
        expect(result.current.content.currentPage).toBe(3);
        expect(lastParams().pageNumber).toBe(3);
    });

    it('UrlState_UpdatesTheFilterAndResetsOnlyContentPaging_WhenEditingAFilter', () => {
        // Arrange
        const { result } = renderHook(() => ({ content: useSubjectContent(CONTAINER_ID), location: useLocation() }), {
            wrapper: ({ children }) =>
                createElement(
                    MemoryRouter,
                    { initialEntries: ['/?tab=content&contentPage=3&releasesPage=2'] },
                    children,
                ),
        });

        // Act
        act(() => result.current.content.headerFilterHandlers.onTitleChange('Breaking'));

        // Assert
        const params = new URLSearchParams(result.current.location.search);
        expect(params.get('contentFilter')).toBe('title contains "Breaking"');
        expect(params.has('contentPage')).toBe(false);
        expect(params.get('releasesPage')).toBe('2');
        expect(params.get('tab')).toBe('content');
    });

    it.each([
        {
            action: 'applying',
            values: { title: 'Breaking', contentType: '["tv","tv-season"]', fulfillment: 'Fulfilled' },
            expectedQuery:
                'title contains "Breaking" and contentType = "tv" and level = "tv-season" and fulfillment = Fulfilled',
            expectedFilter:
                'Relationships has (RelatedMetadataContainerId = "3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c") and (title contains "Breaking" and contentType = "tv" and level = "tv-season" and fulfillment = Fulfilled)',
        },
        {
            action: 'clearing',
            values: { title: '', contentType: '', fulfillment: '' },
            expectedQuery: null,
            expectedFilter: 'Relationships has (RelatedMetadataContainerId = "3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c")',
        },
    ])(
        'FilterCommit_UpdatesAllFieldsInOneHistoryEntry_When$action',
        async ({ values, expectedQuery, expectedFilter }) => {
            // Arrange
            const { result } = renderHook(
                () => ({ content: useSubjectContent(CONTAINER_ID), navigate: useNavigate(), location: useLocation() }),
                {
                    wrapper: ({ children }) =>
                        createElement(
                            MemoryRouter,
                            {
                                initialEntries: [
                                    `/?${new URLSearchParams({
                                        contentFilter:
                                            'title contains "Old" and contentType = "movie" and fulfillment = Missing',
                                        contentPage: '3',
                                        contentSort: 'title:asc',
                                        tab: 'related',
                                        source: 'library',
                                    })}`,
                                ],
                            },
                            children,
                        ),
                },
            );

            // Act
            act(() => result.current.content.onFilterCommit(values));

            // Assert
            const params = new URLSearchParams(result.current.location.search);
            expect(params.get('contentFilter')).toBe(expectedQuery);
            expect(params.has('contentPage')).toBe(false);
            expect(params.get('contentSort')).toBe('title:asc');
            expect(params.get('tab')).toBe('related');
            expect(params.get('source')).toBe('library');
            await waitFor(() => expect(lastParams().filter).toBe(expectedFilter));

            // Act
            await act(() => result.current.navigate(-1));

            // Assert
            expect(result.current.content.headerFilters.title).toBe('Old');
            expect(result.current.content.headerFilters.contentType).toBe('["movie",null]');
            expect(result.current.content.headerFilters.fulfillment).toBe('Missing');
            expect(result.current.content.currentPage).toBe(3);
            await waitFor(() =>
                expect(lastParams().filter).toBe(
                    'Relationships has (RelatedMetadataContainerId = "3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c") and (title contains "Old" and contentType = "movie" and fulfillment = Missing)',
                ),
            );
        },
    );

    it('composes topLevelOnly, paging, and default publishDate-descending sort', () => {
        renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        expect(lastParams()).toEqual({
            topLevelOnly: true,
            pageNumber: 1,
            pageSize: 25,
            sortBy: 'PublishDate',
            sortDirection: 'Descending',
            filter: 'Relationships has (RelatedMetadataContainerId = "3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c")',
        });
    });

    it('scopes the MagpieQL filter to the metadata container of the subject', () => {
        renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        expect(lastParams().filter).toBe(
            'Relationships has (RelatedMetadataContainerId = "3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c")',
        );
    });

    it('translates the title filter into a title-contains MagpieQL criterion and resets to page 1', async () => {
        const { result } = renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        act(() => result.current.onPaginationChange({ pageIndex: 2, pageSize: 25 }));
        await waitFor(() => expect(lastParams().pageNumber).toBe(3));

        act(() => result.current.headerFilterHandlers.onTitleChange('Breaking'));

        await waitFor(() => expect(lastParams().filter).toBe(`${SCOPE} and (title contains "Breaking")`));
        expect(lastParams().pageNumber).toBe(1);
    });

    it('translates a broad type selection into a contentType MagpieQL criterion', async () => {
        const { result } = renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        act(() => result.current.headerFilterHandlers.onContentTypeChange(JSON.stringify(['movie', null])));

        await waitFor(() => expect(lastParams().filter).toBe(`${SCOPE} and (contentType = "movie")`));
    });

    it('translates a level-specific type selection into contentType and level MagpieQL criteria', async () => {
        const { result } = renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        act(() => result.current.headerFilterHandlers.onContentTypeChange(JSON.stringify(['tv', 'tv-season'])));

        await waitFor(() =>
            expect(lastParams().filter).toBe(`${SCOPE} and (contentType = "tv" and level = "tv-season")`),
        );
    });

    it('sends the status filter only inside MagpieQL', async () => {
        const { result } = renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        act(() => result.current.headerFilterHandlers.onFulfillmentChange('Missing'));

        await waitFor(() => expect(lastParams().filter).toBe(`${SCOPE} and (fulfillment = Missing)`));
        expect(lastParams().fulfillment).toBeUndefined();
    });

    it('composes title and status filters together', async () => {
        const { result } = renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        act(() => result.current.headerFilterHandlers.onTitleChange('Bad'));
        act(() => result.current.headerFilterHandlers.onFulfillmentChange('Fulfilled'));

        await waitFor(() => {
            const params = lastParams();
            expect(params.filter).toBe(`${SCOPE} and (title contains "Bad" and fulfillment = Fulfilled)`);
            expect(params.fulfillment).toBeUndefined();
        });
    });

    it('maps sorting to server fields and resets to page 1', async () => {
        const { result } = renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        act(() => result.current.onPaginationChange({ pageIndex: 1, pageSize: 25 }));
        await waitFor(() => expect(lastParams().pageNumber).toBe(2));

        act(() => result.current.onSortingChange([{ id: 'title', desc: false }]));

        await waitFor(() => {
            const params = lastParams();
            expect(params.sortBy).toBe('Name');
            expect(params.sortDirection).toBe('Ascending');
            expect(params.pageNumber).toBe(1);
        });
    });

    it('advances the requested page number when paging', async () => {
        const { result } = renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        act(() => result.current.onPaginationChange({ pageIndex: 2, pageSize: 25 }));

        await waitFor(() => expect(lastParams().pageNumber).toBe(3));
    });

    it('exposes the server total and derives the page count', () => {
        mockUseContentList.mockReturnValue({
            data: { items: [], pageNumber: 1, pageSize: 25, totalItems: 60 },
            isLoading: false,
            error: null,
        } as unknown as ReturnType<typeof useContentList>);

        const { result } = renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });

        expect(result.current.totalItems).toBe(60);
        expect(result.current.pageCount).toBe(3);
    });

    it('UseSubjectContent_PreservesFiltersAndPaging_WhenViewportChanges', async () => {
        // Arrange
        const { result, rerender } = renderHook(() => useSubjectContent(CONTAINER_ID), { wrapper: MemoryRouter });
        act(() => result.current.headerFilterHandlers.onTitleChange('zzz-nomatch'));
        await waitFor(() => expect(lastParams().filter).toBe(`${SCOPE} and (title contains "zzz-nomatch")`));
        act(() => result.current.onPaginationChange({ pageIndex: 1, pageSize: 25 }));

        // Act
        mockUseCardLayout.mockReturnValue(true);
        rerender();

        // Assert
        expect(lastParams().filter).toBe(`${SCOPE} and (title contains "zzz-nomatch")`);
        expect(lastParams().pageNumber).toBe(2);
        expect(result.current.hasActiveFilters).toBe(true);

        // Act
        mockUseCardLayout.mockReturnValue(false);
        rerender();

        // Assert
        expect(lastParams().filter).toBe(`${SCOPE} and (title contains "zzz-nomatch")`);
        expect(result.current.hasActiveFilters).toBe(true);
        expect(result.current.headerFilters.title).toBe('zzz-nomatch');
    });

    it('disables the query until a metadata container id is present', () => {
        renderHook(() => useSubjectContent(undefined), { wrapper: MemoryRouter });

        expect(mockUseContentList).toHaveBeenLastCalledWith(expect.anything(), { enabled: false });
    });
});
