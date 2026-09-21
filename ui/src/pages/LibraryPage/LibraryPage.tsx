import { Link } from 'react-router-dom';
import { ArrowLeftIcon, GridFourIcon, ListIcon } from '@phosphor-icons/react';

import { BulkActionsToolbar } from '@/components/ui/BulkActionsToolbar';
import { ContentListSlot } from '@/components/ui/Slot/ContentSlots';
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
    const { currentContentType, selectedLevel, routeContentTypeSlug, pageTitle, pageIconName } = library;
    const [storedView, setStoredView] = usePersistentState<string>('library-view', 'list');
    const view = storedView === 'gallery' ? 'gallery' : 'list';

    if (library.hasInvalidContentTypeSlug) {
        return null;
    }

    if (library.showTypePicker && currentContentType) {
        return <LibraryTypePicker contentType={currentContentType} rootLevels={library.rootLevels} />;
    }

    return (
        <div className="library-page" data-page="library">
            <PullToRefresh onRefresh={library.handleRefresh} className="library-page__pull-to-refresh">
                <ContentListSlot>
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
                        filterQuery={library.filterQuery}
                        onFilterQueryChange={library.handleFilterQueryChange}
                        showSaveControl={library.showSaveControl}
                        saveControlLabel={library.saveControlLabel}
                        onOpenSaveFilter={library.handleOpenSaveFilter}
                        isSavingFilter={library.isSavingFilter}
                        filterBarExpanded={library.filterBarExpanded}
                        onFilterBarExpandedChange={library.setFilterBarExpanded}
                        tableLayoutQuery={TABLE_LAYOUT_QUERY}
                        defaultContentType={routeContentTypeSlug}
                        restrictContentType={routeContentTypeSlug}
                        lockContentLevel={selectedLevel?.slug}
                        referencedSavedFilter={library.referencedSavedFilter}
                        isAppliedFilterDirty={library.isAppliedFilterDirty}
                    />

                    {library.hasBulkActions && (
                        <BulkActionsToolbar
                            selection={library.selection}
                            label="Library selection actions"
                            itemNounPlural="items"
                            onClear={library.handleClearSelection}
                        >
                            <LibraryBulkActionButtons
                                canEditContent={library.canEditContent}
                                canManageContent={library.canManageContent}
                                canDeleteContent={library.canDeleteContent}
                                onOpenBulkTags={library.handleOpenBulkTags}
                                onBulkSetMonitoring={library.handleBulkSetMonitoring}
                                isSettingMonitoring={library.isSettingMonitoring}
                                onOpenBulkRootFolder={library.openBulkRootFolder}
                                onOpenBulkQualityTierList={library.openBulkQualityTierList}
                                onOpenBulkDelete={library.openBulkDeleteConfirm}
                            />
                        </BulkActionsToolbar>
                    )}

                    <LibraryContentSection
                        isLoading={library.isLoading}
                        contentTypeValidationPending={library.contentTypeValidationPending}
                        error={library.error}
                        onRetry={() => library.refetch()}
                        onEditFilter={() => library.setFilterBarExpanded(true)}
                        contentItems={library.contentItems}
                        totalItems={library.totalItems}
                        onContentClick={library.handleContentClick}
                        sorting={library.sorting}
                        onSortingChange={library.handleSortingChange}
                        pagination={library.pagination}
                        onPaginationChange={library.handlePaginationChange}
                        headerFilters={library.headerFilters}
                        headerFilterHandlers={library.headerFilterHandlers}
                        hasBulkActions={library.hasBulkActions}
                        selection={library.selection}
                        selectionMode={library.selectionMode}
                        onEnterSelectionMode={library.enterSelectionMode}
                        onExitSelectionMode={library.exitSelectionMode}
                        onBeginCardSelection={library.beginCardSelection}
                        hasActiveSearchOrFilter={library.hasActiveSearchOrFilter}
                        pageCount={library.pageCount}
                        currentPage={library.currentPage}
                        goToPage={library.goToPage}
                        canEditContent={library.canEditContent}
                        addContentTo={library.addContentTo}
                        onClearSearchAndFilters={library.handleClearSearchAndFilters}
                        view={view}
                    />
                </ContentListSlot>
            </PullToRefresh>

            {library.hasBulkActions && library.selectionMode && (
                <LibraryMobileSelectionBar
                    selection={library.selection}
                    onClearSelection={library.handleClearSelection}
                    canEditContent={library.canEditContent}
                    canManageContent={library.canManageContent}
                    canDeleteContent={library.canDeleteContent}
                    onOpenBulkTags={library.handleOpenBulkTags}
                    onBulkSetMonitoring={library.handleBulkSetMonitoring}
                    isSettingMonitoring={library.isSettingMonitoring}
                    onOpenBulkRootFolder={library.openBulkRootFolder}
                    onOpenBulkQualityTierList={library.openBulkQualityTierList}
                    onOpenBulkDelete={library.openBulkDeleteConfirm}
                />
            )}

            <LibraryDialogs
                bulkTagsOpen={library.bulkTagsOpen}
                onCloseBulkTags={library.closeBulkTags}
                bulkTags={library.bulkTags}
                selectedItems={library.selectedItems}
                selectedCount={library.selectedCount}
                onApplyTags={library.handleApplyTags}
                isApplyingTags={library.isApplyingTags}
                bulkRootFolderOpen={library.bulkRootFolderOpen}
                onCloseBulkRootFolder={library.closeBulkRootFolder}
                rootFolderOptions={library.rootFolderOptions}
                onApplyRootFolder={library.handleApplyRootFolder}
                isApplyingRootFolder={library.isApplyingRootFolder}
                bulkQualityTierListOpen={library.bulkQualityTierListOpen}
                onCloseBulkQualityTierList={library.closeBulkQualityTierList}
                qualityTierListOptions={library.qualityTierListOptions}
                onApplyQualityTierList={library.handleApplyQualityTierList}
                isApplyingQualityTierList={library.isApplyingQualityTierList}
                saveFilterDialogOpen={library.saveFilterDialogOpen}
                onCloseSaveFilter={library.closeSaveFilterDialog}
                onSaveFilter={library.handleSaveFilter}
                canOfferUpdate={library.canOfferUpdate}
                onUpdateFilter={library.handleUpdateFilter}
                referencedSavedFilter={library.referencedSavedFilter}
                isSavingFilter={library.isSavingFilter}
                isUpdatingFilter={library.isUpdatingFilter}
                filterQuery={library.filterQuery}
                saveFilterError={library.saveFilterError}
                existingFilterNames={library.savedContentFilters.map((f) => f.name)}
                bulkDeleteConfirmOpen={library.bulkDeleteConfirmOpen}
                onConfirmBulkDelete={library.handleConfirmBulkDelete}
                onCancelBulkDelete={library.closeBulkDeleteConfirm}
                bulkDeleteFiles={library.bulkDeleteFiles}
                onBulkDeleteFilesChange={library.setBulkDeleteFiles}
                isDeleting={library.isDeleting}
            />
        </div>
    );
}

LibraryPage.displayName = 'LibraryPage';

export default LibraryPage;
