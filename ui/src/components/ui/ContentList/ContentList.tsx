import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback } from 'react';
import { type ColumnDef, type SortingState, type OnChangeFn, type VisibilityState } from '@tanstack/react-table';

import { DataTable, type DataTableProps } from '../DataTable';
import { AcquisitionBadge } from '../AcquisitionBadge';
import { selectColumn } from '../DataTable/selectColumn';
import type { BulkSelectionState } from '@/types/bulkSelection';
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
import { formatFileSize } from '@/lib/formatSize';
import type { AcquisitionStatus, FulfillmentStatus, MonitoringMode } from '@/types';

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

    monitoring: MonitoringMode | null;

    effectiveMonitoring: MonitoringMode;

    fulfillment: FulfillmentStatus;

    importPending?: boolean;

    year?: number;

    publishDate?: string;

    dateAdded?: string;

    fulfilledAt?: string;

    primaryMediaSize?: number;

    primaryMediaFileCount?: number;

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
    advancedFields?: readonly string[];
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
    extends
        Omit<ComponentPropsWithoutRef<'div'>, 'children'>,
        Omit<DataTableProps<ContentItem>, 'columns' | 'data' | 'rowSelection' | 'onRowSelectionChange'> {
    items: ContentItem[];

    onContentClick?: (item: ContentItem) => void;

    loading?: boolean;

    selection?: BulkSelectionState;

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
    { value: 'false', label: 'Unmonitored' },
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
    monitoring: MonitoringMode | null;
    effectiveMonitoring: MonitoringMode;
}) {
    return (
        <ContentMonitoringIndicator
            monitoring={monitoring}
            effectiveMonitoring={effectiveMonitoring}
            className="content-list__monitor-eye"
            activeClassName="content-list__monitor-eye--on"
            dataMonitoring={effectiveMonitoring !== 'Unmonitored' ? 'true' : 'false'}
        />
    );
}

function FulfillmentCell({ fulfillment, importPending }: { fulfillment: FulfillmentStatus; importPending?: boolean }) {
    return <ContentFulfillmentBadge fulfillment={fulfillment} importPending={importPending} />;
}

function StatusCell({
    fulfillment,
    importPending,
    acquisitionStatus,
}: {
    fulfillment: FulfillmentStatus;
    importPending?: boolean;
    acquisitionStatus?: AcquisitionStatus;
}) {
    return (
        <div className="content-list__status-cell">
            <FulfillmentCell fulfillment={fulfillment} importPending={importPending} />
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
            selection,
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
                    meta: { headerLabel: 'Title', mobileRole: 'header' },
                    header:
                        filterEnabled('title') && headerFilters
                            ? () => (
                                  <ColumnHeaderFilter
                                      type="text"
                                      label="Title"
                                      advanced={headerFilters.advancedFields?.includes('title')}
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
                                      advanced={
                                          headerFilters.advancedFields?.includes('contentType') ||
                                          headerFilters.advancedFields?.includes('level')
                                      }
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
                                      advanced={headerFilters.advancedFields?.includes('monitoring')}
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
                                      advanced={headerFilters.advancedFields?.includes('fulfillment')}
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
                            importPending={row.original.importPending}
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
                                      advanced={
                                          headerFilters.advancedFields?.includes('publishDateFrom') ||
                                          headerFilters.advancedFields?.includes('publishDateTo')
                                      }
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
                {
                    id: 'fulfilledDate',
                    accessorKey: 'fulfilledAt',
                    header: 'Fulfilled',
                    size: 100,
                    sortUndefined: 'last',
                    cell: ({ row }) => <DateAddedCell dateAdded={row.original.fulfilledAt} />,
                },
                {
                    id: 'primaryMediaSize',
                    accessorKey: 'primaryMediaSize',
                    header: 'Media size',
                    size: 110,
                    sortUndefined: 'last',
                    cell: ({ row }) =>
                        row.original.primaryMediaSize === undefined
                            ? '—'
                            : formatFileSize(row.original.primaryMediaSize),
                },
                {
                    id: 'primaryMediaFileCount',
                    accessorKey: 'primaryMediaFileCount',
                    header: 'Media files',
                    size: 100,
                    sortUndefined: 'last',
                    cell: ({ row }) => row.original.primaryMediaFileCount ?? '—',
                },
            );

            return cols;
        }, [headerFilters, headerFilterHandlers, headerFilterColumns]);

        const tableColumns = useMemo(
            () =>
                selection
                    ? [
                          selectColumn<ContentItem>({
                              selection,
                              getId: (item) => item.id,
                              headerLabel: 'Select all',
                              rowLabel: (item) => item.title,
                          }),
                          ...columns,
                      ]
                    : columns,
            [selection, columns],
        );

        const rowSelection = useMemo(
            () =>
                Object.fromEntries(
                    items.filter((item) => selection?.isRowChecked(item.id)).map((item) => [item.id, true]),
                ),
            [items, selection],
        );

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
                data-selection-enabled={selection ? 'true' : undefined}
                {...props}
            >
                <ContentListActionsSlot className="content-list__extension-actions" />
                <ContentListColumnsSlot className="content-list__extension-columns" />

                <DataTable<ContentItem>
                    columns={tableColumns}
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
                    rowSelection={rowSelection}
                />
            </div>
        );
    },
);

ContentList.displayName = 'ContentList';

export default ContentList;
