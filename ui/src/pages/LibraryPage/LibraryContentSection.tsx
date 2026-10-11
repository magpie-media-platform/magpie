import { Link } from 'react-router-dom';
import type { PaginationState, SortingState, Updater } from '@tanstack/react-table';
import { FilmSlateIcon, FunnelIcon, MagnifyingGlassIcon, PlusIcon, WarningCircleIcon } from '@phosphor-icons/react';

import {
    ContentList,
    type ContentItem,
    type ContentHeaderFilters,
    type ContentHeaderFilterHandlers,
} from '@/components/ui/ContentList';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { Pager } from '@/components/ui/Pager';
import { Skeleton } from '@/components/ui/Skeleton';
import { MobileListToolbar, type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import type { BulkSelection } from '@/hooks/useBulkSelection';
import { isValidationError, getErrorMessage } from '@/lib/errors';
import type { ContentListItem } from '@/types';

import { LibraryAlphabetRail } from './LibraryAlphabetRail';
import { LibraryGalleryTile } from './LibraryGalleryTile';
import { LibraryGalleryToolbar } from './LibraryGalleryToolbar';
import { LibraryMobileCard } from './LibraryMobileCard';
import { PAGE_SIZE } from './libraryPageConstants';

const LIBRARY_SORT_OPTIONS: MobileListSortOption[] = [
    { id: 'title', label: 'Title' },
    { id: 'publishDate', label: 'Published', initialDirection: 'desc' },
    { id: 'dateAdded', label: 'Date Added', initialDirection: 'desc' },
    { id: 'fulfilledDate', label: 'Fulfilled', initialDirection: 'desc' },
    { id: 'primaryMediaSize', label: 'Media size', initialDirection: 'desc' },
    { id: 'primaryMediaFileCount', label: 'Media files', initialDirection: 'desc' },
];

interface LibraryContentSectionProps {
    isLoading: boolean;
    isPlaceholderData: boolean;
    contentTypeValidationPending: boolean;
    error: unknown;
    onRetry: () => void;
    onEditFilter: () => void;
    contentItems: ContentListItem[];
    totalItems: number;
    onContentClick: (item: ContentItem) => void;
    sorting: SortingState;
    onSortingChange: (updater: SortingState | ((prev: SortingState) => SortingState)) => void;
    pagination: PaginationState;
    onPaginationChange: (updater: Updater<PaginationState>) => void;
    headerFilters: ContentHeaderFilters;
    headerFilterHandlers: ContentHeaderFilterHandlers;
    titlePrefix: string;
    onTitlePrefixChange: (prefix: string) => void;
    hasBulkActions: boolean;
    selection: BulkSelection;
    selectionMode: boolean;
    onEnterSelectionMode: () => void;
    onExitSelectionMode: () => void;
    onBeginCardSelection: (id: string) => void;
    hasActiveSearchOrFilter: boolean;
    pageCount: number;
    currentPage: number;
    goToPage: (page: number) => void;
    canAddContent: boolean;
    addContentTo: string;
    onClearSearchAndFilters: () => void;
    view: 'list' | 'gallery';
}

export function LibraryContentSection({
    isLoading,
    isPlaceholderData,
    contentTypeValidationPending,
    error,
    onRetry,
    onEditFilter,
    contentItems,
    totalItems,
    onContentClick,
    sorting,
    onSortingChange,
    pagination,
    onPaginationChange,
    headerFilters,
    headerFilterHandlers,
    titlePrefix,
    onTitlePrefixChange,
    hasBulkActions,
    selection,
    selectionMode,
    onEnterSelectionMode,
    onExitSelectionMode,
    onBeginCardSelection,
    hasActiveSearchOrFilter,
    pageCount,
    currentPage,
    goToPage,
    canAddContent,
    addContentTo,
    onClearSearchAndFilters,
    view,
}: LibraryContentSectionProps) {
    const emptyState = hasActiveSearchOrFilter ? (
        <EmptyState
            icon={<MagnifyingGlassIcon weight="duotone" />}
            title="No results found"
            description="No content matches your current search or filters. Try adjusting your criteria or clear them to see all content."
            action={
                <Button variant="default" size="sm" onClick={onClearSearchAndFilters}>
                    Clear search and filters
                </Button>
            }
            size="sm"
        />
    ) : (
        <EmptyState
            icon={<FilmSlateIcon weight="duotone" />}
            title="Your library is empty"
            description="Added content will appear here. Use Add Content to start building the library."
            action={
                canAddContent ? (
                    <Button asChild variant="primary" size="sm">
                        <Link to={addContentTo} data-action="add-content">
                            <PlusIcon weight="bold" aria-hidden="true" />
                            Add content
                        </Link>
                    </Button>
                ) : undefined
            }
            size="sm"
        />
    );

    return (
        <section className="library-page__content" aria-label="Content list" aria-busy={isPlaceholderData || undefined}>
            {isLoading || contentTypeValidationPending ? (
                <div className="library-page__loading" data-testid="library-page__loading">
                    <Skeleton className="library-page__skeleton" />
                    <Skeleton className="library-page__skeleton" />
                    <Skeleton className="library-page__skeleton" />
                </div>
            ) : error ? (
                isValidationError(error) ? (
                    <EmptyState
                        icon={<FunnelIcon weight="duotone" />}
                        title="Invalid filter"
                        description={getErrorMessage(error)}
                        action={
                            <Button variant="secondary" size="sm" onClick={onEditFilter} data-action="edit-filter">
                                Edit filter
                            </Button>
                        }
                        size="sm"
                        data-testid="library-page__filter-error"
                    />
                ) : (
                    <EmptyState
                        icon={<WarningCircleIcon weight="duotone" />}
                        title="Failed to load content"
                        onRetry={onRetry}
                        size="sm"
                    />
                )
            ) : view === 'gallery' ? (
                <div className="library-page__gallery" data-testid="library-page__gallery">
                    <LibraryGalleryToolbar
                        sorting={sorting}
                        sortOptions={LIBRARY_SORT_OPTIONS}
                        onSortingChange={onSortingChange}
                        selectedCount={selection.selectedCount}
                        selection={
                            hasBulkActions
                                ? {
                                      active: selectionMode,
                                      onToggle: selectionMode ? onExitSelectionMode : onEnterSelectionMode,
                                  }
                                : undefined
                        }
                    />
                    <div className="library-page__gallery-body">
                        {contentItems.length === 0 ? (
                            <div className="library-page__gallery-empty">{emptyState}</div>
                        ) : (
                            <ul className="library-page__gallery-grid" role="list" aria-label="Content gallery">
                                {contentItems.map((item) => (
                                    <li key={item.id} className="library-page__gallery-cell">
                                        <LibraryGalleryTile
                                            item={item}
                                            selectionMode={hasBulkActions && selectionMode}
                                            selected={hasBulkActions ? selection.isRowChecked(item.id) : undefined}
                                            onToggle={
                                                hasBulkActions
                                                    ? () =>
                                                          selection.toggleRow(item.id, !selection.isRowChecked(item.id))
                                                    : undefined
                                            }
                                            onLongPress={
                                                hasBulkActions ? () => onBeginCardSelection(item.id) : undefined
                                            }
                                        />
                                    </li>
                                ))}
                            </ul>
                        )}
                        <LibraryAlphabetRail titlePrefix={titlePrefix} onTitlePrefixChange={onTitlePrefixChange} />
                    </div>
                    {pageCount > 1 && (
                        <Pager
                            className="library-page__gallery-pager"
                            page={currentPage}
                            pageCount={pageCount}
                            totalItems={totalItems}
                            onFirst={() => goToPage(1)}
                            onPrevious={() => goToPage(currentPage - 1)}
                            onNext={() => goToPage(currentPage + 1)}
                            onLast={() => goToPage(pageCount)}
                        />
                    )}
                </div>
            ) : (
                <>
                    <div className="library-page__table" data-empty={contentItems.length === 0 ? 'true' : undefined}>
                        <ContentList
                            items={contentItems}
                            onContentClick={onContentClick}
                            enableSorting={true}
                            manualSorting={true}
                            sorting={sorting}
                            onSortingChange={onSortingChange}
                            enablePagination={true}
                            manualPagination={true}
                            pagination={pagination}
                            onPaginationChange={onPaginationChange}
                            rowCount={totalItems}
                            pageSize={PAGE_SIZE}
                            selection={hasBulkActions ? selection : undefined}
                            headerFilters={headerFilters}
                            headerFilterHandlers={headerFilterHandlers}
                            emptyState={emptyState}
                            keepHeaderWhenEmpty={hasActiveSearchOrFilter}
                        />
                    </div>

                    {contentItems.length > 0 && (
                        <div className="library-page__mobile-list" data-testid="library-page__mobile-list">
                            <MobileListToolbar
                                className="library-page__mobile-toolbar"
                                sorting={sorting}
                                sortOptions={LIBRARY_SORT_OPTIONS}
                                onSortingChange={onSortingChange}
                                selection={
                                    hasBulkActions
                                        ? {
                                              active: selectionMode,
                                              onToggle: selectionMode ? onExitSelectionMode : onEnterSelectionMode,
                                          }
                                        : undefined
                                }
                            />
                            {contentItems.map((item) => (
                                <LibraryMobileCard
                                    key={item.id}
                                    item={item}
                                    selectionMode={hasBulkActions && selectionMode}
                                    selected={hasBulkActions ? selection.isRowChecked(item.id) : undefined}
                                    onToggle={
                                        hasBulkActions
                                            ? () => selection.toggleRow(item.id, !selection.isRowChecked(item.id))
                                            : undefined
                                    }
                                    onLongPress={hasBulkActions ? () => onBeginCardSelection(item.id) : undefined}
                                />
                            ))}
                            {pageCount > 1 && (
                                <Pager
                                    className="library-page__mobile-pager"
                                    page={currentPage}
                                    pageCount={pageCount}
                                    totalItems={totalItems}
                                    onFirst={() => goToPage(1)}
                                    onPrevious={() => goToPage(currentPage - 1)}
                                    onNext={() => goToPage(currentPage + 1)}
                                    onLast={() => goToPage(pageCount)}
                                />
                            )}
                        </div>
                    )}
                </>
            )}
        </section>
    );
}

LibraryContentSection.displayName = 'LibraryContentSection';

export default LibraryContentSection;
