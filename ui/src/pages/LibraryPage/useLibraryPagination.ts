import { useCallback, useMemo } from 'react';
import { type PaginationState, type Updater } from '@tanstack/react-table';

import { PAGE_SIZE, type LibraryFilterParams, type UpdateLibraryFilterParams } from './libraryPageConstants';

interface UseLibraryPaginationArgs {
    params: LibraryFilterParams;
    updateParams: UpdateLibraryFilterParams;
}

export function useLibraryPagination({ params, updateParams }: UseLibraryPaginationArgs) {
    const pageNumber = useMemo(() => {
        const raw = Number(params.page);
        return Number.isInteger(raw) && raw >= 1 ? raw : 1;
    }, [params.page]);

    const pagination = useMemo<PaginationState>(
        () => ({ pageIndex: pageNumber - 1, pageSize: PAGE_SIZE }),
        [pageNumber],
    );

    const handlePaginationChange = useCallback(
        (updater: Updater<PaginationState>) => {
            const next = typeof updater === 'function' ? updater(pagination) : updater;
            updateParams({ page: String(next.pageIndex + 1) }, { replace: true });
        },
        [pagination, updateParams],
    );

    const currentPage = pagination.pageIndex + 1;
    const goToPage = useCallback(
        (page: number) => handlePaginationChange({ pageIndex: page - 1, pageSize: PAGE_SIZE }),
        [handlePaginationChange],
    );

    return {
        pageNumber,
        pagination,
        handlePaginationChange,
        currentPage,
        goToPage,
    };
}
