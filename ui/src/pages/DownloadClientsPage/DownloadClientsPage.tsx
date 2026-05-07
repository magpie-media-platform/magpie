import { useState, useMemo, useCallback, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { HardDrives, Trash, PencilSimple, Clock, Power, Plus } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Switch';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import {
    useDownloadClients,
    type ApiDownloadClient,
    type DownloadClientStatusValue,
    downloadClientStatusLabels,
    downloadClientStatusVariants,
} from '@/hooks/useDownloadClients';
import { useTestDownloadClient } from '@/hooks/useTestConnection';
import { formatRelativeTime } from '@/lib/formatTime';
import { mapStatusParam } from '@/lib/urlParams';

import { DownloadClientForm, type DownloadClientFormValues } from './DownloadClientForm';
import { toStringSettings, buildSettingsPatch } from './downloadClientUtils';

import { EmptyState } from '@/components/ui/EmptyState';

import './DownloadClientsPage.css';
import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';

import { Tooltip } from '@/components/ui/Tooltip';

export interface DownloadClientItem {
    id: string;
    name: string;
    provider: string;
    enabled: boolean;
    status: DownloadClientStatusValue;
    lastCheckedAt: string | null;
    settings: Record<string, string | number | boolean | undefined>;
    createdAt: string;
}

interface ClientFilterState {
    status: string;

    enabled: string;

    provider: string;
}

const ALL_VALUE = '__all__';

function transformClient(client: ApiDownloadClient): DownloadClientItem {
    return {
        id: client.id,
        name: client.name,
        provider: client.providerType,
        enabled: client.enabled,
        status: client.status,
        lastCheckedAt: client.lastChecked ?? null,
        settings: client.settings ?? {},
        createdAt: client.createdAt,
    };
}

const statusOptions = [
    { value: 'all', label: 'All Status' },
    { value: '0', label: 'Unknown' },
    { value: '1', label: 'Healthy' },
    { value: '2', label: 'Unhealthy' },
    { value: '3', label: 'Stale' },
];

const enabledOptions = [
    { value: 'all', label: 'All Enabled' },
    { value: 'true', label: 'Enabled' },
    { value: 'false', label: 'Disabled' },
];

const initialFilters: ClientFilterState = { status: '', enabled: '', provider: '' };

