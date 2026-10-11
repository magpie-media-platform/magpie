import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ArrowClockwiseIcon, ClockCounterClockwiseIcon, PulseIcon } from '@phosphor-icons/react';
import type { ColumnDef, SortingState } from '@tanstack/react-table';

import { entryLabel, RelatedEventsModal } from '@/components/activity/RelatedEventsModal';
import { LocalContentLink } from '@/components/content/LocalContentPreview';
import { Alert } from '@/components/ui/Alert';
import { Badge, type BadgeVariant } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import {
    BETWEEN_DATES_PRESET,
    ColumnHeaderFilter,
    type DateRangeValue,
    type FacetOption,
} from '@/components/ui/ColumnHeaderFilter';
import { DataTable } from '@/components/ui/DataTable';
import { ListFilterToolbar, type ListFilterField, type ListFilterValues } from '@/components/ui/ListFilterToolbar';
import { type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { decodeMultiValue, encodeMultiValue, MultiSelectColumnFilter } from '@/components/ui/MultiSelectColumnFilter';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pager } from '@/components/ui/Pager';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton } from '@/components/ui/QuickActionsButton';
import { Skeleton } from '@/components/ui/Skeleton';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { MagpieActivityEntryKind, useAuditHistory, type MagpieActivityEntry } from '@/hooks/useMagpieActivity';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { useUserMap } from '@/hooks/useUserMap';
import { useUsers } from '@/hooks/useUsers';
import { humanizeCustomKey } from '@/lib/contentTypeDisplay';
import { formatDateTime } from '@/lib/dateFormatting';
import { getErrorMessage } from '@/lib/errors';
import { formatRelativeTime } from '@/lib/formatTime';
import { humanizeJobType } from '@/lib/jobExecution';
import { getMagpieActivityStatusBadge } from '@/lib/magpieActivityStatusBadge';

import { PurgeActivityLogDialog } from './PurgeActivityLogDialog';

import './ActivityLogPage.css';

const PAGE_SIZE = 25;

const MAGPIE_USER = 'magpie';

const SYSTEM_PROVIDER_TYPE = 'system';

const TIME_PRESETS: FacetOption[] = [
    { value: '1h', label: 'Last hour' },
    { value: '24h', label: 'Last 24 hours' },
    { value: '7d', label: 'Last 7 days' },
    { value: '30d', label: 'Last 30 days' },
];

const TIME_PRESET_MS: Record<string, number> = {
    '1h': 3_600_000,
    '24h': 86_400_000,
    '7d': 604_800_000,
    '30d': 2_592_000_000,
};

const CATEGORY_OPTIONS: FacetOption[] = ['Security', 'Content', 'Jobs', 'Integrations', 'System'].map((category) => ({
    value: category,
    label: category,
}));

const CATEGORY_BADGES: Record<string, BadgeVariant> = {
    Security: 'warning',
    Content: 'info',
    Jobs: 'success',
    Integrations: 'primary',
    System: 'default',
};

const STATUS_OPTIONS: FacetOption[] = [
    'Queued',
    'Running',
    'Completed',
    'Failed',
    'Cancelled',
    'CancellationRequested',
].map((status) => ({ value: status, label: getMagpieActivityStatusBadge(status).label }));

const SORT_OPTIONS: MobileListSortOption[] = [{ id: 'time', label: 'Time', initialDirection: 'desc' }];

const DEFAULT_SORT: SortingState = [{ id: 'time', desc: true }];

const DEFAULT_FILTERS = { time: '', from: '', to: '', category: '', search: '', user: '', status: '' };

type ActivityLogFilters = typeof DEFAULT_FILTERS & Record<string, string>;

function targetPath(entityType: string, id: string): string | null {
    switch (entityType) {
        case 'JobExecution':
            return `/admin/jobs?job=${encodeURIComponent(id)}`;
        case 'User':
            return '/admin/users';
        case 'Extension':
            return `/admin/extensions/${encodeURIComponent(id)}`;
        case 'Subject':
            return `/subjects/${encodeURIComponent(id)}`;
        case 'Wishlist':
            return `/wishlists/${encodeURIComponent(id)}`;
        case 'QualityTierList':
            return `/admin/quality-tiers/${encodeURIComponent(id)}`;
        default:
            return null;
    }
}

