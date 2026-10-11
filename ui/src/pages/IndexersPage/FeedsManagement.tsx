import { useState, useMemo, useCallback } from 'react';
import { RssIcon, TrashIcon, PencilSimpleIcon, PlusIcon, PowerIcon, ArrowClockwiseIcon } from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Badge } from '@/components/ui/Badge';
import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { DataTable, selectColumn } from '@/components/ui/DataTable';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { Pager } from '@/components/ui/Pager';
import { Switch } from '@/components/ui/Switch';
import { useToast } from '@/components/ui/Toast';
import { Tooltip } from '@/components/ui/Tooltip';

import { useBulkRun } from '@/hooks/useBulkRun';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { useIndexers, type IndexerFeedItem } from '@/hooks/useIndexers';
import { useToggle } from '@/hooks/useToggle';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { useInstanceName } from '@/hooks/useInstanceName';
import { getErrorMessage } from '@/lib/errors';
import { formatRelativeTime } from '@/lib/formatTime';

import { EmptyState } from '@/components/ui/EmptyState';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { IndexerFeedForm, type IndexerFeedFormValues } from './IndexerFeedForm';

import '@/styles/bulk-actions-toolbar.css';

const PAGE_SIZE = 25;

const DEFAULT_SORTING: SortingState = [{ id: 'name', desc: false }];

const SORT_KEYS = ['name', 'provider', 'enabled', 'lastSync'] as const;

function toIndexerFeedFormValues(feed: IndexerFeedItem): IndexerFeedFormValues {
    return {
        id: feed.id,
        name: feed.name,
        description: feed.description ?? '',
        provider: feed.provider,
        enabled: feed.enabled,
        settings: feed.settings,
    };
}

