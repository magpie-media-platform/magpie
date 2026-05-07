import { useState, useMemo, useCallback } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { FilmSlate, MagnifyingGlass, Star, Tag, WarningCircle, X } from '@phosphor-icons/react';
import { type RowSelectionState, type PaginationState } from '@tanstack/react-table';
import { useQueryClient } from '@tanstack/react-query';

import { ContentList, type ContentItem } from '@/components/ui/ContentList';
import {
    ContentSorting,
    type ContentSortState,
    type ContentSortField,
    type ContentSortDirection,
} from '@/components/ui/ContentSorting';
import { ContentSearch } from '@/components/ui/ContentSearch';
import { ContentListSlot } from '@/components/ui/Slot/ContentSlots';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import { BulkTagAssignment, type BulkTagItem, type BulkTagAssignmentResult } from '@/components/ui/BulkTagAssignment';
import { useToast } from '@/components/ui/Toast';
import { FilterBar } from '@/components/ui/FilterBar';
import { SaveFilterDialog } from '@/components/ui/SaveFilterDialog';
import { useContentList, useContentTags, contentQueryKeys, useBulkUpdateContent } from '@/hooks/useContent';
import { useContentFilters, useCreateContentFilter } from '@/hooks/useFilters';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { useAuth, Role } from '@/auth';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import { type ContentQueryParams } from '@/types';
import { parseFilterQuery } from '@/lib/filterQuery';

import '@/styles/bulk-actions-toolbar.css';
import './LibraryPage.css';

const VALID_SORT_FIELDS: ContentSortField[] = ['title', 'dateAdded', 'lastSearched'];

const VALID_SORT_DIRECTIONS: ContentSortDirection[] = ['asc', 'desc'];

const LIBRARY_FILTER_DEFAULTS = { filter: '', search: '', sort: 'title', sortDir: 'asc' };

const PAGE_SIZE = 50;

const SERVER_SORT_FIELD: Record<ContentSortField, string | null> = {
    title: 'Name',
    dateAdded: 'CreatedAt',
    lastSearched: null,
};

