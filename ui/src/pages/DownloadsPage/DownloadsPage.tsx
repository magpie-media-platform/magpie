import { useState, useMemo, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import {
    CloudArrowDown,
    Pause,
    Play,
    Trash,
    ClockCounterClockwise,
    ListBullets,
    CheckCircle,
    XCircle,
    Warning,
    Clock,
    FolderOpen,
} from '@phosphor-icons/react';

import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { Badge } from '@/components/ui/Badge';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { DownloadFilters } from '@/components/ui/DownloadFilters';
import { DownloadQueueSlot } from '@/components/ui/Slot/DownloadSlots';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';
import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';
import {
    useDownloads,
    type ApiDownload,
    type DownloadStatusValue,
    downloadStatusLabels,
    downloadStatusVariants,
} from '@/hooks/useDownloads';
import { useDownloadClients } from '@/hooks/useDownloadClients';
import { type DownloadClientOption, type StateFilterOption, type DownloadFilterState } from '@/types';
import { readFiltersFromParams, writeFiltersToParams } from '@/lib/downloadFilters';
import { formatDateTime } from '@/lib/dateFormatting';

import { EmptyState } from '@/components/ui/EmptyState';

import './DownloadsPage.css';

const activeStatuses: DownloadStatusValue[] = [0, 1, 2, 3, 4];

const terminalStatuses: DownloadStatusValue[] = [5, 6, 7];

const queueStateOptions: StateFilterOption[] = [
    { value: 'all', label: 'All States' },
    { value: 2, label: 'Downloading' },
    { value: 1, label: 'Queued' },
    { value: 0, label: 'Grabbed' },
    { value: 3, label: 'Paused' },
    { value: 4, label: 'Stalled' },
];

const historyStateOptions: StateFilterOption[] = [
    { value: 'all', label: 'All States' },
    { value: 5, label: 'Downloaded' },
    { value: 6, label: 'Imported' },
    { value: 7, label: 'Failed' },
];

export function DownloadsPage() {
    useDocumentTitle('Downloads');
    const [searchParams, setSearchParams] = useSearchParams();
    const [urlState, setUrlState] = useUrlFilterState({
        tab: 'queue',
        client: '',
        state: '',
        hclient: '',
        hstate: '',
    });

    const activeTab = urlState.tab;

    const setActiveTab = useCallback(
        (tab: string) => {
            setUrlState({
                tab,
                ...(tab !== 'queue' ? { client: '', state: '' } : {}),
                ...(tab !== 'history' ? { hclient: '', hstate: '' } : {}),
            });
        },
        [setUrlState],
    );

    const queueFilters: DownloadFilterState = useMemo(
        () => readFiltersFromParams(searchParams, 'client', 'state'),
        [searchParams],
    );

    const setQueueFilters = useCallback(
        (filters: DownloadFilterState) => {
            writeFiltersToParams(setSearchParams, filters, 'client', 'state');
        },
        [setSearchParams],
    );

    const historyFilters: DownloadFilterState = useMemo(
        () => readFiltersFromParams(searchParams, 'hclient', 'hstate'),
        [searchParams],
    );

    const setHistoryFilters = useCallback(
        (filters: DownloadFilterState) => {
            writeFiltersToParams(setSearchParams, filters, 'hclient', 'hstate');
        },
        [setSearchParams],
    );

    const { downloads, removeDownload, pauseDownload, resumeDownload, refetch: refetchDownloads } = useDownloads();

    const { clients: apiClients } = useDownloadClients();

    const clients: DownloadClientOption[] = useMemo(
        () => apiClients.map((c) => ({ id: c.id, name: c.name })),
        [apiClients],
    );

    const queueDownloads = useMemo(() => downloads.filter((d) => activeStatuses.includes(d.status)), [downloads]);

    const historyDownloads = useMemo(() => downloads.filter((d) => terminalStatuses.includes(d.status)), [downloads]);

    const filteredQueueDownloads = useMemo(() => {
        let items = [...queueDownloads];

        if (queueFilters.client) {
            items = items.filter((d) => d.downloadClientId === queueFilters.client);
        }

        if (queueFilters.state) {
            items = items.filter((d) => d.status === Number(queueFilters.state));
        }

        return items;
    }, [queueDownloads, queueFilters]);

    const filteredHistoryDownloads = useMemo(() => {
        let items = [...historyDownloads];

        if (historyFilters.client) {
            items = items.filter((d) => d.downloadClientId === historyFilters.client);
        }

        if (historyFilters.state) {
            items = items.filter((d) => d.status === Number(historyFilters.state));
        }

        return items;
    }, [historyDownloads, historyFilters]);

    const statusCounts = useMemo(
        () => ({
            grabbed: queueDownloads.filter((d) => d.status === 0).length,
            queued: queueDownloads.filter((d) => d.status === 1).length,
            downloading: queueDownloads.filter((d) => d.status === 2).length,
            paused: queueDownloads.filter((d) => d.status === 3).length,
            stalled: queueDownloads.filter((d) => d.status === 4).length,
        }),
        [queueDownloads],
    );

    const historyCounts = useMemo(
        () => ({
            downloaded: historyDownloads.filter((d) => d.status === 5).length,
            imported: historyDownloads.filter((d) => d.status === 6).length,
            failed: historyDownloads.filter((d) => d.status === 7).length,
        }),
        [historyDownloads],
    );

    const totalActive = queueDownloads.length;

    const { toast } = useToast();

    const [downloadToDelete, setDownloadToDelete] = useState<ApiDownload | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);

    const handleRemove = useCallback((download: ApiDownload) => {
        setDownloadToDelete(download);
    }, []);

    const handleConfirmRemove = useCallback(async () => {
        if (!downloadToDelete) {
            return;
        }

        setIsDeleting(true);
        try {
            await removeDownload(downloadToDelete.id);
            toast({
                title: 'Download removed',
                description: 'The download has been removed from the queue',
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Error',
                description: 'Failed to remove download',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDownloadToDelete(null);
        }
    }, [downloadToDelete, removeDownload, toast]);

    const handleCancelRemove = useCallback(() => {
        setDownloadToDelete(null);
    }, []);

    const handlePause = useCallback(
        async (id: string) => {
            try {
                await pauseDownload(id);
                toast({
                    title: 'Download paused',
                    description: 'The download has been paused',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Error',
                    description: 'Failed to pause download',
                    variant: 'error',
                });
            }
        },
        [pauseDownload, toast],
    );

    const handleResume = useCallback(
        async (id: string) => {
            try {
                await resumeDownload(id);
                toast({
                    title: 'Download resumed',
                    description: 'The download has been resumed',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Error',
                    description: 'Failed to resume download',
                    variant: 'error',
                });
            }
        },
        [resumeDownload, toast],
    );

    const getClientName = useCallback(
        (clientId: string) => {
            return clients.find((c) => c.id === clientId)?.name ?? '';
        },
        [clients],
    );

    const hasActiveQueueFilters = queueFilters.client !== '' || queueFilters.state !== '';
    const hasActiveHistoryFilters = historyFilters.client !== '' || historyFilters.state !== '';

    const handleRefresh = useCallback(async () => {
        await refetchDownloads();
    }, [refetchDownloads]);

    return (
        <div className="downloads-page" data-page="downloads">
            <PullToRefresh onRefresh={handleRefresh} className="downloads-page__pull-to-refresh">
                <header className="downloads-page__header">
                    <div className="downloads-page__header-left">
                        <CloudArrowDown
                            weight="duotone"
                            className="downloads-page__icon"
                            data-testid="downloads-page__icon"
                            aria-hidden="true"
                        />
                        <h1 className="downloads-page__title">Downloads</h1>
                        <span className="page-count downloads-page__count" data-testid="downloads-page__count">
                            {totalActive}
                        </span>
                    </div>
                </header>

                <Tabs value={activeTab} onValueChange={setActiveTab} className="downloads-page__tabs">
                    <TabsList>
                        <TabsTrigger value="queue">
                            <ListBullets weight="bold" aria-hidden="true" />
                            <span>Queue</span>
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
                            <ClockCounterClockwise weight="bold" aria-hidden="true" />
                            <span>History</span>
                            {historyDownloads.length > 0 && (
                                <span
                                    className="tab-count downloads-page__tab-count"
                                    data-testid="downloads-page__tab-count"
                                >
                                    {historyDownloads.length}
                                </span>
                            )}
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="queue">
                        <DownloadQueueSlot>
                            <div className="list-toolbar__summary" aria-label="Queue summary">
                                <Badge
                                    variant="downloading"
                                    size="sm"
                                    badgeStyle="outline"
                                    dot
                                    count={statusCounts.downloading}
                                >
                                    Downloading
                                </Badge>
                                <Badge variant="queued" size="sm" badgeStyle="outline" dot count={statusCounts.queued}>
                                    Queued
                                </Badge>
                                <Badge
                                    variant="grabbed"
                                    size="sm"
                                    badgeStyle="outline"
                                    dot
                                    count={statusCounts.grabbed}
                                >
                                    Grabbed
                                </Badge>
                                <Badge variant="paused" size="sm" badgeStyle="outline" dot count={statusCounts.paused}>
                                    Paused
                                </Badge>
                                <Badge
                                    variant="stalled"
                                    size="sm"
                                    badgeStyle="outline"
                                    dot
                                    count={statusCounts.stalled}
                                >
                                    Stalled
                                </Badge>
                            </div>

                            <DownloadFilters
                                filters={queueFilters}
                                clients={clients}
                                stateOptions={queueStateOptions}
                                onChange={setQueueFilters}
                            />

                            <div className="downloads-page__queue" role="list" aria-label="Download queue">
                                {filteredQueueDownloads.length === 0 ? (
                                    <EmptyState
                                        icon={<CloudArrowDown weight="duotone" />}
                                        title="No active downloads"
                                        description={
                                            hasActiveQueueFilters
                                                ? 'Try adjusting your filters'
                                                : 'Downloads will appear here when items are grabbed'
                                        }
                                    />
                                ) : (
                                    filteredQueueDownloads.map((download) => (
                                        <QueueItem
                                            key={download.id}
                                            download={download}
                                            clientName={getClientName(download.downloadClientId)}
                                            onPause={handlePause}
                                            onResume={handleResume}
                                            onRemove={handleRemove}
                                        />
                                    ))
                                )}
                            </div>
                        </DownloadQueueSlot>
                    </TabsContent>

                    <TabsContent value="history">
                        <div className="list-toolbar__summary" aria-label="History summary">
                            <Badge
                                variant="downloaded"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={historyCounts.downloaded}
                            >
                                Downloaded
                            </Badge>
                            <Badge
                                variant="imported"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={historyCounts.imported}
                            >
                                Imported
                            </Badge>
                            <Badge
                                variant="failed"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={historyCounts.failed}
                            >
                                Failed
                            </Badge>
                        </div>

                        <DownloadFilters
                            filters={historyFilters}
                            clients={clients}
                            stateOptions={historyStateOptions}
                            onChange={setHistoryFilters}
                        />

                        <div className="downloads-page__history" role="list" aria-label="Download history">
                            {filteredHistoryDownloads.length === 0 ? (
                                <EmptyState
                                    icon={<ClockCounterClockwise weight="duotone" />}
                                    title="No download history"
                                    description={
                                        hasActiveHistoryFilters
                                            ? 'Try adjusting your filters'
                                            : 'Completed and failed downloads will appear here'
                                    }
                                />
                            ) : (
                                filteredHistoryDownloads.map((item) => (
                                    <HistoryItem
                                        key={item.id}
                                        item={item}
                                        clientName={getClientName(item.downloadClientId)}
                                    />
                                ))
                            )}
                        </div>
                    </TabsContent>
                </Tabs>
            </PullToRefresh>

            <DeleteConfirmDialog
                open={!!downloadToDelete}
                onConfirm={handleConfirmRemove}
                onCancel={handleCancelRemove}
                itemName={downloadToDelete?.title ?? ''}
                itemType="Download"
                isDeleting={isDeleting}
            />
        </div>
    );
}

DownloadsPage.displayName = 'DownloadsPage';

interface QueueItemProps {
    download: ApiDownload;
    clientName: string;
    onPause: (id: string) => void;
    onResume: (id: string) => void;
    onRemove: (download: ApiDownload) => void;
}

function QueueItem({ download, clientName, onPause, onResume, onRemove }: QueueItemProps) {
    const variant = downloadStatusVariants[download.status];
    const statusLabel = downloadStatusLabels[download.status];

    const showPause = download.status === 2;

    const showResume = download.status === 0 || download.status === 1 || download.status === 3;

    return (
        <div
            className={`downloads-page__item downloads-page__item--${variant}`}
            role="listitem"
            data-download-id={download.id}
            data-status={variant}
        >
            <div className="downloads-page__item-main">
                <div className="downloads-page__item-info">
                    <span
                        className="downloads-page__item-title"
                        data-testid="downloads-page__item-title"
                        title={download.title}
                    >
                        {download.title}
                    </span>
                    <div className="downloads-page__item-meta">
                        {clientName ? (
                            <span className="downloads-page__item-client">{clientName}</span>
                        ) : (
                            <span className="downloads-page__item-client downloads-page__item-client--unknown">
                                &mdash;
                            </span>
                        )}
                        <Badge variant={variant} size="sm" dot>
                            {statusLabel}
                        </Badge>
                        <span className="downloads-page__item-date">
                            <Clock weight="regular" aria-hidden="true" />
                            {formatDateTime(download.createdAt)}
                        </span>
                    </div>
                    {download.statusReason && (
                        <span className="downloads-page__item-message">{download.statusReason}</span>
                    )}
                </div>
                <div className="downloads-page__item-actions card-actions">
                    {showPause && (
                        <Tooltip content="Pause">
                            <button
                                type="button"
                                className="downloads-page__action-btn card-actions__btn"
                                data-testid="downloads-page__action-btn"
                                onClick={() => onPause(download.id)}
                                aria-label={`Pause ${download.title}`}
                                data-action="pause"
                                data-target={download.id}
                            >
                                <Pause weight="bold" />
                            </button>
                        </Tooltip>
                    )}
                    {showResume && (
                        <Tooltip content="Resume">
                            <button
                                type="button"
                                className="downloads-page__action-btn card-actions__btn"
                                data-testid="downloads-page__action-btn"
                                onClick={() => onResume(download.id)}
                                aria-label={`Resume ${download.title}`}
                                data-action="resume"
                                data-target={download.id}
                            >
                                <Play weight="bold" />
                            </button>
                        </Tooltip>
                    )}
                    <Tooltip content="Remove">
                        <button
                            type="button"
                            className="downloads-page__action-btn downloads-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="downloads-page__action-btn"
                            onClick={() => onRemove(download)}
                            aria-label={`Remove ${download.title}`}
                            data-action="remove"
                            data-target={download.id}
                        >
                            <Trash weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}

interface HistoryItemProps {
    item: ApiDownload;
    clientName: string;
}

function HistoryItem({ item, clientName }: HistoryItemProps) {
    const variant = downloadStatusVariants[item.status];
    const statusLabel = downloadStatusLabels[item.status];
    const isFailed = item.status === 7;
    const StatusIcon = isFailed ? XCircle : CheckCircle;

    return (
        <div
            className={`downloads-page__history-item downloads-page__history-item--${variant}`}
            role="listitem"
            data-testid="downloads-page__history-item"
            data-download-id={item.id}
            data-status={variant}
        >
            <div className="downloads-page__history-item-main">
                <div className="downloads-page__history-item-status">
                    <StatusIcon
                        weight="fill"
                        className={`downloads-page__history-status-icon downloads-page__history-status-icon--${variant}`}
                        data-testid="downloads-page__history-status-icon"
                        aria-hidden="true"
                    />
                </div>
                <div className="downloads-page__history-item-info">
                    <span
                        className="downloads-page__history-item-title"
                        data-testid="downloads-page__history-item-title"
                        title={item.title}
                    >
                        {item.title}
                    </span>
                    <div className="downloads-page__history-item-meta">
                        {clientName ? (
                            <span className="downloads-page__history-item-client">{clientName}</span>
                        ) : (
                            <span className="downloads-page__history-item-client downloads-page__history-item-client--unknown">
                                &mdash;
                            </span>
                        )}
                        <Badge variant={isFailed ? 'unhealthy' : 'fulfilled'} size="sm">
                            {statusLabel}
                        </Badge>
                        {item.dateCompleted && (
                            <span className="downloads-page__history-item-date">
                                <Clock weight="regular" aria-hidden="true" />
                                {formatDateTime(item.dateCompleted)}
                            </span>
                        )}
                    </div>
                    {item.outputPath && (
                        <div className="downloads-page__history-item-path">
                            <FolderOpen weight="regular" aria-hidden="true" />
                            <span>{item.outputPath}</span>
                        </div>
                    )}
                    {item.statusReason && (
                        <div className="downloads-page__history-item-message">
                            <Warning weight="fill" aria-hidden="true" />
                            <span>{item.statusReason}</span>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}

export default DownloadsPage;
