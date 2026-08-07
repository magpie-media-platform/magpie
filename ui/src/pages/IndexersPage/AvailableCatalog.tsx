import { useMemo, useCallback, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ListPlusIcon, GearIcon } from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { Badge } from '@/components/ui/Badge';
import type { BadgeVariant } from '@/types/badge';
import { Button } from '@/components/ui/Button';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { DataTable } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { Pager } from '@/components/ui/Pager';
import { MultiSelectColumnFilter, encodeMultiValue, decodeMultiValue } from '@/components/ui/MultiSelectColumnFilter';

import { useIndexers, indexerQueryKeys, type ApiIndexer, type IndexerAvailabilityValue } from '@/hooks/useIndexers';
import { useUrlTableState } from '@/hooks/useUrlTableState';

import { ConfigureInstanceDialog, type ConfigureInstanceTarget } from './ConfigureInstanceDialog';
import './AvailableCatalog.css';

const PAGE_SIZE = 25;

const DEFAULT_SORTING: SortingState = [{ id: 'name', desc: false }];

const SORT_KEYS = ['name', 'providerType', 'availability'] as const;

const availabilityLabels: Record<IndexerAvailabilityValue, string> = {
    Unknown: 'Unknown',
    Public: 'Public',
    Private: 'Private',
    SemiPrivate: 'Semi-Private',
};

const availabilityVariants: Record<IndexerAvailabilityValue, BadgeVariant> = {
    Unknown: 'unknown',
    Public: 'healthy',
    Private: 'unhealthy',
    SemiPrivate: 'degraded',
};

const availabilityOrder: IndexerAvailabilityValue[] = ['Public', 'SemiPrivate', 'Private', 'Unknown'];

const DEFAULT_FILTERS = { provider: '', language: '', category: '', availability: '' };

