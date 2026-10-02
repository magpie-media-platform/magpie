import {
    forwardRef,
    type ComponentPropsWithoutRef,
    type ReactNode,
    useEffect,
    useMemo,
    useRef,
    useState,
    useCallback,
} from 'react';
import {
    flexRender,
    getCoreRowModel,
    getSortedRowModel,
    getPaginationRowModel,
    useReactTable,
    type ColumnDef,
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
import { SelectionCircleToggle } from '@/components/ui/SelectionCircleToggle';

import './DataTable.css';

declare module '@tanstack/react-table' {
    // TanStack declaration merging needs both generic parameters, unused or not.
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    interface ColumnMeta<TData extends RowData, TValue> {
        headerLabel?: string;
        mobileRole?: DataTableMobileRole;
    }
}

export type DataTableSize = 'sm' | 'md';

/** Selection wiring for the mobile card collapse's overlay circle. */
export interface DataTableMobileSelectionOverlay<TData> {
    isSelected: (row: TData) => boolean;

    onToggle: (row: TData) => void;

    canSelect?: (row: TData) => boolean;

    rowLabel?: (row: TData) => string;
}

/** Role a cell plays in the mobile card collapse. */
export type DataTableMobileRole = 'header' | 'field' | 'actions' | 'hidden';

function sameIds(left: readonly string[], right: readonly string[]): boolean {
    return left.length === right.length && left.every((id, index) => id === right[index]);
}

function resolveMobileRole(hasLabel: boolean, explicit: DataTableMobileRole | undefined): DataTableMobileRole {
    return explicit ?? (hasLabel ? 'field' : 'actions');
}

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

    /**
     * Called with the ids of the rows the table renders on the current page, in render order, when that set changes.
     * The table compares the ids itself, so an unmemoized callback does not repeat the report.
     */
    onPageRowIdsChange?: (ids: string[]) => void;

    rowCount?: number;

    manualPagination?: boolean;

    manualSorting?: boolean;

    onRowClick?: (row: TData) => void;

    emptyState?: ReactNode;

    filterActions?: ReactNode;

    keepHeaderWhenEmpty?: boolean;

    loading?: boolean;

    getRowId?: (row: TData, index: number) => string;

    /** On the mobile card collapse, whether a selection-mode toggle on the page is currently active. */
    mobileSelectionActive?: boolean;

    /** When set, renders a selection circle over each selectable row's corner instead of an inline checkbox. */
    mobileSelectionOverlay?: DataTableMobileSelectionOverlay<TData>;
}

function DataTableInner<TData>(
    {
        columns,
        data,
        size = 'md',
        enableRowSelection = false,
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
        onPageRowIdsChange,
        rowCount,
        manualPagination = false,
        manualSorting = false,
        onRowClick,
        emptyState,
        filterActions,
        keepHeaderWhenEmpty = false,
        loading = false,
        getRowId,
        mobileSelectionActive,
        mobileSelectionOverlay,
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
            ...(columnVisibility ? { columnVisibility } : {}),
        },
        enableRowSelection,
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
    const pageRowIds = rows.map((row) => row.id);
    const reportedRowIdsRef = useRef<string[]>([]);

    useEffect(() => {
        if (sameIds(reportedRowIdsRef.current, pageRowIds)) {
            return;
        }
        reportedRowIdsRef.current = pageRowIds;
        onPageRowIdsChange?.(pageRowIds);
    });

    const isEmpty = rows.length === 0;
    const headerGroups = isEmpty && !keepHeaderWhenEmpty ? [] : table.getHeaderGroups();

    const classes = useMemo(
        () =>
            [
                'data-table',
                `data-table--${size}`,
                loading && 'data-table--loading',
                filterActions && 'data-table--with-filter-bar',
                className,
            ]
                .filter(Boolean)
                .join(' '),
        [size, loading, filterActions, className],
    );

    return (
        <div
            ref={ref}
            className={classes}
            data-component="data-table"
            data-size={size}
            data-loading={loading || undefined}
            data-mobile-selection-active={
                mobileSelectionActive === undefined ? undefined : String(mobileSelectionActive)
            }
            {...props}
        >
            {filterActions && (
                <div className="data-table__filter-bar" data-component="data-table-filter-bar">
                    {filterActions}
                </div>
            )}

            <div className="data-table__wrapper">
                <table className="data-table__table" role="grid">
                    <thead role="rowgroup" className="data-table__header">
                        {headerGroups.map((headerGroup) => (
                            <tr role="row" key={headerGroup.id} className="data-table__header-row">
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
                                            role="columnheader"
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
                    <tbody role="rowgroup" className="data-table__body">
                        {rows.length === 0 ? (
                            <tr role="row" className="data-table__empty-row">
                                <td
                                    role="gridcell"
                                    colSpan={table.getVisibleLeafColumns().length}
                                    className="data-table__empty-cell"
                                >
                                    {emptyState ?? <div className="data-table__empty-state">No data available</div>}
                                </td>
                            </tr>
                        ) : (
                            rows.map((row) => (
                                <tr
                                    role="row"
                                    key={row.id}
                                    className={[
                                        'data-table__row',
                                        row.getIsSelected() && 'data-table__row--selected',
                                        onRowClick && 'data-table__row--clickable',
                                    ]
                                        .filter(Boolean)
                                        .join(' ')}
                                    onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                                    onKeyDown={
                                        onRowClick
                                            ? (e) => {
                                                  if (e.key === 'Enter' || e.key === ' ') {
                                                      e.preventDefault();
                                                      onRowClick(row.original);
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
                                    {mobileSelectionActive && mobileSelectionOverlay && (
                                        <td role="gridcell" className="data-table__mobile-select-cell">
                                            {(mobileSelectionOverlay.canSelect?.(row.original) ?? true) && (
                                                <SelectionCircleToggle
                                                    selected={mobileSelectionOverlay.isSelected(row.original)}
                                                    onToggle={() => mobileSelectionOverlay.onToggle(row.original)}
                                                    aria-label={`${
                                                        mobileSelectionOverlay.isSelected(row.original)
                                                            ? 'Deselect'
                                                            : 'Select'
                                                    } ${mobileSelectionOverlay.rowLabel?.(row.original) ?? 'row'}`}
                                                    data-action="toggle-row-mobile"
                                                />
                                            )}
                                        </td>
                                    )}
                                    {row.getVisibleCells().map((cell) => {
                                        const header = cell.column.columnDef.header;
                                        const label =
                                            typeof header === 'string'
                                                ? header
                                                : cell.column.columnDef.meta?.headerLabel;
                                        const mobileRole = resolveMobileRole(
                                            Boolean(label),
                                            cell.column.columnDef.meta?.mobileRole,
                                        );
                                        return (
                                            <td
                                                role="gridcell"
                                                key={cell.id}
                                                className="data-table__cell"
                                                data-label={label || undefined}
                                                data-column={cell.column.id}
                                                data-mobile-role={mobileRole}
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

            {shouldPaginate && !hidePagination && table.getPageCount() > 1 && (
                <Pager
                    className="data-table__pager"
                    page={table.getState().pagination.pageIndex + 1}
                    pageCount={Math.max(1, table.getPageCount())}
                    totalItems={rowCount ?? data.length}
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
