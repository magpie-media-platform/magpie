import { describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';

import { useLibraryPagination } from './useLibraryPagination';
import { LIBRARY_FILTER_DEFAULTS, PAGE_SIZE, type LibraryFilterParams } from './libraryPageConstants';

function paramsWith(overrides: Partial<LibraryFilterParams>): LibraryFilterParams {
    return { ...LIBRARY_FILTER_DEFAULTS, ...overrides };
}

describe('useLibraryPagination', () => {
    it('derives a zero-based pagination state from the page param', () => {
        const { result } = renderHook(() =>
            useLibraryPagination({ params: paramsWith({ page: '3' }), updateParams: vi.fn() }),
        );

        expect(result.current.pageNumber).toBe(3);
        expect(result.current.pagination).toEqual({ pageIndex: 2, pageSize: PAGE_SIZE });
    });

    it.each(['0', '-2', 'abc', '1.5'])('falls back to page one for the invalid page param %s', (page) => {
        const { result } = renderHook(() =>
            useLibraryPagination({ params: paramsWith({ page }), updateParams: vi.fn() }),
        );

        expect(result.current.pageNumber).toBe(1);
        expect(result.current.pagination.pageIndex).toBe(0);
    });

    it('writes the one-based page back through updateParams on a pagination change', () => {
        const updateParams = vi.fn();
        const { result } = renderHook(() => useLibraryPagination({ params: paramsWith({ page: '1' }), updateParams }));

        act(() => result.current.handlePaginationChange({ pageIndex: 4, pageSize: PAGE_SIZE }));

        expect(updateParams).toHaveBeenCalledWith({ page: '5' }, { replace: true });
    });

    it('supports a functional updater against the current pagination state', () => {
        const updateParams = vi.fn();
        const { result } = renderHook(() => useLibraryPagination({ params: paramsWith({ page: '2' }), updateParams }));

        act(() => result.current.handlePaginationChange((prev) => ({ ...prev, pageIndex: prev.pageIndex + 1 })));

        expect(updateParams).toHaveBeenCalledWith({ page: '3' }, { replace: true });
    });

    it('navigates to a one-based page through goToPage', () => {
        const updateParams = vi.fn();
        const { result } = renderHook(() => useLibraryPagination({ params: paramsWith({ page: '1' }), updateParams }));

        act(() => result.current.goToPage(7));

        expect(updateParams).toHaveBeenCalledWith({ page: '7' }, { replace: true });
    });
});
