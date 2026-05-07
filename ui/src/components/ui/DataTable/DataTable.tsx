import { forwardRef, type ComponentPropsWithoutRef, type ReactNode, useMemo, useState, useCallback } from 'react';
import {
    flexRender,
    getCoreRowModel,
    getSortedRowModel,
    getPaginationRowModel,
    useReactTable,
    type ColumnDef,
    type SortingState,
    type PaginationState,
    type RowSelectionState,
    type OnChangeFn,
    type Row,
} from '@tanstack/react-table';
import { CaretUp, CaretDown, CaretUpDown } from '@phosphor-icons/react';

import './DataTable.css';

export type DataTableSize = 'sm' | 'md' | 'lg';

const AUTO_PAGINATE_THRESHOLD = 50;

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

    enablePagination?: boolean;

    pageSize?: number;

    pagination?: PaginationState;

    onPaginationChange?: OnChangeFn<PaginationState>;

    rowCount?: number;

    manualPagination?: boolean;

    manualSorting?: boolean;

    onRowClick?: (row: TData) => void;

    emptyState?: ReactNode;

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
        enablePagination = false,
        pageSize = 10,
        pagination: controlledPagination,
        onPaginationChange,
        rowCount,
        manualPagination = false,
        manualSorting = false,
        onRowClick,
        emptyState,
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
        },
        enableRowSelection,
        enableMultiRowSelection,
        onSortingChange: handleSortingChange,
        onRowSelectionChange: handleRowSelectionChange,
        onPaginationChange: handlePaginationChange,
        getCoreRowModel: getCoreRowModel(),
        getSortedRowModel: manualSorting ? undefined : getSortedRowModel(),
        getPaginationRowModel: shouldPaginate && !manualPagination ? getPaginationRowModel() : undefined,
        manualPagination,
        manualSorting,
        rowCount,
        getRowId,
    });

    const rows = table.getRowModel().rows;
    const headerGroups = table.getHeaderGroups();

    const classes = useMemo(
        () =>
            [
                'data-table',
                `data-table--${size}`,
                stickyHeader && 'data-table--sticky-header',
                bordered && 'data-table--bordered',
                striped && 'data-table--striped',
                loading && 'data-table--loading',
                className,
            ]
                .filter(Boolean)
                .join(' '),
        [size, stickyHeader, bordered, striped, loading, className],
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
            <div className="data-table__wrapper">
                <table className="data-table__table" role="grid">
                    <thead className="data-table__header">
                        {headerGroups.map((headerGroup) => (
                            <tr key={headerGroup.id} className="data-table__header-row">
                                {headerGroup.headers.map((header) => {
                                    const canSort = enableSorting && header.column.getCanSort();
                                    const sortDirection = header.column.getIsSorted();

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
                                                    aria-label={
                                                        canSort
                                                            ? `Sort by ${String(header.column.columnDef.header)}`
                                                            : undefined
                                                    }
                                                >
                                                    {flexRender(header.column.columnDef.header, header.getContext())}
                                                    {canSort && (
                                                        <span className="data-table__sort-icon" aria-hidden="true">
                                                            {sortDirection === 'asc' ? (
                                                                <CaretUp weight="bold" />
                                                            ) : sortDirection === 'desc' ? (
                                                                <CaretDown weight="bold" />
                                                            ) : (
                                                                <CaretUpDown weight="bold" />
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
                                <td colSpan={columns.length} className="data-table__empty-cell">
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
                                    data-state={row.getIsSelected() ? 'selected' : undefined}
                                    data-clickable={onRowClick ? true : undefined}
                                    aria-selected={row.getIsSelected() ? true : undefined}
                                >
                                    {row.getVisibleCells().map((cell) => {
                                        const header = cell.column.columnDef.header;
                                        const label = typeof header === 'string' ? header : undefined;
                                        return (
                                            <td
                                                key={cell.id}
                                                className="data-table__cell"
                                                data-label={label || undefined}
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

            {shouldPaginate && rows.length > 0 && (
                <div className="data-table__pagination">
                    <div className="data-table__pagination-info">
                        {table.getState().pagination.pageIndex * table.getState().pagination.pageSize + 1}-
                        {Math.min(
                            (table.getState().pagination.pageIndex + 1) * table.getState().pagination.pageSize,
                            rowCount ?? data.length,
                        )}{' '}
                        of {rowCount ?? data.length}
                    </div>
                    <div className="data-table__pagination-controls">
                        <button
                            type="button"
                            className="data-table__pagination-button"
                            onClick={() => table.setPageIndex(0)}
                            disabled={!table.getCanPreviousPage()}
                            aria-label="Go to first page"
                        >
                            First
                        </button>
                        <button
                            type="button"
                            className="data-table__pagination-button"
                            onClick={() => table.previousPage()}
                            disabled={!table.getCanPreviousPage()}
                            aria-label="Go to previous page"
                        >
                            Previous
                        </button>
                        <span className="data-table__pagination-page">
                            Page {table.getState().pagination.pageIndex + 1} of {table.getPageCount()}
                        </span>
                        <button
                            type="button"
                            className="data-table__pagination-button"
                            onClick={() => table.nextPage()}
                            disabled={!table.getCanNextPage()}
                            aria-label="Go to next page"
                        >
                            Next
                        </button>
                        <button
                            type="button"
                            className="data-table__pagination-button"
                            onClick={() => table.setPageIndex(table.getPageCount() - 1)}
                            disabled={!table.getCanNextPage()}
                            aria-label="Go to last page"
                        >
                            Last
                        </button>
                    </div>
                </div>
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
