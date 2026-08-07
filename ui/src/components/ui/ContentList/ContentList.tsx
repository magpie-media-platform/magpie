import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback } from 'react';
import {
    type ColumnDef,
    type RowSelectionState,
    type SortingState,
    type OnChangeFn,
    type VisibilityState,
} from '@tanstack/react-table';

import { DataTable, type DataTableProps } from '../DataTable';
import { AcquisitionBadge } from '../AcquisitionBadge';
import { Checkbox } from '../Checkbox';
import { ColumnHeaderFilter, type FacetOption } from '../ColumnHeaderFilter';
import {
    ContentFulfillmentBadge,
    ContentMonitoringIndicator,
    ContentPoster,
    ContentTypeBadge,
} from '@/components/content/ContentCardPrimitives';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { EmptyState } from '@/components/ui/EmptyState';
import { ContentListActionsSlot, ContentListColumnsSlot } from '@/components/ui/Slot';
import { formatRelativeTime } from '@/lib/formatTime';
import { formatShortDate } from '@/lib/dateFormatting';
import type { AcquisitionStatus, FulfillmentStatus } from '@/types';

import './ContentList.css';

export type { FulfillmentStatus };

export interface ContentItem {
    id: string;

    title: string;

    parentId?: string;

    posterUrl?: string;

    levelLabel?: string;

    level?: string;

    contentType?: string;

    monitoring: boolean | null;

    effectiveMonitoring: boolean;

    fulfillment: FulfillmentStatus;

    year?: number;

    publishDate?: string;

    dateAdded?: string;

    lastSearched?: string;

    acquisitionStatus?: AcquisitionStatus;
}

export interface ContentHeaderFilters {
    contentTypeOptions: FacetOption[];
    contentType: string;
    contentTypeHideAll?: boolean;
    contentTypeLocked?: boolean;
    monitoring: string;
    fulfillment: string;
    title: string;
    publishDateFrom: string;
    publishDateTo: string;
}

export interface ContentHeaderFilterHandlers {
    onContentTypeChange: (value: string) => void;
    onMonitoringChange: (value: string) => void;
    onFulfillmentChange: (value: string) => void;
    onTitleChange: (value: string) => void;
    onPublishDateChange: (range: { from: string; to: string }) => void;
}

type ContentHeaderFilterColumn = 'title' | 'contentType' | 'monitoring' | 'fulfillment' | 'publishDate';

export interface ContentListProps
    extends Omit<ComponentPropsWithoutRef<'div'>, 'children'>, Omit<DataTableProps<ContentItem>, 'columns' | 'data'> {
    items: ContentItem[];

    onContentClick?: (item: ContentItem) => void;

    loading?: boolean;

    enableSelection?: boolean;

    rowSelection?: RowSelectionState;

    onRowSelectionChange?: OnChangeFn<RowSelectionState>;

    sorting?: SortingState;

    onSortingChange?: OnChangeFn<SortingState>;

    manualSorting?: boolean;

    columnVisibility?: VisibilityState;

    headerFilters?: ContentHeaderFilters;

    headerFilterHandlers?: ContentHeaderFilterHandlers;

    headerFilterColumns?: readonly ContentHeaderFilterColumn[];
}

const MONITORING_FACETS: FacetOption[] = [
    { value: 'true', label: 'Monitored' },
    { value: 'false', label: 'Not monitored' },
];

const FULFILLMENT_FACETS: FacetOption[] = [
    { value: 'Missing', label: 'Missing' },
    { value: 'Partial', label: 'Partial' },
    { value: 'Upgradable', label: 'Upgradable' },
    { value: 'Fulfilled', label: 'Fulfilled' },
    { value: 'FulfilledExternally', label: 'Fulfilled Externally' },
];

function TitleCell({ item }: { item: ContentItem }) {
    return (
        <div className="content-list__title-cell">
            <ContentLineageTitle
                className="content-list__title"
                qualifiedName={{ id: item.id, parentId: item.parentId, title: item.title }}
                fallback={{ contentType: item.contentType, level: item.level }}
            />
        </div>
    );
}

function TypeCell({ level, levelLabel, contentType }: { level?: string; levelLabel?: string; contentType?: string }) {
    return <ContentTypeBadge level={level} levelLabel={levelLabel} contentType={contentType} />;
}

function MonitoringCell({
    monitoring,
    effectiveMonitoring,
}: {
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
}) {
    return (
        <ContentMonitoringIndicator
            monitoring={monitoring}
            effectiveMonitoring={effectiveMonitoring}
            className="content-list__monitor-eye"
            activeClassName="content-list__monitor-eye--on"
            dataMonitoring={effectiveMonitoring ? 'true' : 'false'}
        />
    );
}

function FulfillmentCell({
    fulfillment,
    effectiveMonitoring,
}: {
    fulfillment: FulfillmentStatus;
    effectiveMonitoring: boolean;
}) {
    return <ContentFulfillmentBadge fulfillment={fulfillment} effectiveMonitoring={effectiveMonitoring} />;
}

function StatusCell({
    fulfillment,
    effectiveMonitoring,
    acquisitionStatus,
}: {
    fulfillment: FulfillmentStatus;
    effectiveMonitoring: boolean;
    acquisitionStatus?: AcquisitionStatus;
}) {
    return (
        <div className="content-list__status-cell">
            <FulfillmentCell fulfillment={fulfillment} effectiveMonitoring={effectiveMonitoring} />
            {acquisitionStatus && <AcquisitionBadge status={acquisitionStatus} />}
        </div>
    );
}

function DateAddedCell({ dateAdded }: { dateAdded?: string }) {
    return <span className="content-list__date-added">{formatRelativeTime(dateAdded, 'N/A')}</span>;
}

