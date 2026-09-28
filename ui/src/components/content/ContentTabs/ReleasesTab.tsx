import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback, useState } from 'react';
import { CircleNotchIcon, DownloadSimpleIcon, MagnifyingGlassIcon } from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { ReleaseStatusBadge } from '@/components/releases/ReleaseStatusBadge';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { DataTable } from '@/components/ui/DataTable';
import { MobileListToolbar } from '@/components/ui/MobileListToolbar';
import { sortTableRows } from '@/lib/sortTableRows';

import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { canRegrab } from '@/hooks/useDownloads';
import { formatDate } from '@/lib/dateFormatting';
import { formatFileSize } from '@/lib/formatSize';

import type { DownloadStatusValue, ReleaseScopeStatus } from '@/types';

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

    acquisitionWaitingReason?: string | null;

    failureReason?: string | null;

    status?: ReleaseScopeStatus;
}

export interface ReleasesTabProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    releases: ReleaseRow[];

    loading?: boolean;

    grabbingReleaseIds?: ReadonlySet<string>;

    onGrab: (release: ReleaseRow) => void;

    canGrab?: boolean;

    onUnmatchChange?: (release: ReleaseRow, status: ReleaseScopeStatus) => void;

    unmatchingReleaseIds?: ReadonlySet<string>;

    onTitleClick?: (release: ReleaseRow) => void;

    emptyMessage?: string;
}

const RELEASE_SORT_OPTIONS = [
    { id: 'title', label: 'Title' },
    { id: 'size', label: 'Size' },
    { id: 'status', label: 'Status' },
    { id: 'publishDate', label: 'Publish date' },
];

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

function StatusCell({
    status,
    failureReason,
    waitingReason,
    unmatched,
}: {
    status?: DownloadStatusValue | null;
    failureReason?: string | null;
    waitingReason?: string | null;
    unmatched?: boolean;
}) {
    if (!unmatched) {
        return <AcquisitionStatus status={status} failureReason={failureReason} waitingReason={waitingReason} />;
    }
    const unmatchedBadge = (
        <Badge variant="default" size="sm" dot data-acquisition-status="Unmatched">
            Unmatched
        </Badge>
    );
    if (!status && !waitingReason) {
        return unmatchedBadge;
    }
    return (
        <span className="releases-tab__statuses">
            <AcquisitionStatus status={status} failureReason={failureReason} waitingReason={waitingReason} />
            {unmatchedBadge}
        </span>
    );
}

function AcquisitionStatus({
    status,
    failureReason,
    waitingReason,
}: {
    status?: DownloadStatusValue | null;
    failureReason?: string | null;
    waitingReason?: string | null;
}) {
    if (waitingReason) {
        return (
            <span data-acquisition-status="Waiting">
                <Badge variant="queued" size="sm" dot>
                    Waiting for capacity
                </Badge>
                <span className="releases-tab__waiting-reason">{waitingReason}</span>
            </span>
        );
    }
    if (!status) {
        return <span className="releases-tab__empty-cell">-</span>;
    }
    return <ReleaseStatusBadge status={status} failureReason={failureReason} />;
}

function UnmatchButton({
    release,
    isUnmatching,
    onUnmatchChange,
}: {
    release: ReleaseRow;
    isUnmatching: boolean;
    onUnmatchChange: (release: ReleaseRow, status: ReleaseScopeStatus) => void;
}) {
    return (
        <Button
            variant="ghost"
            size="sm"
            onClick={(event) => {
                event.stopPropagation();
                onUnmatchChange(release, release.status === 'Unmatched' ? 'Matched' : 'Unmatched');
            }}
            disabled={isUnmatching}
            aria-busy={isUnmatching || undefined}
            data-action={release.status === 'Unmatched' ? 'undo-unmatch' : 'unmatch'}
            data-release-id={release.id}
        >
            {release.status === 'Unmatched' ? 'Undo unmatch' : 'Unmatch'}
        </Button>
    );
}

function ActionCell({
    release,
    isGrabbing,
    canGrab,
    onGrab,
    isUnmatching,
    onUnmatchChange,
}: {
    release: ReleaseRow;
    isGrabbing: boolean;
    canGrab: boolean;
    onGrab: (release: ReleaseRow) => void;
    isUnmatching: boolean;
    onUnmatchChange?: (release: ReleaseRow, status: ReleaseScopeStatus) => void;
}) {
    const status = release.acquisitionStatus;
    const showGrab = canGrab && !release.acquisitionWaitingReason && canRegrab(status);
    if (!showGrab && !onUnmatchChange) {
        return null;
    }

    return (
        <span className="releases-tab__actions">
            {showGrab && <GrabButton release={release} isGrabbing={isGrabbing} onGrab={onGrab} />}
            {onUnmatchChange && (
                <UnmatchButton release={release} isUnmatching={isUnmatching} onUnmatchChange={onUnmatchChange} />
            )}
        </span>
    );
}

function GrabButton({
    release,
    isGrabbing,
    onGrab,
}: {
    release: ReleaseRow;
    isGrabbing: boolean;
    onGrab: (release: ReleaseRow) => void;
}) {
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
            onUnmatchChange,
            unmatchingReleaseIds = new Set(),
            onTitleClick,
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

        const columns: ColumnDef<ReleaseRow>[] = useMemo(
            () => [
                {
                    id: 'title',
                    accessorKey: 'title',
                    header: 'Title',
                    meta: { mobileRole: 'header' },
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
                            waitingReason={row.original.acquisitionWaitingReason}
                            unmatched={row.original.status === 'Unmatched'}
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
                    size: onUnmatchChange ? 200 : 90,
                    cell: ({ row }) => (
                        <ActionCell
                            release={row.original}
                            isGrabbing={grabbingReleaseIds.has(row.original.id)}
                            canGrab={canGrab}
                            onGrab={handleGrab}
                            isUnmatching={unmatchingReleaseIds.has(row.original.id)}
                            onUnmatchChange={onUnmatchChange}
                        />
                    ),
                },
            ],
            [grabbingReleaseIds, canGrab, handleGrab, onTitleClick, onUnmatchChange, unmatchingReleaseIds],
        );

        const getRowId = useCallback((row: ReleaseRow) => row.id, []);

        const sortedReleases = useMemo(
            () => (usesCardLayout ? sortTableRows(releases, columns, sorting) : releases),
            [usesCardLayout, releases, columns, sorting],
        );
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
                {usesCardLayout && (
                    <MobileListToolbar
                        className="releases-tab__sort-controls"
                        data-testid="releases-tab__sort-controls"
                        sorting={sorting}
                        sortOptions={RELEASE_SORT_OPTIONS}
                        onSortingChange={setSorting}
                        allowUnsorted
                    />
                )}
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
                                            waitingReason={release.acquisitionWaitingReason}
                                            unmatched={release.status === 'Unmatched'}
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
                                        isUnmatching={unmatchingReleaseIds.has(release.id)}
                                        onUnmatchChange={onUnmatchChange}
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
