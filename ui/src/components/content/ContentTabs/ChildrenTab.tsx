import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { CaretRightIcon } from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { DataTable } from '@/components/ui/DataTable';
import { AcquisitionBadge } from '@/components/ui/AcquisitionBadge';
import {
    ContentFulfillmentBadge,
    ContentMonitoringIndicator,
    ContentPoster,
} from '@/components/content/ContentCardPrimitives';
import { Pager } from '@/components/ui/Pager';
import { MobileListToolbar } from '@/components/ui/MobileListToolbar';
import { sortTableRows } from '@/lib/sortTableRows';
import type { FulfillmentStatus } from '@/components/ui/ContentList';
import type { AcquisitionStatus, MonitoringMode } from '@/types';
import { useCardLayout } from '@/hooks/ui/useCardLayout';

import { formatDate } from '@/lib/dateFormatting';
import { formatFileSize, formatPrimaryMediaSummary } from '@/lib/formatSize';

import './ContentTabs.css';

export interface ChildItem {
    id: string;

    title: string;

    subtitle?: string;

    posterUrl?: string;

    monitoring: MonitoringMode | null;

    effectiveMonitoring: MonitoringMode;

    fulfillment: FulfillmentStatus;

    importPending?: boolean;

    acquisitionStatus?: AcquisitionStatus;

    number?: number;

    date?: string;

    fileCount?: number;
    primaryMediaSize?: number;
    primaryMediaFileCount?: number;
    fulfilledAt?: string;
}

export interface ChildrenTabProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    items: ChildItem[];

    parentId: string;

    basePath?: string;

    dateColumnHeader?: string;

    onChildClick?: (child: ChildItem) => void;

    loading?: boolean;

    emptyMessage?: string;
}

const DEFAULT_CHILD_SORTING: SortingState = [{ id: 'number', desc: false }];
const EMPTY_CHILD_SORTING: SortingState = [];
const CHILD_PAGE_SIZE = 50;

function TitleCell({ title, subtitle, number }: { title: string; subtitle?: string; number?: number }) {
    return (
        <div className="children-tab__title-cell">
            <span className="children-tab__title" title={title}>
                {number !== undefined && <span className="children-tab__number">{number}.</span>}
                {title}
            </span>
            {subtitle && (
                <span className="children-tab__subtitle" title={subtitle}>
                    {subtitle}
                </span>
            )}
        </div>
    );
}

function MonitoringCell({ child }: { child: ChildItem }) {
    return (
        <ContentMonitoringIndicator
            monitoring={child.monitoring}
            effectiveMonitoring={child.effectiveMonitoring}
            className="children-tab__monitoring"
            activeClassName="children-tab__monitoring--active"
            inactiveClassName="children-tab__monitoring--inactive"
        />
    );
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
    if (acquisitionStatus) {
        return <AcquisitionBadge status={acquisitionStatus} />;
    }
    return <ContentFulfillmentBadge fulfillment={fulfillment} importPending={importPending} />;
}

