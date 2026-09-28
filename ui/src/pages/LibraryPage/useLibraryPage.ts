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

export { PAGE_SIZE } from './libraryPageConstants';
export type { SavedFilterReference } from './libraryPageConstants';

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
    // An empty bulk filter selects nothing, so the all-levels scope needs a filter to bound it.
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
        contentType: route.contentTypeSlug,
        level: route.selectedLevel?.slug,
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
        if (!content.isLoading && !filters.isRestoringFilter && pageNumber > pageCount) {
            goToPage(pageCount);
        }
    }, [content.isLoading, filters.isRestoringFilter, pageNumber, pageCount, goToPage]);

    return {
        hasInvalidContentTypeSlug: route.hasInvalidContentTypeSlug,
        showTypePicker: route.showTypePicker,
        currentContentType: route.currentContentType,
        rootLevels: route.rootLevels,
        selectedLevel: route.selectedLevel,
        routeContentTypeSlug: route.routeContentTypeSlug,
        pageTitle: route.pageTitle,
        pageIconName: route.pageIconName,
        allLevels,
        handleRefresh: filters.isRestoringFilter ? filters.retrySavedFilters : content.handleRefresh,

        canEditContent,
        canAddContent,
        canManageContent,
        canDeleteContent,
        hasBulkActions,

        filterQuery: filters.filterQuery,
        isRestoringFilter: filters.isRestoringFilter,
        filterRestoreError: filters.filterRestoreError,
        retrySavedFilters: filters.retrySavedFilters,
        handleFilterQueryChange: filters.handleFilterQueryChange,
        showSaveControl: filters.showSaveControl,
        saveControlLabel: filters.saveControlLabel,
        handleOpenSaveFilter: filters.handleOpenSaveFilter,
        filterBarExpanded: filters.filterBarExpanded,
        setFilterBarExpanded: filters.setFilterBarExpanded,
        referencedSavedFilter: filters.referencedSavedFilter,
        isAppliedFilterDirty: filters.isAppliedFilterDirty,
        hasActiveSearchOrFilter: filters.hasActiveSearchOrFilter,
        handleClearSearchAndFilters: filters.handleClearSearchAndFilters,
        addContentTo: route.addContentTo,

        isLoading: content.isLoading || filters.isRestoringFilter,
        contentTypeValidationPending: route.contentTypeValidationPending,
        error: content.error,
        refetch: content.refetch,
        contentItems: content.contentItems,
        totalItems: content.totalItems,
        handleContentClick: route.handleContentClick,
        sorting: filters.sorting,
        handleSortingChange: filters.handleSortingChange,
        pagination: pagination.pagination,
        handlePaginationChange: pagination.handlePaginationChange,
        headerFilters: filters.headerFilters,
        headerFilterHandlers: filters.headerFilterHandlers,
        pageCount,
        currentPage: pagination.currentPage,
        goToPage: pagination.goToPage,

        selection: selection.selection,
        selectedItems: selection.selectedItems,
        selectedCount: selection.selectedCount,
        hasSelection: selection.hasSelection,
        handleClearSelection: selection.handleClearSelection,

        selectionMode,
        enterSelectionMode,
        exitSelectionMode,
        beginCardSelection,

        bulkTags: bulk.bulkTags,
        rootFolderOptions: bulk.rootFolderOptions,
        qualityTierListOptions: bulk.qualityTierListOptions,
        handleOpenBulkTags: bulk.handleOpenBulkTags,
        handleApplyTags: bulk.handleApplyTags,
        handleBulkSetMonitoring: bulk.handleBulkSetMonitoring,
        handleConfirmBulkDelete: bulk.handleConfirmBulkDelete,
        handleApplyRootFolder: bulk.handleApplyRootFolder,
        handleApplyQualityTierList: bulk.handleApplyQualityTierList,
        openBulkRootFolder: bulk.openBulkRootFolder,
        openBulkQualityTierList: bulk.openBulkQualityTierList,
        openBulkDeleteConfirm: bulk.openBulkDeleteConfirm,

        bulkTagsOpen: bulk.bulkTagsOpen,
        closeBulkTags: bulk.closeBulkTags,
        bulkRootFolderOpen: bulk.bulkRootFolderOpen,
        closeBulkRootFolder: bulk.closeBulkRootFolder,
        bulkQualityTierListOpen: bulk.bulkQualityTierListOpen,
        closeBulkQualityTierList: bulk.closeBulkQualityTierList,
        saveFilterDialogOpen: filters.saveFilterDialogOpen,
        closeSaveFilterDialog: filters.closeSaveFilterDialog,
        bulkDeleteConfirmOpen: bulk.bulkDeleteConfirmOpen,
        closeBulkDeleteConfirm: bulk.closeBulkDeleteConfirm,
        bulkDeleteFiles: bulk.bulkDeleteFiles,
        setBulkDeleteFiles: bulk.setBulkDeleteFiles,

        canOfferUpdate: filters.canOfferUpdate,
        handleSaveFilter: filters.handleSaveFilter,
        handleUpdateFilter: filters.handleUpdateFilter,
        saveFilterError: filters.saveFilterError,
        saveFilterQueryError: filters.saveFilterQueryError,
        savedContentFilters: filters.savedContentFilters,

        isSavingFilter: filters.isSavingFilter,
        isUpdatingFilter: filters.isUpdatingFilter,
        isApplyingTags: bulk.isApplyingTags,
        isSettingMonitoring: bulk.isSettingMonitoring,
        isDeleting: bulk.isDeleting,
        isApplyingRootFolder: bulk.isApplyingRootFolder,
        isApplyingQualityTierList: bulk.isApplyingQualityTierList,
    };
}
