import { useCallback, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';
import { CheckCircleIcon, CompassIcon, FunnelSimpleIcon, ImageBrokenIcon, WarningIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { DataTable } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { MobileListToolbar, type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { Pager } from '@/components/ui/Pager';
import { Skeleton } from '@/components/ui/Skeleton';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import {
    useWishlistDiscovered,
    type WishlistDiscoveredRow,
    type WishlistDiscoveredInLibraryFilter,
    type WishlistDiscoveredStatusFilter,
    type WishlistDiscoveredSort,
} from '@/hooks/useWishlistDiscovered';
import { formatShortDate } from '@/lib/dateFormatting';

import './WishlistDiscoveredList.css';

const PAGE_SIZE = 25;

const IN_LIBRARY_OPTIONS: FacetOption[] = [
    { value: 'InLibrary', label: 'In library' },
    { value: 'NotInLibrary', label: 'Not in library' },
];

const STATUS_OPTIONS: FacetOption[] = [
    { value: 'Missing', label: 'Missing' },
    { value: 'Found', label: 'Fulfilled' },
];

const SORT_COLUMNS: Record<string, { sort: WishlistDiscoveredSort; naturalDesc: boolean }> = {
    discovered: { sort: 'Discovered', naturalDesc: true },
    added: { sort: 'Added', naturalDesc: true },
    publish: { sort: 'PublishDate', naturalDesc: true },
    title: { sort: 'Title', naturalDesc: false },
    status: { sort: 'Status', naturalDesc: true },
};

const SORT_KEYS = Object.keys(SORT_COLUMNS);

const DEFAULT_SORTING: SortingState = [{ id: 'publish', desc: true }];

const DEFAULT_FILTERS = { inLibrary: '', status: '', search: '' };

const WISHLIST_DISCOVERED_SORT_OPTIONS: MobileListSortOption[] = [
    { id: 'title', label: 'Title' },
    { id: 'discovered', label: 'Discovered', initialDirection: 'desc' },
    { id: 'added', label: 'Added', initialDirection: 'desc' },
    { id: 'publish', label: 'Published', initialDirection: 'desc' },
    { id: 'status', label: 'Status', initialDirection: 'desc' },
];

interface WishlistDiscoveredListProps {
    wishlistId: string;

    onOpenFilter?: () => void;

    canEditFilter?: boolean;
}

export function WishlistDiscoveredList({
    wishlistId,
    onOpenFilter,
    canEditFilter = true,
}: WishlistDiscoveredListProps) {
    const usesCardLayout = useCardLayout();
    const { sorting, filters, page, setSorting, setFilters, setPage } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: DEFAULT_FILTERS,
        sortKeys: SORT_KEYS,
    });

    const inLibrary = (filters.inLibrary || 'All') as WishlistDiscoveredInLibraryFilter;
    const status = (filters.status || 'All') as WishlistDiscoveredStatusFilter;
    const pageNumber = page;

    const sortColumn = sorting[0];
    const sort: WishlistDiscoveredSort = sortColumn
        ? (SORT_COLUMNS[sortColumn.id]?.sort ?? 'Discovered')
        : 'Discovered';
    const sortDescending = sortColumn?.desc ?? true;

    const { rows, pagination, counts, isLoading, error, refetch } = useWishlistDiscovered(wishlistId, {
        pageNumber,
        pageSize: PAGE_SIZE,
        inLibrary,
        status,
        sort,
        sortDescending,
        search: filters.search,
    });

    const totalItems = pagination?.totalItems ?? 0;
    const pageCount = Math.max(1, Math.ceil(totalItems / PAGE_SIZE));

    const handleInLibraryChange = useCallback(
        (value: string) => {
            setFilters({ inLibrary: value });
        },
        [setFilters],
    );

    const handleStatusChange = useCallback(
        (value: string) => {
            setFilters({ status: value });
        },
        [setFilters],
    );

    const handleSortingChange = useCallback(
        (updater: SortingState | ((prev: SortingState) => SortingState)) => {
            const next = typeof updater === 'function' ? updater(sorting) : updater;
            if (next.length === 0 && sorting.length > 0) {
                const naturalDesc = SORT_COLUMNS[sorting[0].id]?.naturalDesc ?? true;
                setSorting([{ id: sorting[0].id, desc: naturalDesc }]);
                return;
            }
            setSorting(next);
        },
        [sorting, setSorting],
    );

    const handleClearFilters = useCallback(() => {
        setFilters(DEFAULT_FILTERS);
    }, [setFilters]);

    const hasActiveFilters = filters.inLibrary !== '' || filters.status !== '' || filters.search !== '';

    const countsHeader = useMemo(() => {
        if (!counts) {
            return null;
        }
        return `Discovered ${counts.totalDiscovered} · In library ${counts.inLibrary} · Not in library ${counts.notInLibrary} · Missing ${counts.missing}`;
    }, [counts]);

    const columns = useMemo<ColumnDef<WishlistDiscoveredRow, unknown>[]>(
        () => [
            {
                id: 'poster',
                header: '',
                enableSorting: false,
                size: 64,
                cell: ({ row }) => <DiscoveredPoster row={row.original} />,
            },
            {
                id: 'title',
                meta: { headerLabel: 'Title' },
                header: () => (
                    <ColumnHeaderFilter
                        type="text"
                        label="Title"
                        active={filters.search.length > 0}
                        value={filters.search}
                        onChange={(value) => setFilters({ search: value })}
                    />
                ),
                accessorFn: (row) => row.title ?? '',
                cell: ({ row }) => <DiscoveredTitle row={row.original} />,
            },
            {
                id: 'library',
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="Library presence"
                        active={filters.inLibrary.length > 0}
                        options={IN_LIBRARY_OPTIONS}
                        value={filters.inLibrary}
                        onChange={handleInLibraryChange}
                        allLabel="All"
                    />
                ),
                accessorFn: (row) => row.linkId !== null,
                enableSorting: false,
                size: 152,
                cell: ({ row }) => (row.original.linkId === null ? 'Not in library' : 'In library'),
            },
            {
                id: 'discovered',
                header: 'Discovered',
                accessorFn: (row) => row.discoveredAt,
                sortDescFirst: true,
                size: 112,
                cell: ({ row }) => formatShortDate(row.original.discoveredAt),
            },
            {
                id: 'added',
                header: 'Added',
                accessorFn: (row) => row.addedAt ?? '',
                sortDescFirst: true,
                size: 96,
                cell: ({ row }) => (row.original.linkId !== null ? formatShortDate(row.original.addedAt) : '-'),
            },
            {
                id: 'publish',
                header: 'Published',
                accessorFn: (row) => row.publishDate ?? '',
                sortDescFirst: true,
                size: 104,
                cell: ({ row }) => (row.original.linkId !== null ? formatShortDate(row.original.publishDate) : '-'),
            },
            {
                id: 'status',
                meta: { headerLabel: 'Status' },
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="Status"
                        active={filters.status.length > 0}
                        options={STATUS_OPTIONS}
                        value={filters.status}
                        onChange={handleStatusChange}
                        allLabel="All"
                    />
                ),
                accessorFn: (row) => row.status ?? '',
                sortDescFirst: true,
                size: 116,
                cell: ({ row }) => <DiscoveredStatus row={row.original} />,
            },
        ],
        [filters.inLibrary, filters.search, filters.status, handleInLibraryChange, handleStatusChange, setFilters],
    );

    const emptyState = (
        <EmptyState
            icon={<CompassIcon weight="duotone" />}
            title={hasActiveFilters ? 'No matching discovered items' : 'Nothing discovered yet'}
            description={
                hasActiveFilters
                    ? 'No discovered items match the active filters.'
                    : 'Content found for this wishlist will appear here.'
            }
            action={
                hasActiveFilters ? (
                    <Button variant="secondary" size="sm" onClick={handleClearFilters}>
                        Clear filters
                    </Button>
                ) : undefined
            }
            size="sm"
            data-component="wishlist-discovered-empty"
        />
    );

    return (
        <section
            className="wishlist-discovered"
            data-component="wishlist-discovered"
            data-layout={usesCardLayout ? 'card' : 'table'}
            aria-labelledby="wishlist-discovered-heading"
        >
            <div className="wishlist-discovered__header">
                <div className="wishlist-discovered__heading-group">
                    <h2 className="wishlist-discovered__title" id="wishlist-discovered-heading">
                        <CompassIcon weight="duotone" aria-hidden="true" />
                        Discovered
                    </h2>
                    {countsHeader && (
                        <p className="wishlist-discovered__counts" data-testid="wishlist-discovered-counts">
                            {countsHeader}
                        </p>
                    )}
                </div>
                <div className="wishlist-discovered__header-actions">
                    {hasActiveFilters && <ClearFiltersButton onClick={handleClearFilters} />}
                    {onOpenFilter && (
                        <Button variant="secondary" size="sm" onClick={onOpenFilter} data-action="edit-filter">
                            <FunnelSimpleIcon weight="bold" aria-hidden="true" />
                            <span className="wishlist-discovered__action-label">
                                {canEditFilter ? 'Edit filter' : 'View filter'}
                            </span>
                        </Button>
                    )}
                </div>
            </div>

            {usesCardLayout && (
                <div className="wishlist-discovered__mobile-filters" role="group" aria-label="Discovered list filters">
                    <ColumnHeaderFilter
                        type="text"
                        label="Title"
                        active={filters.search.length > 0}
                        value={filters.search}
                        onChange={(value) => setFilters({ search: value })}
                    />
                    <ColumnHeaderFilter
                        type="facet"
                        label="Library presence"
                        active={filters.inLibrary.length > 0}
                        options={IN_LIBRARY_OPTIONS}
                        value={filters.inLibrary}
                        onChange={handleInLibraryChange}
                        allLabel="All"
                    />
                    <ColumnHeaderFilter
                        type="facet"
                        label="Status"
                        active={filters.status.length > 0}
                        options={STATUS_OPTIONS}
                        value={filters.status}
                        onChange={handleStatusChange}
                        allLabel="All"
                    />
                </div>
            )}

            {isLoading ? (
                <div className="wishlist-discovered__list" aria-label="Loading discovered list">
                    <Skeleton className="wishlist-discovered__skeleton" />
                    <Skeleton className="wishlist-discovered__skeleton" />
                    <Skeleton className="wishlist-discovered__skeleton" />
                </div>
            ) : error ? (
                <EmptyState
                    icon={<WarningIcon weight="duotone" />}
                    title="Failed to load discovered items"
                    action={
                        <Button variant="secondary" size="sm" onClick={() => void refetch()}>
                            Retry
                        </Button>
                    }
                    data-component="wishlist-discovered-error"
                />
            ) : (
                <div className="wishlist-discovered__content">
                    {usesCardLayout ? (
                        rows.length > 0 ? (
                            <>
                                <MobileListToolbar
                                    className="wishlist-discovered__mobile-toolbar"
                                    sorting={sorting}
                                    sortOptions={WISHLIST_DISCOVERED_SORT_OPTIONS}
                                    onSortingChange={handleSortingChange}
                                />
                                <div
                                    className="wishlist-discovered__mobile-list"
                                    data-testid="wishlist-discovered__mobile-list"
                                >
                                    {rows.map((row) => (
                                        <DiscoveredMobileCard key={row.discoveredId} row={row} />
                                    ))}
                                </div>
                            </>
                        ) : (
                            emptyState
                        )
                    ) : (
                        <div className="wishlist-discovered__table-wrap">
                            <DataTable<WishlistDiscoveredRow>
                                columns={columns}
                                data={rows}
                                size="sm"
                                manualSorting
                                manualPagination
                                sorting={sorting}
                                onSortingChange={handleSortingChange}
                                getRowId={(row) => row.discoveredId}
                                className="wishlist-discovered__table"
                                data-testid="wishlist-discovered-table"
                                keepHeaderWhenEmpty={hasActiveFilters}
                                emptyState={emptyState}
                            />
                        </div>
                    )}
                </div>
            )}

            {totalItems > PAGE_SIZE && (
                <Pager
                    className="wishlist-discovered__pagination"
                    page={pageNumber}
                    pageCount={pageCount}
                    onFirst={() => setPage(1)}
                    onPrevious={() => setPage(Math.max(1, pageNumber - 1))}
                    onNext={() => setPage(Math.min(pageCount, pageNumber + 1))}
                    onLast={() => setPage(pageCount)}
                />
            )}
        </section>
    );
}

