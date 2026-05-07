import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback } from 'react';
import { ImageBroken } from '@phosphor-icons/react';
import { type ColumnDef, type RowSelectionState, type OnChangeFn } from '@tanstack/react-table';

import { DataTable, type DataTableProps } from '../DataTable';
import { Badge, type BadgeVariant } from '../Badge';
import { Checkbox } from '../Checkbox';
import { EmptyState } from '@/components/ui/EmptyState';
import { formatRelativeTime } from '@/lib/formatTime';

import './ContentList.css';

export type ContentKind = 'episode' | 'movie' | 'music' | 'book' | 'unknown';

export type MonitoringStatus = 'none' | 'active' | 'excluded';

export type FulfillmentStatus = 'missing' | 'upgradable' | 'fulfilled' | 'fulfilledExternally';

export interface ContentItem {
    id: string;

    title: string;

    posterUrl?: string;

    kind: ContentKind;

    monitoring: MonitoringStatus;

    fulfillment: FulfillmentStatus;

    year?: number;

    dateAdded?: string;

    lastSearched?: string;
}

export interface ContentListProps
    extends Omit<ComponentPropsWithoutRef<'div'>, 'children'>, Omit<DataTableProps<ContentItem>, 'columns' | 'data'> {
    items: ContentItem[];

    onContentClick?: (item: ContentItem) => void;

    loading?: boolean;

    enableSelection?: boolean;

    rowSelection?: RowSelectionState;

    onRowSelectionChange?: OnChangeFn<RowSelectionState>;
}

function getFulfillmentVariant(status: FulfillmentStatus): BadgeVariant {
    const variants: Record<FulfillmentStatus, BadgeVariant> = {
        missing: 'missing',
        upgradable: 'upgradable',
        fulfilled: 'fulfilled',
        fulfilledExternally: 'fulfilledExternally',
    };
    return variants[status];
}

function formatFulfillmentLabel(status: FulfillmentStatus): string {
    const labels: Record<FulfillmentStatus, string> = {
        missing: 'Missing',
        upgradable: 'Upgradable',
        fulfilled: 'Fulfilled',
        fulfilledExternally: 'External',
    };
    return labels[status];
}

function formatKindLabel(kind: ContentKind): string {
    const labels: Record<ContentKind, string> = {
        episode: 'Episode',
        movie: 'Movie',
        music: 'Music',
        book: 'Book',
        unknown: 'Unknown',
    };
    return labels[kind];
}

function PosterCell({ posterUrl, title }: { posterUrl?: string; title: string }) {
    if (!posterUrl) {
        return (
            <div className="content-list__poster content-list__poster--placeholder">
                <ImageBroken weight="thin" aria-hidden="true" />
            </div>
        );
    }

    return <img src={posterUrl} alt={`${title} poster`} className="content-list__poster" loading="lazy" />;
}

function TitleCell({ title, year }: { title: string; year?: number }) {
    return (
        <div className="content-list__title-cell">
            <span className="content-list__title">{title}</span>
            {year != null && <span className="content-list__year">{year}</span>}
        </div>
    );
}

function KindCell({ kind }: { kind: ContentKind }) {
    return (
        <Badge variant="default" size="sm">
            {formatKindLabel(kind)}
        </Badge>
    );
}

function getMonitoringVariant(status: MonitoringStatus): BadgeVariant {
    const variants: Record<MonitoringStatus, BadgeVariant> = {
        none: 'default',
        active: 'healthy',
        excluded: 'default',
    };
    return variants[status];
}

function formatMonitoringLabel(status: MonitoringStatus): string {
    const labels: Record<MonitoringStatus, string> = {
        none: 'None',
        active: 'Monitored',
        excluded: 'Excluded',
    };
    return labels[status];
}

function MonitoringCell({ monitoring }: { monitoring: MonitoringStatus }) {
    return (
        <Badge variant={getMonitoringVariant(monitoring)} size="sm" dot>
            {formatMonitoringLabel(monitoring)}
        </Badge>
    );
}

function FulfillmentCell({ fulfillment }: { fulfillment: FulfillmentStatus }) {
    return (
        <Badge variant={getFulfillmentVariant(fulfillment)} size="sm" dot>
            {formatFulfillmentLabel(fulfillment)}
        </Badge>
    );
}

function DateAddedCell({ dateAdded }: { dateAdded?: string }) {
    return <span className="content-list__date-added">{formatRelativeTime(dateAdded, 'N/A')}</span>;
}