export function LibraryPage() {
    useDocumentTitle('Library');
    const { contentType: contentTypeSlug } = useParams<{ contentType?: string }>();
    const [params, updateParams] = useUrlFilterState(LIBRARY_FILTER_DEFAULTS);
    const navigate = useNavigate();
    const queryClient = useQueryClient();

    const filterQuery = params.filter;
    const [filterBarExpanded, setFilterBarExpanded] = useState(() => filterQuery.length > 0);
    const debouncedFilterQuery = useDebouncedValue(filterQuery, 300);

    const search = params.search;

    const sort: ContentSortState = useMemo(
        () => ({
            field:
                params.sort && VALID_SORT_FIELDS.includes(params.sort as ContentSortField)
                    ? (params.sort as ContentSortField)
                    : 'title',
            direction:
                params.sortDir && VALID_SORT_DIRECTIONS.includes(params.sortDir as ContentSortDirection)
                    ? (params.sortDir as ContentSortDirection)
                    : 'asc',
        }),
        [params.sort, params.sortDir],
    );
    const [rowSelection, setRowSelection] = useState<RowSelectionState>({});
    const [pagination, setPagination] = useState<PaginationState>({ pageIndex: 0, pageSize: PAGE_SIZE });
    const [bulkTagsOpen, , openBulkTags, closeBulkTags] = useToggle();
    const [saveFilterDialogOpen, , openSaveFilterDialog, closeSaveFilterDialog] = useToggle();
    const [saveFilterError, setSaveFilterError] = useState<string | null>(null);
    const { toast } = useToast();

    const { hasRole } = useAuth();
    const canAddContent = hasRole(Role.Requester);

    const { filters: savedContentFilters } = useContentFilters();
    const savedFilterOptions = useMemo(
        () => savedContentFilters.filter((f) => f.query).map((f) => ({ id: f.id, name: f.name, query: f.query })),
        [savedContentFilters],
    );

    const createFilterMutation = useCreateContentFilter();

    const bulkUpdateMutation = useBulkUpdateContent();

    const { contentTypes } = useLibraryTypes();

    const currentContentType = useMemo(() => {
        if (!contentTypeSlug) {
            return null;
        }
        return contentTypes.find((type) => type.slug === contentTypeSlug) ?? null;
    }, [contentTypeSlug, contentTypes]);

    const pageTitle = currentContentType?.name ?? 'Library';
    const PageIcon = FilmSlate;

    const queryParams = useMemo((): ContentQueryParams => {
        const params: ContentQueryParams = {
            page: pagination.pageIndex + 1,
            pageSize: PAGE_SIZE,
        };

        if (contentTypeSlug) {
            params.contentType = contentTypeSlug;
        }

        if (search.trim()) {
            params.search = search.trim();
        }

        if (debouncedFilterQuery.trim()) {
            params.filter = debouncedFilterQuery.trim();
        }

        const serverField = SERVER_SORT_FIELD[sort.field];
        if (serverField) {
            params.sortBy = serverField;
            params.sortDirection = sort.direction === 'asc' ? 'Ascending' : 'Descending';
        }

        return params;
    }, [contentTypeSlug, search, debouncedFilterQuery, sort, pagination.pageIndex]);

    const { data: contentResult, isLoading, error, refetch } = useContentList(queryParams);

    const { data: apiTags } = useContentTags();

    const bulkTags: BulkTagItem[] = useMemo(() => {
        if (!apiTags || !Array.isArray(apiTags)) {
            return [];
        }
        return apiTags.map((tag: { name: string }) => ({
            id: tag.name,
            name: tag.name,
        }));
    }, [apiTags]);

    const handleContentClick = useCallback(
        (item: ContentItem) => {
            navigate(`/library/${item.id}`);
        },
        [navigate],
    );

    const resetPagination = useCallback(() => {
        setPagination((prev) => (prev.pageIndex === 0 ? prev : { ...prev, pageIndex: 0 }));
    }, []);

    const handleFilterQueryChange = useCallback(
        (query: string) => {
            updateParams({ filter: query });
            if (query.length > 0) {
                setFilterBarExpanded(true);
            }
            setRowSelection({});
            resetPagination();
        },
        [updateParams, resetPagination],
    );

    const handleSortChange = useCallback(
        (newSort: ContentSortState) => {
            updateParams({ sort: newSort.field, sortDir: newSort.direction });
            resetPagination();
        },
        [updateParams, resetPagination],
    );

    const handleSearchChange = useCallback(
        (newSearch: string) => {
            updateParams({ search: newSearch });
            resetPagination();
        },
        [updateParams, resetPagination],
    );

    const handleRefresh = useCallback(async () => {
        await queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
    }, [queryClient]);

    const sortedItems = useMemo(() => {
        const items = contentResult?.items ?? [];

        if (sort.field !== 'lastSearched') {
            return items;
        }

        return [...items].sort((a, b) => {
            const searchA = a.lastSearched ? new Date(a.lastSearched).getTime() : 0;
            const searchB = b.lastSearched ? new Date(b.lastSearched).getTime() : 0;
            const comparison = searchA - searchB;
            return sort.direction === 'asc' ? comparison : -comparison;
        });
    }, [contentResult?.items, sort]);

    const selectedCount = Object.keys(rowSelection).filter((key) => rowSelection[key]).length;
    const hasSelection = selectedCount > 0;

    const selectedItems = useMemo(() => {
        const items = contentResult?.items ?? [];
        return items
            .filter((item) => rowSelection[item.id])
            .map((item) => ({
                id: item.id,
                title: item.title,
            }));
    }, [contentResult?.items, rowSelection]);

    const handleClearSelection = useCallback(() => {
        setRowSelection({});
    }, []);

    const handleOpenBulkTags = useCallback(() => {
        openBulkTags();
    }, [openBulkTags]);

    const handleApplyTags = useCallback(
        async (result: BulkTagAssignmentResult) => {
            const selectedIds = Object.keys(rowSelection).filter((key) => rowSelection[key]);

            try {
                const updateResult = await bulkUpdateMutation.mutateAsync({
                    ids: selectedIds,
                    tagsToAdd: result.tagsToAdd,
                    tagsToRemove: result.tagsToRemove,
                });

                const addCount = result.tagsToAdd.length;
                const removeCount = result.tagsToRemove.length;
                let description = '';
                if (addCount > 0 && removeCount > 0) {
                    description = `Added ${addCount} tag${addCount !== 1 ? 's' : ''}, removed ${removeCount} tag${removeCount !== 1 ? 's' : ''}`;
                } else if (addCount > 0) {
                    description = `Added ${addCount} tag${addCount !== 1 ? 's' : ''}`;
                } else if (removeCount > 0) {
                    description = `Removed ${removeCount} tag${removeCount !== 1 ? 's' : ''}`;
                }

                if (updateResult.failed > 0) {
                    toast({
                        title: `${updateResult.succeeded} of ${updateResult.total} items updated`,
                        description: `${updateResult.failed} item${updateResult.failed !== 1 ? 's' : ''} failed to update`,
                        variant: 'warning',
                    });
                } else {
                    toast({
                        title: `${selectedCount} item${selectedCount !== 1 ? 's' : ''} updated`,
                        description,
                        variant: 'success',
                    });
                }

                closeBulkTags();
                setRowSelection({});
            } catch {
                toast({
                    title: 'Failed to update tags',
                    description: 'All items failed to update. Please try again.',
                    variant: 'error',
                });
            }
        },
        [bulkUpdateMutation, closeBulkTags, rowSelection, selectedCount, toast],
    );

    const handleOpenSaveFilter = useCallback(() => {
        setSaveFilterError(null);
        openSaveFilterDialog();
    }, [openSaveFilterDialog]);

    const handleSaveFilter = useCallback(
        async (name: string) => {
            setSaveFilterError(null);

            const parseResult = await parseFilterQuery(filterQuery, 'content');

            if (parseResult.error) {
                setSaveFilterError(parseResult.error.message);
                return;
            }

            try {
                await createFilterMutation.mutateAsync({
                    name,
                    criteria: parseResult.criteria as {
                        contentHierarchyItem?: { and?: unknown[] };
                        mediaFiles?: { and?: unknown[] };
                    },
                    isShared: false,
                });

                closeSaveFilterDialog();
                toast({
                    title: 'Filter saved',
                    description: `"${name}" has been saved to your filters.`,
                    variant: 'success',
                });
            } catch {
                setSaveFilterError('Failed to save filter. Please try again.');
            }
        },
        [closeSaveFilterDialog, filterQuery, createFilterMutation, toast],
    );

    const contentItems: ContentItem[] = sortedItems;

    const hasActiveFilter = filterQuery.trim().length > 0;

    const hasActiveSearchOrFilter = search.trim().length > 0 || hasActiveFilter;

    const handleClearSearchAndFilters = useCallback(() => {
        updateParams({ search: '', filter: '' });
        setFilterBarExpanded(false);
        setRowSelection({});
        resetPagination();
    }, [updateParams, resetPagination]);

    const emptyState = hasActiveSearchOrFilter ? (
        <EmptyState
            icon={<MagnifyingGlass weight="duotone" />}
            title="No results found"
            description="No content matches your current search or filters. Try adjusting your criteria or clear them to see all content."
            action={
                <Button variant="default" size="sm" onClick={handleClearSearchAndFilters}>
                    Clear search and filters
                </Button>
            }
            size="sm"
        />
    ) : (
        <EmptyState
            icon={<FilmSlate weight="duotone" />}
            title="Your library is empty"
            description="Content will appear here once it has been added. Create a wishlist to start monitoring and tracking movies or TV shows."
            action={
                canAddContent ? (
                    <Button variant="primary" size="sm" onClick={() => navigate('/wishlists')}>
                        <Star weight="duotone" aria-hidden="true" />
                        Go to Wishlists
                    </Button>
                ) : undefined
            }
            size="sm"
        />
    );

    return (
        <div className="library-page" data-page="library">
            <PullToRefresh onRefresh={handleRefresh} className="library-page__pull-to-refresh">
                <ContentListSlot>
                    <header className="library-page__header">
                        <div className="library-page__header-left">
                            <PageIcon
                                weight="duotone"
                                className="library-page__icon"
                                aria-hidden="true"
                                data-testid="library-page__icon"
                            />
                            <h1 className="library-page__title">{pageTitle}</h1>
                        </div>
                        <div className="library-page__header-right">
                            <ContentSearch
                                value={search}
                                onChange={handleSearchChange}
                                placeholder="Search by title..."
                            />
                        </div>
                    </header>

                    <section className="library-page__controls" aria-label="Filters and sorting">
                        <div className="library-page__filters">
                            <FilterBar
                                value={filterQuery}
                                onChange={handleFilterQueryChange}
                                context="content"
                                showSave={hasActiveFilter}
                                onSave={handleOpenSaveFilter}
                                isSaving={createFilterMutation.isPending}
                                expanded={filterBarExpanded}
                                onExpandedChange={setFilterBarExpanded}
                                savedFilters={savedFilterOptions}
                            />
                        </div>

                        <div className="library-page__sorting">
                            <ContentSorting sort={sort} onChange={handleSortChange} />
                        </div>
                    </section>

                    {hasSelection && (
                        <div className="bulk-actions-toolbar" data-component="bulk-actions-toolbar">
                            <div className="bulk-actions-toolbar__info">
                                <span className="bulk-actions-toolbar__count">{selectedCount} selected</span>
                                <button
                                    type="button"
                                    className="bulk-actions-toolbar__clear"
                                    onClick={handleClearSelection}
                                    aria-label="Clear selection"
                                    data-action="clear-selection"
                                >
                                    <X weight="bold" aria-hidden="true" />
                                    Clear
                                </button>
                            </div>
                            <div className="bulk-actions-toolbar__buttons">
                                <Button
                                    variant="default"
                                    size="sm"
                                    onClick={handleOpenBulkTags}
                                    data-action="bulk-tags"
                                >
                                    <Tag weight="bold" aria-hidden="true" />
                                    <span className="bulk-actions-toolbar__btn-label">Assign Tags</span>
                                </Button>
                            </div>
                        </div>
                    )}

                    <section className="library-page__content" aria-label="Content list">
                        {isLoading ? (
                            <div className="library-page__loading" data-testid="library-page__loading">
                                <Skeleton className="library-page__skeleton" />
                                <Skeleton className="library-page__skeleton" />
                                <Skeleton className="library-page__skeleton" />
                            </div>
                        ) : error ? (
                            <EmptyState
                                icon={<WarningCircle weight="duotone" />}
                                title="Failed to load content"
                                description="Something went wrong. Please try again."
                                action={
                                    <Button variant="secondary" size="sm" onClick={() => refetch()}>
                                        Retry
                                    </Button>
                                }
                                size="sm"
                            />
                        ) : (
                            <ContentList
                                items={contentItems}
                                onContentClick={handleContentClick}
                                enableSorting={false}
                                enablePagination={true}
                                manualPagination={true}
                                pagination={pagination}
                                onPaginationChange={setPagination}
                                rowCount={contentResult?.totalItems ?? 0}
                                pageSize={PAGE_SIZE}
                                enableSelection={true}
                                rowSelection={rowSelection}
                                onRowSelectionChange={setRowSelection}
                                emptyState={emptyState}
                            />
                        )}
                    </section>
                </ContentListSlot>
            </PullToRefresh>

            <BulkTagAssignment
                open={bulkTagsOpen}
                onOpenChange={(open) => !open && closeBulkTags()}
                availableTags={bulkTags}
                selectedItems={selectedItems}
                onApply={handleApplyTags}
                isApplying={bulkUpdateMutation.isPending}
            />

            <SaveFilterDialog
                open={saveFilterDialogOpen}
                onOpenChange={(open) => !open && closeSaveFilterDialog()}
                onSave={handleSaveFilter}
                isSaving={createFilterMutation.isPending}
                filterQuery={filterQuery}
                error={saveFilterError}
            />
        </div>
    );
}

LibraryPage.displayName = 'LibraryPage';

export default LibraryPage;
