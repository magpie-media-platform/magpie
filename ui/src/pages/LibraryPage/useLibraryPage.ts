import { useCallback, useEffect, useState } from 'react';

import { useAuth, Role } from '@/auth';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';

import { LIBRARY_FILTER_DEFAULTS, PAGE_SIZE } from './libraryPageConstants';
import { useLibraryRoute } from './useLibraryRoute';
import { useLibraryPagination } from './useLibraryPagination';
import { useLibraryFilters } from './useLibraryFilters';
import { useLibraryContent } from './useLibraryContent';
import { useLibrarySelection } from './useLibrarySelection';
import { useLibraryBulkActions } from './useLibraryBulkActions';

export function useLibraryPage() {
    useDocumentTitle('Library');

    const [params, updateParams] = useUrlFilterState(LIBRARY_FILTER_DEFAULTS);

    const { hasRole, hasPermission, user } = useAuth();
    const canEditContent = hasPermission('content.edit');
    const canAddContent = hasPermission('content.add');
    const canDeleteContent = hasPermission('content.delete');
    const canManageContent = hasRole(Role.Manager);
    const route = useLibraryRoute();

    const pagination = useLibraryPagination({ params, updateParams });
    const filters = useLibraryFilters({
        params,
        updateParams,
        contentTypes: route.contentTypes,
        routeContentTypeSlug: route.routeContentTypeSlug,
        selectedLevel: route.selectedLevel,
        user,
        isAdmin: hasRole(Role.Admin),
        canWriteFilters: hasPermission('filters.own'),
    });
    const allLevels = params.levels === 'all' && filters.filterQuery.trim() !== '';
    const content = useLibraryContent({
        contentTypeSlug: route.contentTypeSlug,
        currentContentType: route.currentContentType,
        routeContentTypeSlug: route.routeContentTypeSlug,
        selectedLevel: route.selectedLevel,
        allLevels,
        libraryTypesError: route.libraryTypesError,
        isRestoringFilter: filters.isRestoringFilter,
        debouncedFilterQuery: filters.debouncedFilterQuery,
        filterQuery: filters.filterQuery,
        sortField: filters.sortField,
        sortDir: filters.sortDir,
        pageIndex: pagination.pagination.pageIndex,
    });
    const hasBulkActions = !filters.isRestoringFilter && (canEditContent || canManageContent || canDeleteContent);
    const selection = useLibrarySelection({
        pageRowIds: content.pageRowIds,
        totalItems: content.totalItems,
        contentItems: content.contentItems,
        resetKey: [
            route.routeContentTypeSlug,
            route.selectedLevel?.slug,
            allLevels,
            params.savedFilter,
            filters.filterQuery.trim(),
        ].join('\u0000'),
    });

    const bulk = useLibraryBulkActions({
        selection: selection.selection,
        bulkFilter: content.bulkFilter,
    });

    const [selectionMode, setSelectionMode] = useState(false);
    const enterSelectionMode = useCallback(() => setSelectionMode(true), []);
    const exitSelectionMode = useCallback(() => {
        setSelectionMode(false);
        selection.selection.clear();
    }, [selection.selection]);
    const beginCardSelection = useCallback(
        (id: string) => {
            setSelectionMode(true);
            selection.selection.toggleRow(id, true);
        },
        [selection.selection],
    );

    const pageCount = Math.max(1, Math.ceil(content.totalItems / PAGE_SIZE));
    const { pageNumber, goToPage } = pagination;
    useEffect(() => {
        if (!content.isLoading && !content.isPlaceholderData && !filters.isRestoringFilter && pageNumber > pageCount) {
            goToPage(pageCount);
        }
    }, [content.isLoading, content.isPlaceholderData, filters.isRestoringFilter, pageNumber, pageCount, goToPage]);

    return {
        route,
        filters,
        content,
        pagination,
        selection,
        bulk,
        allLevels,
        handleRefresh: filters.isRestoringFilter ? filters.retrySavedFilters : content.handleRefresh,
        canEditContent,
        canAddContent,
        canManageContent,
        canDeleteContent,
        hasBulkActions,
        isLoading: content.isLoading || filters.isRestoringFilter,
        pageCount,
        selectionMode,
        enterSelectionMode,
        exitSelectionMode,
        beginCardSelection,
    };
}