export function FeedsManagement() {
    const instanceName = useInstanceName();
    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingFeed, setEditingFeed] = useState<IndexerFeedFormValues | null>(null);
    const [deletingFeed, setDeletingFeed] = useState<IndexerFeedItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [pageRowIds, setPageRowIds] = useState<string[]>([]);
    const [isBulkDeleteOpen, setIsBulkDeleteOpen] = useState(false);
    const [isBulkDeleting, setIsBulkDeleting] = useState(false);
    const [isBulkSettingEnabled, setIsBulkSettingEnabled] = useState(false);
    const { toast } = useToast();
    const runBulk = useBulkRun();

    const {
        feeds,
        isLoadingFeeds,
        createFeed: apiCreateFeed,
        updateFeed: apiUpdateFeed,
        toggleFeedEnabled,
        deleteFeed: apiDeleteFeed,
        syncFeed: apiSyncFeed,
        syncingFeedId,
        isCreatingFeed,
        isUpdatingFeed,
    } = useIndexers();
    const currentEditingFeed = useMemo(() => {
        if (!editingFeed?.id) {
            return editingFeed;
        }
        const feed = feeds.find((candidate) => candidate.id === editingFeed.id);
        return feed ? toIndexerFeedFormValues(feed) : editingFeed;
    }, [editingFeed, feeds]);

    const { sorting, page, setSorting, setPage } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: {},
        sortKeys: SORT_KEYS,
        pageParam: 'fpage',
        sortParam: 'fsort',
    });

    const handleToggleEnabled = useCallback(
        async (id: string, enabled: boolean) => {
            try {
                await toggleFeedEnabled(id, enabled);
            } catch (error) {
                toast({
                    title: 'Error',
                    description: getErrorMessage(error, 'Failed to toggle feed'),
                    variant: 'error',
                });
            }
        },
        [toggleFeedEnabled, toast],
    );

    const handleSync = useCallback(
        async (feed: IndexerFeedItem) => {
            try {
                await apiSyncFeed(feed.id);
                toast({
                    title: 'Sync started',
                    description: `${instanceName} queued a sync for "${feed.name}".`,
                    variant: 'success',
                });
            } catch (error) {
                toast({
                    title: 'Error',
                    description: getErrorMessage(error, 'Failed to start sync'),
                    variant: 'error',
                });
            }
        },
        [apiSyncFeed, toast, instanceName],
    );

    const handleAdd = useCallback(() => {
        setEditingFeed(null);
        openForm();
    }, [openForm]);

    const handleEdit = useCallback(
        (feed: IndexerFeedItem) => {
            setEditingFeed(toIndexerFeedFormValues(feed));
            openForm();
        },
        [openForm],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingFeed) {
            return;
        }
        setIsDeleting(true);

        try {
            await apiDeleteFeed(deletingFeed.id);
            toast({
                title: 'Feed deleted',
                description: `${instanceName} removed "${deletingFeed.name}".`,
                variant: 'success',
            });
        } catch (error) {
            toast({
                title: 'Error',
                description: getErrorMessage(error, 'Failed to delete feed'),
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingFeed(null);
        }
    }, [deletingFeed, apiDeleteFeed, toast, instanceName]);

    const handleCancelDelete = useCallback(() => {
        setDeletingFeed(null);
    }, []);

    const selection = useBulkSelection({ pageRowIds, totalCount: feeds.length });
    const selectedFeeds = useMemo(() => feeds.filter((f) => selection.isRowChecked(f.id)), [feeds, selection]);

    const handleBulkDelete = useCallback(async () => {
        setIsBulkDeleting(true);
        try {
            await runBulk(selectedFeeds, selection, {
                action: (feed) => apiDeleteFeed(feed.id),
                verb: 'delete',
                itemNounPlural: 'feeds',
            });
        } finally {
            setIsBulkDeleting(false);
            setIsBulkDeleteOpen(false);
        }
    }, [apiDeleteFeed, runBulk, selectedFeeds, selection]);

    const handleBulkSetEnabled = useCallback(
        async (enabled: boolean) => {
            setIsBulkSettingEnabled(true);
            try {
                await runBulk(selectedFeeds, selection, {
                    action: (feed) => toggleFeedEnabled(feed.id, enabled),
                    verb: enabled ? 'enable' : 'disable',
                    itemNounPlural: 'feeds',
                });
            } finally {
                setIsBulkSettingEnabled(false);
            }
        },
        [runBulk, selectedFeeds, selection, toggleFeedEnabled],
    );

    const handleFormSubmit = useCallback(
        async (values: IndexerFeedFormValues) => {
            try {
                if (values.id) {
                    const settings = Object.keys(values.settings).length > 0 ? values.settings : undefined;
                    await apiUpdateFeed(values.id, {
                        name: { value: values.name },
                        description: { value: values.description },
                        providerType: { value: values.provider },
                        enabled: { value: values.enabled },
                        ...(settings ? { settings: { add: settings } } : {}),
                    });
                    toast({
                        title: 'Feed updated',
                        description: `${instanceName} updated ${values.name}.`,
                        variant: 'success',
                    });
                } else {
                    await apiCreateFeed({
                        name: values.name,
                        description: values.description || undefined,
                        providerType: values.provider,
                        enabled: values.enabled,
                        settings: values.settings,
                    });
                    toast({
                        title: 'Feed created',
                        description: `${instanceName} added ${values.name}.`,
                        variant: 'success',
                    });
                }
                closeForm();
            } catch (error) {
                toast({
                    title: 'Error',
                    description: getErrorMessage(error, 'Failed to save feed'),
                    variant: 'error',
                });
                throw error;
            }
        },
        [apiCreateFeed, apiUpdateFeed, toast, closeForm, instanceName],
    );

    const addActions: QuickAction[] = useMemo(
        () => [{ id: 'add-feed', label: 'Add Feed', icon: <PlusIcon weight="bold" />, onClick: handleAdd }],
        [handleAdd],
    );

    const columns = useMemo<ColumnDef<IndexerFeedItem>[]>(
        () => [
            selectColumn<IndexerFeedItem>({
                selection,
                getId: (row) => row.id,
                headerLabel: 'Select all indexer feeds',
                rowLabel: (row) => row.name,
            }),
            {
                id: 'name',
                accessorKey: 'name',
                header: 'Name',
                meta: { mobileRole: 'header' },
                size: 220,
                cell: ({ row }) => (
                    <span className="indexers-table__name" title={row.original.name}>
                        <span className="indexers-table__name-text">{row.original.name}</span>
                    </span>
                ),
            },
            {
                id: 'provider',
                accessorKey: 'provider',
                header: 'Provider',
                size: 150,
                cell: ({ row }) => <span className="indexers-table__definition">{row.original.provider}</span>,
            },
            {
                id: 'description',
                accessorKey: 'description',
                enableSorting: false,
                header: 'Description',
                size: 260,
                cell: ({ row }) =>
                    row.original.description ? (
                        <span className="indexers-table__description" title={row.original.description}>
                            {row.original.description}
                        </span>
                    ) : (
                        <span className="indexers-table__empty-cell">-</span>
                    ),
            },
            {
                id: 'enabled',
                accessorKey: 'enabled',
                header: 'Enabled',
                meta: { mobileRole: 'hidden' },
                size: 110,
                cell: ({ row }) => (
                    <Badge variant={row.original.enabled ? 'healthy' : 'unknown'} size="sm">
                        {row.original.enabled ? 'Enabled' : 'Disabled'}
                    </Badge>
                ),
            },
            {
                id: 'lastSync',
                accessorFn: (row) => {
                    const time = row.lastSync ? new Date(row.lastSync).getTime() : NaN;
                    return isNaN(time) ? undefined : time;
                },
                sortUndefined: 'last',
                header: 'Synced',
                size: 150,
                cell: ({ row }) => (
                    <span className="indexers-table__checked">{formatRelativeTime(row.original.lastSync)}</span>
                ),
            },
            {
                id: 'actions',
                header: '',
                enableSorting: false,
                size: 160,
                cell: ({ row }) => {
                    const feed = row.original;
                    const isSyncing = syncingFeedId === feed.id;
                    return (
                        <div className="indexers-page__item-actions card-actions">
                            <Tooltip content={feed.enabled ? 'Disable' : 'Enable'}>
                                <div className="indexers-page__item-toggle card-actions__toggle">
                                    <PowerIcon
                                        weight="bold"
                                        className="indexers-page__toggle-icon card-actions__toggle-icon"
                                        aria-hidden="true"
                                    />
                                    <span className="card-actions__toggle-label">Enabled</span>
                                    <Switch
                                        checked={feed.enabled}
                                        onCheckedChange={(checked) => handleToggleEnabled(feed.id, checked)}
                                        aria-label={`${feed.enabled ? 'Disable' : 'Enable'} ${feed.name}`}
                                        data-action="toggle-enabled"
                                        data-target={feed.id}
                                    />
                                </div>
                            </Tooltip>
                            <Tooltip content="Sync now">
                                <button
                                    type="button"
                                    className="indexers-page__action-btn card-actions__btn"
                                    data-testid="indexers-page__action-btn"
                                    onClick={() => handleSync(feed)}
                                    disabled={isSyncing}
                                    aria-label={`Sync ${feed.name} now`}
                                    data-action="sync"
                                    data-target={feed.id}
                                >
                                    <ArrowClockwiseIcon weight="bold" />
                                </button>
                            </Tooltip>
                            <Tooltip content="Edit">
                                <button
                                    type="button"
                                    className="indexers-page__action-btn card-actions__btn"
                                    data-testid="indexers-page__action-btn"
                                    onClick={() => handleEdit(feed)}
                                    aria-label={`Edit ${feed.name}`}
                                    data-action="edit"
                                    data-target={feed.id}
                                >
                                    <PencilSimpleIcon weight="bold" />
                                </button>
                            </Tooltip>
                            <Tooltip content="Delete">
                                <button
                                    type="button"
                                    className="indexers-page__action-btn indexers-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                                    data-testid="indexers-page__action-btn"
                                    onClick={() => setDeletingFeed(feed)}
                                    aria-label={`Delete ${feed.name}`}
                                    data-action="delete"
                                    data-target={feed.id}
                                >
                                    <TrashIcon weight="bold" />
                                </button>
                            </Tooltip>
                        </div>
                    );
                },
            },
        ],
        [selection, handleToggleEnabled, handleEdit, handleSync, syncingFeedId],
    );

    const pageCount = Math.max(1, Math.ceil(feeds.length / PAGE_SIZE));
    const currentPage = Math.min(page, pageCount);
    const pagination = useMemo(() => ({ pageIndex: currentPage - 1, pageSize: PAGE_SIZE }), [currentPage]);

    const getRowId = useCallback((row: IndexerFeedItem) => row.id, []);

    const emptyState = (
        <EmptyState
            icon={<RssIcon weight="duotone" />}
            title="No feeds configured"
            description="Add a feed to automatically sync indexer definitions"
            size="sm"
        />
    );

    return (
        <>
            <div className="indexers-page__header-actions">
                <Button variant="primary" size="sm" data-action="add-feed" onClick={handleAdd}>
                    <PlusIcon weight="bold" aria-hidden="true" />
                    <span>Add Feed</span>
                </Button>
            </div>

            <BulkActionsToolbar selection={selection} label="Indexer feed selection actions" itemNounPlural="feeds">
                <Button
                    variant="secondary"
                    size="sm"
                    loading={isBulkSettingEnabled}
                    disabled={isBulkSettingEnabled}
                    onClick={() => void handleBulkSetEnabled(true)}
                    aria-label="Enable selected feeds"
                    data-action="bulk-enable"
                >
                    <PowerIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Enable</span>
                </Button>
                <Button
                    variant="secondary"
                    size="sm"
                    loading={isBulkSettingEnabled}
                    disabled={isBulkSettingEnabled}
                    onClick={() => void handleBulkSetEnabled(false)}
                    aria-label="Disable selected feeds"
                    data-action="bulk-disable"
                >
                    <PowerIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Disable</span>
                </Button>
                <Button
                    variant="destructive"
                    size="sm"
                    onClick={() => setIsBulkDeleteOpen(true)}
                    aria-label="Delete selected feeds"
                    data-action="bulk-delete"
                >
                    <TrashIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Delete</span>
                </Button>
            </BulkActionsToolbar>

            <SelectModeToggle active={selection.selectionMode} onToggle={selection.toggleSelectionMode} />

            <div className="indexers-table" data-area="feeds">
                <DataTable<IndexerFeedItem>
                    columns={columns}
                    data={feeds}
                    enableSorting
                    sorting={sorting}
                    onSortingChange={(updater) =>
                        setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                    }
                    enablePagination={feeds.length > PAGE_SIZE}
                    hidePagination
                    pageSize={PAGE_SIZE}
                    pagination={pagination}
                    getRowId={getRowId}
                    onPageRowIdsChange={setPageRowIds}
                    loading={isLoadingFeeds}
                    emptyState={emptyState}
                    mobileSelectionActive={selection.selectionMode}
                    mobileSelectionOverlay={{
                        isSelected: (feed) => selection.isRowChecked(feed.id),
                        onToggle: (feed) => selection.toggleRow(feed.id, !selection.isRowChecked(feed.id)),
                    }}
                    aria-label="Indexer feeds"
                />
                {feeds.length > PAGE_SIZE && (
                    <Pager
                        className="indexers-table__pager"
                        page={currentPage}
                        pageCount={pageCount}
                        totalItems={feeds.length}
                        onFirst={() => setPage(1)}
                        onPrevious={() => setPage(Math.max(1, currentPage - 1))}
                        onNext={() => setPage(Math.min(pageCount, currentPage + 1))}
                        onLast={() => setPage(pageCount)}
                    />
                )}
            </div>

            <QuickActionsButton actions={addActions} title="Add Feed" />

            <IndexerFeedForm
                open={formOpen}
                onOpenChange={(open) => {
                    if (!open) {
                        closeForm();
                    }
                }}
                initialValues={currentEditingFeed}
                onSubmit={handleFormSubmit}
                isSubmitting={isCreatingFeed || isUpdatingFeed}
            />

            <DeleteConfirmDialog
                open={!!deletingFeed}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingFeed?.name ?? ''}
                itemType="Indexer Feed"
                isDeleting={isDeleting}
            />

            <DeleteConfirmDialog
                open={isBulkDeleteOpen}
                onConfirm={handleBulkDelete}
                onCancel={() => setIsBulkDeleteOpen(false)}
                itemName={`${selection.selectedCount} selected`}
                itemType="Indexer Feeds"
                isDeleting={isBulkDeleting}
            />

            {selection.selectionMode && (
                <MobileSelectionBar
                    data-component="indexer-feeds-mobile-selection-bar"
                    selection={selection}
                    itemNounPlural="feeds"
                >
                    <Button
                        variant="secondary"
                        size="sm"
                        loading={isBulkSettingEnabled}
                        disabled={isBulkSettingEnabled || !selection.hasSelection}
                        onClick={() => void handleBulkSetEnabled(true)}
                        aria-label="Enable selected feeds"
                        data-action="bulk-enable"
                    >
                        <PowerIcon weight="bold" aria-hidden="true" />
                        Enable
                    </Button>
                    <Button
                        variant="secondary"
                        size="sm"
                        loading={isBulkSettingEnabled}
                        disabled={isBulkSettingEnabled || !selection.hasSelection}
                        onClick={() => void handleBulkSetEnabled(false)}
                        aria-label="Disable selected feeds"
                        data-action="bulk-disable"
                    >
                        <PowerIcon weight="bold" aria-hidden="true" />
                        Disable
                    </Button>
                    <Button
                        variant="destructive"
                        size="sm"
                        disabled={!selection.hasSelection}
                        onClick={() => setIsBulkDeleteOpen(true)}
                        aria-label="Delete selected feeds"
                        data-action="bulk-delete"
                    >
                        <TrashIcon weight="bold" aria-hidden="true" />
                        Delete
                    </Button>
                </MobileSelectionBar>
            )}
        </>
    );
}

FeedsManagement.displayName = 'FeedsManagement';

export default FeedsManagement;
