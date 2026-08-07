import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { type ColumnDef } from '@tanstack/react-table';
import {
    ArrowsDownUpIcon,
    CheckCircleIcon,
    ClockIcon,
    InfoIcon,
    MagnifyingGlassIcon,
    SortAscendingIcon,
    SortDescendingIcon,
    WarningIcon,
    XCircleIcon,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { DataTable } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { Pager } from '@/components/ui/Pager';
import { entryLabel, RelatedEventsModal } from '@/components/activity/RelatedEventsModal';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { MagpieActivityEntryKind, useContentMagpieActivity, type MagpieActivityEntry } from '@/hooks/useMagpieActivity';
import { formatDateTime } from '@/lib/dateFormatting';
import { formatRelativeTime } from '@/lib/formatTime';
import { getMagpieActivityStatusBadge } from '@/lib/magpieActivityStatusBadge';

import './ActivityTab.css';

const PAGE_SIZE = 25;
const SORT_KEYS = ['activity', 'status', 'type', 'timestamp'] as const;

const ACTIVITY_SORT_LABELS: Record<string, string> = {
    activity: 'Activity',
    status: 'Status',
    type: 'Type',
    timestamp: 'Timestamp',
};

function jobStatusIcon(status?: string | null) {
    switch (status) {
        case 'Completed':
            return (
                <CheckCircleIcon weight="fill" className="activity-tab__status-icon activity-tab__status-icon--ok" />
            );
        case 'Failed':
            return <XCircleIcon weight="fill" className="activity-tab__status-icon activity-tab__status-icon--bad" />;
        case 'Cancelled':
        case 'CancellationRequested':
            return <WarningIcon weight="fill" className="activity-tab__status-icon activity-tab__status-icon--warn" />;
        case 'Running':
            return <ClockIcon weight="fill" className="activity-tab__status-icon activity-tab__status-icon--info" />;
        case null:
        case undefined:
        default:
            return <InfoIcon weight="duotone" className="activity-tab__status-icon" aria-hidden="true" />;
    }
}

interface ActivityTabProps {
    contentHierarchyItemId: string;
}

export function ActivityTab({ contentHierarchyItemId }: ActivityTabProps): ReactNode {
    const { sorting, page, setSorting, setPage } = useUrlTableState<Record<string, string>>({
        defaultSort: [{ id: 'timestamp', desc: true }],
        defaultFilters: {},
        sortKeys: SORT_KEYS,
    });

    const params = useMemo(
        () => ({
            pageNumber: page,
            pageSize: PAGE_SIZE,
        }),
        [page],
    );

    const { activity, isLoading, error } = useContentMagpieActivity(contentHierarchyItemId, params);

    const entries = useMemo(() => activity?.entries ?? [], [activity?.entries]);
    const totalCount = activity?.totalCount ?? 0;
    const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
    const safePage = Math.min(page, totalPages);
    const activeSort = sorting[0];
    const activeSortLabel = activeSort ? ACTIVITY_SORT_LABELS[activeSort.id] : undefined;
    const DirectionIcon = activeSort?.desc ? SortDescendingIcon : SortAscendingIcon;

    const [selectedEntry, setSelectedEntry] = useState<MagpieActivityEntry | null>(null);
    const [detailOpen, setDetailOpen] = useState(false);

    const selectedExecutionId = selectedEntry?.kind === MagpieActivityEntryKind.JobExecution ? selectedEntry.id : null;

    const openDetail = useCallback((entry: MagpieActivityEntry) => {
        setSelectedEntry(entry);
        setDetailOpen(true);
    }, []);

    const onDetailOpenChange = useCallback((open: boolean) => {
        setDetailOpen(open);
        if (!open) {
            setSelectedEntry(null);
        }
    }, []);

    const handleSortFieldChange = useCallback(
        (id: string) => {
            const currentSort = sorting[0];
            setSorting([{ id, desc: currentSort?.id === id ? currentSort.desc : false }]);
        },
        [sorting, setSorting],
    );

    const handleSortDirectionToggle = useCallback(() => {
        if (!activeSort) {
            return;
        }
        setSorting([{ id: activeSort.id, desc: !activeSort.desc }]);
    }, [activeSort, setSorting]);

    const columns = useMemo<ColumnDef<MagpieActivityEntry, unknown>[]>(
        () => [
            {
                id: 'activity',
                header: 'Activity',
                size: 190,
                accessorFn: entryLabel,
                cell: ({ row }) => <span className="activity-tab__activity">{entryLabel(row.original)}</span>,
            },
            {
                id: 'status',
                header: 'Status',
                size: 150,
                accessorFn: (row) =>
                    row.kind === MagpieActivityEntryKind.JobExecution ? (row.status ?? '') : 'Recorded',
                cell: ({ row }) => {
                    const entry = row.original;
                    if (entry.kind === MagpieActivityEntryKind.JobExecution && entry.status) {
                        const { variant, label: statusLabel } = getMagpieActivityStatusBadge(entry.status);
                        return (
                            <span className="activity-tab__status-cell">
                                {jobStatusIcon(entry.status)}
                                <Badge variant={variant} size="sm">
                                    {statusLabel}
                                </Badge>
                            </span>
                        );
                    }
                    return (
                        <span className="activity-tab__status-cell">
                            <InfoIcon
                                weight="fill"
                                className="activity-tab__status-icon activity-tab__status-icon--neutral"
                                aria-hidden="true"
                            />
                            <Badge variant="default" size="sm">
                                Recorded
                            </Badge>
                        </span>
                    );
                },
            },
            {
                id: 'type',
                header: 'Type',
                size: 140,
                accessorFn: (row) => row.category,
                cell: ({ row }) => (
                    <Badge variant="info" size="sm" badgeStyle="outline">
                        {row.original.category}
                    </Badge>
                ),
            },
            {
                id: 'detail',
                header: 'Detail',
                enableSorting: false,
                size: 360,
                cell: ({ row }) => {
                    const detail = row.original.detail;
                    if (!detail) {
                        return (
                            <span className="activity-tab__detail activity-tab__detail--empty" aria-label="No detail">
                                -
                            </span>
                        );
                    }
                    return (
                        <span className="activity-tab__detail line-clamp-2" title={detail}>
                            {detail}
                        </span>
                    );
                },
            },
            {
                id: 'timestamp',
                header: 'Timestamp',
                size: 180,
                accessorFn: (row) => {
                    const time = new Date(row.timestamp).getTime();
                    return isNaN(time) ? 0 : time;
                },
                cell: ({ row }) => (
                    <span className="activity-tab__time" title={formatRelativeTime(row.original.timestamp)}>
                        {formatDateTime(row.original.timestamp)}
                    </span>
                ),
            },
            {
                id: 'actions',
                header: '',
                enableSorting: false,
                size: 120,
                cell: ({ row }) => {
                    const entry = row.original;
                    return (
                        <div className="activity-tab__actions-cell">
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => openDetail(entry)}
                                data-action="view-detail"
                                data-correlation-id={entry.correlationId ?? undefined}
                            >
                                Detail
                            </Button>
                        </div>
                    );
                },
            },
        ],
        [openDetail],
    );

    return (
        <div className="activity-tab" data-component="activity-tab">
            {error && (
                <p className="activity-tab__error" role="alert">
                    Failed to load activity: {error.message}
                </p>
            )}

            <div className="activity-tab__sort-controls" data-testid="activity-tab__sort-controls">
                <ArrowsDownUpIcon className="activity-tab__sort-icon" weight="duotone" aria-hidden="true" />
                <Select value={activeSort?.id ?? ''} onValueChange={handleSortFieldChange}>
                    <SelectTrigger size="sm" aria-label="Sort activity by" data-sort="field">
                        <SelectValue placeholder="Sort by" />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value="activity">Activity</SelectItem>
                        <SelectItem value="status">Status</SelectItem>
                        <SelectItem value="type">Type</SelectItem>
                        <SelectItem value="timestamp">Timestamp</SelectItem>
                    </SelectContent>
                </Select>
                <Button
                    variant="ghost"
                    size="sm"
                    onClick={handleSortDirectionToggle}
                    disabled={!activeSort}
                    aria-label={
                        activeSort
                            ? `Sort activity by ${activeSortLabel} ${activeSort.desc ? 'ascending' : 'descending'}`
                            : 'Choose an activity sort field before changing direction'
                    }
                    data-sort="direction"
                    data-direction={activeSort?.desc ? 'desc' : 'asc'}
                    className="activity-tab__sort-direction"
                >
                    <DirectionIcon weight="bold" aria-hidden="true" />
                </Button>
            </div>

            {isLoading ? (
                <div className="activity-tab__skeletons">
                    {Array.from({ length: 4 }).map((_, i) => (
                        <Skeleton key={i} height={48} />
                    ))}
                </div>
            ) : (
                <DataTable<MagpieActivityEntry>
                    columns={columns}
                    data={entries}
                    size="sm"
                    manualPagination
                    sorting={sorting}
                    onSortingChange={(updater) =>
                        setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                    }
                    rowCount={totalCount}
                    getRowId={(row) => `${row.kind}-${row.id}`}
                    data-testid="activity-tab__table"
                    emptyState={
                        <EmptyState
                            icon={<MagnifyingGlassIcon weight="duotone" />}
                            title="No activity entries"
                            description="No jobs or audit records have touched this item yet."
                        />
                    }
                />
            )}

            {!isLoading && totalPages > 1 && (
                <Pager
                    className="activity-tab__pager"
                    page={safePage}
                    pageCount={totalPages}
                    onFirst={() => setPage(1)}
                    onPrevious={() => setPage(Math.max(1, safePage - 1))}
                    onNext={() => setPage(Math.min(totalPages, safePage + 1))}
                    onLast={() => setPage(totalPages)}
                />
            )}

            <RelatedEventsModal
                executionId={selectedExecutionId}
                correlationId={selectedEntry?.correlationId ?? null}
                selfEntry={selectedEntry}
                seedEntityId={contentHierarchyItemId}
                open={detailOpen}
                onOpenChange={onDetailOpenChange}
            />
        </div>
    );
}

ActivityTab.displayName = 'ActivityTab';

export default ActivityTab;
