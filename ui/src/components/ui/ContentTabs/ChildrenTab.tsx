import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { CaretRight, Eye, EyeSlash, ImageBroken } from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { DataTable } from '../DataTable';
import { Badge, type BadgeVariant } from '../Badge';
import type { FulfillmentStatus, MonitoringStatus } from '../ContentList';

import './ContentTabs.css';

export interface ChildItem {
    id: string;

    title: string;

    subtitle?: string;

    posterUrl?: string;

    monitoring: MonitoringStatus;

    fulfillment: FulfillmentStatus;

    number?: number;

    airDate?: string;

    fileCount?: number;
}

export interface ChildrenTabProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    items: ChildItem[];

    parentId: string;

    basePath?: string;

    onChildClick?: (child: ChildItem) => void;

    loading?: boolean;

    emptyMessage?: string;
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

function formatDate(dateStr: string): string {
    try {
        const date = new Date(dateStr);
        return date.toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
        });
    } catch {
        return dateStr;
    }
}

function PosterCell({ posterUrl, title }: { posterUrl?: string; title: string }) {
    if (!posterUrl) {
        return (
            <div className="children-tab__poster children-tab__poster--placeholder">
                <ImageBroken weight="thin" aria-hidden="true" />
            </div>
        );
    }

    return <img src={posterUrl} alt={`${title} poster`} className="children-tab__poster" loading="lazy" />;
}

function TitleCell({ title, subtitle, number }: { title: string; subtitle?: string; number?: number }) {
    return (
        <div className="children-tab__title-cell">
            <span className="children-tab__title">
                {number !== undefined && <span className="children-tab__number">{number}.</span>}
                {title}
            </span>
            {subtitle && <span className="children-tab__subtitle">{subtitle}</span>}
        </div>
    );
}

function MonitoringCell({ monitoring }: { monitoring: MonitoringStatus }) {
    const isMonitored = monitoring === 'active';
    const label = monitoring === 'none' ? 'No decision' : isMonitored ? 'Monitored' : 'Not monitored';

    return (
        <div
            className={`children-tab__monitoring ${isMonitored ? 'children-tab__monitoring--active' : 'children-tab__monitoring--inactive'}`}
            title={label}
        >
            {isMonitored ? <Eye weight="fill" aria-label={label} /> : <EyeSlash weight="regular" aria-label={label} />}
        </div>
    );
}

function FulfillmentCell({ fulfillment }: { fulfillment: FulfillmentStatus }) {
    return (
        <Badge variant={getFulfillmentVariant(fulfillment)} size="sm" dot>
            {formatFulfillmentLabel(fulfillment)}
        </Badge>
    );
}

export const ChildrenTab = forwardRef<HTMLDivElement, ChildrenTabProps>(
    (
        {
            items: childItems,
            parentId: _parentId,
            basePath = '/library',
            onChildClick,
            loading = false,
            emptyMessage = 'No children found',
            className,
            ...props
        },
        ref,
    ) => {
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
                    id: 'airDate',
                    accessorKey: 'airDate',
                    header: 'Air Date',
                    size: 120,
                    cell: ({ row }) => (row.original.airDate ? formatDate(row.original.airDate) : '-'),
                },
                {
                    id: 'monitoring',
                    accessorKey: 'monitoring',
                    header: '',
                    enableSorting: false,
                    size: 50,
                    cell: ({ row }) => <MonitoringCell monitoring={row.original.monitoring} />,
                },
                {
                    id: 'fulfillment',
                    accessorKey: 'fulfillment',
                    header: 'Status',
                    size: 110,
                    cell: ({ row }) => <FulfillmentCell fulfillment={row.original.fulfillment} />,
                },
                {
                    id: 'action',
                    header: '',
                    enableSorting: false,
                    size: 40,
                    cell: () => <CaretRight weight="bold" className="children-tab__caret" aria-hidden="true" />,
                },
            ],
            [],
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

        const classes = ['children-tab', className].filter(Boolean).join(' ');

        const renderMobileList = () => (
            <div className="children-tab__mobile-list">
                {childItems.length === 0 ? (
                    <div className="children-tab__empty">
                        <p>{emptyMessage}</p>
                    </div>
                ) : (
                    childItems.map((child) => (
                        <Link
                            key={child.id}
                            to={`${basePath}/${child.id}`}
                            className="children-tab__mobile-card"
                            data-child-id={child.id}
                        >
                            <PosterCell posterUrl={child.posterUrl} title={child.title} />
                            <div className="children-tab__mobile-info">
                                <TitleCell title={child.title} subtitle={child.subtitle} number={child.number} />
                                {child.airDate && (
                                    <span className="children-tab__mobile-date">{formatDate(child.airDate)}</span>
                                )}
                                <FulfillmentCell fulfillment={child.fulfillment} />
                            </div>
                            <div className="children-tab__mobile-status">
                                <MonitoringCell monitoring={child.monitoring} />
                                <CaretRight weight="bold" className="children-tab__caret" aria-hidden="true" />
                            </div>
                        </Link>
                    ))
                )}
            </div>
        );

        return (
            <div ref={ref} className={classes} data-component="children-tab" {...props}>
                <DataTable<ChildItem>
                    columns={columns}
                    data={childItems}
                    onRowClick={handleRowClick}
                    loading={loading}
                    enableSorting={true}
                    enablePagination={false}
                    getRowId={getRowId}
                    emptyState={
                        <div className="children-tab__empty">
                            <p>{emptyMessage}</p>
                        </div>
                    }
                />

                {renderMobileList()}
            </div>
        );
    },
);

ChildrenTab.displayName = 'ChildrenTab';

export default ChildrenTab;
