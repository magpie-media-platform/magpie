import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import {
    ArrowsDownUpIcon,
    CaretRightIcon,
    EyeIcon,
    EyeSlashIcon,
    ImageBrokenIcon,
    SortAscendingIcon,
    SortDescendingIcon,
} from '@phosphor-icons/react';
import {
    getCoreRowModel,
    getSortedRowModel,
    useReactTable,
    type ColumnDef,
    type SortingState,
} from '@tanstack/react-table';

import { DataTable } from '@/components/ui/DataTable';
import { AcquisitionBadge } from '@/components/ui/AcquisitionBadge';
import { ContentFulfillmentBadge } from '@/components/content/ContentCardPrimitives';
import { Button } from '@/components/ui/Button';
import { Pager } from '@/components/ui/Pager';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Tooltip } from '@/components/ui/Tooltip';
import type { FulfillmentStatus } from '@/components/ui/ContentList';
import type { AcquisitionStatus } from '@/types';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { resolveEffectiveMonitoring, type ResolvedMonitoring } from '@/lib/monitoring';

import { formatDate } from '@/lib/dateFormatting';

import './ContentTabs.css';

export interface ChildItem {
    id: string;

    title: string;

    subtitle?: string;

    posterUrl?: string;

    monitoring: boolean | null;

    effectiveMonitoring: boolean;

    fulfillment: FulfillmentStatus;

    acquisitionStatus?: AcquisitionStatus;

    number?: number;

    date?: string;

