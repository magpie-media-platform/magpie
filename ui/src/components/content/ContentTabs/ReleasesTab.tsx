import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback, useState } from 'react';
import {
    ArrowsDownUpIcon,
    CircleNotchIcon,
    DownloadSimpleIcon,
    MagnifyingGlassIcon,
    SortAscendingIcon,
    SortDescendingIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';
import {
    getCoreRowModel,
    getSortedRowModel,
    useReactTable,
    type ColumnDef,
    type SortingState,
} from '@tanstack/react-table';

import { Alert } from '@/components/ui/Alert';
import { Badge, type BadgeVariant } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { DataTable } from '@/components/ui/DataTable';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';

import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { formatDate } from '@/lib/dateFormatting';
import { formatFileSize } from '@/lib/formatSize';

import type { DownloadStatusValue } from '@/types';

import './ContentTabs.css';

export interface ReleaseRow {
    id: string;

    title: string;

    indexerName?: string | null;

    protocol: string;

    publishDate: string;

    size?: number | null;

    infoUrl?: string | null;

    externalId?: string | null;

    categories?: string[];

    languages?: string[];

    properties?: string | null;

    isMatch?: boolean | null;

    rejectionReason?: string | null;

    acquisitionStatus?: DownloadStatusValue | null;

    failureReason?: string | null;
}

export interface ReleasesTabProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    releases: ReleaseRow[];

    loading?: boolean;

    grabbingReleaseIds?: ReadonlySet<string>;

    onGrab: (release: ReleaseRow) => void;

    canGrab?: boolean;

    onTitleClick?: (release: ReleaseRow) => void;

    acquisitionBlockReason?: string | null;

    emptyMessage?: string;
}

const RELEASE_SORT_LABELS: Record<string, string> = {
    title: 'Title',
    size: 'Size',
    status: 'Status',
    publishDate: 'Publish date',
};

function SizeCell({ size }: { size?: number | null }) {
    if (size === null || size === undefined || size <= 0) {
        return <span className="releases-tab__empty-cell">-</span>;
    }
    return <span>{formatFileSize(size)}</span>;
}

function TitleCell({ release, onTitleClick }: { release: ReleaseRow; onTitleClick?: (release: ReleaseRow) => void }) {
    const title = onTitleClick ? (
        <button
            type="button"
            className="releases-tab__title-button"
            onClick={() => onTitleClick(release)}
            title={release.title}
            data-action="release-details"
            data-release-id={release.id}
        >
            {release.title}
        </button>
    ) : (
        <span className="releases-tab__title" title={release.title}>
            {release.title}
        </span>
    );

    return (
        <span className="releases-tab__title-cell">
            {title}
            {release.indexerName && <span className="releases-tab__sources">{release.indexerName}</span>}
        </span>
    );
}

const RELEASE_STATUS_META: Record<DownloadStatusValue, { variant: BadgeVariant; label: string }> = {
    Grabbed: { variant: 'grabbed', label: 'Grabbed' },
    Queued: { variant: 'queued', label: 'Queued' },
    Downloading: { variant: 'downloading', label: 'Downloading' },
    Paused: { variant: 'paused', label: 'Paused' },
    Stalled: { variant: 'stalled', label: 'Stalled' },
    Downloaded: { variant: 'downloaded', label: 'Downloaded' },
    Importing: { variant: 'importing', label: 'Importing' },
    Imported: { variant: 'imported', label: 'Imported' },
    Failed: { variant: 'failed', label: 'Failed' },
    Cancelled: { variant: 'cancelled', label: 'Cancelled' },
};