export function AvailableCatalog() {
    const { indexers, isLoadingIndexers, refetch } = useIndexers();
    const queryClient = useQueryClient();

    const { sorting, filters, page, setSorting, setFilters, setPage } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: DEFAULT_FILTERS,
        sortKeys: SORT_KEYS,
        pageParam: 'apage',
        sortParam: 'asort',
    });

    const provider = useMemo(() => decodeMultiValue(filters.provider), [filters.provider]);
    const language = useMemo(() => decodeMultiValue(filters.language), [filters.language]);
    const category = useMemo(() => decodeMultiValue(filters.category), [filters.category]);
    const availability = useMemo(() => decodeMultiValue(filters.availability), [filters.availability]);

    const setMulti = useCallback(
        (key: keyof typeof DEFAULT_FILTERS, next: string[]) => {
            setFilters({ [key]: encodeMultiValue(next) } as Partial<typeof DEFAULT_FILTERS>);
        },
        [setFilters],
    );

    const providerOptions = useMemo(() => [...new Set(indexers.map((i) => i.providerType))].sort(), [indexers]);
    const languageOptions = useMemo(() => [...new Set(indexers.flatMap((i) => i.languages ?? []))].sort(), [indexers]);
    const categoryOptions = useMemo(() => [...new Set(indexers.flatMap((i) => i.categories ?? []))].sort(), [indexers]);
    const availabilityOptions = useMemo(
        () =>
            availabilityOrder
                .filter((a) => indexers.some((i) => i.availability === a))
                .map((a) => ({ value: a, label: availabilityLabels[a] })),
        [indexers],
    );

    const filtered = useMemo(() => {
        return indexers.filter((d) => {
            if (provider.length > 0 && !provider.includes(d.providerType)) {
                return false;
            }
            if (availability.length > 0 && !availability.includes(d.availability)) {
                return false;
            }
            if (language.length > 0 && !(d.languages ?? []).some((l) => language.includes(l))) {
                return false;
            }
            if (category.length > 0 && !(d.categories ?? []).some((c) => category.includes(c))) {
                return false;
            }
            return true;
        });
    }, [indexers, provider, availability, language, category]);

    const hasActiveFilters =
        provider.length > 0 || language.length > 0 || category.length > 0 || availability.length > 0;

    const handleClearFilters = useCallback(() => {
        setFilters({ provider: '', language: '', category: '', availability: '' });
    }, [setFilters]);

    const [target, setTarget] = useState<ConfigureInstanceTarget | null>(null);

    const handleConfigure = useCallback((definition: ApiIndexer) => {
        setTarget({ id: definition.id, name: definition.name, providerType: definition.providerType });
    }, []);

    const columns = useMemo<ColumnDef<ApiIndexer>[]>(
        () => [
            {
                id: 'name',
                accessorKey: 'name',
                header: 'Name',
                size: 260,
                cell: ({ row }) => {
                    const def = row.original;
                    const configured = def.connectionCount ?? 0;
                    return (
                        <div className="available-catalog__name-cell">
                            <span className="available-catalog__name" title={def.name}>
                                {def.name}
                            </span>
                            {configured > 0 && (
                                <Badge variant="info" size="sm" data-testid="catalog-configured-badge">
                                    {configured} configured
                                </Badge>
                            )}
                        </div>
                    );
                },
            },
            {
                id: 'providerType',
                accessorKey: 'providerType',
                header: () => (
                    <MultiSelectColumnFilter
                        label="Provider"
                        options={providerOptions}
                        value={provider}
                        onChange={(next) => setMulti('provider', next)}
                    />
                ),
                meta: { headerLabel: 'Provider' },
                size: 120,
                cell: ({ row }) => (
                    <span className="available-catalog__provider" title={row.original.providerType}>
                        {row.original.providerType}
                    </span>
                ),
            },
            {
                id: 'languages',
                accessorFn: (row) => (row.languages ?? []).join(', '),
                enableSorting: false,
                header: () => (
                    <MultiSelectColumnFilter
                        label="Languages"
                        options={languageOptions}
                        value={language}
                        onChange={(next) => setMulti('language', next)}
                    />
                ),
                meta: { headerLabel: 'Languages' },
                size: 140,
                cell: ({ row }) => {
                    const langs = row.original.languages ?? [];
                    if (langs.length === 0) {
                        return <span className="available-catalog__empty-cell">-</span>;
                    }
                    return (
                        <span className="available-catalog__tags" title={langs.join(', ')}>
                            {langs.join(', ')}
                        </span>
                    );
                },
            },
            {
                id: 'categories',
                accessorFn: (row) => (row.categories ?? []).join(', '),
                enableSorting: false,
                header: () => (
                    <MultiSelectColumnFilter
                        label="Categories"
                        options={categoryOptions}
                        value={category}
                        onChange={(next) => setMulti('category', next)}
                    />
                ),
                meta: { headerLabel: 'Categories' },
                size: 200,
                cell: ({ row }) => {
                    const cats = row.original.categories ?? [];
                    if (cats.length === 0) {
                        return <span className="available-catalog__empty-cell">-</span>;
                    }
                    return (
                        <span className="available-catalog__tags" title={cats.join(', ')}>
                            {cats.slice(0, 4).join(' · ')}
                            {cats.length > 4 ? ' ...' : ''}
                        </span>
                    );
                },
            },
            {
                id: 'availability',
                accessorKey: 'availability',
                header: () => (
                    <MultiSelectColumnFilter
                        label="Availability"
                        options={availabilityOptions}
                        value={availability}
                        onChange={(next) => setMulti('availability', next)}
                    />
                ),
                meta: { headerLabel: 'Availability' },
                size: 190,
                cell: ({ row }) => {
                    const a = row.original.availability;
                    return (
                        <Badge variant={availabilityVariants[a]} size="sm" dot>
                            {availabilityLabels[a]}
                        </Badge>
                    );
                },
            },
            {
                id: 'configure',
                header: '',
                enableSorting: false,
                size: 140,
                cell: ({ row }) => (
                    <Button
                        variant="secondary"
                        size="sm"
                        onClick={(event) => {
                            event.stopPropagation();
                            handleConfigure(row.original);
                        }}
                        data-action="configure"
                        data-indexer-id={row.original.id}
                    >
                        <GearIcon weight="bold" aria-hidden="true" />
                        Configure
                    </Button>
                ),
            },
        ],
        [
            providerOptions,
            languageOptions,
            categoryOptions,
            availabilityOptions,
            provider,
            language,
            category,
            availability,
            setMulti,
            handleConfigure,
        ],
    );

    const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
    const currentPage = Math.min(page, pageCount);
    const pagination = useMemo(() => ({ pageIndex: currentPage - 1, pageSize: PAGE_SIZE }), [currentPage]);

    const getRowId = useCallback((row: ApiIndexer) => row.id, []);

    const emptyState = (
        <EmptyState
            icon={<ListPlusIcon weight="duotone" />}
            title={hasActiveFilters ? 'No matching indexers' : 'No indexer definitions available'}
            description={
                hasActiveFilters
                    ? 'No definitions match the active filters. Adjust or clear the filters to see more.'
                    : 'No indexer definitions are available yet.'
            }
            action={
                hasActiveFilters ? (
                    <Button variant="secondary" size="sm" onClick={handleClearFilters}>
                        Clear filters
                    </Button>
                ) : undefined
            }
            size="sm"
        />
    );

    return (
        <div className="available-catalog" data-component="available-catalog">
            {hasActiveFilters && (
                <div className="available-catalog__toolbar">
                    <ClearFiltersButton onClick={handleClearFilters} />
                </div>
            )}

            <div className="available-catalog__table">
                <DataTable<ApiIndexer>
                    columns={columns}
                    data={filtered}
                    enableSorting
                    sorting={sorting}
                    onSortingChange={(updater) =>
                        setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                    }
                    enablePagination={filtered.length > PAGE_SIZE}
                    hidePagination
                    pageSize={PAGE_SIZE}
                    pagination={pagination}
                    getRowId={getRowId}
                    loading={isLoadingIndexers}
                    emptyState={emptyState}
                    keepHeaderWhenEmpty={hasActiveFilters}
                    aria-label="Available indexers"
                />
                {filtered.length > PAGE_SIZE && (
                    <Pager
                        className="available-catalog__pager"
                        page={currentPage}
                        pageCount={pageCount}
                        onFirst={() => setPage(1)}
                        onPrevious={() => setPage(Math.max(1, currentPage - 1))}
                        onNext={() => setPage(Math.min(pageCount, currentPage + 1))}
                        onLast={() => setPage(pageCount)}
                    />
                )}
            </div>

            <ConfigureInstanceDialog
                open={!!target}
                onOpenChange={(open) => {
                    if (!open) {
                        setTarget(null);
                    }
                }}
                indexer={target}
                onSaved={() => {
                    void refetch();
                    void queryClient.invalidateQueries({ queryKey: indexerQueryKeys.connections() });
                }}
            />
        </div>
    );
}

AvailableCatalog.displayName = 'AvailableCatalog';

export default AvailableCatalog;
