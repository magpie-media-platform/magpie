import { useState, useMemo, useCallback, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Television, Trash, PencilSimple, Clock, Power, Plus } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Switch } from '@/components/ui/Switch';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { Tooltip } from '@/components/ui/Tooltip';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import {
    useMediaServers,
    type ApiMediaServer,
    type MediaServerStatusValue,
    mediaServerStatusLabels,
    mediaServerStatusVariants,
} from '@/hooks/useMediaServers';
import { useTestMediaServer } from '@/hooks/useTestConnection';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { formatRelativeTime } from '@/lib/formatTime';
import { mapStatusParam } from '@/lib/urlParams';

import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { MediaServerForm, type MediaServerFormValues } from './MediaServerForm';

import './MediaServersPage.css';
import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';

export interface MediaServerItem {
    id: string;
    name: string;
    provider: string;
    enabled: boolean;
    status: MediaServerStatusValue;
    lastCheckedAt: string | null;
    settings: Record<string, string | number | boolean | undefined>;
    createdAt: string;
}

interface ServerFilterState {
    status: string;

    enabled: string;
}

const ALL_VALUE = '__all__';

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

const initialFilters: ServerFilterState = { status: '', enabled: '' };

export function MediaServersPage() {
    useDocumentTitle('Media Servers');
    const [searchParams] = useSearchParams();

    const [filters, setFilters] = useState<ServerFilterState>(() => {
        const statusParam = searchParams.get('status');
        return {
            status: mapStatusParam(statusParam),
            enabled: '',
        };
    });
    const [formOpen, , openForm, closeForm] = useToggle();
    const returnFocusRef = useRef<HTMLElement | null>(null);
    const [editingServer, setEditingServer] = useState<MediaServerFormValues | null>(null);
    const [deletingServer, setDeletingServer] = useState<MediaServerItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();

    const {
        servers: apiServers,
        toggleServerEnabled,
        createServer,
        updateServer,
        deleteServer,
        isCreating,
        isUpdating,
        refetch,
    } = useMediaServers();

    const { test: testServer, isPending: isTesting } = useTestMediaServer();

    const servers = useMemo(() => apiServers.map(transformServer), [apiServers]);

    const filteredServers = useMemo(() => {
        let items = [...servers];

        if (filters.status) {
            items = items.filter((s) => s.status === Number(filters.status));
        }

        if (filters.enabled) {
            items = items.filter((s) => s.enabled === (filters.enabled === 'true'));
        }

        return items;
    }, [servers, filters]);

    const statusCounts = useMemo(
        () => ({
            unknown: servers.filter((s) => s.status === 0).length,
            healthy: servers.filter((s) => s.status === 1).length,
            unhealthy: servers.filter((s) => s.status === 2).length,
            stale: servers.filter((s) => s.status === 3).length,
        }),
        [servers],
    );

    const totalServers = servers.length;

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

    const handleClearFilters = useCallback(() => {
        setFilters(initialFilters);
    }, []);

    const handleToggleServerEnabled = useCallback(
        async (id: string, enabled: boolean) => {
            try {
                await toggleServerEnabled(id, enabled);
            } catch {
                toast({
                    title: 'Error',
                    description: `Failed to ${enabled ? 'enable' : 'disable'} server`,
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
            }
        },
        [servers, testServer, toast],
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
                setEditingServer({
                    id: server.id,
                    name: server.name,
                    provider: server.provider,
                    enabled: server.enabled,
                    settings: server.settings,
                });
                openForm();
            }
        },
        [servers, openForm],
    );

    const handleFormSubmit = useCallback(
        async (values: MediaServerFormValues) => {
            try {
                const stringSettings: Record<string, string> = {};
                for (const [key, value] of Object.entries(values.settings)) {
                    if (value !== undefined && value !== null) {
                        stringSettings[key] = String(value);
                    }
                }

                if (values.id) {
                    const existing = apiServers.find((s) => s.id === values.id);
                    const existingKeys = new Set(Object.keys(existing?.settings ?? {}));
                    const add: Record<string, string> = {};
                    const update: Record<string, string> = {};
                    for (const [key, value] of Object.entries(stringSettings)) {
                        if (existingKeys.has(key)) {
                            update[key] = value;
                        } else {
                            add[key] = value;
                        }
                    }
                    const settings: { add?: Record<string, string>; update?: Record<string, string> } = {};
                    if (Object.keys(add).length > 0) {
                        settings.add = add;
                    }
                    if (Object.keys(update).length > 0) {
                        settings.update = update;
                    }

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
            } catch {
                toast({
                    title: values.name || 'Error',
                    description: `Failed to ${values.id ? 'update' : 'create'} media server`,
                    variant: 'error',
                });
            }
        },
        [apiServers, createServer, updateServer, toast, closeForm],
    );

    const handleFormClose = useCallback(
        (open: boolean) => {
            if (!open) {
                closeForm();
                setEditingServer(null);
            }
        },
        [closeForm],
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

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const addServerActions: QuickAction[] = useMemo(
        () => [{ id: 'add-server', label: 'Add Server', icon: <Plus weight="bold" />, onClick: handleAddServer }],
        [handleAddServer],
    );

    const hasActiveFilters = filters.status !== '' || filters.enabled !== '';

    const statusLabel = filters.status
        ? (statusOptions.find((o) => o.value === filters.status)?.label ?? 'Unknown')
        : 'All Status';

    const enabledLabel = filters.enabled
        ? (enabledOptions.find((o) => o.value === filters.enabled)?.label ?? 'Unknown')
        : 'All Enabled';

    return (
        <div className="media-servers-page" data-page="media-servers">
            <PullToRefresh onRefresh={handleRefresh} className="media-servers-page__pull-to-refresh">
                <header className="media-servers-page__header">
                    <div className="media-servers-page__header-left">
                        <Television
                            weight="duotone"
                            className="media-servers-page__icon"
                            aria-hidden="true"
                            data-testid="media-servers-page__icon"
                        />
                        <h1 className="media-servers-page__title">Media Servers</h1>
                        <span className="page-count media-servers-page__count" data-testid="media-servers-page__count">
                            {totalServers}
                        </span>
                    </div>
                    <div className="media-servers-page__header-actions">
                        <Button variant="primary" size="sm" data-action="add-server" onClick={handleAddServer}>
                            <Plus weight="bold" aria-hidden="true" />
                            <span className="media-servers-page__add-label">Add Server</span>
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

                <div className="list-toolbar__filters" data-component="server-filters">
                    <div className="list-toolbar__filters-controls">
                        <Select value={filters.status || ALL_VALUE} onValueChange={handleStatusChange}>
                            <SelectTrigger
                                size="sm"
                                placeholder="All Status"
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

                <div className="media-servers-page__list" role="list" aria-label="Media servers">
                    {filteredServers.length === 0 ? (
                        <EmptyState
                            icon={<Television weight="duotone" />}
                            title="No media servers found"
                            description={
                                hasActiveFilters
                                    ? 'Try adjusting your filters'
                                    : 'Add a media server to integrate with your library'
                            }
                            action={
                                !hasActiveFilters ? (
                                    <Button
                                        variant="primary"
                                        size="sm"
                                        onClick={handleAddServer}
                                        data-action="add-server-empty"
                                    >
                                        <Plus weight="bold" aria-hidden="true" />
                                        <span>Add Server</span>
                                    </Button>
                                ) : undefined
                            }
                        />
                    ) : (
                        filteredServers.map((server) => (
                            <ServerRow
                                key={server.id}
                                server={server}
                                onToggleEnabled={handleToggleServerEnabled}
                                onEdit={handleEditServer}
                                onDelete={handleDeleteServer}
                            />
                        ))
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

            <MediaServerForm
                open={formOpen}
                onOpenChange={handleFormClose}
                initialValues={editingServer}
                onSubmit={handleFormSubmit}
                isSubmitting={isCreating || isUpdating}
                onTest={editingServer?.id ? () => handleTestServer(editingServer.id!) : undefined}
                isTesting={isTesting}
                returnFocusRef={returnFocusRef}
            />
        </div>
    );
}

MediaServersPage.displayName = 'MediaServersPage';

interface ServerRowProps {
    server: MediaServerItem;
    onToggleEnabled: (id: string, enabled: boolean) => void;
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
}

function ServerRow({ server, onToggleEnabled, onEdit, onDelete }: ServerRowProps) {
    const variant = mediaServerStatusVariants[server.status];
    const statusLabel = mediaServerStatusLabels[server.status];

    return (
        <div
            className={`media-servers-page__item${server.enabled ? '' : ' media-servers-page__item--disabled'}`}
            role="listitem"
            data-testid="media-servers-page__item"
            data-server-id={server.id}
            data-status={variant}
            data-enabled={server.enabled}
        >
            <div className="media-servers-page__item-main">
                <div className="media-servers-page__item-info">
                    <div className="media-servers-page__item-header">
                        <span className="media-servers-page__item-name">{server.name}</span>
                        <Badge variant={variant as Parameters<typeof Badge>[0]['variant']} size="sm" dot>
                            {statusLabel}
                        </Badge>
                    </div>
                    <div className="media-servers-page__item-meta">
                        <span className="media-servers-page__item-provider">{server.provider}</span>
                        <span className="media-servers-page__item-checked">
                            <Clock weight="regular" aria-hidden="true" />
                            Checked {formatRelativeTime(server.lastCheckedAt)}
                        </span>
                    </div>
                </div>
                <div className="media-servers-page__item-actions card-actions">
                    <div className="media-servers-page__item-toggle" title={server.enabled ? 'Disable' : 'Enable'}>
                        <Power weight="bold" className="media-servers-page__toggle-icon" aria-hidden="true" />
                        <Switch
                            checked={server.enabled}
                            onCheckedChange={(checked) => onToggleEnabled(server.id, checked)}
                            aria-label={`${server.enabled ? 'Disable' : 'Enable'} ${server.name}`}
                            data-action="toggle-enabled"
                            data-target={server.id}
                        />
                    </div>
                    <Tooltip content="Edit">
                        <button
                            type="button"
                            className="media-servers-page__action-btn card-actions__btn"
                            data-testid="media-servers-page__action-btn"
                            onClick={() => onEdit(server.id)}
                            aria-label={`Edit ${server.name}`}
                            data-action="edit"
                            data-target={server.id}
                        >
                            <PencilSimple weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip content="Delete">
                        <button
                            type="button"
                            className="media-servers-page__action-btn media-servers-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="media-servers-page__action-btn"
                            onClick={() => onDelete(server.id)}
                            aria-label={`Delete ${server.name}`}
                            data-action="delete"
                            data-target={server.id}
                        >
                            <Trash weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}

export default MediaServersPage;