function TargetCell({ entry }: { entry: MagpieActivityEntry }): ReactNode {
    const { entityId: id, entityType } = entry;
    if (!id || !entityType) {
        return <span className="activity-log-page__empty">—</span>;
    }
    const name =
        entry.entityName && entityType === 'JobExecution'
            ? humanizeJobType(entry.entityName)
            : (entry.entityName ?? humanizeCustomKey(entityType));
    if (entityType === 'ContentHierarchyItem') {
        return <LocalContentLink contentId={id}>{name}</LocalContentLink>;
    }
    const path = targetPath(entityType, id);
    return path ? <Link to={path}>{name}</Link> : <span>{name}</span>;
}

function relatedExecutionId(entry: MagpieActivityEntry): string | null {
    if (entry.kind === MagpieActivityEntryKind.JobExecution) {
        return entry.id;
    }
    return entry.jobExecutionId ?? (entry.entityType === 'JobExecution' ? (entry.entityId ?? null) : null);
}

export function ActivityLogPage() {
    useDocumentTitle('Activity Log');
    const { filters, sorting, page, setFilters, setSorting, setPage } = useUrlTableState<ActivityLogFilters>({
        defaultSort: DEFAULT_SORT,
        sortKeys: ['time'],
        defaultFilters: DEFAULT_FILTERS,
    });
    const userMap = useUserMap();
    const { users } = useUsers();
    const systemUserIds = useMemo(
        () => new Set(users.filter((user) => user.providerType === SYSTEM_PROVIDER_TYPE).map((user) => user.id)),
        [users],
    );
    const [selectedEntry, setSelectedEntry] = useState<MagpieActivityEntry | null>(null);
    const [purgeOpen, setPurgeOpen] = useState(false);

    const timePreset = TIME_PRESET_MS[filters.time] ? filters.time : '';
    const timeRange: DateRangeValue = {
        preset: timePreset || (filters.from || filters.to ? BETWEEN_DATES_PRESET.value : ''),
        from: filters.from,
        to: filters.to,
    };
    const categories = useMemo(() => decodeMultiValue(filters.category), [filters.category]);
    const userOptions = useMemo<FacetOption[]>(
        () => [
            { value: MAGPIE_USER, label: 'Magpie' },
            ...[...userMap]
                .filter(([id]) => !systemUserIds.has(id))
                .map(([id, username]) => ({ value: id, label: username }))
                .sort((left, right) => left.label.localeCompare(right.label)),
        ],
        [systemUserIds, userMap],
    );

    const setTimeRange = useCallback(
        (range: DateRangeValue) =>
            setFilters(
                range.preset === BETWEEN_DATES_PRESET.value
                    ? { time: '', from: range.from, to: range.to }
                    : { time: range.preset, from: '', to: '' },
            ),
        [setFilters],
    );

    const {
        page: result,
        isLoading,
        isFetching,
        error,
        refetch,
    } = useAuditHistory({
        sinceMs: TIME_PRESET_MS[timePreset],
        from: filters.from ? new Date(`${filters.from}T00:00:00`).toISOString() : undefined,
        to: filters.to ? new Date(`${filters.to}T23:59:59.999`).toISOString() : undefined,
        category: categories.length > 0 ? categories : undefined,
        userId: filters.user && filters.user !== MAGPIE_USER ? filters.user : undefined,
        hasUser: filters.user === MAGPIE_USER ? false : undefined,
        search: filters.search || undefined,
        status: filters.status || undefined,
        sortDirection: sorting[0]?.desc === false ? 'Ascending' : 'Descending',
        pageNumber: page,
        pageSize: PAGE_SIZE,
    });

    const entries = result?.items ?? [];
    const totalItems = result?.totalItems ?? 0;
    const pageCount = Math.max(1, Math.ceil(totalItems / PAGE_SIZE));

    useEffect(() => {
        if (result && !isFetching && page > pageCount) {
            setPage(pageCount);
        }
    }, [isFetching, page, pageCount, result, setPage]);

    const updateSorting = (next: SortingState) => setSorting(next.length === 0 ? [{ id: 'time', desc: false }] : next);

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const columns = useMemo<ColumnDef<MagpieActivityEntry, unknown>[]>(
        () => [
            {
                id: 'time',
                accessorFn: (row) => row.timestamp,
                meta: { headerLabel: 'Time', mobileRole: 'header' },
                header: () => (
                    <ColumnHeaderFilter
                        type="dateRange"
                        label="Time"
                        active={timeRange.preset !== ''}
                        preset={timeRange.preset}
                        presets={TIME_PRESETS}
                        from={timeRange.from}
                        to={timeRange.to}
                        onChange={setTimeRange}
                    />
                ),
                cell: ({ row }) => (
                    <span className="activity-log-page__time">
                        <span>{formatRelativeTime(row.original.timestamp)}</span>
                        <span className="activity-log-page__exact-time">
                            {formatDateTime(row.original.timestamp, { seconds: true })}
                        </span>
                    </span>
                ),
            },
            {
                id: 'category',
                enableSorting: false,
                meta: { mobileRole: 'header' },
                header: () => (
                    <MultiSelectColumnFilter
                        label="Category"
                        options={CATEGORY_OPTIONS}
                        value={categories}
                        onChange={(next) => setFilters({ category: encodeMultiValue(next) })}
                    />
                ),
                cell: ({ row }) => (
                    <Badge variant={CATEGORY_BADGES[row.original.category] ?? 'default'} size="sm">
                        {row.original.category}
                    </Badge>
                ),
            },
            {
                id: 'event',
                enableSorting: false,
                meta: { mobileRole: 'field' },
                header: () => (
                    <ColumnHeaderFilter
                        type="text"
                        label="Event"
                        active={filters.search !== ''}
                        value={filters.search}
                        onChange={(search) => setFilters({ search })}
                    />
                ),
                cell: ({ row }) => (
                    <span className="activity-log-page__event">
                        <button
                            type="button"
                            className="activity-log-page__event-name"
                            onClick={() => setSelectedEntry(row.original)}
                            data-action="view-detail"
                        >
                            {entryLabel(row.original)}
                        </button>
                        {row.original.detail && (
                            <span className="activity-log-page__detail">{row.original.detail}</span>
                        )}
                    </span>
                ),
            },
            {
                id: 'user',
                enableSorting: false,
                meta: { mobileRole: 'field' },
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="User"
                        active={filters.user !== ''}
                        options={userOptions}
                        value={filters.user}
                        onChange={(user) => setFilters({ user })}
                    />
                ),
                cell: ({ row }) => {
                    const { userId } = row.original;
                    if (!userId) {
                        return <span className="activity-log-page__empty">—</span>;
                    }
                    return systemUserIds.has(userId) ? 'Magpie' : (userMap.get(userId) ?? userId);
                },
            },
            {
                id: 'target',
                enableSorting: false,
                meta: { mobileRole: 'hidden' },
                header: 'Target',
                cell: ({ row }) => <TargetCell entry={row.original} />,
            },
            {
                id: 'status',
                enableSorting: false,
                meta: { mobileRole: 'field' },
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="Status"
                        active={filters.status !== ''}
                        options={STATUS_OPTIONS}
                        value={filters.status}
                        onChange={(status) => setFilters({ status })}
                    />
                ),
                cell: ({ row }) => {
                    if (row.original.kind !== MagpieActivityEntryKind.JobExecution || !row.original.status) {
                        return <span className="activity-log-page__empty">—</span>;
                    }
                    const { variant, label } = getMagpieActivityStatusBadge(row.original.status);
                    return (
                        <Badge variant={variant} size="sm">
                            {label}
                        </Badge>
                    );
                },
            },
        ],
        [
            categories,
            filters.search,
            filters.status,
            filters.user,
            setFilters,
            setTimeRange,
            systemUserIds,
            timeRange.from,
            timeRange.preset,
            timeRange.to,
            userMap,
            userOptions,
        ],
    );

    const filterFields: ListFilterField[] = [
        { id: 'time', label: 'Time', kind: 'dateRange', value: timeRange, presets: TIME_PRESETS },
        { id: 'category', label: 'Category', kind: 'multi', value: categories, options: CATEGORY_OPTIONS },
        { id: 'search', label: 'Event', kind: 'text', value: filters.search },
        { id: 'user', label: 'User', kind: 'facet', value: filters.user, options: userOptions },
        { id: 'status', label: 'Status', kind: 'facet', value: filters.status, options: STATUS_OPTIONS },
    ];

    const commitFilterFields = (values: ListFilterValues) => {
        const range = values.time as DateRangeValue;
        setFilters({
            time: range.preset === BETWEEN_DATES_PRESET.value ? '' : range.preset,
            from: range.preset === BETWEEN_DATES_PRESET.value ? range.from : '',
            to: range.preset === BETWEEN_DATES_PRESET.value ? range.to : '',
            category: encodeMultiValue(values.category as string[]),
            search: values.search as string,
            user: values.user as string,
            status: values.status as string,
        });
    };

    const count = result && (
        <span className="activity-log-page__count">
            {`${totalItems.toLocaleString()} ${totalItems === 1 ? 'result' : 'results'}`}
        </span>
    );

    return (
        <div className="activity-log-page" data-page="activity-log">
            <PullToRefresh onRefresh={handleRefresh} className="activity-log-page__pull-to-refresh">
                <PageHeader
                    title="Activity Log"
                    icon={<PulseIcon weight="duotone" aria-hidden="true" />}
                    actions={
                        <>
                            {count}
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => void refetch()}
                                loading={isFetching && !isLoading}
                                data-action="refresh"
                            >
                                <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                                Refresh
                            </Button>
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => setPurgeOpen(true)}
                                data-action="purge-activity-log"
                            >
                                <ClockCounterClockwiseIcon weight="bold" aria-hidden="true" />
                                Purge activity log
                            </Button>
                        </>
                    }
                />

                <ListFilterToolbar
                    fields={filterFields}
                    onCommit={commitFilterFields}
                    sorting={sorting}
                    sortOptions={SORT_OPTIONS}
                    onSortingChange={setSorting}
                    dialogClearAll
                    pillMultiSelect
                />

                <div className="activity-log-page__phone-count">{count}</div>

                {error && <Alert variant="error">{getErrorMessage(error)}</Alert>}

                <DataTable<MagpieActivityEntry>
                    className="activity-log-page__table"
                    columns={columns}
                    data={entries}
                    enableSorting
                    manualSorting
                    manualPagination
                    sorting={sorting}
                    onSortingChange={(updater) =>
                        updateSorting(typeof updater === 'function' ? updater(sorting) : updater)
                    }
                    rowCount={totalItems}
                    getRowId={(row) => `${row.kind}-${row.id}`}
                    loading={isLoading}
                    emptyState={isLoading ? <Skeleton height={48} /> : undefined}
                    keepHeaderWhenEmpty
                />

                {pageCount > 1 && (
                    <Pager
                        className="activity-log-page__pager"
                        page={page}
                        pageCount={pageCount}
                        totalItems={totalItems}
                        onFirst={() => setPage(1)}
                        onPrevious={() => setPage(Math.max(1, page - 1))}
                        onNext={() => setPage(Math.min(pageCount, page + 1))}
                        onLast={() => setPage(pageCount)}
                    />
                )}
            </PullToRefresh>

            <QuickActionsButton
                actions={[
                    {
                        id: 'purge-activity-log',
                        label: 'Purge activity log',
                        icon: <ClockCounterClockwiseIcon weight="bold" />,
                        onClick: () => setPurgeOpen(true),
                    },
                ]}
            />

            <RelatedEventsModal
                executionId={selectedEntry ? relatedExecutionId(selectedEntry) : null}
                correlationId={selectedEntry?.correlationId ?? null}
                selfEntry={selectedEntry}
                open={selectedEntry !== null}
                onOpenChange={(open) => {
                    if (!open) setSelectedEntry(null);
                }}
            />

            <PurgeActivityLogDialog open={purgeOpen} onOpenChange={setPurgeOpen} />
        </div>
    );
}

ActivityLogPage.displayName = 'ActivityLogPage';

export default ActivityLogPage;