export const ChildrenTab = forwardRef<HTMLDivElement, ChildrenTabProps>(
    (
        {
            items: childItems,
            parentId,
            basePath = '/library',
            dateColumnHeader = 'Date',
            onChildClick,
            loading = false,
            emptyMessage = 'No children found',
            className,
            ...props
        },
        ref,
    ) => {
        const [sorting, setSorting] = useState<SortingState>(DEFAULT_CHILD_SORTING);
        const [mobilePaging, setMobilePaging] = useState({ scope: '', page: 1 });
        const usesCardLayout = useCardLayout();
        const sortOptions = useMemo(
            () => [
                { id: 'number', label: 'Number' },
                { id: 'title', label: 'Title' },
                { id: 'date', label: dateColumnHeader },
                { id: 'fulfillment', label: 'Status' },
                { id: 'fulfilledAt', label: 'Fulfilled' },
                { id: 'primaryMediaSize', label: 'Media size' },
                { id: 'primaryMediaFileCount', label: 'Media files' },
            ],
            [dateColumnHeader],
        );
        const columns: ColumnDef<ChildItem>[] = useMemo(
            () => [
                {
                    id: 'poster',
                    accessorKey: 'posterUrl',
                    header: '',
                    enableSorting: false,
                    size: 50,
                    cell: ({ row }) => (
                        <ContentPoster
                            posterUrl={row.original.posterUrl}
                            title={row.original.title}
                            className="children-tab__poster"
                            placeholderClassName="children-tab__poster--placeholder"
                        />
                    ),
                },
                {
                    id: 'title',
                    accessorKey: 'title',
                    header: 'Title',
                    meta: { mobileRole: 'header' },
                    size: 300,
                    cell: ({ row }) => (
                        <TitleCell
                            title={row.original.title}
                            subtitle={row.original.subtitle}
                            number={row.original.number}
                        />
                    ),
                },
                {
                    id: 'date',
                    accessorKey: 'date',
                    header: dateColumnHeader,
                    size: 120,
                    cell: ({ row }) => (row.original.date ? formatDate(row.original.date) : '-'),
                },
                {
                    id: 'monitoring',
                    accessorKey: 'monitoring',
                    header: '',
                    enableSorting: false,
                    size: 50,
                    cell: ({ row }) => <MonitoringCell child={row.original} />,
                },
                {
                    id: 'fulfilledAt',
                    accessorKey: 'fulfilledAt',
                    header: 'Fulfilled',
                    size: 120,
                    sortUndefined: 'last',
                    cell: ({ row }) => (row.original.fulfilledAt ? formatDate(row.original.fulfilledAt) : '—'),
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
                {
                    id: 'fulfillment',
                    accessorKey: 'fulfillment',
                    header: 'Status',
                    size: 110,
                    cell: ({ row }) => (
                        <StatusCell
                            fulfillment={row.original.fulfillment}
                            importPending={row.original.importPending}
                            acquisitionStatus={row.original.acquisitionStatus}
                        />
                    ),
                },
                {
                    id: 'action',
                    header: '',
                    enableSorting: false,
                    size: 40,
                    cell: () => <CaretRightIcon weight="bold" className="children-tab__caret" aria-hidden="true" />,
                },
            ],
            [dateColumnHeader],
        );

        const handleRowClick = useCallback(
            (row: ChildItem) => {
                if (onChildClick) {
                    onChildClick(row);
                }
            },
            [onChildClick],
        );

        const getRowId = useCallback((row: ChildItem) => row.id, []);

        const activeSort = sorting[0];
        const isNumberSort = activeSort?.id === 'number';
        const numberSortDesc = isNumberSort && activeSort.desc;
        const mobilePageScope = `${parentId}:${activeSort?.id ?? ''}:${activeSort?.desc ? 'desc' : 'asc'}`;

        const orderedItems = useMemo(() => {
            if (!isNumberSort) {
                return childItems;
            }
            return [...childItems].sort((a, b) => {
                if (a.number == null && b.number == null) {
                    return 0;
                }
                if (a.number == null) {
                    return 1;
                }
                if (b.number == null) {
                    return -1;
                }
                return numberSortDesc ? b.number - a.number : a.number - b.number;
            });
        }, [childItems, isNumberSort, numberSortDesc]);

        const tableSorting = isNumberSort ? EMPTY_CHILD_SORTING : sorting;

        const sortedChildren = useMemo(
            () => (usesCardLayout ? sortTableRows(orderedItems, columns, tableSorting) : orderedItems),
            [usesCardLayout, orderedItems, columns, tableSorting],
        );
        const mobilePageCount = Math.max(1, Math.ceil(sortedChildren.length / CHILD_PAGE_SIZE));
        const requestedMobilePage = mobilePaging.scope === mobilePageScope ? mobilePaging.page : 1;
        const mobilePage = Math.min(requestedMobilePage, mobilePageCount);
        const pagedChildren = sortedChildren.slice((mobilePage - 1) * CHILD_PAGE_SIZE, mobilePage * CHILD_PAGE_SIZE);
        const goToMobilePage = (page: number) => setMobilePaging({ scope: mobilePageScope, page });
        const classes = ['children-tab', className].filter(Boolean).join(' ');

        const renderMobileList = () => (
            <div className="children-tab__mobile-list">
                {childItems.length === 0 ? (
                    <div className="children-tab__empty">
                        <p>{emptyMessage}</p>
                    </div>
                ) : (
                    pagedChildren.map((child) => (
                        <Link
                            key={child.id}
                            to={`${basePath}/${child.id}`}
                            className="children-tab__mobile-card"
                            data-child-id={child.id}
                        >
                            <ContentPoster
                                posterUrl={child.posterUrl}
                                title={child.title}
                                className="children-tab__poster"
                                placeholderClassName="children-tab__poster--placeholder"
                            />
                            <div className="children-tab__mobile-info">
                                <TitleCell title={child.title} subtitle={child.subtitle} number={child.number} />
                                {child.date && (
                                    <span className="children-tab__mobile-date">{formatDate(child.date)}</span>
                                )}
                                <span className="children-tab__mobile-date">
                                    Media:{' '}
                                    {formatPrimaryMediaSummary(child.primaryMediaFileCount, child.primaryMediaSize)}
                                </span>
                                <span className="children-tab__mobile-date">
                                    Fulfilled: {child.fulfilledAt ? formatDate(child.fulfilledAt) : '—'}
                                </span>
                                <StatusCell
                                    fulfillment={child.fulfillment}
                                    importPending={child.importPending}
                                    acquisitionStatus={child.acquisitionStatus}
                                />
                            </div>
                            <div className="children-tab__mobile-status">
                                <MonitoringCell child={child} />
                                <CaretRightIcon weight="bold" className="children-tab__caret" aria-hidden="true" />
                            </div>
                        </Link>
                    ))
                )}
                {mobilePageCount > 1 && (
                    <Pager
                        className="children-tab__mobile-pager"
                        page={mobilePage}
                        pageCount={mobilePageCount}
                        totalItems={sortedChildren.length}
                        onFirst={() => goToMobilePage(1)}
                        onPrevious={() => goToMobilePage(mobilePage - 1)}
                        onNext={() => goToMobilePage(mobilePage + 1)}
                        onLast={() => goToMobilePage(mobilePageCount)}
                    />
                )}
            </div>
        );

        return (
            <div
                ref={ref}
                className={classes}
                data-component="children-tab"
                data-layout={usesCardLayout ? 'card' : 'table'}
                {...props}
            >
                {usesCardLayout && (
                    <MobileListToolbar
                        className="children-tab__sort-controls"
                        data-testid="children-tab__sort-controls"
                        sorting={sorting}
                        sortOptions={sortOptions}
                        onSortingChange={setSorting}
                        allowUnsorted
                    />
                )}
                {usesCardLayout ? (
                    renderMobileList()
                ) : (
                    <DataTable<ChildItem>
                        columns={columns}
                        data={orderedItems}
                        onRowClick={handleRowClick}
                        loading={loading}
                        enableSorting={true}
                        sorting={tableSorting}
                        onSortingChange={setSorting}
                        enablePagination={orderedItems.length > CHILD_PAGE_SIZE}
                        pageSize={CHILD_PAGE_SIZE}
                        getRowId={getRowId}
                        emptyState={
                            <div className="children-tab__empty">
                                <p>{emptyMessage}</p>
                            </div>
                        }
                    />
                )}
            </div>
        );
    },
);

ChildrenTab.displayName = 'ChildrenTab';

export default ChildrenTab;