function humanizeFailureReason(reason?: string | null): string | undefined {
    if (!reason) {
        return undefined;
    }
    return reason.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function StatusCell({ status, failureReason }: { status?: DownloadStatusValue | null; failureReason?: string | null }) {
    if (!status) {
        return <span className="releases-tab__empty-cell">-</span>;
    }
    const meta = RELEASE_STATUS_META[status];
    const reason = status === 'Failed' ? humanizeFailureReason(failureReason) : undefined;
    return (
        <Badge variant={meta.variant} size="sm" dot title={reason} data-acquisition-status={status}>
            {meta.label}
        </Badge>
    );
}

function ActionCell({
    release,
    isGrabbing,
    canGrab,
    onGrab,
}: {
    release: ReleaseRow;
    isGrabbing: boolean;
    canGrab: boolean;
    onGrab: (release: ReleaseRow) => void;
}) {
    if (!canGrab) {
        return null;
    }

    const status = release.acquisitionStatus;
    if (status && status !== 'Failed' && status !== 'Cancelled') {
        return null;
    }

    return (
        <Button
            variant="secondary"
            size="sm"
            onClick={(event) => {
                event.stopPropagation();
                onGrab(release);
            }}
            disabled={isGrabbing}
            aria-busy={isGrabbing || undefined}
            data-action="grab"
            data-release-id={release.id}
        >
            {isGrabbing ? (
                <>
                    <CircleNotchIcon weight="bold" className="animate-spin" aria-hidden="true" />
                    Grabbing...
                </>
            ) : (
                <>
                    <DownloadSimpleIcon weight="bold" aria-hidden="true" />
                    Grab
                </>
            )}
        </Button>
    );
}

export const ReleasesTab = forwardRef<HTMLDivElement, ReleasesTabProps>(
    (
        {
            releases,
            loading = false,
            grabbingReleaseIds = new Set(),
            onGrab,
            canGrab = true,
            onTitleClick,
            acquisitionBlockReason = null,
            emptyMessage = 'No releases found. Try Search Now to query indexers.',
            className,
            ...props
        },
        ref,
    ) => {
        const [sorting, setSorting] = useState<SortingState>([]);
        const usesCardLayout = useCardLayout();
        const handleGrab = useCallback(
            (release: ReleaseRow) => {
                onGrab(release);
            },
            [onGrab],
        );

        const blockBanner = acquisitionBlockReason ? (
            <Alert
                variant="warning"
                className="releases-tab__block-banner"
                icon={<WarningCircleIcon weight="fill" aria-hidden="true" />}
                title="Automatic grab is not available"
                data-testid="releases-tab__block-banner"
            >
                {acquisitionBlockReason}
            </Alert>
        ) : null;

        const columns: ColumnDef<ReleaseRow>[] = useMemo(
            () => [
                {
                    id: 'title',
                    accessorKey: 'title',
                    header: 'Title',
                    cell: ({ row }) => <TitleCell release={row.original} onTitleClick={onTitleClick} />,
                },
                {
                    id: 'size',
                    accessorKey: 'size',
                    header: 'Size',
                    size: 75,
                    cell: ({ row }) => <SizeCell size={row.original.size} />,
                },
                {
                    id: 'status',
                    accessorKey: 'acquisitionStatus',
                    header: 'Status',
                    size: 140,
                    cell: ({ row }) => (
                        <StatusCell
                            status={row.original.acquisitionStatus}
                            failureReason={row.original.failureReason}
                        />
                    ),
                },
                {
                    id: 'publishDate',
                    accessorKey: 'publishDate',
                    header: 'Published',
                    size: 100,
                    cell: ({ row }) => formatDate(row.original.publishDate),
                },
                {
                    id: 'action',
                    header: '',
                    enableSorting: false,
                    size: 90,
                    cell: ({ row }) => (
                        <ActionCell
                            release={row.original}
                            isGrabbing={grabbingReleaseIds.has(row.original.id)}
                            canGrab={canGrab}
                            onGrab={handleGrab}
                        />
                    ),
                },
            ],
            [grabbingReleaseIds, canGrab, handleGrab, onTitleClick],
        );

        const getRowId = useCallback((row: ReleaseRow) => row.id, []);

        const mobileTable = useReactTable({
            columns,
            data: releases,
            state: { sorting },
            getCoreRowModel: getCoreRowModel(),
            getSortedRowModel: getSortedRowModel(),
            getRowId,
        });
        const sortedReleases = mobileTable.getSortedRowModel().rows.map((row) => row.original);
        const activeSort = sorting[0];
        const activeSortLabel = activeSort ? RELEASE_SORT_LABELS[activeSort.id] : undefined;
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

        const classes = ['releases-tab', className].filter(Boolean).join(' ');

        if (!loading && releases.length === 0) {
            return (
                <div
                    ref={ref}
                    className={classes}
                    data-component="releases-tab"
                    data-layout={usesCardLayout ? 'card' : 'table'}
                    {...props}
                >
                    {blockBanner}
                    <div className="releases-tab__empty">
                        <MagnifyingGlassIcon weight="thin" aria-hidden="true" />
                        <p>{emptyMessage}</p>
                    </div>
                </div>
            );
        }

        return (
            <div
                ref={ref}
                className={classes}
                data-component="releases-tab"
                data-layout={usesCardLayout ? 'card' : 'table'}
                {...props}
            >
                {blockBanner}
                <div className="releases-tab__sort-controls" data-testid="releases-tab__sort-controls">
                    <ArrowsDownUpIcon className="releases-tab__sort-icon" weight="duotone" aria-hidden="true" />
                    <Select value={activeSort?.id ?? ''} onValueChange={handleSortFieldChange}>
                        <SelectTrigger size="sm" aria-label="Sort releases by" data-sort="field">
                            <SelectValue placeholder="Sort by" />
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value="title">Title</SelectItem>
                            <SelectItem value="size">Size</SelectItem>
                            <SelectItem value="status">Status</SelectItem>
                            <SelectItem value="publishDate">Publish date</SelectItem>
                        </SelectContent>
                    </Select>
                    <Button
                        variant="ghost"
                        size="sm"
                        onClick={handleSortDirectionToggle}
                        disabled={!activeSort}
                        aria-label={
                            activeSort
                                ? `Sort releases by ${activeSortLabel} ${activeSort.desc ? 'ascending' : 'descending'}`
                                : 'Choose a release sort field before changing direction'
                        }
                        data-sort="direction"
                        data-direction={activeSort?.desc ? 'desc' : 'asc'}
                        className="releases-tab__sort-direction"
                    >
                        <DirectionIcon weight="bold" aria-hidden="true" />
                    </Button>
                </div>
                {usesCardLayout ? (
                    <div className="releases-tab__mobile-list">
                        {sortedReleases.map((release) => {
                            const isGrabbing = grabbingReleaseIds.has(release.id);
                            return (
                                <div
                                    key={release.id}
                                    className="releases-tab__mobile-card"
                                    data-release-id={release.id}
                                >
                                    <TitleCell release={release} onTitleClick={onTitleClick} />
                                    <div className="releases-tab__mobile-meta">
                                        <SizeCell size={release.size} />
                                        <StatusCell
                                            status={release.acquisitionStatus}
                                            failureReason={release.failureReason}
                                        />
                                        <span className="releases-tab__mobile-date">
                                            {formatDate(release.publishDate)}
                                        </span>
                                    </div>
                                    <ActionCell
                                        release={release}
                                        isGrabbing={isGrabbing}
                                        canGrab={canGrab}
                                        onGrab={handleGrab}
                                    />
                                </div>
                            );
                        })}
                    </div>
                ) : (
                    <div className="releases-tab__table">
                        <DataTable<ReleaseRow>
                            className="releases-tab__data-table"
                            columns={columns}
                            data={releases}
                            size="sm"
                            loading={loading}
                            enableSorting={true}
                            sorting={sorting}
                            onSortingChange={setSorting}
                            enablePagination={releases.length > 25}
                            pageSize={25}
                            getRowId={getRowId}
                        />
                    </div>
                )}
            </div>
        );
    },
);

ReleasesTab.displayName = 'ReleasesTab';

export default ReleasesTab;