export const ContentList = forwardRef<HTMLDivElement, ContentListProps>(
    (
        {
            items,
            onContentClick,
            loading = false,
            className,
            enableSorting = true,
            enablePagination = false,
            pageSize = 25,
            manualPagination = false,
            pagination,
            onPaginationChange,
            rowCount,
            emptyState,
            enableSelection = false,
            rowSelection,
            onRowSelectionChange,
            ...props
        },
        ref,
    ) => {
        const columns: ColumnDef<ContentItem>[] = useMemo(() => {
            const cols: ColumnDef<ContentItem>[] = [];

            if (enableSelection) {
                cols.push({
                    id: 'select',
                    header: ({ table }) => (
                        <div className="content-list__select-all" onClick={(e) => e.stopPropagation()}>
                            <Checkbox
                                checked={
                                    table.getIsAllPageRowsSelected() ||
                                    (table.getIsSomePageRowsSelected() && 'indeterminate')
                                }
                                onCheckedChange={(checked) => table.toggleAllPageRowsSelected(!!checked)}
                                aria-label="Select all"
                            />
                        </div>
                    ),
                    cell: ({ row }) => (
                        <div className="content-list__select-cell" onClick={(e) => e.stopPropagation()}>
                            <Checkbox
                                checked={row.getIsSelected()}
                                onCheckedChange={(checked) => row.toggleSelected(!!checked)}
                                aria-label={`Select ${row.original.title}`}
                            />
                        </div>
                    ),
                    enableSorting: false,
                    size: 50,
                });
            }

            cols.push(
                {
                    id: 'poster',
                    accessorKey: 'posterUrl',
                    header: '',
                    enableSorting: false,
                    size: 48,
                    cell: ({ row }) => <PosterCell posterUrl={row.original.posterUrl} title={row.original.title} />,
                },
                {
                    id: 'title',
                    accessorKey: 'title',
                    header: 'Title',
                    size: 300,
                    cell: ({ row }) => <TitleCell title={row.original.title} year={row.original.year} />,
                },
                {
                    id: 'kind',
                    accessorKey: 'kind',
                    header: 'Kind',
                    size: 100,
                    cell: ({ row }) => <KindCell kind={row.original.kind} />,
                },
                {
                    id: 'monitoring',
                    accessorKey: 'monitoring',
                    header: 'Monitoring',
                    size: 120,
                    cell: ({ row }) => <MonitoringCell monitoring={row.original.monitoring} />,
                },
                {
                    id: 'fulfillment',
                    accessorKey: 'fulfillment',
                    header: 'Status',
                    size: 120,
                    cell: ({ row }) => <FulfillmentCell fulfillment={row.original.fulfillment} />,
                },
                {
                    id: 'dateAdded',
                    accessorKey: 'dateAdded',
                    header: 'Added',
                    size: 100,
                    cell: ({ row }) => <DateAddedCell dateAdded={row.original.dateAdded} />,
                },
            );

            return cols;
        }, [enableSelection]);

        const handleRowClick = useCallback(
            (row: ContentItem) => {
                if (onContentClick) {
                    onContentClick(row);
                }
            },
            [onContentClick],
        );

        const getRowId = useCallback((row: ContentItem) => row.id, []);

        const classes = ['content-list', className].filter(Boolean).join(' ');

        const defaultEmptyState = <EmptyState title="No content found" size="sm" />;

        return (
            <div
                ref={ref}
                className={classes}
                data-component="content-list"
                data-selection-enabled={enableSelection ? 'true' : undefined}
                {...props}
            >
                <DataTable<ContentItem>
                    columns={columns}
                    data={items}
                    onRowClick={onContentClick ? handleRowClick : undefined}
                    loading={loading}
                    enableSorting={enableSorting}
                    enablePagination={enablePagination}
                    pageSize={pageSize}
                    manualPagination={manualPagination}
                    pagination={pagination}
                    onPaginationChange={onPaginationChange}
                    rowCount={rowCount}
                    getRowId={getRowId}
                    emptyState={emptyState ?? defaultEmptyState}
                    enableRowSelection={enableSelection}
                    enableMultiRowSelection={enableSelection}
                    rowSelection={rowSelection}
                    onRowSelectionChange={onRowSelectionChange}
                />
            </div>
        );
    },
);

ContentList.displayName = 'ContentList';

export default ContentList;
