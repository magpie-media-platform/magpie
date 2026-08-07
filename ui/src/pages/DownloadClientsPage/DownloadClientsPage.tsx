import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { HardDrivesIcon, PlusIcon, WarningCircleIcon } from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { useUrlTableState } from '@/hooks/useUrlTableState';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { DataTable } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { Pager } from '@/components/ui/Pager';
import { MultiSelectColumnFilter, encodeMultiValue, decodeMultiValue } from '@/components/ui/MultiSelectColumnFilter';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { IntegrationEmptyState } from '@/components/integrations/IntegrationEmptyState';
import { IntegrationRowActions } from '@/components/integrations/IntegrationRowActions';
import { useToast } from '@/components/ui/Toast';
import {
    useDownloadClients,
    type ApiDownloadClient,
    type DownloadClientStatusValue,
    downloadClientStatusLabels,
    downloadClientStatusVariants,
} from '@/hooks/useDownloadClients';
import {
    useTestDownloadClient,
    useTestDownloadClientSettings,
    type IntegrationSettingsTestRequest,
} from '@/hooks/useTestConnection';
import { isAbortError } from '@/lib/api';
import { getErrorMessage } from '@/lib/errors';
import { formatRelativeTime } from '@/lib/formatTime';
import { buildSettingsPatch, toStringSettings } from '@/lib/integrationSettings';
import { mapStatusParam } from '@/lib/urlParams';

import { DownloadClientForm, type DownloadClientFormValues } from '@/components/configuration/DownloadClientForm';

import './DownloadClientsPage.css';
import '@/styles/card-actions.css';

interface DownloadClientItem {
    id: string;
    name: string;
    provider: string;
    enabled: boolean;
    status: DownloadClientStatusValue;
    lastCheckedAt: string | null;
    cleanupDeleteFiles: boolean;
    keepFailedOnClient: boolean;
    settings: Record<string, string | number | boolean | undefined>;
    createdAt: string;
}

function transformClient(client: ApiDownloadClient): DownloadClientItem {
    return {
        id: client.id,
        name: client.name,
        provider: client.providerType,
        enabled: client.enabled,
        status: client.status,
        lastCheckedAt: client.lastChecked ?? null,
        cleanupDeleteFiles: client.cleanupDeleteFiles,
        keepFailedOnClient: client.keepFailedOnClient,
        settings: client.settings ?? {},
        createdAt: client.createdAt,
    };
}

function toDownloadClientFormValues(client: DownloadClientItem): DownloadClientFormValues {
    return {
        id: client.id,
        name: client.name,
        provider: client.provider,
        enabled: client.enabled,
        cleanupDeleteFiles: client.cleanupDeleteFiles,
        keepFailedOnClient: client.keepFailedOnClient,
        settings: client.settings,
    };
}

const PAGE_SIZE = 25;

const DEFAULT_SORTING: SortingState = [{ id: 'name', desc: false }];

const SORT_KEYS = ['name', 'provider', 'status', 'enabled', 'checked'] as const;

const DEFAULT_FILTERS = { provider: '', status: '', enabled: '' };

const enabledOptions: FacetOption[] = [
    { value: 'true', label: 'Enabled' },
    { value: 'false', label: 'Disabled' },
];

