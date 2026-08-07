import { useState, useMemo, useCallback, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
    CloudArrowDownIcon,
    PauseIcon,
    PlayIcon,
    TrashIcon,
    ClockCounterClockwiseIcon,
    ListBulletsIcon,
    StopCircleIcon,
    LinkBreakIcon,
    XIcon,
} from '@phosphor-icons/react';
import { type SortingState } from '@tanstack/react-table';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { useUrlTabState } from '@/hooks/useUrlTabState';
import { useAuth } from '@/auth';
import { type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { useToast } from '@/components/ui/Toast';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { Button } from '@/components/ui/Button';
import { DataTable } from '@/components/ui/DataTable';
import { Pager } from '@/components/ui/Pager';
import { SelectAllBanner } from '@/components/ui/SelectAllBanner';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { DownloadDetailsDialog } from '@/components/downloads/DownloadDetailsDialog';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { MobileListToolbar, type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { Checkbox } from '@/components/ui/Checkbox';
import { Skeleton } from '@/components/ui/Skeleton';
import {
    useDownloads,
    useDownloadStatusTotal,
    type ApiDownload,
    type DownloadStatusValue,
    type DownloadQueryParams,
    type DownloadBulkFilter,
    type DownloadBulkActionResult,
    downloadStatusVariants,
} from '@/hooks/useDownloads';
import { useBulkSelection, type BulkSelection } from '@/hooks/useBulkSelection';
import { useDownloadClients } from '@/hooks/useDownloadClients';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { formatDateTime } from '@/lib/dateFormatting';
import { type DownloadClientOption, type StateFilterOption, type DownloadFilterState } from '@/types';
import { ACTIVE_DOWNLOAD_STATUSES } from '@/types/downloads';

import {
    StatusBadge,
    HoldAnnotation,
    DownloadProgress,
    TitleCell,
    RowActions,
    buildDownloadColumns,
    displayLineageLeafTitle,
    getEndedAt,
    stoppableStatuses,
    type RowActionHandlers,
} from './buildDownloadColumns';
import {
    canonicalizeDownloadSearchParams,
    DEFAULT_DOWNLOAD_TAB,
    DOWNLOAD_TABS,
    DOWNLOAD_URL_PARAMS,
    HISTORY_DOWNLOAD_SORT_KEYS,
    HISTORY_DOWNLOAD_STATUSES,
    QUEUE_DOWNLOAD_SORT_KEYS,
} from './downloadUrlState';

import '@/styles/bulk-actions-toolbar.css';
import './DownloadsPage.css';

const DOWNLOADS_PAGE_SIZE = 25;

const queueStateOptions: StateFilterOption[] = [
    { value: 'all', label: 'All States' },
    { value: 'Downloading', label: 'Downloading' },
    { value: 'Queued', label: 'Queued' },
    { value: 'Grabbed', label: 'Grabbed' },
    { value: 'Paused', label: 'Paused' },
    { value: 'Stalled', label: 'Stalled' },
];

const historyStateOptions: StateFilterOption[] = [
    { value: 'all', label: 'All States' },
    { value: 'Downloaded', label: 'Downloaded' },
    { value: 'Importing', label: 'Importing' },
    { value: 'Imported', label: 'Imported' },
    { value: 'Failed', label: 'Failed' },
];

const SORT_COLUMN_TO_PROPERTY: Record<string, string> = {
    title: 'ContentHierarchyName',
    client: 'DownloadClientName',
    status: 'Status',
    progress: 'Progress',
    added: 'CreatedAt',
    completed: 'DateCompleted',
    lastStatus: 'ModifiedAt',
};

type QueueUrlFilters = {
    [DOWNLOAD_URL_PARAMS.queueClient]: string;
    [DOWNLOAD_URL_PARAMS.queueState]: string;
} & Record<string, string>;
type HistoryUrlFilters = {
    [DOWNLOAD_URL_PARAMS.historyClient]: string;
    [DOWNLOAD_URL_PARAMS.historyState]: string;
} & Record<string, string>;

interface ConfirmTarget {
    allPages: boolean;
    count: number;
    records: ApiDownload[];
    filter: DownloadBulkFilter;
    excludeIds: string[];
    selection?: BulkSelection;
}

async function runPerDownloadActions(ids: string[], action: (id: string) => Promise<unknown>) {
    const results = await Promise.allSettled(ids.map((id) => action(id)));
    const succeededIds = ids.filter((_, index) => results[index].status === 'fulfilled');
    return { succeededIds, failedCount: ids.length - succeededIds.length };
}

const QUEUE_SORT_OPTIONS: MobileListSortOption[] = [
    { id: 'title', label: 'Title' },
    { id: 'client', label: 'Client' },
    { id: 'status', label: 'Status' },
    { id: 'progress', label: 'Progress', initialDirection: 'desc' },
    { id: 'added', label: 'Added', initialDirection: 'desc' },
    { id: 'lastStatus', label: 'Last status', initialDirection: 'desc' },
];

const HISTORY_SORT_OPTIONS: MobileListSortOption[] = [
    { id: 'title', label: 'Title' },
    { id: 'client', label: 'Client' },
    { id: 'status', label: 'Status' },
    { id: 'completed', label: 'Ended', initialDirection: 'desc' },
    { id: 'lastStatus', label: 'Last status', initialDirection: 'desc' },
];

export function DownloadsPage() {
    useDocumentTitle('Downloads');
    const [searchParams, setSearchParams] = useSearchParams();

    useEffect(() => {
        const canonicalParams = canonicalizeDownloadSearchParams(searchParams);
        if (canonicalParams) {
            setSearchParams(canonicalParams, { replace: true });
        }
    }, [searchParams, setSearchParams]);

    const [activeTab, setActiveTab] = useUrlTabState({ defaultTab: DEFAULT_DOWNLOAD_TAB, allowedTabs: DOWNLOAD_TABS });

    const queueTable = useUrlTableState<QueueUrlFilters>({
        defaultSort: [{ id: 'added', desc: true }],
        defaultFilters: { [DOWNLOAD_URL_PARAMS.queueClient]: '', [DOWNLOAD_URL_PARAMS.queueState]: '' },
        sortKeys: QUEUE_DOWNLOAD_SORT_KEYS,
        pageParam: DOWNLOAD_URL_PARAMS.queuePage,
        sortParam: DOWNLOAD_URL_PARAMS.queueSort,
    });

    const historyTable = useUrlTableState<HistoryUrlFilters>({
        defaultSort: [{ id: 'completed', desc: true }],
        defaultFilters: { [DOWNLOAD_URL_PARAMS.historyClient]: '', [DOWNLOAD_URL_PARAMS.historyState]: '' },
        sortKeys: HISTORY_DOWNLOAD_SORT_KEYS,
        pageParam: DOWNLOAD_URL_PARAMS.historyPage,
        sortParam: DOWNLOAD_URL_PARAMS.historySort,
    });

    const queueFilters: DownloadFilterState = useMemo(
        () => ({
            client: queueTable.filters[DOWNLOAD_URL_PARAMS.queueClient],
            state: queueTable.filters[DOWNLOAD_URL_PARAMS.queueState],
        }),
        [queueTable.filters],
    );
    const historyFilters: DownloadFilterState = useMemo(
        () => ({
            client: historyTable.filters[DOWNLOAD_URL_PARAMS.historyClient],
            state: historyTable.filters[DOWNLOAD_URL_PARAMS.historyState],
        }),
        [historyTable.filters],
    );

    const setQueueFilters = useCallback(
        (filters: DownloadFilterState) =>
            queueTable.setFilters({
                [DOWNLOAD_URL_PARAMS.queueClient]: filters.client,
                [DOWNLOAD_URL_PARAMS.queueState]: filters.state,
            }),
        [queueTable],
    );
    const setHistoryFilters = useCallback(
        (filters: DownloadFilterState) =>
            historyTable.setFilters({
                [DOWNLOAD_URL_PARAMS.historyClient]: filters.client,
                [DOWNLOAD_URL_PARAMS.historyState]: filters.state,
            }),
        [historyTable],
    );

    const resolveSort = (sorting: typeof queueTable.sorting, fallbackColumn: string) => {
        const active = sorting[0];
        const column = active?.id ?? fallbackColumn;
        const desc = active?.desc ?? true;
        return {
            sortBy: SORT_COLUMN_TO_PROPERTY[column] ?? SORT_COLUMN_TO_PROPERTY[fallbackColumn],
            sortDirection: (desc ? 2 : 1) as 1 | 2,
        };
    };

    const queueSort = resolveSort(queueTable.sorting, 'added');
    const historySort = resolveSort(historyTable.sorting, 'completed');

    const queueParams: DownloadQueryParams = useMemo(
        () => ({
            downloadClientId: queueFilters.client || undefined,
            ...(queueFilters.state
                ? { status: queueFilters.state as DownloadStatusValue }
                : { statuses: ACTIVE_DOWNLOAD_STATUSES }),
            pageNumber: queueTable.page,
            pageSize: DOWNLOADS_PAGE_SIZE,
            sortBy: queueSort.sortBy,
            sortDirection: queueSort.sortDirection,
        }),
        [queueFilters.client, queueFilters.state, queueTable.page, queueSort.sortBy, queueSort.sortDirection],
    );

    const historyParams: DownloadQueryParams = useMemo(
        () => ({
            downloadClientId: historyFilters.client || undefined,
            ...(historyFilters.state
                ? { status: historyFilters.state as DownloadStatusValue }
                : { statuses: HISTORY_DOWNLOAD_STATUSES }),
            pageNumber: historyTable.page,
            pageSize: DOWNLOADS_PAGE_SIZE,
            sortBy: historySort.sortBy,
            sortDirection: historySort.sortDirection,
        }),
        [historyFilters.client, historyFilters.state, historyTable.page, historySort.sortBy, historySort.sortDirection],
    );

    const {
        downloads: pagedQueueDownloads,
        pagination: queuePagination,
        isLoading: queueLoading,
        cancelDownload,
        removeDownload,
        deleteDownloadWithFiles,
        pauseDownload,
        resumeDownload,
        forceStartDownload,
        bulkForgetDownloads,
        bulkRemoveDownloads,
        bulkPauseDownloads,
        bulkResumeDownloads,
        bulkStopDownloads,
        refetch: refetchQueue,
    } = useDownloads(queueParams);

    const {
        downloads: pagedHistoryDownloads,
        pagination: historyPagination,
        isLoading: historyLoading,
        refetch: refetchHistory,
    } = useDownloads(historyParams);

    const { clients: apiClients } = useDownloadClients();

    const clients: DownloadClientOption[] = useMemo(
        () => apiClients.map((c) => ({ id: c.id, name: c.name })),
        [apiClients],
    );

    const queueTotal = queuePagination?.totalItems ?? 0;
    const historyTotal = historyPagination?.totalItems ?? 0;

    const queueStatusFiltered = queueFilters.state !== '';
    const historyStatusFiltered = historyFilters.state !== '';

    const { total: queueStatusTotal } = useDownloadStatusTotal(
        ACTIVE_DOWNLOAD_STATUSES,
        queueFilters.client,
        queueStatusFiltered,
    );
    const { total: historyStatusTotal } = useDownloadStatusTotal(
        HISTORY_DOWNLOAD_STATUSES,
        historyFilters.client,
        historyStatusFiltered,
    );

    const totalActive = queueStatusFiltered ? queueStatusTotal : queueTotal;
    const totalHistory = historyStatusFiltered ? historyStatusTotal : historyTotal;
    const queuePageCount = Math.max(1, Math.ceil(queueTotal / DOWNLOADS_PAGE_SIZE));
    const historyPageCount = Math.max(1, Math.ceil(historyTotal / DOWNLOADS_PAGE_SIZE));

    const clampedQueuePage = Math.min(queueTable.page, queuePageCount);
    const clampedHistoryPage = Math.min(historyTable.page, historyPageCount);
    const setQueuePage = queueTable.setPage;
    const setHistoryPage = historyTable.setPage;

    useEffect(() => {
        if (queuePagination && queueTable.page > queuePageCount) {
            setQueuePage(queuePageCount);
        }
    }, [queuePagination, queueTable.page, queuePageCount, setQueuePage]);

    useEffect(() => {
        if (historyPagination && historyTable.page > historyPageCount) {
            setHistoryPage(historyPageCount);
        }
    }, [historyPagination, historyTable.page, historyPageCount, setHistoryPage]);

    const refetchDownloads = useCallback(async () => {
        await Promise.all([refetchQueue(), refetchHistory()]);
    }, [refetchQueue, refetchHistory]);

    const { toast } = useToast();
    const { hasPermission } = useAuth();
    const canControlDownloads = hasPermission('downloads.control');
    const canManageDownloads = hasPermission('downloads.manage');

    const canSelectQueueRows = canControlDownloads;
    const canSelectHistoryRows = canManageDownloads;

    const getClientName = useCallback(
        (download: ApiDownload) =>
            download.downloadClientName ?? clients.find((c) => c.id === download.downloadClientId)?.name ?? '',
        [clients],
    );

    const queuePageRowIds = useMemo(() => pagedQueueDownloads.map((d) => d.id), [pagedQueueDownloads]);
    const historyPageRowIds = useMemo(() => pagedHistoryDownloads.map((d) => d.id), [pagedHistoryDownloads]);

    const queueSelection = useBulkSelection({ pageRowIds: queuePageRowIds, totalCount: queueTotal });
    const historySelection = useBulkSelection({ pageRowIds: historyPageRowIds, totalCount: historyTotal });

    const queueBulkFilter: DownloadBulkFilter = useMemo(
        () => ({
            downloadClientId: queueFilters.client || undefined,
            ...(queueFilters.state
                ? { status: queueFilters.state as DownloadStatusValue }
                : { statuses: ACTIVE_DOWNLOAD_STATUSES }),
        }),
        [queueFilters.client, queueFilters.state],
    );
    const historyBulkFilter: DownloadBulkFilter = useMemo(
        () => ({
            downloadClientId: historyFilters.client || undefined,
            ...(historyFilters.state
                ? { status: historyFilters.state as DownloadStatusValue }
                : { statuses: HISTORY_DOWNLOAD_STATUSES }),
        }),
        [historyFilters.client, historyFilters.state],
    );

    const [forgetTarget, setForgetTarget] = useState<ConfirmTarget | null>(null);
    const [isForgetting, setIsForgetting] = useState(false);

    const [removeTarget, setRemoveTarget] = useState<ConfirmTarget | null>(null);
    const [isRemoving, setIsRemoving] = useState(false);

    const [detailsDownload, setDetailsDownload] = useState<ApiDownload | null>(null);

    const singleTarget = useCallback(
        (download: ApiDownload): ConfirmTarget => ({
            allPages: false,
            count: 1,
            records: [download],
            filter: {},
            excludeIds: [],
        }),
        [],
    );

    const handleForget = useCallback(
        (download: ApiDownload) => setForgetTarget(singleTarget(download)),
        [singleTarget],
    );
    const handleRemove = useCallback(
        (download: ApiDownload) => setRemoveTarget(singleTarget(download)),
        [singleTarget],
    );
    const handleViewDetails = useCallback((download: ApiDownload) => setDetailsDownload(download), []);

    const showPerDownloadFailures = useCallback(
        (succeededCount: number, failedCount: number, action: string) => {
            toast({
                title: succeededCount > 0 ? `Failed to ${action} some downloads` : `Failed to ${action} downloads`,
                description:
                    succeededCount > 0
                        ? `${succeededCount} succeeded and ${failedCount} failed. Failed downloads remain selected.`
                        : `${failedCount} failed. Failed downloads remain selected.`,
                variant: succeededCount > 0 ? 'warning' : 'error',
            });
        },
        [toast],
    );

    const handleConfirmForget = useCallback(async () => {
        if (!forgetTarget || forgetTarget.count === 0) {
            return;
        }
        setIsForgetting(true);
        try {
            if (forgetTarget.allPages) {
                await bulkForgetDownloads({ filter: forgetTarget.filter, excludeIds: forgetTarget.excludeIds });
            } else {
                const { succeededIds, failedCount } = await runPerDownloadActions(
                    forgetTarget.records.map((download) => download.id),
                    removeDownload,
                );
                if (failedCount > 0) {
                    succeededIds.forEach((id) => forgetTarget.selection?.toggleRow(id, false));
                    if (forgetTarget.selection) {
                        showPerDownloadFailures(succeededIds.length, failedCount, 'forget');
                    } else {
                        toast({ title: 'Error', description: 'Failed to forget download', variant: 'error' });
                    }
                    return;
                }
            }
            const count = forgetTarget.count;
            toast({
                title: count === 1 ? 'Download forgotten' : `${count} downloads forgotten`,
                description: 'Magpie stopped tracking it. The transfer is left in the download client.',
                variant: 'success',
            });
            forgetTarget.selection?.clear();
        } catch {
            toast({ title: 'Error', description: 'Failed to forget download', variant: 'error' });
        } finally {
            setIsForgetting(false);
            setForgetTarget(null);
        }
    }, [forgetTarget, bulkForgetDownloads, removeDownload, showPerDownloadFailures, toast]);

    const handleConfirmRemove = useCallback(async () => {
        if (!removeTarget || removeTarget.count === 0) {
            return;
        }
        setIsRemoving(true);
        try {
            if (removeTarget.allPages) {
                await bulkRemoveDownloads({ filter: removeTarget.filter, excludeIds: removeTarget.excludeIds });
            } else {
                const { succeededIds, failedCount } = await runPerDownloadActions(
                    removeTarget.records.map((download) => download.id),
                    deleteDownloadWithFiles,
                );
                if (failedCount > 0) {
                    succeededIds.forEach((id) => removeTarget.selection?.toggleRow(id, false));
                    if (removeTarget.selection) {
                        showPerDownloadFailures(succeededIds.length, failedCount, 'remove');
                    } else {
                        toast({ title: 'Error', description: 'Failed to remove download and files', variant: 'error' });
                    }
                    return;
                }
            }
            const count = removeTarget.count;
            toast({
                title: count === 1 ? 'Download removed' : `${count} downloads removed`,
                description: 'Removed the download and its files from the download client.',
                variant: 'success',
            });
            removeTarget.selection?.clear();
        } catch {
            toast({ title: 'Error', description: 'Failed to remove download and files', variant: 'error' });
        } finally {
            setIsRemoving(false);
            setRemoveTarget(null);
        }
    }, [removeTarget, bulkRemoveDownloads, deleteDownloadWithFiles, showPerDownloadFailures, toast]);

    const handleStop = useCallback(
        async (id: string) => {
            try {
                await cancelDownload(id);
                toast({ title: 'Download stopped', variant: 'success' });
            } catch {
                toast({ title: 'Error', description: 'Failed to stop download', variant: 'error' });
            }
        },
        [cancelDownload, toast],
    );

    const handlePause = useCallback(
        async (id: string) => {
            try {
                await pauseDownload(id);
                toast({ title: 'Download paused', variant: 'success' });
            } catch {
                toast({ title: 'Error', description: 'Failed to pause download', variant: 'error' });
            }
        },
        [pauseDownload, toast],
    );

    const handleResume = useCallback(
        async (id: string) => {
            try {
                await resumeDownload(id);
                toast({ title: 'Download resumed', variant: 'success' });
            } catch {
                toast({ title: 'Error', description: 'Failed to resume download', variant: 'error' });
            }
        },
        [resumeDownload, toast],
    );

    const handleForceStart = useCallback(
        async (download: ApiDownload) => {
            try {
                await forceStartDownload(download.id);
                toast({
                    title: `Kicked ${displayLineageLeafTitle(download)}`,
                    description: 'Forced the transfer to start.',
                    variant: 'success',
                });
            } catch {
                toast({ title: 'Error', description: 'Failed to force start download', variant: 'error' });
            }
        },
        [forceStartDownload, toast],
    );

    const runBulkTransition = useCallback(
        async (
            selection: BulkSelection,
            filter: DownloadBulkFilter,
            pageModeIds: string[],
            bulkAction: (args: {
                filter: DownloadBulkFilter;
                excludeIds: string[];
            }) => Promise<DownloadBulkActionResult>,
            perId: (id: string) => Promise<unknown>,
            successTitle: string,
            completedAction: string,
            errorMessage: string,
            emptyTitle?: string,
        ) => {
            try {
                const resolved = selection.resolve();
                if (resolved.mode === 'all') {
                    const result = await bulkAction({ filter, excludeIds: resolved.excludeIds });
                    if (emptyTitle && result.affected === 0) {
                        toast({
                            title: emptyTitle,
                            description: 'No selected downloads support this action in their current state.',
                            variant: 'warning',
                        });
                        selection.clear();
                        return;
                    }
                } else {
                    const { succeededIds, failedCount } = await runPerDownloadActions(pageModeIds, perId);
                    if (failedCount > 0) {
                        const attemptedIds = new Set(pageModeIds);
                        const unattemptedIds = resolved.ids.filter((id) => !attemptedIds.has(id));
                        [...succeededIds, ...unattemptedIds].forEach((id) => selection.toggleRow(id, false));
                        showPerDownloadFailures(succeededIds.length, failedCount, completedAction);
                        return;
                    }
                }
                toast({ title: successTitle, variant: 'success' });
                selection.clear();
            } catch {
                toast({ title: 'Error', description: errorMessage, variant: 'error' });
            }
        },
        [showPerDownloadFailures, toast],
    );

    const handleBulkPause = useCallback(
        (selection: BulkSelection, filter: DownloadBulkFilter, pageModeIds: string[]) =>
            runBulkTransition(
                selection,
                filter,
                pageModeIds,
                bulkPauseDownloads,
                pauseDownload,
                'Downloads paused',
                'pause',
                'Failed to pause downloads',
                'No downloads paused',
            ),
        [runBulkTransition, bulkPauseDownloads, pauseDownload],
    );

    const handleBulkResume = useCallback(
        (selection: BulkSelection, filter: DownloadBulkFilter, pageModeIds: string[]) =>
            runBulkTransition(
                selection,
                filter,
                pageModeIds,
                bulkResumeDownloads,
                resumeDownload,
                'Downloads resumed',
                'resume',
                'Failed to resume downloads',
                'No downloads started',
            ),
        [runBulkTransition, bulkResumeDownloads, resumeDownload],
    );

    const handleBulkStop = useCallback(
        (selection: BulkSelection, filter: DownloadBulkFilter, pageModeIds: string[]) =>
            runBulkTransition(
                selection,
                filter,
                pageModeIds,
                bulkStopDownloads,
                cancelDownload,
                'Downloads stopped',
                'stop',
                'Failed to stop downloads',
            ),
        [runBulkTransition, bulkStopDownloads, cancelDownload],
    );

    const rowHandlers: RowActionHandlers = useMemo(
        () => ({
            onPause: handlePause,
            onResume: handleResume,
            onForceStart: handleForceStart,
            onStop: handleStop,
            onForget: handleForget,
            onRemove: handleRemove,
            onViewDetails: handleViewDetails,
        }),
        [handlePause, handleResume, handleForceStart, handleStop, handleForget, handleRemove, handleViewDetails],
    );

    const clientFacetOptions: FacetOption[] = useMemo(
        () => clients.map((client) => ({ value: client.id, label: client.name })),
        [clients],
    );

    const toFacetOptions = (options: StateFilterOption[]): FacetOption[] =>
        options.filter((o) => o.value !== 'all').map((o) => ({ value: String(o.value), label: o.label }));

    const queueStateFacets = useMemo(() => toFacetOptions(queueStateOptions), []);
    const historyStateFacets = useMemo(() => toFacetOptions(historyStateOptions), []);

    const queueColumns = useMemo(
        () =>
            buildDownloadColumns({
                tab: 'queue',
                filters: queueFilters,
                onChange: setQueueFilters,
                clientOptions: clientFacetOptions,
                stateOptions: queueStateFacets,
                selection: queueSelection,
                canControlDownloads,
                canManageDownloads,
                getClientName,
                rowHandlers,
            }),
        [
            queueFilters,
            setQueueFilters,
            clientFacetOptions,
            queueStateFacets,
            queueSelection,
            canControlDownloads,
            canManageDownloads,
            getClientName,
            rowHandlers,
        ],
    );
    const historyColumns = useMemo(
        () =>
            buildDownloadColumns({
                tab: 'history',
                filters: historyFilters,
                onChange: setHistoryFilters,
                clientOptions: clientFacetOptions,
                stateOptions: historyStateFacets,
                selection: historySelection,
                canControlDownloads,
                canManageDownloads,
                getClientName,
                rowHandlers,
            }),
        [
            historyFilters,
            setHistoryFilters,
            clientFacetOptions,
            historyStateFacets,
            historySelection,
            canControlDownloads,
            canManageDownloads,
            getClientName,
            rowHandlers,
        ],
    );

    const getRowId = useCallback((row: ApiDownload) => row.id, []);

    const hasActiveQueueFilters = queueFilters.client !== '' || queueFilters.state !== '';
    const hasActiveHistoryFilters = historyFilters.client !== '' || historyFilters.state !== '';

    const queueSelectedRecords = useMemo(
        () => pagedQueueDownloads.filter((d) => queueSelection.rowSelection[d.id]),
        [pagedQueueDownloads, queueSelection.rowSelection],
    );
    const historySelectedRecords = useMemo(
        () => pagedHistoryDownloads.filter((d) => historySelection.rowSelection[d.id]),
        [pagedHistoryDownloads, historySelection.rowSelection],
    );

    const queueClear = queueSelection.clear;
    const historyClear = historySelection.clear;
    useEffect(() => queueClear(), [queueFilters.client, queueFilters.state, queueTable.page, queueClear]);
    useEffect(() => historyClear(), [historyFilters.client, historyFilters.state, historyTable.page, historyClear]);

    const handleRefresh = useCallback(async () => {
        await refetchDownloads();
    }, [refetchDownloads]);

    const usesCardLayout = useCardLayout();
    const [queueSelectionMode, setQueueSelectionMode] = useState(false);
    const [historySelectionMode, setHistorySelectionMode] = useState(false);

    const renderMobileCard = (download: ApiDownload, tab: 'queue' | 'history') => {
        const selection = tab === 'queue' ? queueSelection : historySelection;
        const canSelect = tab === 'queue' ? canSelectQueueRows : canSelectHistoryRows;
        const selectionMode = tab === 'queue' ? queueSelectionMode : historySelectionMode;

        return (
            <MobileMediaCard
                key={download.id}
                role="listitem"
                data-download-id={download.id}
                data-status={downloadStatusVariants[download.status]}
                posterUrl={download.posterUrl ?? undefined}
                posterAlt={`${displayLineageLeafTitle(download)} artwork`}
                title={<TitleCell download={download} />}
                titleAccessory={
                    selectionMode && canSelect ? (
                        <Checkbox
                            checked={selection.isRowChecked(download.id)}
                            onCheckedChange={(checked) => selection.toggleRow(download.id, Boolean(checked))}
                            aria-label={`Select ${displayLineageLeafTitle(download)}`}
                            data-action="select-row"
                        />
                    ) : (
                        <div className="downloads-page__mobile-actions">
                            <RowActions
                                download={download}
                                handlers={rowHandlers}
                                canControl={canControlDownloads}
                                canManage={canManageDownloads}
                            />
                        </div>
                    )
                }
                trailing={tab === 'queue' ? <DownloadProgress download={download} /> : undefined}
            >
                <div className="downloads-page__mobile-status">
                    <StatusBadge status={download.status} />
                    <HoldAnnotation download={download} />
                </div>
                {getClientName(download) && <span className="downloads-page__client">{getClientName(download)}</span>}
                <span>
                    {tab === 'queue' ? 'Added' : 'Ended'}:{' '}
                    {tab === 'queue' ? formatDateTime(download.createdAt) : formatDateTime(getEndedAt(download))}
                </span>
            </MobileMediaCard>
        );
    };

    const updateHistorySorting = (next: SortingState) => {
        const [current] = historyTable.sorting;
        const resolved = next.length === 0 && current ? [{ ...current, desc: !current.desc }] : next;
        historyTable.setSorting(resolved);
    };

    const toggleMobileSelectionMode = (tab: 'queue' | 'history') => {
        if (tab === 'queue') {
            if (queueSelectionMode) {
                queueSelection.clear();
            }
            setQueueSelectionMode((active) => !active);
            return;
        }
        if (historySelectionMode) {
            historySelection.clear();
        }
        setHistorySelectionMode((active) => !active);
    };

    const renderBulkBar = (
        tab: 'queue' | 'history',
        selection: BulkSelection,
        bulkFilter: DownloadBulkFilter,
        selectedRecords: ApiDownload[],
        totalCount: number,
        mobile = false,
    ) => {
        const { allPages, hasSelection, selectedCount } = selection;
        const pausable = selectedRecords.filter((d) => Boolean(d.canPause)).map((d) => d.id);
        const resumable = selectedRecords.filter((d) => Boolean(d.canResume)).map((d) => d.id);
        const stoppable = selectedRecords.filter((d) => stoppableStatuses.has(d.status)).map((d) => d.id);

        const canResume = allPages || resumable.length > 0;
        const canPause = allPages || pausable.length > 0;
        const canStop = allPages || stoppable.length > 0;

        const toConfirmTarget = (): ConfirmTarget => {
            const resolved = selection.resolve();
            return resolved.mode === 'all'
                ? {
                      allPages: true,
                      count: selectedCount,
                      records: [],
                      filter: bulkFilter,
                      excludeIds: resolved.excludeIds,
                      selection,
                  }
                : {
                      allPages: false,
                      count: selectedRecords.length,
                      records: selectedRecords,
                      filter: bulkFilter,
                      excludeIds: [],
                      selection,
                  };
        };

        const actions = (
            <>
                {tab === 'queue' && canControlDownloads && (
                    <>
                        <Button
                            variant="default"
                            size="sm"
                            disabled={!canResume}
                            onClick={() => handleBulkResume(selection, bulkFilter, resumable)}
                            aria-label="Start"
                            data-action="bulk-resume"
                        >
                            <PlayIcon weight="bold" aria-hidden="true" />
                            <span className="bulk-actions-toolbar__btn-label">Start</span>
                        </Button>
                        <Button
                            variant="default"
                            size="sm"
                            disabled={!canPause}
                            onClick={() => handleBulkPause(selection, bulkFilter, pausable)}
                            aria-label="Pause"
                            data-action="bulk-pause"
                        >
                            <PauseIcon weight="bold" aria-hidden="true" />
                            <span className="bulk-actions-toolbar__btn-label">Pause</span>
                        </Button>
                    </>
                )}
                {tab === 'queue' && canManageDownloads && (
                    <Button
                        variant="default"
                        size="sm"
                        disabled={!canStop}
                        onClick={() => handleBulkStop(selection, bulkFilter, stoppable)}
                        aria-label="Stop"
                        data-action="bulk-stop"
                    >
                        <StopCircleIcon weight="bold" aria-hidden="true" />
                        <span className="bulk-actions-toolbar__btn-label">Stop</span>
                    </Button>
                )}
                {canManageDownloads && (
                    <>
                        <Button
                            variant="warning"
                            size="sm"
                            disabled={!hasSelection}
                            onClick={() => setForgetTarget(toConfirmTarget())}
                            aria-label="Forget"
                            data-action="bulk-forget"
                        >
                            <LinkBreakIcon weight="bold" aria-hidden="true" />
                            <span className="bulk-actions-toolbar__btn-label">Forget</span>
                        </Button>
                        <Button
                            variant="destructive"
                            size="sm"
                            disabled={!hasSelection}
                            onClick={() => setRemoveTarget(toConfirmTarget())}
                            aria-label="Remove"
                            data-action="bulk-remove"
                        >
                            <TrashIcon weight="bold" aria-hidden="true" />
                            <span className="bulk-actions-toolbar__btn-label">Remove</span>
                        </Button>
                    </>
                )}
            </>
        );

        if (mobile) {
            return (
                <MobileSelectionBar
                    className="downloads-page__mobile-selection-bar"
                    data-component={`downloads-mobile-selection-bar-${tab}`}
                    selection={selection}
                    pageSelectedCount={selectedRecords.length}
                    totalCount={totalCount}
                    itemNounPlural="downloads"
                >
                    {actions}
                </MobileSelectionBar>
            );
        }

        return (
            <div className="bulk-actions-toolbar" data-component={`downloads-bulk-bar-${tab}`}>
                {(selection.showAcrossPagesPrompt || allPages) && (
                    <SelectAllBanner
                        allPages={allPages}
                        pageSelectedCount={selectedRecords.length}
                        totalCount={totalCount}
                        selectedCount={selectedCount}
                        onSelectAllAcrossPages={selection.selectAllAcrossPages}
                        onClear={selection.clear}
                        itemNounPlural="downloads"
                    />
                )}
                <div className="bulk-actions-toolbar__info">
                    <span className="bulk-actions-toolbar__count">{selectedCount} selected</span>
                    <button
                        type="button"
                        className="bulk-actions-toolbar__clear"
                        onClick={selection.clear}
                        disabled={!hasSelection}
                        aria-label="Clear selection"
                        data-action="clear-selection"
                    >
                        <XIcon weight="bold" aria-hidden="true" />
                        Clear
                    </button>
                </div>
                <div className="bulk-actions-toolbar__buttons">{actions}</div>
            </div>
        );
    };

    const queueEmptyState = (
        <EmptyState
            icon={<CloudArrowDownIcon weight="duotone" />}
            title="No active downloads"
            description={
                hasActiveQueueFilters
                    ? 'Try adjusting your filters'
                    : 'Downloads will appear here when items are grabbed'
            }
        />
    );

    const historyEmptyState = (
        <EmptyState
            icon={<ClockCounterClockwiseIcon weight="duotone" />}
            title="No download history"
            description={
                hasActiveHistoryFilters
                    ? 'Try adjusting your filters'
                    : 'Completed and failed downloads will appear here'
            }
        />
    );

    const queueClearFilters = hasActiveQueueFilters ? (
        <ClearFiltersButton
            onClick={() => setQueueFilters({ client: '', state: '' })}
            data-action="clear-filters-queue"
        />
    ) : undefined;

    const historyClearFilters = hasActiveHistoryFilters ? (
        <ClearFiltersButton
            onClick={() => setHistoryFilters({ client: '', state: '' })}
            data-action="clear-filters-history"
        />
    ) : undefined;

    const loadingState = (
        <div className="downloads-page__loading" data-testid="downloads-page__loading">
            <Skeleton className="downloads-page__skeleton" />
            <Skeleton className="downloads-page__skeleton" />
            <Skeleton className="downloads-page__skeleton" />
        </div>
    );

    const queueInitialLoading = queueLoading && pagedQueueDownloads.length === 0;
    const historyInitialLoading = historyLoading && pagedHistoryDownloads.length === 0;

    const queuePlaceholder = queueInitialLoading ? loadingState : queueEmptyState;
    const historyPlaceholder = historyInitialLoading ? loadingState : historyEmptyState;

    const forgetCount = forgetTarget?.count ?? 0;
    const removeCount = removeTarget?.count ?? 0;
    const forgetRecord = forgetCount === 1 ? forgetTarget?.records[0] : undefined;
    const removeRecord = removeCount === 1 ? removeTarget?.records[0] : undefined;
    const downloadLabel = forgetCount === 1 ? 'this download' : `these ${forgetCount} downloads`;
    const downloadPronoun = forgetCount === 1 ? 'It' : 'They';
    const selectedDownloads = removeCount === 1 ? 'this download' : `these ${removeCount} downloads`;

    return (
        <div className="downloads-page" data-page="downloads" data-layout={usesCardLayout ? 'card' : 'table'}>
            <PullToRefresh onRefresh={handleRefresh} className="downloads-page__pull-to-refresh">
                <PageHeader
                    className="downloads-page__header"
                    title="Downloads"
                    description="Active queue plus the history of completed, imported, and failed downloads."
                    icon={<CloudArrowDownIcon weight="duotone" data-testid="downloads-page__icon" aria-hidden="true" />}
                />

                <Tabs value={activeTab} onValueChange={setActiveTab} className="downloads-page__tabs">
                    <TabsList>
                        <TabsTrigger value="queue">
                            <ListBulletsIcon weight="bold" aria-hidden="true" />
                            <span>Active</span>
                            {totalActive > 0 && (
                                <span
                                    className="tab-count downloads-page__tab-count"
                                    data-testid="downloads-page__tab-count"
                                >
                                    {totalActive}
                                </span>
                            )}
                        </TabsTrigger>
                        <TabsTrigger value="history">
                            <ClockCounterClockwiseIcon weight="bold" aria-hidden="true" />
                            <span>History</span>
                            {totalHistory > 0 && (
                                <span
                                    className="tab-count downloads-page__tab-count"
                                    data-testid="downloads-page__tab-count"
                                >
                                    {totalHistory}
                                </span>
                            )}
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="queue">
                        {!usesCardLayout &&
                            canSelectQueueRows &&
                            pagedQueueDownloads.length > 0 &&
                            renderBulkBar('queue', queueSelection, queueBulkFilter, queueSelectedRecords, queueTotal)}

                        {!usesCardLayout && (
                            <div className="downloads-page__table">
                                <DataTable<ApiDownload>
                                    columns={queueColumns}
                                    data={pagedQueueDownloads}
                                    enableSorting
                                    manualSorting
                                    manualPagination
                                    enableRowSelection
                                    rowSelection={queueSelection.rowSelection}
                                    sorting={queueTable.sorting}
                                    onSortingChange={(updater) =>
                                        queueTable.setSorting(
                                            typeof updater === 'function' ? updater(queueTable.sorting) : updater,
                                        )
                                    }
                                    rowCount={queueTotal}
                                    getRowId={getRowId}
                                    emptyState={queuePlaceholder}
                                    filterActions={queueClearFilters}
                                    keepHeaderWhenEmpty={hasActiveQueueFilters}
                                />
                            </div>
                        )}

                        {usesCardLayout && (pagedQueueDownloads.length > 0 || hasActiveQueueFilters) && (
                            <MobileListToolbar
                                className="downloads-page__mobile-toolbar"
                                sorting={queueTable.sorting}
                                sortOptions={QUEUE_SORT_OPTIONS}
                                onSortingChange={queueTable.setSorting}
                                filterActions={queueClearFilters}
                                selection={
                                    canSelectQueueRows
                                        ? {
                                              active: queueSelectionMode,
                                              onToggle: () => toggleMobileSelectionMode('queue'),
                                          }
                                        : undefined
                                }
                            />
                        )}

                        {usesCardLayout && (
                            <div className="downloads-page__mobile-list" role="list" aria-label="Download queue">
                                {pagedQueueDownloads.length === 0
                                    ? queuePlaceholder
                                    : pagedQueueDownloads.map((download) => renderMobileCard(download, 'queue'))}
                            </div>
                        )}

                        {usesCardLayout &&
                            canSelectQueueRows &&
                            queueSelectionMode &&
                            renderBulkBar(
                                'queue',
                                queueSelection,
                                queueBulkFilter,
                                queueSelectedRecords,
                                queueTotal,
                                true,
                            )}

                        {queuePageCount > 1 && (
                            <Pager
                                className="downloads-page__pager"
                                page={clampedQueuePage}
                                pageCount={queuePageCount}
                                onFirst={() => queueTable.setPage(1)}
                                onPrevious={() => queueTable.setPage(Math.max(1, clampedQueuePage - 1))}
                                onNext={() => queueTable.setPage(Math.min(queuePageCount, clampedQueuePage + 1))}
                                onLast={() => queueTable.setPage(queuePageCount)}
                            />
                        )}
                    </TabsContent>

                    <TabsContent value="history">
                        {!usesCardLayout &&
                            canSelectHistoryRows &&
                            pagedHistoryDownloads.length > 0 &&
                            renderBulkBar(
                                'history',
                                historySelection,
                                historyBulkFilter,
                                historySelectedRecords,
                                historyTotal,
                            )}

                        {!usesCardLayout && (
                            <div className="downloads-page__table">
                                <DataTable<ApiDownload>
                                    columns={historyColumns}
                                    data={pagedHistoryDownloads}
                                    enableSorting
                                    manualSorting
                                    manualPagination
                                    enableRowSelection
                                    rowSelection={historySelection.rowSelection}
                                    sorting={historyTable.sorting}
                                    onSortingChange={(updater) =>
                                        updateHistorySorting(
                                            typeof updater === 'function' ? updater(historyTable.sorting) : updater,
                                        )
                                    }
                                    rowCount={historyTotal}
                                    getRowId={getRowId}
                                    emptyState={historyPlaceholder}
                                    filterActions={historyClearFilters}
                                    keepHeaderWhenEmpty={hasActiveHistoryFilters}
                                />
                            </div>
                        )}

                        {usesCardLayout && (pagedHistoryDownloads.length > 0 || hasActiveHistoryFilters) && (
                            <MobileListToolbar
                                className="downloads-page__mobile-toolbar"
                                sorting={historyTable.sorting}
                                sortOptions={HISTORY_SORT_OPTIONS}
                                onSortingChange={updateHistorySorting}
                                filterActions={historyClearFilters}
                                selection={
                                    canSelectHistoryRows
                                        ? {
                                              active: historySelectionMode,
                                              onToggle: () => toggleMobileSelectionMode('history'),
                                          }
                                        : undefined
                                }
                            />
                        )}

                        {usesCardLayout && (
                            <div className="downloads-page__mobile-list" role="list" aria-label="Download history">
                                {pagedHistoryDownloads.length === 0
                                    ? historyPlaceholder
                                    : pagedHistoryDownloads.map((download) => renderMobileCard(download, 'history'))}
                            </div>
                        )}

                        {usesCardLayout &&
                            canSelectHistoryRows &&
                            historySelectionMode &&
                            renderBulkBar(
                                'history',
                                historySelection,
                                historyBulkFilter,
                                historySelectedRecords,
                                historyTotal,
                                true,
                            )}

                        {historyPageCount > 1 && (
                            <Pager
                                className="downloads-page__pager"
                                page={clampedHistoryPage}
                                pageCount={historyPageCount}
                                onFirst={() => historyTable.setPage(1)}
                                onPrevious={() => historyTable.setPage(Math.max(1, clampedHistoryPage - 1))}
                                onNext={() => historyTable.setPage(Math.min(historyPageCount, clampedHistoryPage + 1))}
                                onLast={() => historyTable.setPage(historyPageCount)}
                            />
                        )}
                    </TabsContent>
                </Tabs>
            </PullToRefresh>

            <DeleteConfirmDialog
                open={!!forgetTarget}
                onConfirm={handleConfirmForget}
                onCancel={() => setForgetTarget(null)}
                itemName={
                    forgetCount === 1 && forgetTarget?.records[0]
                        ? displayLineageLeafTitle(forgetTarget.records[0])
                        : ''
                }
                itemType="Download"
                title={forgetCount === 1 ? 'Forget download' : `Forget ${forgetCount} downloads`}
                confirmVariant="warning"
                confirmLabel="Forget"
                confirmBusyLabel="Forgetting..."
                confirmIcon={<LinkBreakIcon weight="bold" aria-hidden="true" />}
                isDeleting={isForgetting}
                description={
                    <>
                        Stop tracking{' '}
                        {forgetRecord?.contentHierarchyItemId ? (
                            <ContentLineageTitle
                                qualifiedName={{
                                    id: forgetRecord.contentHierarchyItemId,
                                    parentId: forgetRecord.contentHierarchyParentId,
                                    title: displayLineageLeafTitle(forgetRecord),
                                }}
                            />
                        ) : forgetRecord ? (
                            displayLineageLeafTitle(forgetRecord)
                        ) : (
                            downloadLabel
                        )}
                        ? {downloadPronoun} will remain in the download client.
                    </>
                }
            />

            <DeleteConfirmDialog
                open={!!removeTarget}
                onConfirm={handleConfirmRemove}
                onCancel={() => setRemoveTarget(null)}
                itemName={
                    removeCount === 1 && removeTarget?.records[0]
                        ? displayLineageLeafTitle(removeTarget.records[0])
                        : ''
                }
                itemType="Download"
                title={removeCount === 1 ? 'Remove download' : `Remove ${removeCount} downloads`}
                confirmLabel="Remove"
                confirmBusyLabel="Removing..."
                isDeleting={isRemoving}
                description={
                    <>
                        Remove{' '}
                        {removeRecord?.contentHierarchyItemId ? (
                            <ContentLineageTitle
                                qualifiedName={{
                                    id: removeRecord.contentHierarchyItemId,
                                    parentId: removeRecord.contentHierarchyParentId,
                                    title: displayLineageLeafTitle(removeRecord),
                                }}
                            />
                        ) : removeRecord ? (
                            displayLineageLeafTitle(removeRecord)
                        ) : (
                            selectedDownloads
                        )}{' '}
                        and all associated files from the download client? The files will be deleted from disk. This
                        cannot be undone.
                    </>
                }
            />

            <DownloadDetailsDialog
                download={detailsDownload}
                clientName={detailsDownload ? getClientName(detailsDownload) : ''}
                open={!!detailsDownload}
                onOpenChange={(open) => {
                    if (!open) {
                        setDetailsDownload(null);
                    }
                }}
            />
        </div>
    );
}

DownloadsPage.displayName = 'DownloadsPage';

export default DownloadsPage;
