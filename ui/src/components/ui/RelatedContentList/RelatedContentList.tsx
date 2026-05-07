import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { Eye, EyeSlash, ImageBroken, FilmSlate } from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { DataTable } from '../DataTable';
import { Badge, type BadgeVariant } from '../Badge';
import { EmptyState } from '../EmptyState';

import './RelatedContentList.css';

export type RelatedContentKind = 'episode' | 'movie' | 'music' | 'book' | 'unknown';

export type RelatedMonitoringStatus = 'none' | 'active' | 'excluded';

export type RelatedFulfillmentStatus = 'missing' | 'upgradable' | 'fulfilled' | 'fulfilledExternally';

export interface RelatedContentItem {
    id: string;

    title: string;

    kind: RelatedContentKind;

    monitoring: RelatedMonitoringStatus;

    fulfillment: RelatedFulfillmentStatus;

    posterUrl?: string;

    year?: number;
}

export interface RelatedContentListProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    items: RelatedContentItem[];

    loading?: boolean;
}

function getFulfillmentVariant(status: RelatedFulfillmentStatus): BadgeVariant {
    const variants: Record<RelatedFulfillmentStatus, BadgeVariant> = {
        missing: 'missing',
        upgradable: 'upgradable',
        fulfilled: 'fulfilled',
        fulfilledExternally: 'fulfilledExternally',
    };
    return variants[status];
}

function formatFulfillmentLabel(status: RelatedFulfillmentStatus): string {
    const labels: Record<RelatedFulfillmentStatus, string> = {
        missing: 'Missing',
        upgradable: 'Upgradable',
        fulfilled: 'Fulfilled',
        fulfilledExternally: 'External',
    };
    return labels[status];
}

function formatKindLabel(kind: RelatedContentKind): string {
    const labels: Record<RelatedContentKind, string> = {
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
            <div className="related-content-list__poster related-content-list__poster--placeholder">
                <ImageBroken weight="thin" aria-hidden="true" />
            </div>
        );
    }

    return <img src={posterUrl} alt={`${title} poster`} className="related-content-list__poster" loading="lazy" />;
}

function TitleCell({ id, title, year }: { id: string; title: string; year?: number }) {
    return (
        <div className="related-content-list__title-cell">
            <Link to={`/library/${id}`} className="related-content-list__title-link">
                {title}
            </Link>
            {year != null && <span className="related-content-list__year">{year}</span>}
        </div>
    );
}

function KindCell({ kind }: { kind: RelatedContentKind }) {
    return (
        <Badge variant="default" size="sm">
            {formatKindLabel(kind)}
        </Badge>
    );
}

function MonitoringCell({ monitoring }: { monitoring: RelatedMonitoringStatus }) {
    const isMonitored = monitoring === 'active';
    const label = monitoring === 'none' ? 'No decision' : isMonitored ? 'Monitored' : 'Not monitored';

    return (
        <div
            className={`related-content-list__monitoring ${isMonitored ? 'related-content-list__monitoring--active' : 'related-content-list__monitoring--inactive'}`}
            title={label}
        >
            {isMonitored ? <Eye weight="fill" aria-label={label} /> : <EyeSlash weight="regular" aria-label={label} />}
            <span className="related-content-list__monitoring-label">
                {monitoring === 'none' ? '--' : isMonitored ? 'Yes' : 'No'}
            </span>
        </div>
    );
}

function FulfillmentCell({ fulfillment }: { fulfillment: RelatedFulfillmentStatus }) {
    return (
        <Badge variant={getFulfillmentVariant(fulfillment)} size="sm" dot>
            {formatFulfillmentLabel(fulfillment)}
        </Badge>
    );
}

export const RelatedContentList = forwardRef<HTMLDivElement, RelatedContentListProps>(
    ({ items, loading = false, className, ...props }, ref) => {
        const columns: ColumnDef<RelatedContentItem>[] = useMemo(
            () => [
                {
                    id: 'poster',
                    accessorKey: 'posterUrl',
                    header: '',
                    enableSorting: false,
                    size: 60,
                    cell: ({ row }) => <PosterCell posterUrl={row.original.posterUrl} title={row.original.title} />,
                },
                {
                    id: 'title',
                    accessorKey: 'title',
                    header: 'Title',
                    size: 300,
                    cell: ({ row }) => (
                        <TitleCell id={row.original.id} title={row.original.title} year={row.original.year} />
                    ),
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
            ],
            [],
        );

        const getRowId = useCallback((row: RelatedContentItem) => row.id, []);

        const classes = ['related-content-list', className].filter(Boolean).join(' ');

        const emptyState = (
            <EmptyState
                icon={<FilmSlate weight="thin" />}
                title="No related content"
                description="No content items are linked to this subject."
                size="sm"
            />
        );

        return (
            <div ref={ref} className={classes} data-component="related-content-list" {...props}>
                <DataTable<RelatedContentItem>
                    columns={columns}
                    data={items}
                    loading={loading}
                    enableSorting={true}
                    enablePagination={false}
                    getRowId={getRowId}
                    emptyState={emptyState}
                />
            </div>
        );
    },
);

RelatedContentList.displayName = 'RelatedContentList';

export default RelatedContentList;