export function DownloadClientsPage() {
    useDocumentTitle('Download Clients');

    const { sorting, filters, page, setSorting, setFilters, setPage } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: DEFAULT_FILTERS,
        sortKeys: SORT_KEYS,
    });

    const provider = useMemo(() => decodeMultiValue(filters.provider), [filters.provider]);
    const status = useMemo(() => mapStatusParam(filters.status), [filters.status]);
    const enabled = filters.enabled;

    const [clientFormOpen, , openClientForm, closeClientForm] = useToggle();
    const returnFocusRef = useRef<HTMLElement | null>(null);
    const [editingClient, setEditingClient] = useState<DownloadClientFormValues | null>(null);
    const [deletingClient, setDeletingClient] = useState<DownloadClientItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [testingId, setTestingId] = useState<string | null>(null);
    const { toast } = useToast();

    const {
        clients: apiClients,
        toggleClientEnabled,
        createClient,
        updateClient,
        deleteClient,
        isCreating,
        isUpdating,
        isLoading,
        error,
        refetch,
    } = useDownloadClients();

    const { test: testClient } = useTestDownloadClient();
    const {
        test: testClientSettings,
        cancel: cancelTestClientSettings,
        isPending: isTestingSettings,
    } = useTestDownloadClientSettings();
    const testClosedRef = useRef(false);

    useEffect(() => () => cancelTestClientSettings(), [cancelTestClientSettings]);

    const clients = useMemo(() => apiClients.map(transformClient), [apiClients]);
    const currentEditingClient = useMemo(() => {
        if (!editingClient?.id) {
            return editingClient;
        }
        const client = clients.find((candidate) => candidate.id === editingClient.id);
        return client ? toDownloadClientFormValues(client) : editingClient;
    }, [clients, editingClient]);

    const filteredClients = useMemo(() => {
        return clients.filter((c) => {
            if (provider.length > 0 && !provider.includes(c.provider)) {
                return false;
            }
            if (status && c.status !== status) {
                return false;
            }
            if (enabled && c.enabled !== (enabled === 'true')) {
                return false;
            }
            return true;
        });
    }, [clients, provider, status, enabled]);

    const providerOptions = useMemo(() => [...new Set(clients.map((c) => c.provider))].sort(), [clients]);
    const statusOptions = useMemo<FacetOption[]>(() => {
        const present = [...new Set(clients.map((c) => c.status))];
        return present.map((s) => ({ value: s, label: downloadClientStatusLabels[s] }));
    }, [clients]);

    const setMulti = useCallback(
        (key: 'provider', next: string[]) => {
            setFilters({ [key]: encodeMultiValue(next) });
        },
        [setFilters],
    );

    const handleClearFilters = useCallback(() => {
        setFilters({ provider: '', status: '', enabled: '' });
    }, [setFilters]);

    const handleToggleClientEnabled = useCallback(
        async (id: string, isEnabled: boolean) => {
            try {
                await toggleClientEnabled(id, isEnabled);
            } catch (error) {
                toast({
                    title: 'Error',
                    description: getErrorMessage(error, `Failed to ${isEnabled ? 'enable' : 'disable'} client`),
                    variant: 'error',
                });
            }
        },
        [toggleClientEnabled, toast],
    );

    const handleTestClient = useCallback(
        async (id: string) => {
            const client = clients.find((c) => c.id === id);
            const name = client?.name ?? 'Download client';
            setTestingId(id);
            try {
                const result = await testClient(id);
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
        [clients, testClient, toast],
    );

    const handleFormTest = useCallback(
        async (request: IntegrationSettingsTestRequest) => {
            const name = currentEditingClient?.name || 'Download client';
            testClosedRef.current = false;
            try {
                const result = await testClientSettings(request);
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
        [currentEditingClient, testClientSettings, toast],
    );

    const handleFormClose = useCallback(
        (open: boolean) => {
            if (!open) {
                testClosedRef.current = true;
                cancelTestClientSettings();
                closeClientForm();
                setEditingClient(null);
            }
        },
        [closeClientForm, cancelTestClientSettings],
    );

    const handleAddClient = useCallback(() => {
        returnFocusRef.current = document.activeElement as HTMLElement;
        setEditingClient(null);
        openClientForm();
    }, [openClientForm]);

    const handleEditClient = useCallback(
        (id: string) => {
            returnFocusRef.current = document.activeElement as HTMLElement;
            const client = clients.find((c) => c.id === id);
            if (client) {
                setEditingClient(toDownloadClientFormValues(client));
                openClientForm();
            }
        },
        [clients, openClientForm],
    );

    const handleDeleteClient = useCallback(
        (id: string) => {
            const client = clients.find((c) => c.id === id);
            if (!client) {
                return;
            }
            setDeletingClient(client);
        },
        [clients],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingClient) {
            return;
        }

        setIsDeleting(true);

        try {
            await deleteClient(deletingClient.id);
            toast({
                title: deletingClient.name,
                description: 'Download client deleted',
                variant: 'success',
            });
        } catch {
            toast({
                title: deletingClient.name,
                description: 'Failed to delete download client',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingClient(null);
        }
    }, [deletingClient, deleteClient, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingClient(null);
    }, []);

    const handleClientFormSubmit = useCallback(
        async (values: DownloadClientFormValues) => {
            try {
                const stringSettings = toStringSettings(values.settings);

                if (values.id) {
                    const settings = buildSettingsPatch(
                        stringSettings,
                        toStringSettings(currentEditingClient?.settings ?? {}),
                    );

                    await updateClient(values.id, {
                        name: { value: values.name },
                        enabled: { value: values.enabled },
                        cleanupDeleteFiles: { value: values.cleanupDeleteFiles },
                        keepFailedOnClient: { value: values.keepFailedOnClient },
                        settings,
                    });
                    toast({
                        title: values.name,
                        description: 'Download client updated',
                        variant: 'success',
                    });
                } else {
                    await createClient({
                        providerType: values.provider,
                        name: values.name,
                        enabled: values.enabled,
                        cleanupDeleteFiles: values.cleanupDeleteFiles,
                        keepFailedOnClient: values.keepFailedOnClient,
                        settings: stringSettings,
                    });
                    toast({
                        title: values.name,
                        description: 'Download client created',
                        variant: 'success',
                    });
                }
                closeClientForm();
            } catch (error) {
                toast({
                    title: values.name || 'Error',
                    description: `Failed to ${values.id ? 'update' : 'create'} download client`,
                    variant: 'error',
                });
                throw error;
            }
        },
        [createClient, currentEditingClient, updateClient, toast, closeClientForm],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const addClientActions: QuickAction[] = useMemo(
        () => [{ id: 'add-client', label: 'Add Client', icon: <PlusIcon weight="bold" />, onClick: handleAddClient }],
        [handleAddClient],
    );

    const columns = useMemo<ColumnDef<DownloadClientItem>[]>(
        () => [
            {
                id: 'name',
                accessorFn: (row) => row.name,
                header: 'Name',
                size: 240,
                cell: ({ row }) => (
                    <span className="download-clients-table__name" title={row.original.name}>
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
                cell: ({ row }) => <span className="download-clients-table__provider">{row.original.provider}</span>,
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
                meta: { headerLabel: 'Status' },
                size: 140,
                cell: ({ row }) => {
                    const variant = downloadClientStatusVariants[row.original.status];
                    return (
                        <Badge variant={variant as Parameters<typeof Badge>[0]['variant']} size="sm" dot>
                            {downloadClientStatusLabels[row.original.status]}
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
                meta: { headerLabel: 'Enabled' },
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
                    <span className="download-clients-table__checked">
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
                        prefix="download-clients-page"
                        name={row.original.name}
                        id={row.original.id}
                        enabled={row.original.enabled}
                        isTesting={testingId === row.original.id}
                        onTest={() => handleTestClient(row.original.id)}
                        onToggleEnabled={(value) => handleToggleClientEnabled(row.original.id, value)}
                        onEdit={() => handleEditClient(row.original.id)}
                        onDelete={() => handleDeleteClient(row.original.id)}
                    />
                ),
            },
        ],
        [
            providerOptions,
            statusOptions,
            provider,
            status,
            enabled,
            setMulti,
            setFilters,
            testingId,
            handleTestClient,
            handleToggleClientEnabled,
            handleEditClient,
            handleDeleteClient,
        ],
    );

    const hasActiveFilters = provider.length > 0 || status !== '' || enabled !== '';

    const pageCount = Math.max(1, Math.ceil(filteredClients.length / PAGE_SIZE));
    const currentPage = Math.min(page, pageCount);
    const pagination = useMemo(() => ({ pageIndex: currentPage - 1, pageSize: PAGE_SIZE }), [currentPage]);

    const getRowId = useCallback((row: DownloadClientItem) => row.id, []);

    const emptyState = (
        <IntegrationEmptyState
            icon={<HardDrivesIcon weight="duotone" />}
            entity="download clients"
            hasActiveFilters={hasActiveFilters}
            addDescription="Add a download client to start downloading content"
            onClearFilters={handleClearFilters}
        />
    );

    const errorState = (
        <EmptyState
            icon={<WarningCircleIcon weight="duotone" />}
            title="Failed to load download clients"
            action={
                <Button variant="secondary" size="sm" onClick={() => refetch()}>
                    Retry
                </Button>
            }
        />
    );

    return (
        <div className="download-clients-page" data-page="download-clients">
            <PullToRefresh onRefresh={handleRefresh} className="download-clients-page__pull-to-refresh">
                <PageHeader
                    className="download-clients-page__header"
                    title="Download Clients"
                    description="Download clients Magpie uses to send releases."
                    icon={
                        <HardDrivesIcon weight="duotone" aria-hidden="true" data-testid="download-clients-page__icon" />
                    }
                    actions={
                        <div className="download-clients-page__header-actions">
                            {hasActiveFilters && <ClearFiltersButton onClick={handleClearFilters} />}
                            <Button variant="primary" size="sm" data-action="add-client" onClick={handleAddClient}>
                                <PlusIcon weight="bold" aria-hidden="true" />
                                <span className="download-clients-page__add-label">Add Client</span>
                            </Button>
                        </div>
                    }
                />

                <div className="download-clients-table" data-area="clients">
                    <DataTable<DownloadClientItem>
                        columns={columns}
                        data={filteredClients}
                        enableSorting
                        sorting={sorting}
                        onSortingChange={(updater) =>
                            setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                        }
                        enablePagination={filteredClients.length > PAGE_SIZE}
                        hidePagination
                        pageSize={PAGE_SIZE}
                        pagination={pagination}
                        getRowId={getRowId}
                        loading={isLoading}
                        emptyState={error ? errorState : emptyState}
                        keepHeaderWhenEmpty={hasActiveFilters}
                        aria-label="Download clients"
                    />
                    {filteredClients.length > PAGE_SIZE && (
                        <Pager
                            className="download-clients-table__pager"
                            page={currentPage}
                            pageCount={pageCount}
                            onFirst={() => setPage(1)}
                            onPrevious={() => setPage(Math.max(1, currentPage - 1))}
                            onNext={() => setPage(Math.min(pageCount, currentPage + 1))}
                            onLast={() => setPage(pageCount)}
                        />
                    )}
                </div>
            </PullToRefresh>

            <QuickActionsButton actions={addClientActions} title="Add Client" />

            <DownloadClientForm
                open={clientFormOpen}
                onOpenChange={handleFormClose}
                initialValues={currentEditingClient}
                onSubmit={handleClientFormSubmit}
                isSubmitting={isCreating || isUpdating}
                onTest={handleFormTest}
                isTesting={isTestingSettings}
                returnFocusRef={returnFocusRef}
            />

            <DeleteConfirmDialog
                open={!!deletingClient}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingClient?.name ?? ''}
                itemType="Download Client"
                isDeleting={isDeleting}
            />
        </div>
    );
}

DownloadClientsPage.displayName = 'DownloadClientsPage';

export default DownloadClientsPage;