function PublishedCell({ publishDate }: { publishDate?: string }) {
    return <span className="content-list__published">{formatShortDate(publishDate, '--')}</span>;
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
            sorting,
            onSortingChange,
            manualSorting = false,
            columnVisibility,
            headerFilters,
            headerFilterHandlers,
            headerFilterColumns,
            keepHeaderWhenEmpty,
            ...props
        },
        ref,
    ) => {
        const columns: ColumnDef<ContentItem>[] = useMemo(() => {
            const filterEnabled = (column: ContentHeaderFilterColumn) =>
                Boolean(headerFilters) && (headerFilterColumns == null || headerFilterColumns.includes(column));

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
                    cell: ({ row }) => (
                        <ContentPoster
                            posterUrl={row.original.posterUrl}
                            title={row.original.title}
                            className="content-list__poster"
                            placeholderClassName="content-list__poster--placeholder"
                        />
                    ),
                },
                {
                    id: 'title',
                    accessorKey: 'title',
                    meta: { headerLabel: 'Title' },
                    header:
                        filterEnabled('title') && headerFilters
                            ? () => (
                                  <ColumnHeaderFilter
                                      type="text"
                                      label="Title"
                                      active={headerFilters.title.length > 0}
                                      value={headerFilters.title}
                                      onChange={(v) => headerFilterHandlers?.onTitleChange(v)}
                                  />
                              )
                            : 'Title',
                    size: 300,
                    cell: ({ row }) => <TitleCell item={row.original} />,
                },
                {
                    id: 'contentType',
                    accessorKey: 'contentType',
                    header:
                        filterEnabled('contentType') && headerFilters
                            ? () => (
                                  <ColumnHeaderFilter
                                      type="facet"
                                      label="Type"
                                      active={headerFilters.contentType.length > 0}
                                      options={headerFilters.contentTypeOptions}
                                      value={headerFilters.contentType}
                                      onChange={(v) => headerFilterHandlers?.onContentTypeChange(v)}
                                      hideAll={headerFilters.contentTypeHideAll}
                                      disabled={headerFilters.contentTypeLocked}
                                  />
                              )
                            : 'Type',
                    enableSorting: false,
                    size: 100,
                    cell: ({ row }) => (
                        <TypeCell
                            level={row.original.level}
                            levelLabel={row.original.levelLabel}
                            contentType={row.original.contentType}
                        />
                    ),
                },
                {
                    id: 'monitoring',
                    accessorKey: 'monitoring',
                    header:
                        filterEnabled('monitoring') && headerFilters
                            ? () => (
                                  <ColumnHeaderFilter
                                      type="facet"
                                      label="Monitoring"
                                      active={headerFilters.monitoring.length > 0}
                                      options={MONITORING_FACETS}
                                      value={headerFilters.monitoring}
                                      onChange={(v) => headerFilterHandlers?.onMonitoringChange(v)}
                                  />
                              )
                            : '',
                    enableSorting: false,
                    size: 56,
                    cell: ({ row }) => (
                        <MonitoringCell
                            monitoring={row.original.monitoring}
                            effectiveMonitoring={row.original.effectiveMonitoring}
                        />
                    ),
                },
                {
                    id: 'fulfillment',
                    accessorKey: 'fulfillment',
                    header:
                        filterEnabled('fulfillment') && headerFilters
                            ? () => (
                                  <ColumnHeaderFilter
                                      type="facet"
                                      label="Status"
                                      active={headerFilters.fulfillment.length > 0}
                                      options={FULFILLMENT_FACETS}
                                      value={headerFilters.fulfillment}
                                      onChange={(v) => headerFilterHandlers?.onFulfillmentChange(v)}
                                  />
                              )
                            : 'Status',
                    enableSorting: false,
                    size: 230,
                    cell: ({ row }) => (
                        <StatusCell
                            fulfillment={row.original.fulfillment}
                            effectiveMonitoring={row.original.effectiveMonitoring}
                            acquisitionStatus={row.original.acquisitionStatus}
                        />
                    ),
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
                    meta: { headerLabel: 'Published' },
                    header:
                        filterEnabled('publishDate') && headerFilters
                            ? () => (
                                  <ColumnHeaderFilter
                                      type="dateRange"
                                      label="Published"
                                      active={
                                          headerFilters.publishDateFrom.length > 0 ||
                                          headerFilters.publishDateTo.length > 0
                                      }
                                      from={headerFilters.publishDateFrom}
                                      to={headerFilters.publishDateTo}
                                      onChange={(range) => headerFilterHandlers?.onPublishDateChange(range)}
                                  />
                              )
                            : 'Published',
                    size: 156,
                    sortUndefined: 'last',
                    cell: ({ row }) => <PublishedCell publishDate={row.original.publishDate} />,
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
        }, [enableSelection, headerFilters, headerFilterHandlers, headerFilterColumns]);

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
                <ContentListActionsSlot className="content-list__extension-actions" />
                <ContentListColumnsSlot className="content-list__extension-columns" />

                <DataTable<ContentItem>
                    columns={columns}
                    data={items}
                    onRowClick={onContentClick ? handleRowClick : undefined}
                    loading={loading}
                    enableSorting={enableSorting}
                    sorting={sorting}
                    onSortingChange={onSortingChange}
                    manualSorting={manualSorting}
                    columnVisibility={columnVisibility}
                    enablePagination={enablePagination}
                    pageSize={pageSize}
                    manualPagination={manualPagination}
                    pagination={pagination}
                    onPaginationChange={onPaginationChange}
                    rowCount={rowCount}
                    getRowId={getRowId}
                    emptyState={emptyState ?? defaultEmptyState}
                    keepHeaderWhenEmpty={keepHeaderWhenEmpty}
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
