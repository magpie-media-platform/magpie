import { forwardRef, type ComponentPropsWithoutRef, type ReactNode, useMemo, useState, useCallback } from 'react';
import {
    flexRender,
    getCoreRowModel,
    getSortedRowModel,
    getPaginationRowModel,
    getExpandedRowModel,
    useReactTable,
    type ColumnDef,
    type ExpandedState,
    type Header,
    type SortingState,
    type PaginationState,
    type RowSelectionState,
    type OnChangeFn,
    type Row,
    type RowData,
    type VisibilityState,
} from '@tanstack/react-table';
import { CaretUpIcon, CaretDownIcon, CaretUpDownIcon } from '@phosphor-icons/react';

import { Pager } from '@/components/ui/Pager';

import './DataTable.css';

declare module '@tanstack/react-table' {
    // TanStack declaration merging requires both generic parameters.
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    interface ColumnMeta<TData extends RowData, TValue> {
        headerLabel?: string;
    }
}

export type DataTableSize = 'sm' | 'md' | 'lg';

const AUTO_PAGINATE_THRESHOLD = 50;

function HeaderContent<TData>({ header }: { header: Header<TData, unknown> }): ReactNode {
    const def = header.column.columnDef.header;
    const context = header.getContext();
    return typeof def === 'function' ? def(context) : flexRender(def, context);
}

export interface DataTableProps<TData> extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    columns: ColumnDef<TData, unknown>[];

    data: TData[];

    size?: DataTableSize;

    enableRowSelection?: boolean | ((row: Row<TData>) => boolean);

    enableMultiRowSelection?: boolean;

    rowSelection?: RowSelectionState;

    onRowSelectionChange?: OnChangeFn<RowSelectionState>;

    enableSorting?: boolean;

    sorting?: SortingState;

    onSortingChange?: OnChangeFn<SortingState>;

    columnVisibility?: VisibilityState;

    enablePagination?: boolean;

    hidePagination?: boolean;

    pageSize?: number;

    pagination?: PaginationState;

    onPaginationChange?: OnChangeFn<PaginationState>;

    rowCount?: number;

    manualPagination?: boolean;

    manualSorting?: boolean;

    enableExpanding?: boolean;

    getSubRows?: (row: TData, index: number) => TData[] | undefined;

    defaultAllExpanded?: boolean;

    onRowClick?: (row: TData) => void;

    emptyState?: ReactNode;

    filterActions?: ReactNode;

    keepHeaderWhenEmpty?: boolean;

    loading?: boolean;

    getRowId?: (row: TData, index: number) => string;

    stickyHeader?: boolean;

    bordered?: boolean;

    striped?: boolean;
}