    fileCount?: number;
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

const CHILD_SORT_LABELS: Record<string, string> = {
    number: 'Number',
    title: 'Title',
    date: 'Date',
    fulfillment: 'Status',
};

const DEFAULT_CHILD_SORTING: SortingState = [{ id: 'number', desc: false }];
const EMPTY_CHILD_SORTING: SortingState = [];
const CHILD_PAGE_SIZE = 50;

function PosterCell({ posterUrl, title }: { posterUrl?: string; title: string }) {
    if (!posterUrl) {
        return (
            <div className="children-tab__poster children-tab__poster--placeholder">
                <ImageBrokenIcon weight="thin" aria-hidden="true" />
            </div>
        );
    }

    return <img src={posterUrl} alt={`${title} artwork`} className="children-tab__poster" loading="lazy" />;
}

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

function MonitoringCell({ effective }: { effective: ResolvedMonitoring }) {
    const { monitored: isMonitored, inherited } = effective;
    const label = isMonitored ? 'Monitored' : 'Not monitored';
    const baseDescription = isMonitored
        ? 'Magpie will search for and grab this item automatically'
        : 'Magpie will not search for or grab this item automatically';
    const description = inherited
        ? `${label}. ${baseDescription} (inherited from parent)`
        : `${label}. ${baseDescription}`;
    const ariaLabel = inherited ? `${label} (inherited): ${baseDescription}` : `${label}: ${baseDescription}`;
    const classes = [
        'children-tab__monitoring',
        isMonitored ? 'children-tab__monitoring--active' : 'children-tab__monitoring--inactive',
        inherited ? 'children-tab__monitoring--inherited' : '',
    ]
        .filter(Boolean)
        .join(' ');

    return (
        <Tooltip content={description}>
            <div className={classes} aria-label={ariaLabel} data-inherited={inherited || undefined}>
                {isMonitored ? (
                    <EyeIcon weight="fill" aria-hidden="true" />
                ) : (
                    <EyeSlashIcon weight="regular" aria-hidden="true" />
                )}
            </div>
        </Tooltip>
    );
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
    if (acquisitionStatus) {
        return <AcquisitionBadge status={acquisitionStatus} />;
    }
    return <ContentFulfillmentBadge fulfillment={fulfillment} effectiveMonitoring={effectiveMonitoring} />;
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
        const columns: ColumnDef<ChildItem>[] = useMemo(
            () => [
                {
                    id: 'poster',
                    accessorKey: 'posterUrl',
                    header: '',
                    enableSorting: false,
                    size: 50,
                    cell: ({ row }) => <PosterCell posterUrl={row.original.posterUrl} title={row.original.title} />,
                },
                {
                    id: 'title',
                    accessorKey: 'title',
                    header: 'Title',
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
                    cell: ({ row }) => (
                        <MonitoringCell
                            effective={resolveEffectiveMonitoring(
                                row.original.monitoring,
                                row.original.effectiveMonitoring,
                            )}
                        />
                    ),
                },
                {
                    id: 'fulfillment',
                    accessorKey: 'fulfillment',
                    header: 'Status',
                    size: 110,
                    cell: ({ row }) => (
                        <StatusCell
                            fulfillment={row.original.fulfillment}
                            effectiveMonitoring={row.original.effectiveMonitoring}
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

        const mobileTable = useReactTable({
            columns,
            data: orderedItems,
            state: { sorting: tableSorting },
            getCoreRowModel: getCoreRowModel(),
            getSortedRowModel: getSortedRowModel(),
            getRowId,
        });
        const sortedChildren = mobileTable.getSortedRowModel().rows.map((row) => row.original);
        const mobilePageCount = Math.max(1, Math.ceil(sortedChildren.length / CHILD_PAGE_SIZE));
        const requestedMobilePage = mobilePaging.scope === mobilePageScope ? mobilePaging.page : 1;
        const mobilePage = Math.min(requestedMobilePage, mobilePageCount);
        const pagedChildren = sortedChildren.slice((mobilePage - 1) * CHILD_PAGE_SIZE, mobilePage * CHILD_PAGE_SIZE);
        const goToMobilePage = (page: number) => setMobilePaging({ scope: mobilePageScope, page });
        const activeSortLabel = activeSort ? CHILD_SORT_LABELS[activeSort.id] : undefined;
        const DirectionIcon = activeSort?.desc ? SortDescendingIcon : SortAscendingIcon;

        const handleSortFieldChange = useCallback((id: string) => {
            setSorting((current) => {
                const currentSort = current[0];
                return [{ id, desc: currentSort?.id === id ? currentSort.desc : false }];
            });
        }, []);

        const handleSortDirectionToggle = useCallback(() => {
            if (!activeSort) {
                return;
            }
            setSorting([{ id: activeSort.id, desc: !activeSort.desc }]);
        }, [activeSort]);

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
                            <PosterCell posterUrl={child.posterUrl} title={child.title} />
                            <div className="children-tab__mobile-info">
                                <TitleCell title={child.title} subtitle={child.subtitle} number={child.number} />
                                {child.date && (
                                    <span className="children-tab__mobile-date">{formatDate(child.date)}</span>
                                )}
                                <StatusCell
                                    fulfillment={child.fulfillment}
                                    effectiveMonitoring={child.effectiveMonitoring}
                                    acquisitionStatus={child.acquisitionStatus}
                                />
                            </div>
                            <div className="children-tab__mobile-status">
                                <MonitoringCell
                                    effective={resolveEffectiveMonitoring(child.monitoring, child.effectiveMonitoring)}
                                />
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
                <div className="children-tab__sort-controls" data-testid="children-tab__sort-controls">
                    <ArrowsDownUpIcon className="children-tab__sort-icon" weight="duotone" aria-hidden="true" />
                    <Select value={activeSort?.id ?? ''} onValueChange={handleSortFieldChange}>
                        <SelectTrigger size="sm" aria-label="Sort children by" data-sort="field">
                            <SelectValue placeholder="Sort by" />
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value="number">Number</SelectItem>
                            <SelectItem value="title">Title</SelectItem>
                            <SelectItem value="date">{dateColumnHeader}</SelectItem>
                            <SelectItem value="fulfillment">Status</SelectItem>
                        </SelectContent>
                    </Select>
                    <Button
                        variant="ghost"
                        size="sm"
                        onClick={handleSortDirectionToggle}
                        disabled={!activeSort}
                        aria-label={
                            activeSort
                                ? `Sort children by ${activeSortLabel} ${activeSort.desc ? 'ascending' : 'descending'}`
                                : 'Choose a child sort field before changing direction'
                        }
                        data-sort="direction"
                        data-direction={activeSort?.desc ? 'desc' : 'asc'}
                        className="children-tab__sort-direction"
                    >
                        <DirectionIcon weight="bold" aria-hidden="true" />
                    </Button>
                </div>
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
