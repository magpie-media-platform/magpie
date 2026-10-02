import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { PlusIcon, PowerIcon, TelevisionIcon, TrashIcon, WarningCircleIcon } from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { useBulkRun } from '@/hooks/useBulkRun';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { Badge } from '@/components/ui/Badge';
import { BulkActionsToolbar } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { DataTable, selectColumn } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { ListFilterToolbar, type ListFilterField } from '@/components/ui/ListFilterToolbar';
import { Pager } from '@/components/ui/Pager';
import { MultiSelectColumnFilter, encodeMultiValue, decodeMultiValue } from '@/components/ui/MultiSelectColumnFilter';
import { PageHeader } from '@/components/ui/PageHeader';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { IntegrationEmptyState } from '@/components/integrations/IntegrationEmptyState';
import { IntegrationRowActions } from '@/components/integrations/IntegrationRowActions';
import { useToast } from '@/components/ui/Toast';
import { useMediaServers, type ApiMediaServer, type MediaServerStatusValue } from '@/hooks/useMediaServers';
import {
    useTestMediaServer,
    useTestMediaServerSettings,
    type IntegrationSettingsTestRequest,
} from '@/hooks/useTestConnection';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { isAbortError } from '@/lib/api';
import { getErrorMessage } from '@/lib/errors';
import { formatRelativeTime } from '@/lib/formatTime';
import { buildSettingsPatch, toStringSettings } from '@/lib/integrationSettings';
import { mapStatusParam } from '@/lib/urlParams';

import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { MediaServerForm, type MediaServerFormValues } from '@/components/configuration/MediaServerForm';

import './MediaServersPage.css';
import '@/styles/bulk-actions-toolbar.css';
import '@/styles/card-actions.css';

interface MediaServerItem {
    id: string;
    name: string;
    provider: string;
    enabled: boolean;
    status: MediaServerStatusValue;
    lastCheckedAt: string | null;
    settings: Record<string, string | number | boolean | undefined>;
    createdAt: string;
}

function transformServer(server: ApiMediaServer): MediaServerItem {
    return {
        id: server.id,
        name: server.name,
        provider: server.providerType,
        enabled: server.enabled,
        status: server.status,
        lastCheckedAt: server.lastChecked ?? null,
        settings: server.settings ?? {},
        createdAt: server.createdAt,
    };
}

function toMediaServerFormValues(server: MediaServerItem): MediaServerFormValues {
    return {
        id: server.id,
        name: server.name,
        provider: server.provider,
        enabled: server.enabled,
        settings: server.settings,
    };
}

const PAGE_SIZE = 25;

const DEFAULT_SORTING: SortingState = [{ id: 'name', desc: false }];

const SORT_KEYS = ['name', 'provider', 'status', 'enabled', 'checked'] as const;
const SORT_OPTIONS = [
    { id: 'name', label: 'Name' },
    { id: 'provider', label: 'Provider' },
    { id: 'status', label: 'Status' },
    { id: 'enabled', label: 'Enabled' },
    { id: 'checked', label: 'Checked' },
];

const DEFAULT_FILTERS = { provider: '', status: '', enabled: '' };

const enabledOptions: FacetOption[] = [
    { value: 'true', label: 'Enabled' },
    { value: 'false', label: 'Disabled' },
];