export function DownloadClientsPage() {
    useDocumentTitle('Download Clients');
    const [searchParams] = useSearchParams();

    const [filters, setFilters] = useState<ClientFilterState>(() => {
        const statusParam = searchParams.get('status');
        return {
            status: mapStatusParam(statusParam),
            enabled: '',
            provider: '',
        };
    });
    const [clientFormOpen, , openClientForm, closeClientForm] = useToggle();
    const returnFocusRef = useRef<HTMLElement | null>(null);
    const [editingClient, setEditingClient] = useState<DownloadClientFormValues | null>(null);
    const [deletingClient, setDeletingClient] = useState<DownloadClientItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();

    const {
        clients: apiClients,
        toggleClientEnabled,
        createClient,
        updateClient,
        deleteClient,
        isCreating,
        isUpdating,
        refetch,
    } = useDownloadClients();

    const { test: testClient, isPending: isTesting } = useTestDownloadClient();

    const clients = useMemo(() => apiClients.map(transformClient), [apiClients]);

    const filteredClients = useMemo(() => {
        let items = [...clients];

        if (filters.status) {
            items = items.filter((c) => c.status === Number(filters.status));
        }

        if (filters.enabled) {
            items = items.filter((c) => c.enabled === (filters.enabled === 'true'));
        }

        if (filters.provider) {
            items = items.filter((c) => c.provider === filters.provider);
        }

        return items;
    }, [clients, filters]);

    const providerOptions = useMemo(() => {
        const unique = [...new Set(clients.map((c) => c.provider))].sort();
        return unique.map((p) => ({ value: p, label: p }));
    }, [clients]);

    const statusCounts = useMemo(
        () => ({
            unknown: clients.filter((c) => c.status === 0).length,
            healthy: clients.filter((c) => c.status === 1).length,
            unhealthy: clients.filter((c) => c.status === 2).length,
            stale: clients.filter((c) => c.status === 3).length,
        }),
        [clients],
    );

    const totalClients = clients.length;

    const handleStatusChange = useCallback((value: string) => {
        setFilters((prev) => ({
            ...prev,
            status: value === ALL_VALUE ? '' : value,
        }));
    }, []);

    const handleEnabledChange = useCallback((value: string) => {
        setFilters((prev) => ({
            ...prev,
            enabled: value === ALL_VALUE ? '' : value,
        }));
    }, []);

    const handleProviderFilterChange = useCallback((value: string) => {
        setFilters((prev) => ({
            ...prev,
            provider: value === ALL_VALUE ? '' : value,
        }));
    }, []);

    const handleClearFilters = useCallback(() => {
        setFilters(initialFilters);
    }, []);

    const handleToggleClientEnabled = useCallback(
        async (id: string, enabled: boolean) => {
            try {
                await toggleClientEnabled(id, enabled);
            } catch {
                toast({
                    title: 'Error',
                    description: `Failed to ${enabled ? 'enable' : 'disable'} client`,
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
            }
        },
        [clients, testClient, toast],
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
                setEditingClient({
                    id: client.id,
                    name: client.name,
                    provider: client.provider,
                    enabled: client.enabled,
                    settings: client.settings,
                });
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
                    const existing = apiClients.find((c) => c.id === values.id);
                    const settings = buildSettingsPatch(stringSettings, existing?.settings ?? {});

                    await updateClient(values.id, {
                        name: { value: values.name },
                        enabled: { value: values.enabled },
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
                        settings: stringSettings,
                    });
                    toast({
                        title: values.name,
                        description: 'Download client created',
                        variant: 'success',
                    });
                }
                closeClientForm();
            } catch {
                toast({
                    title: values.name || 'Error',
                    description: `Failed to ${values.id ? 'update' : 'create'} download client`,
                    variant: 'error',
                });
            }
        },
        [apiClients, createClient, updateClient, toast, closeClientForm],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const addClientActions: QuickAction[] = useMemo(
        () => [{ id: 'add-client', label: 'Add Client', icon: <Plus weight="bold" />, onClick: handleAddClient }],
        [handleAddClient],
    );

    const hasActiveFilters = filters.status !== '' || filters.enabled !== '' || filters.provider !== '';

    const statusLabel = filters.status
        ? (statusOptions.find((o) => o.value === filters.status)?.label ?? 'Unknown')
        : 'Status';

    const enabledLabel = filters.enabled
        ? (enabledOptions.find((o) => o.value === filters.enabled)?.label ?? 'Unknown')
        : 'Enabled';

    const providerLabel = filters.provider
        ? (providerOptions.find((o) => o.value === filters.provider)?.label ?? 'Unknown')
        : 'Provider';

    return (
        <div className="download-clients-page" data-page="download-clients">
            <PullToRefresh onRefresh={handleRefresh} className="download-clients-page__pull-to-refresh">
                <header className="download-clients-page__header">
                    <div className="download-clients-page__header-left">
                        <HardDrives
                            weight="duotone"
                            className="download-clients-page__icon"
                            aria-hidden="true"
                            data-testid="download-clients-page__icon"
                        />
                        <h1 className="download-clients-page__title">Download Clients</h1>
                        <span
                            className="page-count download-clients-page__count"
                            data-testid="download-clients-page__count"
                        >
                            {totalClients}
                        </span>
                    </div>
                    <div className="download-clients-page__header-actions">
                        <Button variant="primary" size="sm" data-action="add-client" onClick={handleAddClient}>
                            <Plus weight="bold" aria-hidden="true" />
                            <span className="download-clients-page__add-label">Add Client</span>
                        </Button>
                    </div>
                </header>

                <div className="list-toolbar__summary" aria-label="Health summary">
                    <Badge variant="healthy" size="sm" badgeStyle="outline" dot count={statusCounts.healthy}>
                        Healthy
                    </Badge>
                    <Badge variant="unhealthy" size="sm" badgeStyle="outline" dot count={statusCounts.unhealthy}>
                        Unhealthy
                    </Badge>
                    <Badge variant="stale" size="sm" badgeStyle="outline" dot count={statusCounts.stale}>
                        Stale
                    </Badge>
                    <Badge variant="unknown" size="sm" badgeStyle="outline" dot count={statusCounts.unknown}>
                        Unknown
                    </Badge>
                </div>

                <div className="list-toolbar__filters" data-component="client-filters">
                    <div className="list-toolbar__filters-controls">
                        <Select value={filters.status || ALL_VALUE} onValueChange={handleStatusChange}>
                            <SelectTrigger
                                size="sm"
                                placeholder="Status"
                                aria-label="Filter by status"
                                data-filter="status"
                            >
                                {statusLabel}
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value={ALL_VALUE}>All Status</SelectItem>
                                {statusOptions
                                    .filter((o) => o.value !== 'all')
                                    .map((opt) => (
                                        <SelectItem key={opt.value} value={opt.value}>
                                            {opt.label}
                                        </SelectItem>
                                    ))}
                            </SelectContent>
                        </Select>

                        <Select value={filters.enabled || ALL_VALUE} onValueChange={handleEnabledChange}>
                            <SelectTrigger
                                size="sm"
                                placeholder="All"
                                aria-label="Filter by enabled state"
                                data-filter="enabled"
                            >
                                {enabledLabel}
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value={ALL_VALUE}>All</SelectItem>
                                {enabledOptions
                                    .filter((o) => o.value !== 'all')
                                    .map((opt) => (
                                        <SelectItem key={opt.value} value={opt.value}>
                                            {opt.label}
                                        </SelectItem>
                                    ))}
                            </SelectContent>
                        </Select>

                        <Select value={filters.provider || ALL_VALUE} onValueChange={handleProviderFilterChange}>
                            <SelectTrigger
                                size="sm"
                                placeholder="Provider"
                                aria-label="Filter by provider type"
                                data-filter="provider"
                            >
                                {providerLabel}
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value={ALL_VALUE}>All Providers</SelectItem>
                                {providerOptions.map((opt) => (
                                    <SelectItem key={opt.value} value={opt.value}>
                                        {opt.label}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>

                        {hasActiveFilters && (
                            <Button
                                variant="ghost"
                                size="sm"
                                onClick={handleClearFilters}
                                className="list-toolbar__clear-filters"
                                data-action="clear-filters"
                            >
                                Clear
                            </Button>
                        )}
                    </div>
                </div>

                <div className="download-clients-page__list" role="list" aria-label="Download clients">
                    {filteredClients.length === 0 ? (
                        <EmptyState
                            icon={<HardDrives weight="duotone" />}
                            title="No download clients found"
                            description={
                                hasActiveFilters
                                    ? 'Try adjusting your filters'
                                    : 'Add a download client to start downloading content'
                            }
                            action={
                                !hasActiveFilters ? (
                                    <Button
                                        variant="primary"
                                        size="sm"
                                        onClick={handleAddClient}
                                        data-action="add-client-empty"
                                    >
                                        <Plus weight="bold" aria-hidden="true" />
                                        <span>Add Client</span>
                                    </Button>
                                ) : undefined
                            }
                        />
                    ) : (
                        filteredClients.map((client) => (
                            <ClientRow
                                key={client.id}
                                client={client}
                                onToggleEnabled={handleToggleClientEnabled}
                                onEdit={handleEditClient}
                                onDelete={handleDeleteClient}
                            />
                        ))
                    )}
                </div>
            </PullToRefresh>

            <QuickActionsButton actions={addClientActions} title="Add Client" />

            <DownloadClientForm
                open={clientFormOpen}
                onOpenChange={(open) => {
                    if (!open) {
                        closeClientForm();
                        setEditingClient(null);
                    }
                }}
                initialValues={editingClient}
                onSubmit={handleClientFormSubmit}
                isSubmitting={isCreating || isUpdating}
                onTest={editingClient?.id ? () => handleTestClient(editingClient.id!) : undefined}
                isTesting={isTesting}
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

interface ClientRowProps {
    client: DownloadClientItem;
    onToggleEnabled: (id: string, enabled: boolean) => void;
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
}

function ClientRow({ client, onToggleEnabled, onEdit, onDelete }: ClientRowProps) {
    const variant = downloadClientStatusVariants[client.status];
    const statusLabel = downloadClientStatusLabels[client.status];

    return (
        <div
            className={`download-clients-page__item${client.enabled ? '' : ' download-clients-page__item--disabled'}`}
            role="listitem"
            data-testid="download-clients-page__item"
            data-client-id={client.id}
            data-status={variant}
            data-enabled={client.enabled}
        >
            <div className="download-clients-page__item-main">
                <div className="download-clients-page__item-info">
                    <div className="download-clients-page__item-header">
                        <span className="download-clients-page__item-name">{client.name}</span>
                        <Badge variant={variant as Parameters<typeof Badge>[0]['variant']} size="sm" dot>
                            {statusLabel}
                        </Badge>
                    </div>
                    <div className="download-clients-page__item-meta">
                        <span className="download-clients-page__item-provider">{client.provider}</span>
                        <span className="download-clients-page__item-checked">
                            <Clock weight="regular" aria-hidden="true" />
                            Checked {formatRelativeTime(client.lastCheckedAt)}
                        </span>
                    </div>
                </div>
                <div className="download-clients-page__item-actions card-actions">
                    <div className="download-clients-page__item-toggle" title={client.enabled ? 'Disable' : 'Enable'}>
                        <Power weight="bold" className="download-clients-page__toggle-icon" aria-hidden="true" />
                        <Switch
                            checked={client.enabled}
                            onCheckedChange={(checked) => onToggleEnabled(client.id, checked)}
                            aria-label={`${client.enabled ? 'Disable' : 'Enable'} ${client.name}`}
                            data-action="toggle-enabled"
                            data-target={client.id}
                        />
                    </div>
                    <Tooltip content="Edit">
                        <button
                            type="button"
                            className="download-clients-page__action-btn card-actions__btn"
                            data-testid="download-clients-page__action-btn"
                            onClick={() => onEdit(client.id)}
                            aria-label={`Edit ${client.name}`}
                            data-action="edit"
                            data-target={client.id}
                        >
                            <PencilSimple weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip content="Delete">
                        <button
                            type="button"
                            className="download-clients-page__action-btn download-clients-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="download-clients-page__action-btn"
                            onClick={() => onDelete(client.id)}
                            aria-label={`Delete ${client.name}`}
                            data-action="delete"
                            data-target={client.id}
                        >
                            <Trash weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}

export default DownloadClientsPage;