WishlistDiscoveredList.displayName = 'WishlistDiscoveredList';

function DiscoveredPoster({ row }: { row: WishlistDiscoveredRow }) {
    const [imageError, setImageError] = useState(false);
    const showPoster = !!row.posterUrl && !imageError;

    return (
        <div className="wishlist-discovered__row-poster">
            {showPoster ? (
                <img src={row.posterUrl!} alt="" loading="lazy" onError={() => setImageError(true)} />
            ) : (
                <div className="wishlist-discovered__poster-placeholder" data-testid="discovered-poster-placeholder">
                    <ImageBrokenIcon weight="duotone" aria-hidden="true" />
                </div>
            )}
        </div>
    );
}

function DiscoveredTitle({ row }: { row: WishlistDiscoveredRow }) {
    const hasTitle = row.title !== null && row.title.trim().length > 0;
    const titleText = hasTitle ? row.title! : 'Untitled';

    if (row.linkId !== null) {
        return (
            <Link
                to={`/library/${row.linkId}`}
                className="wishlist-discovered__row-title wishlist-discovered__row-title--link"
                data-action="open-content"
            >
                <ContentLineageTitle
                    qualifiedName={{
                        id: row.linkId,
                        title: titleText,
                    }}
                />
            </Link>
        );
    }

    return (
        <span
            className={`wishlist-discovered__row-title${hasTitle ? '' : ' wishlist-discovered__row-title--muted'}`}
            title={titleText}
        >
            {titleText}
        </span>
    );
}