export function MediaServersPage() {
    useDocumentTitle('Media Servers');

    const { sorting, filters, page, setSorting, setFilters, setPage } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: DEFAULT_FILTERS,
        sortKeys: SORT_KEYS,
    });

    const provider = useMemo(() => decodeMultiValue(filters.provider), [filters.provider]);
    const status = useMemo(() => mapStatusParam(filters.status), [filters.status]);
    const enabled = filters.enabled;

    const [formOpen, , openForm, closeForm] = useToggle();
    const returnFocusRef = useRef<HTMLElement | null>(null);
    const [editingServer, setEditingServer] = useState<MediaServerFormValues | null>(null);
    const [deletingServer, setDeletingServer] = useState<MediaServerItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [testingId, setTestingId] = useState<string | null>(null);
    const [pageRowIds, setPageRowIds] = useState<string[]>([]);
    const [isBulkDeleteOpen, setIsBulkDeleteOpen] = useState(false);
    const [isBulkDeleting, setIsBulkDeleting] = useState(false);
    const [isBulkSettingEnabled, setIsBulkSettingEnabled] = useState(false);
    const { toast } = useToast();
    const runBulk = useBulkRun();

    const {
        servers: apiServers,
        toggleServerEnabled,
        createServer,
        updateServer,
        deleteServer,
        isCreating,
        isUpdating,
        isLoading,
        error,
        refetch,
    } = useMediaServers();

    const { test: testServer } = useTestMediaServer();
    const {
        test: testServerSettings,
        cancel: cancelTestServerSettings,
        isPending: isTestingSettings,
    } = useTestMediaServerSettings();
    const testClosedRef = useRef(false);

    useEffect(() => () => cancelTestServerSettings(), [cancelTestServerSettings]);

    const servers = useMemo(() => apiServers.map(transformServer), [apiServers]);
    const currentEditingServer = useMemo(() => {
        if (!editingServer?.id) {
            return editingServer;
        }
        const server = servers.find((candidate) => candidate.id === editingServer.id);
        return server ? toMediaServerFormValues(server) : editingServer;
    }, [editingServer, servers]);

    const filteredServers = useMemo(() => {
        return servers.filter((s) => {
            if (provider.length > 0 && !provider.includes(s.provider)) {
                return false;
            }
            if (status && s.status !== status) {
                return false;
            }
            if (enabled && s.enabled !== (enabled === 'true')) {
                return false;
            }
            return true;
        });
    }, [servers, provider, status, enabled]);

    const providerOptions = useMemo(() => [...new Set(servers.map((s) => s.provider))].sort(), [servers]);
    const statusOptions = useMemo<FacetOption[]>(() => {
        const present = [...new Set(servers.map((s) => s.status))];
        return present.map((s) => ({ value: s, label: s }));
    }, [servers]);

    const setMulti = useCallback(
        (key: 'provider', next: string[]) => {
            setFilters({ [key]: encodeMultiValue(next) });
        },
        [setFilters],
    );

    const handleClearFilters = useCallback(() => {
        setFilters({ provider: '', status: '', enabled: '' });
    }, [setFilters]);

    const handleToggleServerEnabled = useCallback(
        async (id: string, isEnabled: boolean) => {
            try {
                await toggleServerEnabled(id, isEnabled);
            } catch (error) {
                toast({
                    title: 'Error',
                    description: getErrorMessage(error, `Failed to ${isEnabled ? 'enable' : 'disable'} server`),
                    variant: 'error',
                });
            }
        },
        [toggleServerEnabled, toast],
    );

    const handleTestServer = useCallback(
        async (id: string) => {
            const server = servers.find((s) => s.id === id);
            const name = server?.name ?? 'Media server';
            setTestingId(id);
            try {
                const result = await testServer(id);
                toast({
                    title: name,
                    description: result.message,
                    variant: result.success ? 'success' : 'error',
                });
            } catch (error) {
                toast({
                    title: name,
                    description: error instanceof Error ? error.message : 'Failed to test connection',
                    variant: 'error',
                });
            } finally {
                setTestingId(null);
            }
        },
        [servers, testServer, toast],
    );

    const handleFormTest = useCallback(
        async (request: IntegrationSettingsTestRequest) => {
            const name = currentEditingServer?.name || 'Media server';
            testClosedRef.current = false;
            try {
                const result = await testServerSettings(request);
                if (testClosedRef.current) {
                    return;
                }
                toast({
                    title: name,
                    description: result.message,
                    variant: result.success ? 'success' : 'error',
                });
            } catch (error) {
                if (testClosedRef.current || isAbortError(error)) {
                    return;
                }
                toast({
                    title: name,
                    description: error instanceof Error ? error.message : 'Failed to test connection',
                    variant: 'error',
                });
            }
        },
        [currentEditingServer, testServerSettings, toast],
    );

    const handleAddServer = useCallback(() => {
        returnFocusRef.current = document.activeElement as HTMLElement;
        setEditingServer(null);
        openForm();
    }, [openForm]);

    const handleEditServer = useCallback(
        (id: string) => {
            returnFocusRef.current = document.activeElement as HTMLElement;
            const server = servers.find((s) => s.id === id);
            if (server) {
                setEditingServer(toMediaServerFormValues(server));
                openForm();
            }
        },
        [servers, openForm],
    );

    const handleFormSubmit = useCallback(
        async (values: MediaServerFormValues) => {
            try {
                const stringSettings = toStringSettings(values.settings);

                if (values.id) {
                    const settings = buildSettingsPatch(
                        stringSettings,
                        toStringSettings(currentEditingServer?.settings ?? {}),
                    );

                    await updateServer(values.id, {
                        name: { value: values.name },
                        enabled: { value: values.enabled },
                        settings,
                    });
                    toast({
                        title: values.name,
                        description: 'Media server updated',
                        variant: 'success',
                    });
                } else {
                    await createServer({
                        providerType: values.provider,
                        name: values.name,
                        enabled: values.enabled,
                        settings: stringSettings,
                    });
                    toast({
                        title: values.name,
                        description: 'Media server created',
                        variant: 'success',
                    });
                }
                closeForm();
                setEditingServer(null);
            } catch (error) {
                toast({
                    title: values.name || 'Error',
                    description: `Failed to ${values.id ? 'update' : 'create'} media server`,
                    variant: 'error',
                });
                throw error;
            }
        },
        [createServer, currentEditingServer, updateServer, toast, closeForm],
    );

    const handleFormClose = useCallback(
        (open: boolean) => {
            if (!open) {
                testClosedRef.current = true;
                cancelTestServerSettings();
                closeForm();
                setEditingServer(null);
            }
        },
        [closeForm, cancelTestServerSettings],
    );

    const handleDeleteServer = useCallback(
        (id: string) => {
            const server = servers.find((s) => s.id === id);
            if (!server) {
                return;
            }
            setDeletingServer(server);
        },
        [servers],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingServer) {
            return;
        }

        setIsDeleting(true);

        try {
            await deleteServer(deletingServer.id);
            toast({
                title: deletingServer.name,
                description: 'Media server deleted',
                variant: 'success',
            });
        } catch {
            toast({
                title: deletingServer.name,
                description: 'Failed to delete media server',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingServer(null);
        }
    }, [deletingServer, deleteServer, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingServer(null);
    }, []);

    const selectionResetKey = useMemo(
        () => [filters.provider, filters.status, filters.enabled].join('|'),
        [filters.provider, filters.status, filters.enabled],
    );
    const selection = useBulkSelection({
        pageRowIds,
        totalCount: filteredServers.length,
        resetKey: selectionResetKey,
    });
    const selectedServers = useMemo(
        () => filteredServers.filter((s) => selection.isRowChecked(s.id)),
        [filteredServers, selection],
    );

    const handleBulkDelete = useCallback(async () => {
        setIsBulkDeleting(true);
        try {
            await runBulk(selectedServers, selection, {
                action: (server) => deleteServer(server.id),
                verb: 'delete',
                itemNounPlural: 'media servers',
            });
        } finally {
            setIsBulkDeleting(false);
            setIsBulkDeleteOpen(false);
        }
    }, [deleteServer, runBulk, selectedServers, selection]);

    const handleBulkSetEnabled = useCallback(
        async (isEnabled: boolean) => {
            setIsBulkSettingEnabled(true);
            try {
                await runBulk(selectedServers, selection, {
                    action: (server) => toggleServerEnabled(server.id, isEnabled),
                    verb: isEnabled ? 'enable' : 'disable',
                    itemNounPlural: 'media servers',
                });
            } finally {
                setIsBulkSettingEnabled(false);
            }
        },
        [runBulk, selectedServers, selection, toggleServerEnabled],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const addServerActions: QuickAction[] = useMemo(
        () => [{ id: 'add-server', label: 'Add Server', icon: <PlusIcon weight="bold" />, onClick: handleAddServer }],
        [handleAddServer],
    );

    const columns = useMemo<ColumnDef<MediaServerItem>[]>(
        () => [
            selectColumn<MediaServerItem>({
                selection,
                getId: (row) => row.id,
                headerLabel: 'Select all media servers',
                rowLabel: (row) => row.name,
            }),
            {
                id: 'name',
                accessorFn: (row) => row.name,
                header: 'Name',
                meta: { mobileRole: 'header' },
                size: 240,
                cell: ({ row }) => (
                    <span className="media-servers-table__name" title={row.original.name}>
                        {row.original.name}
                    </span>
                ),
            },
            {
                id: 'provider',
                accessorFn: (row) => row.provider,
                header: () => (
                    <MultiSelectColumnFilter
                        label="Provider"
                        options={providerOptions}
                        value={provider}
                        onChange={(next) => setMulti('provider', next)}
                    />
                ),
                meta: { headerLabel: 'Provider' },
                size: 200,
                cell: ({ row }) => <span className="media-servers-table__provider">{row.original.provider}</span>,
            },
            {
                id: 'status',
                accessorFn: (row) => row.status,
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="Status"
                        active={status !== ''}
                        options={statusOptions}
                        value={status}
                        onChange={(value) => setFilters({ status: value })}
                    />
                ),
                meta: { headerLabel: 'Status', mobileRole: 'header' },
                size: 140,
                cell: ({ row }) => {
                    const variant = row.original.status.toLowerCase();
                    return (
                        <Badge variant={variant as Parameters<typeof Badge>[0]['variant']} size="sm" dot>
                            {row.original.status}
                        </Badge>
                    );
                },
            },
            {
                id: 'enabled',
                accessorFn: (row) => row.enabled,
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="Enabled"
                        active={enabled !== ''}
                        options={enabledOptions}
                        value={enabled}
                        onChange={(value) => setFilters({ enabled: value })}
                    />
                ),
                meta: { headerLabel: 'Enabled', mobileRole: 'hidden' },
                size: 140,
                cell: ({ row }) => (
                    <Badge variant={row.original.enabled ? 'healthy' : 'unknown'} size="sm">
                        {row.original.enabled ? 'Enabled' : 'Disabled'}
                    </Badge>
                ),
            },
            {
                id: 'checked',
                accessorFn: (row) => {
                    const time = row.lastCheckedAt ? new Date(row.lastCheckedAt).getTime() : NaN;
                    return isNaN(time) ? undefined : time;
                },
                sortUndefined: 'last',
                header: 'Checked',
                size: 150,
                cell: ({ row }) => (
                    <span className="media-servers-table__checked">
                        {formatRelativeTime(row.original.lastCheckedAt)}
                    </span>
                ),
            },
            {
                id: 'actions',
                header: '',
                enableSorting: false,
                size: 180,
                cell: ({ row }) => (
                    <IntegrationRowActions
                        prefix="media-servers-page"
                        name={row.original.name}
                        id={row.original.id}
                        enabled={row.original.enabled}
                        isTesting={testingId === row.original.id}
                        onTest={() => handleTestServer(row.original.id)}
                        onToggleEnabled={(value) => handleToggleServerEnabled(row.original.id, value)}
                        onEdit={() => handleEditServer(row.original.id)}
                        onDelete={() => handleDeleteServer(row.original.id)}
                    />
                ),
            },
        ],
        [
            selection,
            providerOptions,
            statusOptions,
            provider,
            status,
            enabled,
            setMulti,
            setFilters,
            testingId,
            handleTestServer,
            handleToggleServerEnabled,
            handleEditServer,
            handleDeleteServer,
        ],
    );

    const hasActiveFilters = provider.length > 0 || status !== '' || enabled !== '';
    const filterFields: ListFilterField[] = [
        {
            id: 'provider',
            label: 'Provider',
            kind: 'multi',
            value: provider,
            options: providerOptions.map((value) => ({ value, label: value })),
        },
        { id: 'status', label: 'Status', kind: 'facet', value: status, options: statusOptions },
        { id: 'enabled', label: 'Enabled', kind: 'facet', value: enabled, options: enabledOptions },
    ];

    const pageCount = Math.max(1, Math.ceil(filteredServers.length / PAGE_SIZE));
    const currentPage = Math.min(page, pageCount);
    const pagination = useMemo(() => ({ pageIndex: currentPage - 1, pageSize: PAGE_SIZE }), [currentPage]);

    const getRowId = useCallback((row: MediaServerItem) => row.id, []);

    const emptyState = (
        <IntegrationEmptyState
            icon={<TelevisionIcon weight="duotone" />}
            entity="media servers"
            hasActiveFilters={hasActiveFilters}
            addDescription="Add a media server to integrate with your library"
            onClearFilters={handleClearFilters}
        />
    );

    const errorState = (
        <EmptyState
            icon={<WarningCircleIcon weight="duotone" />}
            title="Failed to load media servers"
            action={
                <Button variant="secondary" size="sm" onClick={() => refetch()}>
                    Retry
                </Button>
            }
        />
    );

    return (
        <div className="media-servers-page" data-page="media-servers">
            <PullToRefresh onRefresh={handleRefresh} className="media-servers-page__pull-to-refresh">
                <PageHeader
                    className="media-servers-page__header"
                    title="Media Servers"
                    description="Media server connections used for library refreshes and links."
                    icon={<TelevisionIcon weight="duotone" aria-hidden="true" data-testid="media-servers-page__icon" />}
                    actions={
                        <div className="media-servers-page__header-actions">
                            {hasActiveFilters && <ClearFiltersButton onClick={handleClearFilters} />}
                            <Button variant="primary" size="sm" data-action="add-server" onClick={handleAddServer}>
                                <PlusIcon weight="bold" aria-hidden="true" />
                                <span className="media-servers-page__add-label">Add Server</span>
                            </Button>
                        </div>
                    }
                />

                <BulkActionsToolbar
                    selection={selection}
                    label="Media server selection actions"
                    itemNounPlural="media servers"
                >
                    <Button
                        variant="secondary"
                        size="sm"
                        loading={isBulkSettingEnabled}
                        disabled={isBulkSettingEnabled}
                        onClick={() => void handleBulkSetEnabled(true)}
                        aria-label="Enable selected media servers"
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
                        aria-label="Disable selected media servers"
                        data-action="bulk-disable"
                    >
                        <PowerIcon weight="bold" aria-hidden="true" />
                        <span className="bulk-actions-toolbar__btn-label">Disable</span>
                    </Button>
                    <Button
                        variant="destructive"
                        size="sm"
                        onClick={() => setIsBulkDeleteOpen(true)}
                        aria-label="Delete selected media servers"
                        data-action="bulk-delete"
                    >
                        <TrashIcon weight="bold" aria-hidden="true" />
                        <span className="bulk-actions-toolbar__btn-label">Delete</span>
                    </Button>
                </BulkActionsToolbar>

                <ListFilterToolbar
                    fields={filterFields}
                    onCommit={(values) =>
                        setFilters({
                            provider: encodeMultiValue(values.provider as string[]),
                            status: values.status as string,
                            enabled: values.enabled as string,
                        })
                    }
                    sorting={sorting}
                    sortOptions={SORT_OPTIONS}
                    onSortingChange={setSorting}
                    selection={{ active: selection.selectionMode, onToggle: selection.toggleSelectionMode }}
                />

                <div className="media-servers-table" data-area="servers">
                    <DataTable<MediaServerItem>
                        columns={columns}
                        data={filteredServers}
                        enableSorting
                        sorting={sorting}
                        onSortingChange={(updater) =>
                            setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                        }
                        enablePagination={filteredServers.length > PAGE_SIZE}
                        hidePagination
                        pageSize={PAGE_SIZE}
                        pagination={pagination}
                        getRowId={getRowId}
                        onPageRowIdsChange={setPageRowIds}
                        loading={isLoading}
                        emptyState={error ? errorState : emptyState}
                        keepHeaderWhenEmpty={hasActiveFilters}
                        mobileSelectionActive={selection.selectionMode}
                        mobileSelectionOverlay={{
                            isSelected: (server) => selection.isRowChecked(server.id),
                            onToggle: (server) => selection.toggleRow(server.id, !selection.isRowChecked(server.id)),
                        }}
                        aria-label="Media servers"
                    />
                    {filteredServers.length > PAGE_SIZE && (
                        <Pager
                            className="media-servers-table__pager"
                            page={currentPage}
                            pageCount={pageCount}
                            totalItems={filteredServers.length}
                            onFirst={() => setPage(1)}
                            onPrevious={() => setPage(Math.max(1, currentPage - 1))}
                            onNext={() => setPage(Math.min(pageCount, currentPage + 1))}
                            onLast={() => setPage(pageCount)}
                        />
                    )}
                </div>
            </PullToRefresh>

            <QuickActionsButton actions={addServerActions} title="Add Server" />

            <DeleteConfirmDialog
                open={!!deletingServer}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingServer?.name ?? ''}
                itemType="Media Server"
                isDeleting={isDeleting}
            />

            <DeleteConfirmDialog
                open={isBulkDeleteOpen}
                onConfirm={handleBulkDelete}
                onCancel={() => setIsBulkDeleteOpen(false)}
                itemName={`${selection.selectedCount} selected`}
                itemType="Media Servers"
                isDeleting={isBulkDeleting}
            />

            {selection.selectionMode && (
                <MobileSelectionBar
                    data-component="media-servers-mobile-selection-bar"
                    selection={selection}
                    itemNounPlural="media servers"
                >
                    <Button
                        variant="secondary"
                        size="sm"
                        loading={isBulkSettingEnabled}
                        disabled={isBulkSettingEnabled || !selection.hasSelection}
                        onClick={() => void handleBulkSetEnabled(true)}
                        aria-label="Enable selected media servers"
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
                        aria-label="Disable selected media servers"
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
                        aria-label="Delete selected media servers"
                        data-action="bulk-delete"
                    >
                        <TrashIcon weight="bold" aria-hidden="true" />
                        Delete
                    </Button>
                </MobileSelectionBar>
            )}

            <MediaServerForm
                open={formOpen}
                onOpenChange={handleFormClose}
                initialValues={currentEditingServer}
                onSubmit={handleFormSubmit}
                isSubmitting={isCreating || isUpdating}
                onTest={handleFormTest}
                isTesting={isTestingSettings}
                returnFocusRef={returnFocusRef}
            />
        </div>
    );
}

MediaServersPage.displayName = 'MediaServersPage';

export default MediaServersPage;
