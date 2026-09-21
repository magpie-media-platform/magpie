import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { MagnifyingGlassIcon, TargetIcon, WarningCircleIcon } from '@phosphor-icons/react';
import { useQuery } from '@tanstack/react-query';
import { type ColumnDef, type RowSelectionState, type SortingState } from '@tanstack/react-table';

import { ContentPoster } from '@/components/content/ContentCardPrimitives';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { AcquisitionBadge } from '@/components/ui/AcquisitionBadge';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { Button } from '@/components/ui/Button';
import { selectColumn } from '@/components/ui/DataTable/selectColumn';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { DataTable } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { MobileListToolbar, type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pager } from '@/components/ui/Pager';
import { BulkActionsToolbar } from '@/components/ui/BulkActionsToolbar';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/Toast';
import { useAuth } from '@/auth';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { useContentAncestors, useSearchContentReleases } from '@/hooks/useContent';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useLibraryTypes, type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { fetchWantedChildren, useWantedContent, wantedQueryKeys } from '@/hooks/useWantedContent';
import { resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import { formatShortDate } from '@/lib/dateFormatting';
import { formatRelativeTime } from '@/lib/formatTime';
import { getFulfillmentBadge } from '@/types/content';
import type { ContentListItem, FulfillmentStatus } from '@/types';

import { countMissingDescendants, resolveWantedSearchLeaves } from './wantedSearch';

import './WantedPage.css';

const PAGE_SIZE = 25;

const DEFAULT_SORTING: SortingState = [{ id: 'publishDate', desc: true }];

const SORT_KEYS = ['title', 'contentType', 'fulfillment', 'publishDate', 'lastSearched'] as const;

const WANTED_SORT_OPTIONS: MobileListSortOption[] = [
    { id: 'title', label: 'Title' },
    { id: 'contentType', label: 'Type' },
    { id: 'fulfillment', label: 'Status' },
    { id: 'publishDate', label: 'Published', initialDirection: 'desc' },
    { id: 'lastSearched', label: 'Last Searched', initialDirection: 'desc' },
];

function TitleCell({
    item,
    to,
    contentTypes,
    linked = true,
    qualify = true,
}: {
    item: ContentListItem;
    to: string;
    contentTypes: LibraryTypeDefinition[];
    linked?: boolean;
    qualify?: boolean;
}) {
    const content = (
        <ContentLineageTitle
            qualifiedName={{ id: item.id, parentId: item.parentId, title: item.title }}
            fallback={{ contentType: item.contentType, level: item.level }}
            qualify={qualify}
        />
    );

    return (
        <div className="wanted-page__title-cell">
            {linked ? (
                <Link to={to} className="wanted-page__title-link" data-content-id={item.id}>
                    {content}
                </Link>
            ) : (
                <span className="wanted-page__title-link" data-content-id={item.id}>
                    {content}
                </span>
            )}
            <MissingDescendantCount item={item} contentTypes={contentTypes} />
        </div>
    );
}

function typeLabel(item: ContentListItem, contentTypes: LibraryTypeDefinition[]): string {
    return resolveContentTypeLabel(
        { level: item.level, levelLabel: item.levelLabel, contentType: item.contentType },
        contentTypes,
    );
}

function TypeCell({ item, contentTypes }: { item: ContentListItem; contentTypes: LibraryTypeDefinition[] }) {
    const label = typeLabel(item, contentTypes);
    if (!label) {
        return <span className="wanted-page__empty-cell">-</span>;
    }
    return (
        <Badge variant="default" size="sm">
            {label}
        </Badge>
    );
}

function FulfillmentCell({ fulfillment }: { fulfillment: FulfillmentStatus }) {
    const badge = getFulfillmentBadge(fulfillment);
    return (
        <Badge variant={badge.variant} size="sm" dot>
            {badge.label}
        </Badge>
    );
}

function LastSearchedCell({ value }: { value?: string | null }) {
    return <span className="wanted-page__mobile-last-searched">{formatRelativeTime(value, 'Never')}</span>;
}

function PublishedCell({ value }: { value?: string }) {
    return <span className="wanted-page__published">{formatShortDate(value, '--')}</span>;
}

function isContainerItem(item: ContentListItem, contentTypes: LibraryTypeDefinition[]): boolean {
    const contentType = contentTypes.find((type) => type.slug === item.contentType);
    return contentType?.levels.some((level) => level.parentSlug === item.level) ?? false;
}

function MissingDescendantCount({
    item,
    contentTypes,
}: {
    item: ContentListItem;
    contentTypes: LibraryTypeDefinition[];
}) {
    const contentId = item.id;
    const containerLevels = contentTypes.flatMap((type) =>
        type.levels.flatMap((level) => (level.parentSlug ? [{ contentType: type.slug, level: level.parentSlug }] : [])),
    );
    const container = isContainerItem(item, contentTypes);
    const query = useQuery({
        queryKey: [...wantedQueryKeys.missingDescendants(contentId), containerLevels],
        queryFn: () =>
            countMissingDescendants(
                contentId,
                (candidate) =>
                    containerLevels.some(
                        (entry) => entry.contentType === candidate.contentType && entry.level === candidate.level,
                    ),
                fetchWantedChildren,
            ),
        enabled: container,
        staleTime: 30_000,
    });

    if (!container || query.data === undefined || query.data === 0) {
        return null;
    }

    return (
        <span className="wanted-page__descendant-count">
            {query.data} missing {query.data === 1 ? 'descendant' : 'descendants'}
        </span>
    );
}

function isActionableLeaf(item: ContentListItem, isContainer: boolean): boolean {
    return !isContainer && (item.fulfillment === 'missing' || item.fulfillment === 'upgradable');
}

function sortValue(item: ContentListItem, id: string): string | number | undefined {
    switch (id) {
        case 'title':
            return item.title;
        case 'contentType':
            return item.contentType;
        case 'fulfillment':
            return item.fulfillment;
        case 'publishDate': {
            const value = item.publishDate ? new Date(item.publishDate).getTime() : undefined;
            return value !== undefined && !Number.isNaN(value) ? value : undefined;
        }
        case 'lastSearched': {
            const value = item.lastSearched ? new Date(item.lastSearched).getTime() : undefined;
            return value !== undefined && !Number.isNaN(value) ? value : undefined;
        }
        default:
            return undefined;
    }
}

function sortWantedItems(items: ContentListItem[], sorting: SortingState): ContentListItem[] {
    const [sort] = sorting;
    if (!sort) {
        return items;
    }

    return [...items].sort((left, right) => {
        const leftValue = sortValue(left, sort.id);
        const rightValue = sortValue(right, sort.id);
        if (leftValue === undefined) {
            return rightValue === undefined ? 0 : 1;
        }
        if (rightValue === undefined) {
            return -1;
        }
        const comparison =
            typeof leftValue === 'number' && typeof rightValue === 'number'
                ? leftValue - rightValue
                : String(leftValue).localeCompare(String(rightValue));
        return sort.desc ? -comparison : comparison;
    });
}

export function WantedPage() {
    useDocumentTitle('Wanted');
    const { toast } = useToast();
    const { hasPermission } = useAuth();
    const canSearch = hasPermission('content.request');
    const [searchParams] = useSearchParams();
    const usesCardLayout = useCardLayout();
    const parentId = searchParams.get('parentId') || undefined;
    const {
        items,
        upgradeItems,
        isLoading: isWantedLoading,
        isError,
        isTruncated,
        totalItems,
        refetch,
    } = useWantedContent(parentId);
    const { ancestors } = useContentAncestors(parentId);
    const { contentTypes, isLoading: areContentTypesLoading } = useLibraryTypes();
    const isLoading = isWantedLoading || areContentTypesLoading;
    const searchMutation = useSearchContentReleases();
    const [isSearching, setIsSearching] = useState(false);
    const [mobileSelectionMode, setMobileSelectionMode] = useState(false);

    const { sorting, filters, page, setSorting, setFilters, setPage } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: { type: '' },
        sortKeys: SORT_KEYS,
    });
    const typeFilter = filters.type;
    const setTypeFilter = useCallback((value: string) => setFilters({ type: value }), [setFilters]);

    const isContainer = useCallback((item: ContentListItem) => isContainerItem(item, contentTypes), [contentTypes]);

    const wantedItems = useMemo(() => {
        const stagedItems = items.filter((item) => item.fulfillment === 'missing' || isContainer(item));
        const ids = new Set(stagedItems.map((item) => item.id));
        const upgrades = upgradeItems.filter((item) => !isContainer(item) && !ids.has(item.id));
        return [...stagedItems, ...upgrades];
    }, [items, upgradeItems, isContainer]);

    const typeOptions = useMemo<FacetOption[]>(() => {
        const labels = new Set<string>();
        for (const item of wantedItems) {
            const label = typeLabel(item, contentTypes);
            if (label) {
                labels.add(label);
            }
        }
        return [...labels].sort((a, b) => a.localeCompare(b)).map((label) => ({ value: label, label }));
    }, [wantedItems, contentTypes]);

    const filteredItems = useMemo(() => {
        if (!typeFilter) {
            return wantedItems;
        }
        return wantedItems.filter((item) => typeLabel(item, contentTypes) === typeFilter);
    }, [wantedItems, contentTypes, typeFilter]);

    const sortedItems = useMemo(() => sortWantedItems(filteredItems, sorting), [filteredItems, sorting]);

    const hasActiveFilters = typeFilter !== '';

    const handleClearFilters = useCallback(() => {
        setTypeFilter('');
    }, [setTypeFilter]);

    const pageCount = Math.max(1, Math.ceil(sortedItems.length / PAGE_SIZE));
    const currentPage = Math.min(page, pageCount);

    useEffect(() => {
        if (!isLoading && page > pageCount) {
            setPage(pageCount);
        }
    }, [isLoading, page, pageCount, setPage]);

    const pagination = useMemo(() => ({ pageIndex: currentPage - 1, pageSize: PAGE_SIZE }), [currentPage]);
    const pagedItems = useMemo(
        () => sortedItems.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE),
        [sortedItems, currentPage],
    );
    const pageRowIds = useMemo(() => pagedItems.map((item) => item.id), [pagedItems]);
    const selection = useBulkSelection({ pageRowIds, totalCount: sortedItems.length });
    const clearSelection = selection.clear;
    const selectedItems = useMemo(
        () => sortedItems.filter((item) => selection.isRowChecked(item.id)),
        [selection, sortedItems],
    );
    const selectedIdSet = useMemo(() => new Set(selectedItems.map((item) => item.id)), [selectedItems]);

    useEffect(() => {
        clearSelection();
        setMobileSelectionMode(false);
    }, [clearSelection, parentId, typeFilter]);

    const wantedUrl = useCallback(
        (scopeId?: string) => {
            const params = new URLSearchParams(searchParams);
            if (scopeId) {
                params.set('parentId', scopeId);
            } else {
                params.delete('parentId');
            }
            params.delete('page');
            const query = params.toString();
            return query ? `/wanted?${query}` : '/wanted';
        },
        [searchParams],
    );

    const destinationFor = useCallback(
        (item: ContentListItem) => (isContainer(item) ? wantedUrl(item.id) : `/library/${item.id}`),
        [isContainer, wantedUrl],
    );

    const breadcrumbs = useMemo<BreadcrumbItem[]>(() => {
        if (!parentId || ancestors.length === 0) {
            return [];
        }
        return [
            { id: 'wanted', label: 'Wanted', to: wantedUrl() },
            ...ancestors.map((item) => ({ id: item.id, label: item.title, to: wantedUrl(item.id) })),
        ];
    }, [ancestors, parentId, wantedUrl]);

    const handleRowSelectionChange = useCallback(
        (updater: RowSelectionState | ((previous: RowSelectionState) => RowSelectionState)) => {
            const current = selection.rowSelection;
            const next = typeof updater === 'function' ? updater(current) : updater;
            const changed = pageRowIds.filter((id) => selection.isRowChecked(id) !== Boolean(next[id]));
            if (changed.length === 0) {
                return;
            }
            if (changed.length > 1) {
                selection.toggleHeader(Boolean(next[changed[0]]));
                return;
            }
            const id = changed[0];
            selection.toggleRow(id, Boolean(next[id]));
        },
        [pageRowIds, selection],
    );

    const toggleMobileSelection = useCallback(
        (item: ContentListItem) => {
            selection.toggleRow(item.id, !selection.isRowChecked(item.id));
        },
        [selection],
    );

    const handleClearSelection = useCallback(() => {
        clearSelection();
        setMobileSelectionMode(false);
    }, [clearSelection]);

    const handleSearchNow = useCallback(
        async (targets: ContentListItem[]) => {
            if (targets.length === 0 || isSearching) {
                return;
            }

            setIsSearching(true);
            try {
                const resolution = await resolveWantedSearchLeaves(targets, isContainer, fetchWantedChildren);
                if (resolution.leafIds.length === 0) {
                    const activeSearchCountLabel = `${resolution.skippedActiveCount} eligible ${resolution.skippedActiveCount === 1 ? 'leaf is' : 'leaves are'}`;
                    toast({
                        title: 'No eligible searches',
                        description:
                            resolution.skippedActiveCount > 0
                                ? `${activeSearchCountLabel} already being searched.`
                                : 'The selected items do not contain searchable wanted content.',
                        variant: 'default',
                    });
                    return;
                }

                let queuedCount = 0;
                let failedCount = 0;
                const skippedCount = resolution.skippedActiveCount;
                for (const id of resolution.leafIds) {
                    try {
                        await searchMutation.mutateAsync({ id, mode: 'Queue' });
                        queuedCount += 1;
                    } catch {
                        failedCount += 1;
                    }
                }

                if (queuedCount > 0) {
                    const details = [
                        `${queuedCount} leaf ${queuedCount === 1 ? 'search' : 'searches'} queued.`,
                        skippedCount > 0
                            ? `${skippedCount} ${skippedCount === 1 ? 'leaf was' : 'leaves were'} skipped.`
                            : '',
                        failedCount > 0
                            ? `${failedCount} ${failedCount === 1 ? 'search failed' : 'searches failed'}.`
                            : '',
                    ].filter(Boolean);
                    toast({
                        title: failedCount > 0 ? 'Searches partially queued' : 'Searches queued',
                        description: details.join(' '),
                        variant: failedCount > 0 ? 'warning' : 'success',
                    });
                    if (failedCount === 0) {
                        handleClearSelection();
                    }
                    await refetch();
                } else {
                    toast({
                        title: 'Searches failed',
                        description: 'No searches could be queued. Try again.',
                        variant: 'error',
                    });
                }
            } catch {
                toast({
                    title: 'Search failed',
                    description: 'Failed to resolve the selected wanted content. Please try again.',
                    variant: 'error',
                });
            } finally {
                setIsSearching(false);
            }
        },
        [handleClearSelection, isContainer, isSearching, refetch, searchMutation, toast],
    );

    const columns: ColumnDef<ContentListItem>[] = useMemo(() => {
        const selectionColumn: ColumnDef<ContentListItem> = selectColumn<ContentListItem>({
            selection,
            getId: (item) => item.id,
            headerLabel: 'Select all visible wanted items',
            rowLabel: (item) => item.title,
        });

        return [
            ...(canSearch ? [selectionColumn] : []),
            {
                id: 'poster',
                accessorKey: 'posterUrl',
                header: '',
                enableSorting: false,
                size: 48,
                cell: ({ row }) => (
                    <ContentPoster
                        posterUrl={row.original.posterUrl}
                        title={row.original.title}
                        className="wanted-page__poster"
                        placeholderClassName="wanted-page__poster--placeholder"
                    />
                ),
            },
            {
                id: 'title',
                accessorKey: 'title',
                header: 'Title',
                size: 300,
                cell: ({ row }) => (
                    <TitleCell
                        item={row.original}
                        to={destinationFor(row.original)}
                        contentTypes={contentTypes}
                        qualify={!parentId}
                    />
                ),
            },
            {
                id: 'contentType',
                accessorKey: 'contentType',
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="Type"
                        active={typeFilter !== ''}
                        options={typeOptions}
                        value={typeFilter}
                        onChange={setTypeFilter}
                    />
                ),
                meta: { headerLabel: 'Type' },
                size: 130,
                cell: ({ row }) => <TypeCell item={row.original} contentTypes={contentTypes} />,
            },
            {
                id: 'fulfillment',
                accessorKey: 'fulfillment',
                header: 'Status',
                size: 120,
                cell: ({ row }) => <FulfillmentCell fulfillment={row.original.fulfillment} />,
            },
            {
                id: 'publishDate',
                accessorFn: (row) => {
                    if (!row.publishDate) {
                        return undefined;
                    }
                    const time = new Date(row.publishDate).getTime();
                    return isNaN(time) ? undefined : time;
                },
                header: 'Published',
                size: 130,
                sortUndefined: 'last',
                cell: ({ row }) => <PublishedCell value={row.original.publishDate} />,
            },
            {
                id: 'lastSearched',
                accessorKey: 'lastSearched',
                header: 'Last Searched',
                size: 130,
                cell: ({ row }) => <LastSearchedCell value={row.original.lastSearched} />,
            },
            {
                id: 'action',
                header: '',
                enableSorting: false,
                size: 150,
                cell: ({ row }) => {
                    const item = row.original;
                    const container = isContainer(item);
                    if (!canSearch) {
                        return item.acquisitionStatus ? <AcquisitionBadge status={item.acquisitionStatus} /> : null;
                    }
                    if (!container && item.acquisitionStatus) {
                        return <AcquisitionBadge status={item.acquisitionStatus} />;
                    }
                    if (!container && !isActionableLeaf(item, container)) {
                        return null;
                    }
                    const searchButton = (
                        <Button
                            variant="secondary"
                            size="sm"
                            onClick={(event) => {
                                event.stopPropagation();
                                void handleSearchNow([item]);
                            }}
                            loading={isSearching}
                            disabled={isSearching}
                            aria-label={
                                container
                                    ? `Search now for descendants of ${item.title}`
                                    : `Search now for ${item.title}`
                            }
                            data-action="search-now"
                            data-content-id={item.id}
                        >
                            <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                            Search Now
                        </Button>
                    );
                    return container && item.acquisitionStatus ? (
                        <div className="wanted-page__parent-action">
                            <AcquisitionBadge status={item.acquisitionStatus} />
                            {searchButton}
                        </div>
                    ) : (
                        searchButton
                    );
                },
            },
        ];
    }, [
        canSearch,
        contentTypes,
        destinationFor,
        handleSearchNow,
        isContainer,
        isSearching,
        parentId,
        selection,
        setTypeFilter,
        typeFilter,
        typeOptions,
    ]);

    const getRowId = useCallback((row: ContentListItem) => row.id, []);

    const emptyState = (
        <EmptyState
            icon={<TargetIcon weight="duotone" />}
            title={parentId ? 'No wanted content under this item' : 'Nothing wanted right now'}
            description={
                parentId
                    ? 'This item has no monitored content that still needs to be acquired.'
                    : 'Monitored content appears here when files are missing or a quality upgrade is available.'
            }
            action={
                parentId ? (
                    <Button variant="secondary" size="sm" asChild>
                        <Link to={wantedUrl()}>Back to Wanted</Link>
                    </Button>
                ) : (
                    <Button variant="primary" size="sm" asChild>
                        <Link to="/library">Go to Library</Link>
                    </Button>
                )
            }
            size="sm"
        />
    );

    const filteredEmptyState = (
        <EmptyState
            icon={<MagnifyingGlassIcon weight="duotone" />}
            title="No matching wanted items"
            description="No wanted content matches the active filters. Adjust or clear the filters to see more."
            action={
                <Button variant="secondary" size="sm" onClick={handleClearFilters}>
                    Clear filters
                </Button>
            }
            size="sm"
        />
    );

    return (
        <div className="wanted-page" data-page="wanted" data-layout={usesCardLayout ? 'card' : 'table'}>
            {breadcrumbs.length > 0 && <Breadcrumb items={breadcrumbs} className="wanted-page__breadcrumb" />}
            <PageHeader
                className="wanted-page__header"
                title={
                    parentId && ancestors.length > 0 ? `Wanted in ${ancestors[ancestors.length - 1].title}` : 'Wanted'
                }
                description="Monitored content to be acquired."
                icon={<TargetIcon weight="duotone" aria-hidden="true" data-testid="wanted-page__icon" />}
            />

            {isTruncated && (
                <Alert variant="warning" title="Not every wanted item is listed" data-testid="wanted-page__truncated">
                    This page loaded the first {totalItems.toLocaleString()} wanted items. Open a parent item to see the
                    rest.
                </Alert>
            )}

            {canSearch && !usesCardLayout && (
                <BulkActionsToolbar
                    className="wanted-page__desktop-bulk-toolbar"
                    selection={selection}
                    label="Wanted selection actions"
                    itemNounPlural="items"
                    desktopOnly={false}
                    onClear={handleClearSelection}
                >
                    <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => void handleSearchNow(selectedItems)}
                        loading={isSearching}
                        disabled={isSearching}
                        data-action="bulk-search-now"
                    >
                        <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                        Search Now
                    </Button>
                </BulkActionsToolbar>
            )}

            {isLoading ? (
                <div className="wanted-page__loading" data-testid="wanted-page__loading">
                    <Skeleton className="wanted-page__skeleton" />
                    <Skeleton className="wanted-page__skeleton" />
                    <Skeleton className="wanted-page__skeleton" />
                </div>
            ) : isError ? (
                <EmptyState
                    icon={<WarningCircleIcon weight="duotone" />}
                    title="Failed to load wanted content"
                    action={
                        <Button variant="secondary" size="sm" onClick={() => void refetch()}>
                            Retry
                        </Button>
                    }
                    size="sm"
                />
            ) : (
                <>
                    {!usesCardLayout && (
                        <div className="wanted-page__table">
                            <DataTable<ContentListItem>
                                columns={columns}
                                data={sortedItems}
                                enableRowSelection={canSearch}
                                rowSelection={selection.rowSelection}
                                onRowSelectionChange={handleRowSelectionChange}
                                enableSorting={true}
                                manualSorting
                                sorting={sorting}
                                onSortingChange={(updater) =>
                                    setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                                }
                                enablePagination={filteredItems.length > PAGE_SIZE}
                                hidePagination
                                pageSize={PAGE_SIZE}
                                pagination={pagination}
                                getRowId={getRowId}
                                emptyState={hasActiveFilters ? filteredEmptyState : emptyState}
                                keepHeaderWhenEmpty={hasActiveFilters}
                            />
                            {filteredItems.length > PAGE_SIZE && (
                                <Pager
                                    className="wanted-page__pager"
                                    page={currentPage}
                                    pageCount={pageCount}
                                    onFirst={() => setPage(1)}
                                    onPrevious={() => setPage(currentPage - 1)}
                                    onNext={() => setPage(currentPage + 1)}
                                    onLast={() => setPage(pageCount)}
                                />
                            )}
                        </div>
                    )}

                    {usesCardLayout && (
                        <div className="wanted-page__mobile-list">
                            {filteredItems.length === 0 ? (
                                hasActiveFilters ? (
                                    filteredEmptyState
                                ) : (
                                    emptyState
                                )
                            ) : (
                                <>
                                    <MobileListToolbar
                                        className="wanted-page__mobile-toolbar"
                                        sorting={sorting}
                                        sortOptions={WANTED_SORT_OPTIONS}
                                        onSortingChange={setSorting}
                                        selection={
                                            canSearch
                                                ? {
                                                      active: mobileSelectionMode,
                                                      onToggle: mobileSelectionMode
                                                          ? handleClearSelection
                                                          : () => setMobileSelectionMode(true),
                                                  }
                                                : undefined
                                        }
                                    />
                                    {pagedItems.map((item) => {
                                        const container = isContainer(item);
                                        const target = destinationFor(item);
                                        const canSearchItem = container || isActionableLeaf(item, container);
                                        return mobileSelectionMode ? (
                                            <MobileMediaCard
                                                key={item.id}
                                                data-content-id={item.id}
                                                posterUrl={item.posterUrl}
                                                posterAlt={`${item.title} artwork`}
                                                title={
                                                    <TitleCell
                                                        item={item}
                                                        to={target}
                                                        contentTypes={contentTypes}
                                                        linked={false}
                                                        qualify={!parentId}
                                                    />
                                                }
                                                selectable
                                                selected={selectedIdSet.has(item.id)}
                                                onToggleSelected={() => toggleMobileSelection(item)}
                                                selectionLabel={`Select ${item.title}`}
                                            >
                                                <FulfillmentCell fulfillment={item.fulfillment} />
                                                <TypeCell item={item} contentTypes={contentTypes} />
                                            </MobileMediaCard>
                                        ) : (
                                            <div
                                                key={item.id}
                                                className="wanted-page__mobile-row"
                                                data-content-id={item.id}
                                            >
                                                <MobileMediaCard
                                                    posterUrl={item.posterUrl}
                                                    posterAlt={`${item.title} artwork`}
                                                    posterTo={target}
                                                    posterLinkLabel={`Open ${item.title}`}
                                                    title={
                                                        <TitleCell
                                                            item={item}
                                                            to={target}
                                                            contentTypes={contentTypes}
                                                            qualify={!parentId}
                                                        />
                                                    }
                                                    trailing={
                                                        canSearch &&
                                                        canSearchItem &&
                                                        (!item.acquisitionStatus || container) ? (
                                                            <Button
                                                                variant="secondary"
                                                                size="sm"
                                                                onClick={(event) => {
                                                                    event.stopPropagation();
                                                                    void handleSearchNow([item]);
                                                                }}
                                                                loading={isSearching}
                                                                disabled={isSearching}
                                                                aria-label={
                                                                    container
                                                                        ? `Search now for descendants of ${item.title}`
                                                                        : `Search now for ${item.title}`
                                                                }
                                                                data-action="search-now"
                                                                data-content-id={item.id}
                                                            >
                                                                <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                                                                Search Now
                                                            </Button>
                                                        ) : undefined
                                                    }
                                                >
                                                    <FulfillmentCell fulfillment={item.fulfillment} />
                                                    {item.acquisitionStatus && (
                                                        <AcquisitionBadge status={item.acquisitionStatus} />
                                                    )}
                                                    <TypeCell item={item} contentTypes={contentTypes} />
                                                    <span>Published: {formatShortDate(item.publishDate, '--')}</span>
                                                    <span className="wanted-page__mobile-last-searched">
                                                        Last searched: {formatRelativeTime(item.lastSearched, 'Never')}
                                                    </span>
                                                </MobileMediaCard>
                                            </div>
                                        );
                                    })}
                                </>
                            )}
                            {filteredItems.length > PAGE_SIZE && (
                                <Pager
                                    className="wanted-page__pager"
                                    page={currentPage}
                                    pageCount={pageCount}
                                    onFirst={() => setPage(1)}
                                    onPrevious={() => setPage(currentPage - 1)}
                                    onNext={() => setPage(currentPage + 1)}
                                    onLast={() => setPage(pageCount)}
                                />
                            )}
                        </div>
                    )}
                </>
            )}

            {usesCardLayout && canSearch && mobileSelectionMode && (
                <MobileSelectionBar
                    data-component="wanted-mobile-selection-bar"
                    selection={selection}
                    pageSelectedCount={selection.pageSelectedCount}
                    totalCount={sortedItems.length}
                    onClear={selection.clear}
                    itemNounPlural="items"
                >
                    <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => void handleSearchNow(selectedItems)}
                        loading={isSearching}
                        disabled={isSearching || !selection.hasSelection}
                        data-action="bulk-search-now"
                    >
                        <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                        Search Now
                    </Button>
                </MobileSelectionBar>
            )}
        </div>
    );
}

WantedPage.displayName = 'WantedPage';

export default WantedPage;