function DataTableInner<TData>(
    {
        columns,
        data,
        size = 'md',
        enableRowSelection = false,
        enableMultiRowSelection = true,
        rowSelection: controlledRowSelection,
        onRowSelectionChange,
        enableSorting = true,
        sorting: controlledSorting,
        onSortingChange,
        columnVisibility,
        enablePagination = false,
        hidePagination = false,
        pageSize = 10,
        pagination: controlledPagination,
        onPaginationChange,
        rowCount,
        manualPagination = false,
        manualSorting = false,
        enableExpanding = false,
        getSubRows,
        defaultAllExpanded = true,
        onRowClick,
        emptyState,
        filterActions,
        keepHeaderWhenEmpty = false,
        loading = false,
        getRowId,
        stickyHeader = false,
        bordered = false,
        striped = false,
        className,
        ...props
    }: DataTableProps<TData>,
    ref: React.ForwardedRef<HTMLDivElement>,
) {
    const [internalSorting, setInternalSorting] = useState<SortingState>([]);
    const [internalRowSelection, setInternalRowSelection] = useState<RowSelectionState>({});
    const [internalPagination, setInternalPagination] = useState<PaginationState>({
        pageIndex: 0,
        pageSize,
    });
    const [expanded, setExpanded] = useState<ExpandedState>(enableExpanding && defaultAllExpanded ? true : {});

    const shouldPaginate = enablePagination || (!manualPagination && data.length > AUTO_PAGINATE_THRESHOLD);

    const sorting = controlledSorting ?? internalSorting;
    const rowSelectionState = controlledRowSelection ?? internalRowSelection;
    const pagination = controlledPagination ?? internalPagination;

    const handleSortingChange: OnChangeFn<SortingState> = useCallback(
        (updater) => {
            if (onSortingChange) {
                onSortingChange(updater);
            } else {
                setInternalSorting(updater);
            }
        },
        [onSortingChange],
    );

    const handleRowSelectionChange: OnChangeFn<RowSelectionState> = useCallback(
        (updater) => {
            if (onRowSelectionChange) {
                onRowSelectionChange(updater);
            } else {
                setInternalRowSelection(updater);
            }
        },
        [onRowSelectionChange],
    );

    const handlePaginationChange: OnChangeFn<PaginationState> = useCallback(
        (updater) => {
            if (onPaginationChange) {
                onPaginationChange(updater);
            } else {
                setInternalPagination(updater);
            }
        },
        [onPaginationChange],
    );

    const table = useReactTable({
        data,
        columns,
        state: {
            sorting,
            rowSelection: rowSelectionState,
            pagination,
            ...(columnVisibility ? { columnVisibility } : {}),
            ...(enableExpanding ? { expanded } : {}),
        },
        enableRowSelection,
        enableMultiRowSelection,
        enableExpanding,
        onSortingChange: handleSortingChange,
        onRowSelectionChange: handleRowSelectionChange,
        onPaginationChange: handlePaginationChange,
        onExpandedChange: enableExpanding ? setExpanded : undefined,
        getSubRows: enableExpanding ? getSubRows : undefined,
        getCoreRowModel: getCoreRowModel(),
        getSortedRowModel: manualSorting ? undefined : getSortedRowModel(),
        getExpandedRowModel: enableExpanding ? getExpandedRowModel() : undefined,
        getPaginationRowModel: shouldPaginate && !manualPagination ? getPaginationRowModel() : undefined,
        manualPagination,
        manualSorting,
        rowCount,
        getRowId,
    });

    const rows = table.getRowModel().rows;
    const isEmpty = rows.length === 0;
    const headerGroups = isEmpty && !keepHeaderWhenEmpty ? [] : table.getHeaderGroups();

    const classes = useMemo(
        () =>
            [
                'data-table',
                `data-table--${size}`,
                stickyHeader && 'data-table--sticky-header',
                bordered && 'data-table--bordered',
                striped && 'data-table--striped',
                loading && 'data-table--loading',
                filterActions && 'data-table--with-filter-bar',
                className,
            ]
                .filter(Boolean)
                .join(' '),
        [size, stickyHeader, bordered, striped, loading, filterActions, className],
    );

    const handleRowClickInternal = useCallback(
        (row: TData) => {
            if (onRowClick) {
                onRowClick(row);
            }
        },
        [onRowClick],
    );

    return (
        <div
            ref={ref}
            className={classes}
            data-component="data-table"
            data-size={size}
            data-sticky-header={stickyHeader || undefined}
            data-bordered={bordered || undefined}
            data-striped={striped || undefined}
            data-loading={loading || undefined}
            {...props}
        >
            {filterActions && (
                <div className="data-table__filter-bar" data-component="data-table-filter-bar">
                    {filterActions}
                </div>
            )}

            <div className="data-table__wrapper">
                <table className="data-table__table" role="grid">
                    <thead className="data-table__header">
                        {headerGroups.map((headerGroup) => (
                            <tr key={headerGroup.id} className="data-table__header-row">
                                {headerGroup.headers.map((header) => {
                                    const canSort = enableSorting && header.column.getCanSort();
                                    const sortDirection = header.column.getIsSorted();
                                    const headerDef = header.column.columnDef.header;
                                    const sortLabel =
                                        typeof headerDef === 'string'
                                            ? headerDef
                                            : (header.column.columnDef.meta?.headerLabel ?? header.column.id);

                                    return (
                                        <th
                                            key={header.id}
                                            className={[
                                                'data-table__header-cell',
                                                canSort && 'data-table__header-cell--sortable',
                                                sortDirection && 'data-table__header-cell--sorted',
                                            ]
                                                .filter(Boolean)
                                                .join(' ')}
                                            data-column={header.column.id}
                                            style={{
                                                width: header.getSize() !== 150 ? header.getSize() : undefined,
                                            }}
                                            scope="col"
                                            aria-sort={
                                                sortDirection === 'asc'
                                                    ? 'ascending'
                                                    : sortDirection === 'desc'
                                                      ? 'descending'
                                                      : undefined
                                            }
                                        >
                                            {header.isPlaceholder ? null : (
                                                <div
                                                    className="data-table__header-content"
                                                    onClick={
                                                        canSort ? header.column.getToggleSortingHandler() : undefined
                                                    }
                                                    onKeyDown={
                                                        canSort
                                                            ? (e) => {
                                                                  if (e.key === 'Enter' || e.key === ' ') {
                                                                      e.preventDefault();
                                                                      header.column.toggleSorting();
                                                                  }
                                                              }
                                                            : undefined
                                                    }
                                                    tabIndex={canSort ? 0 : undefined}
                                                    role={canSort ? 'button' : undefined}
                                                    aria-label={canSort ? `Sort by ${sortLabel}` : undefined}
                                                >
                                                    <HeaderContent header={header} />
                                                    {canSort && (
                                                        <span className="data-table__sort-icon" aria-hidden="true">
                                                            {sortDirection === 'asc' ? (
                                                                <CaretUpIcon weight="bold" />
                                                            ) : sortDirection === 'desc' ? (
                                                                <CaretDownIcon weight="bold" />
                                                            ) : (
                                                                <CaretUpDownIcon weight="bold" />
                                                            )}
                                                        </span>
                                                    )}
                                                </div>
                                            )}
                                        </th>
                                    );
                                })}
                            </tr>
                        ))}
                    </thead>
                    <tbody className="data-table__body">
                        {rows.length === 0 ? (
                            <tr className="data-table__empty-row">
                                <td colSpan={table.getVisibleLeafColumns().length} className="data-table__empty-cell">
                                    {emptyState ?? <div className="data-table__empty-state">No data available</div>}
                                </td>
                            </tr>
                        ) : (
                            rows.map((row) => (
                                <tr
                                    key={row.id}
                                    className={[
                                        'data-table__row',
                                        row.getIsSelected() && 'data-table__row--selected',
                                        onRowClick && 'data-table__row--clickable',
                                    ]
                                        .filter(Boolean)
                                        .join(' ')}
                                    onClick={onRowClick ? () => handleRowClickInternal(row.original) : undefined}
                                    onKeyDown={
                                        onRowClick
                                            ? (e) => {
                                                  if (e.key === 'Enter' || e.key === ' ') {
                                                      e.preventDefault();
                                                      handleRowClickInternal(row.original);
                                                  }
                                              }
                                            : undefined
                                    }
                                    tabIndex={onRowClick ? 0 : undefined}
                                    data-row-id={row.id}
                                    data-depth={enableExpanding ? row.depth : undefined}
                                    data-expanded={
                                        enableExpanding && row.getCanExpand() ? row.getIsExpanded() : undefined
                                    }
                                    data-state={row.getIsSelected() ? 'selected' : undefined}
                                    data-clickable={onRowClick ? true : undefined}
                                    aria-selected={row.getIsSelected() ? true : undefined}
                                >
                                    {row.getVisibleCells().map((cell) => {
                                        const header = cell.column.columnDef.header;
                                        const label =
                                            typeof header === 'string'
                                                ? header
                                                : cell.column.columnDef.meta?.headerLabel;
                                        return (
                                            <td
                                                key={cell.id}
                                                className={['data-table__cell', !label && 'data-table__cell--unlabeled']
                                                    .filter(Boolean)
                                                    .join(' ')}
                                                data-label={label || undefined}
                                                data-column={cell.column.id}
                                            >
                                                {flexRender(cell.column.columnDef.cell, cell.getContext())}
                                            </td>
                                        );
                                    })}
                                </tr>
                            ))
                        )}
                    </tbody>
                </table>
            </div>

            {shouldPaginate && !hidePagination && rows.length > 0 && (
                <Pager
                    className="data-table__pager"
                    page={table.getState().pagination.pageIndex + 1}
                    pageCount={Math.max(1, table.getPageCount())}
                    onFirst={() => table.setPageIndex(0)}
                    onPrevious={() => table.previousPage()}
                    onNext={() => table.nextPage()}
                    onLast={() => table.setPageIndex(table.getPageCount() - 1)}
                />
            )}

            {loading && (
                <div className="data-table__loading-overlay" aria-live="polite" aria-busy="true">
                    <div className="data-table__spinner" />
                </div>
            )}
        </div>
    );
}

export const DataTable = forwardRef(DataTableInner) as <TData>(
    props: DataTableProps<TData> & { ref?: React.ForwardedRef<HTMLDivElement> },
) => ReturnType<typeof DataTableInner>;

(DataTable as { displayName?: string }).displayName = 'DataTable';

export default DataTable;