function DiscoveredStatus({ row }: { row: WishlistDiscoveredRow }) {
    if (row.linkId === null) {
        return <span className="wishlist-discovered__not-in-library">(not in library)</span>;
    }

    if (!row.status) {
        return <span className="wishlist-discovered__not-in-library">-</span>;
    }

    const isFulfilled = row.status === 'Found';

    return (
        <Badge variant={isFulfilled ? 'fulfilled' : 'missing'} size="sm" dot={row.status === 'Missing'}>
            {isFulfilled ? (
                <CheckCircleIcon weight="bold" aria-hidden="true" />
            ) : (
                <WarningIcon weight="bold" aria-hidden="true" />
            )}
            {isFulfilled ? 'Fulfilled' : 'Missing'}
        </Badge>
    );
}

function DiscoveredMobileCard({ row }: { row: WishlistDiscoveredRow }) {
    const inLibrary = row.linkId !== null;

    return (
        <MobileMediaCard
            data-discovered-id={row.discoveredId}
            posterUrl={row.posterUrl ?? undefined}
            posterAlt=""
            title={<DiscoveredTitle row={row} />}
        >
            <span>Discovered {formatShortDate(row.discoveredAt)}</span>
            {inLibrary && (
                <>
                    <span>Added {formatShortDate(row.addedAt)}</span>
                    <span>Published {formatShortDate(row.publishDate)}</span>
                </>
            )}
            <DiscoveredStatus row={row} />
        </MobileMediaCard>
    );
}

export default WishlistDiscoveredList;
