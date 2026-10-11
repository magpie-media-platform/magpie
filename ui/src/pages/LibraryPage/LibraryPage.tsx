import { Link } from 'react-router-dom';
import { ArrowLeftIcon, GridFourIcon, ListIcon } from '@phosphor-icons/react';

import { BulkActionsToolbar } from '@/components/ui/BulkActionsToolbar';
import { Slot } from '@/components/ui/Slot';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { PageHeader } from '@/components/ui/PageHeader';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/Tabs';
import { TABLE_LAYOUT_QUERY } from '@/hooks/ui/useCardLayout';
import { usePersistentState } from '@/hooks/usePersistentState';

import { LibraryTypePicker } from './LibraryTypePicker';
import { LibraryFilters } from './LibraryFilters';
import { LibraryBulkActionButtons } from './LibraryBulkActionButtons';
import { LibraryMobileSelectionBar } from './LibraryMobileSelectionBar';
import { LibraryContentSection } from './LibraryContentSection';
import { LibraryDialogs } from './LibraryDialogs';
import { useLibraryPage } from './useLibraryPage';

import '@/styles/bulk-actions-toolbar.css';
import './LibraryPage.css';

export function LibraryPage() {
    const library = useLibraryPage();
    const { route, filters, content, bulk, pagination, selection } = library;
    const { currentContentType, selectedLevel, routeContentTypeSlug, pageTitle, pageIconName } = route;
    const [storedView, setStoredView] = usePersistentState<string>('library-view', 'list');
    const view = storedView === 'gallery' ? 'gallery' : 'list';

    if (route.hasInvalidContentTypeSlug) {
        return null;
    }

    if (route.showTypePicker && currentContentType) {
        return <LibraryTypePicker contentType={currentContentType} rootLevels={route.rootLevels} />;
    }

    return (
        <div className="library-page" data-page="library">
            <PullToRefresh onRefresh={library.handleRefresh} className="library-page__pull-to-refresh">
                <Slot slot="content.list">
                    {selectedLevel && routeContentTypeSlug && (
                        <Link
                            to={`/library/content/${routeContentTypeSlug}`}
                            className="library-page__back-link"
                            data-action="back-to-categories"
                        >
                            <ArrowLeftIcon weight="bold" aria-hidden="true" />
                            Back to {currentContentType?.name ?? 'categories'}
                        </Link>
                    )}
                    <PageHeader
                        className="library-page__header"
                        title={pageTitle}
                        description={
                            library.allLevels ? 'Includes matching child items, not only top-level items.' : undefined
                        }
                        icon={
                            <CdnPhosphorIcon
                                name={pageIconName}
                                weight="duotone"
                                aria-hidden="true"
                                data-testid="library-page__icon"
                            />
                        }
                        actions={
                            <Tabs value={view} onValueChange={setStoredView}>
                                <TabsList
                                    className="library-page__view-switch"
                                    aria-label="Content view"
                                    data-testid="library-page__view-switch"
                                >
                                    <TabsTrigger value="list" data-action="view-list">
                                        <ListIcon weight="bold" aria-hidden="true" />
                                        List
                                    </TabsTrigger>
                                    <TabsTrigger value="gallery" data-action="view-gallery">
                                        <GridFourIcon weight="bold" aria-hidden="true" />
                                        Gallery
                                    </TabsTrigger>
                                </TabsList>
                            </Tabs>
                        }
                    />

                    <LibraryFilters
                        filterQuery={filters.filterQuery}
                        isRestoringFilter={filters.isRestoringFilter}
                        filterRestoreError={filters.filterRestoreError}
                        onRetrySavedFilter={filters.retrySavedFilters}
                        onClearFilters={filters.handleClearSearchAndFilters}
                        onFilterQueryChange={filters.handleFilterQueryChange}
                        showSaveControl={filters.showSaveControl}
                        saveControlLabel={filters.saveControlLabel}
                        onOpenSaveFilter={filters.handleOpenSaveFilter}
                        isSavingFilter={filters.isSavingFilter}
                        filterBarExpanded={filters.filterBarExpanded}
                        onFilterBarExpandedChange={filters.setFilterBarExpanded}
                        tableLayoutQuery={TABLE_LAYOUT_QUERY}
                        defaultContentType={routeContentTypeSlug}
                        restrictContentType={routeContentTypeSlug}
                        lockContentLevel={selectedLevel?.slug}
                        referencedSavedFilter={filters.referencedSavedFilter}
                        isAppliedFilterDirty={filters.isAppliedFilterDirty}
                        allowTitlePrefix={view === 'gallery'}
                    />

                    {library.hasBulkActions && (
                        <BulkActionsToolbar
                            selection={selection.selection}
                            label="Library selection actions"
                            itemNounPlural="items"
                            onClear={selection.selection.clear}
                        >
                            <LibraryBulkActionButtons
                                canEditContent={library.canEditContent}
                                canManageContent={library.canManageContent}
                                canDeleteContent={library.canDeleteContent}
                                onOpenBulkTags={bulk.handleOpenBulkTags}
                                onBulkSetMonitoring={bulk.handleBulkSetMonitoring}
                                isSettingMonitoring={bulk.isSettingMonitoring}
                                onOpenBulkRootFolder={bulk.openBulkRootFolder}
                                onOpenBulkQualityTierList={bulk.openBulkQualityTierList}
                                onOpenBulkDelete={bulk.openBulkDeleteConfirm}
                            />
                        </BulkActionsToolbar>
                    )}

                    {!filters.filterRestoreError && (
                        <LibraryContentSection
                            isLoading={library.isLoading}
                            isPlaceholderData={content.isPlaceholderData}
                            contentTypeValidationPending={route.contentTypeValidationPending}
                            error={content.error}
                            onRetry={() => content.refetch()}
                            onEditFilter={() => filters.setFilterBarExpanded(true)}
                            contentItems={content.contentItems}
                            totalItems={content.totalItems}
                            onContentClick={route.handleContentClick}
                            sorting={filters.sorting}
                            onSortingChange={filters.handleSortingChange}
                            pagination={pagination.pagination}
                            onPaginationChange={pagination.handlePaginationChange}
                            headerFilters={filters.headerFilters}
                            headerFilterHandlers={filters.headerFilterHandlers}
                            titlePrefix={filters.titlePrefix}
                            onTitlePrefixChange={filters.handleTitlePrefixChange}
                            hasBulkActions={library.hasBulkActions}
                            selection={selection.selection}
                            selectionMode={library.selectionMode}
                            onEnterSelectionMode={library.enterSelectionMode}
                            onExitSelectionMode={library.exitSelectionMode}
                            onBeginCardSelection={library.beginCardSelection}
                            hasActiveSearchOrFilter={filters.hasActiveSearchOrFilter}
                            pageCount={library.pageCount}
                            currentPage={pagination.pageNumber}
                            goToPage={pagination.goToPage}
                            canAddContent={library.canAddContent}
                            addContentTo={route.addContentTo}
                            onClearSearchAndFilters={filters.handleClearSearchAndFilters}
                            view={view}
                        />
                    )}
                </Slot>
            </PullToRefresh>

            {library.hasBulkActions && library.selectionMode && (
                <LibraryMobileSelectionBar
                    selection={selection.selection}
                    onClearSelection={selection.selection.clear}
                    canEditContent={library.canEditContent}
                    canManageContent={library.canManageContent}
                    canDeleteContent={library.canDeleteContent}
                    onOpenBulkTags={bulk.handleOpenBulkTags}
                    onBulkSetMonitoring={bulk.handleBulkSetMonitoring}
                    isSettingMonitoring={bulk.isSettingMonitoring}
                    onOpenBulkRootFolder={bulk.openBulkRootFolder}
                    onOpenBulkQualityTierList={bulk.openBulkQualityTierList}
                    onOpenBulkDelete={bulk.openBulkDeleteConfirm}
                />
            )}

            <LibraryDialogs
                bulkTagsOpen={bulk.bulkTagsOpen}
                onCloseBulkTags={bulk.closeBulkTags}
                bulkTags={bulk.bulkTags}
                selectedItems={selection.selectedItems}
                selectedCount={selection.selection.selectedCount}
                onApplyTags={bulk.handleApplyTags}
                isApplyingTags={bulk.isApplyingTags}
                bulkRootFolderOpen={bulk.bulkRootFolderOpen}
                onCloseBulkRootFolder={bulk.closeBulkRootFolder}
                rootFolderOptions={bulk.rootFolderOptions}
                onApplyRootFolder={bulk.handleApplyRootFolder}
                isApplyingRootFolder={bulk.isApplyingRootFolder}
                bulkQualityTierListOpen={bulk.bulkQualityTierListOpen}
                onCloseBulkQualityTierList={bulk.closeBulkQualityTierList}
                qualityTierListOptions={bulk.qualityTierListOptions}
                onApplyQualityTierList={bulk.handleApplyQualityTierList}
                isApplyingQualityTierList={bulk.isApplyingQualityTierList}
                saveFilterDialogOpen={filters.saveFilterDialogOpen}
                onCloseSaveFilter={filters.closeSaveFilterDialog}
                onSaveFilter={filters.handleSaveFilter}
                canOfferUpdate={filters.canOfferUpdate}
                onUpdateFilter={filters.handleUpdateFilter}
                referencedSavedFilter={filters.referencedSavedFilter}
                isSavingFilter={filters.isSavingFilter}
                isUpdatingFilter={filters.isUpdatingFilter}
                filterQuery={filters.filterQuery}
                saveFilterError={filters.saveFilterError}
                saveFilterQueryError={filters.saveFilterQueryError}
                existingFilterNames={filters.savedContentFilters.map((f) => f.name)}
                bulkDeleteConfirmOpen={bulk.bulkDeleteConfirmOpen}
                onConfirmBulkDelete={bulk.handleConfirmBulkDelete}
                onCancelBulkDelete={bulk.closeBulkDeleteConfirm}
                bulkDeleteFiles={bulk.bulkDeleteFiles}
                onBulkDeleteFilesChange={bulk.setBulkDeleteFiles}
                bulkDeletePreview={bulk.bulkDeletePreview}
                isDeleting={bulk.isDeleting}
            />
        </div>
    );
}

LibraryPage.displayName = 'LibraryPage';

export default LibraryPage;
