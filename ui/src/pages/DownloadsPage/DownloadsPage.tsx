import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
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
} from '@phosphor-icons/react';
import { type CellContext, type SortingState } from '@tanstack/react-table';

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
import { BulkActionsToolbar } from '@/components/ui/BulkActionsToolbar';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { DownloadDetailsDialog } from '@/components/downloads/DownloadDetailsDialog';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { ListFilterToolbar, type ListFilterField } from '@/components/ui/ListFilterToolbar';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { Skeleton } from '@/components/ui/Skeleton';
import {
    useDownload,
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
import { displayLineageLeafTitle } from '@/lib/downloadTitle';
import { type DownloadClientOption, type DownloadFilterState } from '@/types';
import { ACTIVE_DOWNLOAD_STATUSES } from '@/types/downloads';
import type { SortDirectionName } from '@/types/paging';

import {
    StatusBadge,
    HoldAnnotation,
    DownloadProgress,
    TitleCell,
    RowActions,
    buildDownloadColumns,
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

const QUEUE_STATE_FACETS: FacetOption[] = [
    { value: 'Downloading', label: 'Downloading' },
    { value: 'Queued', label: 'Queued' },
    { value: 'Grabbed', label: 'Grabbed' },
    { value: 'Paused', label: 'Paused' },
    { value: 'Stalled', label: 'Stalled' },
];

const HISTORY_STATE_FACETS: FacetOption[] = [
    { value: 'Downloaded', label: 'Downloaded' },
    { value: 'Importing', label: 'Importing' },
    { value: 'Imported', label: 'Imported' },
    { value: 'Failed', label: 'Failed' },
    { value: 'Faulted', label: 'Faulted' },
    { value: 'Cancelled', label: 'Cancelled' },
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
    ids: string[];
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
        const sortDirection: SortDirectionName = desc ? 'Descending' : 'Ascending';
        return {
            sortBy: SORT_COLUMN_TO_PROPERTY[column] ?? SORT_COLUMN_TO_PROPERTY[fallbackColumn],
            sortDirection,
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
    } = useDownloads(queueParams, activeTab === 'queue');

    const {
        downloads: pagedHistoryDownloads,
        pagination: historyPagination,
        isLoading: historyLoading,
        refetch: refetchHistory,
    } = useDownloads(historyParams, activeTab === 'history');

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

    const showBulkResultError = useCallback(
        (error: string | null) => {
            if (!error) {
                return false;
            }

            toast({ title: 'Error', description: error, variant: 'error' });
            return true;
        },
        [toast],
    );

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
    const detailsQuery = useDownload(detailsDownload?.id);

    const singleTarget = useCallback(
        (download: ApiDownload): ConfirmTarget => ({
            allPages: false,
            count: 1,
            ids: [download.id],
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

    const confirmTargetAction = async ({
        target,
        bulk,
        perId,
        setBusy,
        setTarget,
        texts,
    }: {
        target: ConfirmTarget | null;
        bulk: typeof bulkForgetDownloads;
        perId: (id: string) => Promise<unknown>;
        setBusy: (busy: boolean) => void;
        setTarget: (target: ConfirmTarget | null) => void;
        texts: {
            action: string;
            noneTitle: string;
            successTitle: (count: number) => string;
            successDescription: (count: number) => string;
            failureDescription: string;
        };
    }) => {
        if (!target || target.count === 0) {
            return;
        }
        setBusy(true);
        try {
            let count = target.count;
            if (target.allPages) {
                const result = await bulk({
                    filter: target.filter,
                    excludeIds: target.excludeIds,
                });
                if (showBulkResultError(result.error)) {
                    return;
                }
                if (result.count === 0) {
                    toast({
                        title: texts.noneTitle,
                        description: 'The selected downloads no longer exist.',
                        variant: 'warning',
                    });
                    target.selection?.clear();
                    return;
                }
                count = result.count;
            } else {
                const { succeededIds, failedCount } = await runPerDownloadActions(target.ids, perId);
                if (failedCount > 0) {
                    succeededIds.forEach((id) => target.selection?.toggleRow(id, false));
                    if (target.selection) {
                        showPerDownloadFailures(succeededIds.length, failedCount, texts.action);
                    } else {
                        toast({ title: 'Error', description: texts.failureDescription, variant: 'error' });
                    }
                    return;
                }
            }
            toast({
                title: texts.successTitle(count),
                description: texts.successDescription(count),
                variant: 'success',
            });
            target.selection?.clear();
        } catch {
            toast({ title: 'Error', description: texts.failureDescription, variant: 'error' });
        } finally {
            setBusy(false);
            setTarget(null);
        }
    };

    const handleConfirmForget = () =>
        confirmTargetAction({
            target: forgetTarget,
            bulk: bulkForgetDownloads,
            perId: removeDownload,
            setBusy: setIsForgetting,
            setTarget: setForgetTarget,
            texts: {
                action: 'forget',
                noneTitle: 'No downloads forgotten',
                successTitle: (count) => (count === 1 ? 'Download forgotten' : `${count} downloads forgotten`),
                successDescription: (count) =>
                    `Magpie stopped tracking ${count === 1 ? 'it' : 'them'}. ${count === 1 ? 'The transfer is' : 'The transfers are'} left in the download client.`,
                failureDescription: 'Failed to forget download',
            },
        });

    const handleConfirmRemove = () =>
        confirmTargetAction({
            target: removeTarget,
            bulk: bulkRemoveDownloads,
            perId: deleteDownloadWithFiles,
            setBusy: setIsRemoving,
            setTarget: setRemoveTarget,
            texts: {
                action: 'remove',
                noneTitle: 'No downloads removed',
                successTitle: (count) => (count === 1 ? 'Download removed' : `${count} downloads removed`),
                successDescription: () => 'Removed the download and its files from the download client.',
                failureDescription: 'Failed to remove download and files',
            },
        });

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
                    if (showBulkResultError(result.error)) {
                        return;
                    }
                    if (emptyTitle && result.count === 0) {
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
        [showBulkResultError, showPerDownloadFailures, toast],
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

    const rowActionsRef = useRef({ rowHandlers, canControlDownloads, canManageDownloads });
    rowActionsRef.current = { rowHandlers, canControlDownloads, canManageDownloads };
    const actionsCell = useCallback(({ row }: CellContext<ApiDownload, unknown>) => {
        const {
            rowHandlers: handlers,
            canControlDownloads: canControl,
            canManageDownloads: canManage,
        } = rowActionsRef.current;
        return <RowActions download={row.original} handlers={handlers} canControl={canControl} canManage={canManage} />;
    }, []);

    const clientFacetOptions: FacetOption[] = useMemo(
        () => clients.map((client) => ({ value: client.id, label: client.name })),
        [clients],
    );

    const queueColumns = useMemo(
        () =>
            buildDownloadColumns({
                tab: 'queue',
                filters: queueFilters,
                onChange: setQueueFilters,
                clientOptions: clientFacetOptions,
                stateOptions: QUEUE_STATE_FACETS,
                selection: queueSelection,
                canControlDownloads,
                canManageDownloads,
                getClientName,
                actionsCell,
            }),
        [
            queueFilters,
            setQueueFilters,
            clientFacetOptions,
            queueSelection,
            canControlDownloads,
            canManageDownloads,
            getClientName,
            actionsCell,
        ],
    );
    const historyColumns = useMemo(
        () =>
            buildDownloadColumns({
                tab: 'history',
                filters: historyFilters,
                onChange: setHistoryFilters,
                clientOptions: clientFacetOptions,
                stateOptions: HISTORY_STATE_FACETS,
                selection: historySelection,
                canControlDownloads,
                canManageDownloads,
                getClientName,
                actionsCell,
            }),
        [
            historyFilters,
            setHistoryFilters,
            clientFacetOptions,
            historySelection,
            canControlDownloads,
            canManageDownloads,
            getClientName,
            actionsCell,
        ],
    );

    const getRowId = useCallback((row: ApiDownload) => row.id, []);

    const hasActiveQueueFilters = queueFilters.client !== '' || queueFilters.state !== '';
    const hasActiveHistoryFilters = historyFilters.client !== '' || historyFilters.state !== '';
    const queueFilterFields: ListFilterField[] = [
        { id: 'client', label: 'Client', kind: 'facet', value: queueFilters.client, options: clientFacetOptions },
        { id: 'state', label: 'Status', kind: 'facet', value: queueFilters.state, options: QUEUE_STATE_FACETS },
    ];
    const historyFilterFields: ListFilterField[] = [
        { id: 'client', label: 'Client', kind: 'facet', value: historyFilters.client, options: clientFacetOptions },
        { id: 'state', label: 'Status', kind: 'facet', value: historyFilters.state, options: HISTORY_STATE_FACETS },
    ];

    const knownDownloads = useRef<Record<string, ApiDownload>>({});
    useEffect(() => {
        for (const download of [...pagedQueueDownloads, ...pagedHistoryDownloads]) {
            knownDownloads.current[download.id] = download;
        }
    }, [pagedQueueDownloads, pagedHistoryDownloads]);

    const queueSelectedRecords = useMemo(
        () =>
            queueSelection.selectedIds
                .map((id) => pagedQueueDownloads.find((download) => download.id === id) ?? knownDownloads.current[id])
                .filter((download) => download !== undefined),
        [queueSelection.selectedIds, pagedQueueDownloads],
    );
    const historySelectedRecords = useMemo(
        () =>
            historySelection.selectedIds
                .map((id) => pagedHistoryDownloads.find((download) => download.id === id) ?? knownDownloads.current[id])
                .filter((download) => download !== undefined),
        [historySelection.selectedIds, pagedHistoryDownloads],
    );

    const queueClear = queueSelection.clear;
    const historyClear = historySelection.clear;
    useEffect(() => queueClear(), [queueFilters.client, queueFilters.state, queueClear]);
    useEffect(() => historyClear(), [historyFilters.client, historyFilters.state, historyClear]);

    const handleRefresh = useCallback(async () => {
        await refetchDownloads();
    }, [refetchDownloads]);

    const usesCardLayout = useCardLayout();

    const renderMobileCard = (download: ApiDownload, tab: 'queue' | 'history') => {
        const selection = tab === 'queue' ? queueSelection : historySelection;
        const canSelect = tab === 'queue' ? canSelectQueueRows : canSelectHistoryRows;
        const isSelecting = selection.selectionMode && canSelect;

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
                    isSelecting ? undefined : (
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
                selectable={isSelecting}
                selected={selection.isRowChecked(download.id)}
                onToggleSelected={() => selection.toggleRow(download.id, !selection.isRowChecked(download.id))}
                selectionLabel={`Select ${displayLineageLeafTitle(download)}`}
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
                      ids: [],
                      records: [],
                      filter: bulkFilter,
                      excludeIds: resolved.excludeIds,
                      selection,
                  }
                : {
                      allPages: false,
                      count: selectedCount,
                      ids: resolved.ids,
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
                    data-component={`downloads-mobile-selection-bar-${tab}`}
                    selection={selection}
                    pageSelectedCount={selection.pageSelectedCount}
                    totalCount={totalCount}
                    itemNounPlural="downloads"
                >
                    {actions}
                </MobileSelectionBar>
            );
        }

        return (
            <BulkActionsToolbar
                selection={selection}
                label={`${tab === 'queue' ? 'Queue' : 'History'} selection actions`}
                itemNounPlural="downloads"
                data-component={`downloads-bulk-bar-${tab}`}
            >
                {actions}
            </BulkActionsToolbar>
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

                        <ListFilterToolbar
                            fields={queueFilterFields}
                            onCommit={(values) =>
                                setQueueFilters({ client: values.client as string, state: values.state as string })
                            }
                            sorting={queueTable.sorting}
                            sortOptions={QUEUE_SORT_OPTIONS}
                            onSortingChange={queueTable.setSorting}
                            selection={
                                usesCardLayout && canSelectQueueRows
                                    ? {
                                          active: queueSelection.selectionMode,
                                          onToggle: queueSelection.toggleSelectionMode,
                                      }
                                    : undefined
                            }
                        />

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
                                    keepHeaderWhenEmpty={hasActiveQueueFilters}
                                />
                            </div>
                        )}

                        {usesCardLayout && (
                            <div
                                className="downloads-page__mobile-list"
                                role={pagedQueueDownloads.length > 0 ? 'list' : undefined}
                                aria-label={pagedQueueDownloads.length > 0 ? 'Download queue' : undefined}
                            >
                                {pagedQueueDownloads.length === 0
                                    ? queuePlaceholder
                                    : pagedQueueDownloads.map((download) => renderMobileCard(download, 'queue'))}
                            </div>
                        )}

                        {usesCardLayout &&
                            canSelectQueueRows &&
                            queueSelection.selectionMode &&
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
                                totalItems={queueTotal}
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

                        <ListFilterToolbar
                            fields={historyFilterFields}
                            onCommit={(values) =>
                                setHistoryFilters({ client: values.client as string, state: values.state as string })
                            }
                            sorting={historyTable.sorting}
                            sortOptions={HISTORY_SORT_OPTIONS}
                            onSortingChange={updateHistorySorting}
                            selection={
                                usesCardLayout && canSelectHistoryRows
                                    ? {
                                          active: historySelection.selectionMode,
                                          onToggle: historySelection.toggleSelectionMode,
                                      }
                                    : undefined
                            }
                        />

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
                                    keepHeaderWhenEmpty={hasActiveHistoryFilters}
                                />
                            </div>
                        )}

                        {usesCardLayout && (
                            <div
                                className="downloads-page__mobile-list"
                                role={pagedHistoryDownloads.length > 0 ? 'list' : undefined}
                                aria-label={pagedHistoryDownloads.length > 0 ? 'Download history' : undefined}
                            >
                                {pagedHistoryDownloads.length === 0
                                    ? historyPlaceholder
                                    : pagedHistoryDownloads.map((download) => renderMobileCard(download, 'history'))}
                            </div>
                        )}

                        {usesCardLayout &&
                            canSelectHistoryRows &&
                            historySelection.selectionMode &&
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
                                totalItems={historyTotal}
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
                download={detailsQuery.error ? null : detailsQuery.download}
                selectedDownload={detailsDownload}
                clientName={detailsQuery.download ? getClientName(detailsQuery.download) : ''}
                isLoading={detailsQuery.isLoading}
                error={detailsQuery.error}
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
