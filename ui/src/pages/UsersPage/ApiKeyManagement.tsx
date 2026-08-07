import { useState, useMemo, useCallback, useEffect, useRef, type SubmitEvent } from 'react';
import { KeyIcon, TrashIcon, ClockIcon, PlusIcon, CircleNotchIcon, CopyIcon, CheckIcon } from '@phosphor-icons/react';

import { QuickActionsButton } from '@/components/ui/QuickActionsButton';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Switch';
import { OwnershipToggle } from '@/components/ui/OwnershipToggle';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogFooter,
    DialogTitle,
    DialogDescription,
} from '@/components/ui/Dialog';
import { useToast } from '@/components/ui/Toast';
import { Tooltip } from '@/components/ui/Tooltip';
import { useAuth, Role } from '@/auth';
import { useApiKeys, useAllApiKeys } from '@/hooks/useApiKeys';
import { useUsers } from '@/hooks/useUsers';
import { formatRelativeTime } from '@/lib/formatTime';
import { formatShortDate } from '@/lib/dateFormatting';
import { getErrorMessage } from '@/lib/errors';
import type { ApiKeyItem } from '@/types';

import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { EmptyState } from '@/components/ui/EmptyState';

export type ApiKeyScope = 'mine' | 'all';

function isExpired(dateStr: string | null): boolean {
    if (!dateStr) {
        return false;
    }
    return new Date(dateStr) < new Date();
}

interface ApiKeyManagementProps {
    scope: ApiKeyScope;
    onScopeChange: (scope: ApiKeyScope) => void;
}

export function ApiKeyManagement({ scope, onScopeChange }: ApiKeyManagementProps) {
    const { user: authUser, hasRole } = useAuth();
    const authUserId = authUser?.id;
    const canViewAll = hasRole(Role.Admin);
    const { users } = useUsers();
    const [deletingApiKey, setDeletingApiKey] = useState<ApiKeyItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();

    const activeScope: ApiKeyScope = canViewAll ? scope : 'mine';
    const isViewingOwnKeys = activeScope === 'mine';

    const {
        apiKeys: myApiKeys,
        isLoading: isLoadingMyKeys,
        createApiKey,
        isCreating: isCreatingKey,
        deleteApiKey: deleteMyApiKey,
        isDeleting: isDeletingMyKey,
        toggleEnabled: toggleMyKeyEnabled,
    } = useApiKeys();

    const {
        apiKeys: allApiKeys,
        isLoading: isLoadingAllKeys,
        deleteApiKey: deleteAnyApiKey,
        isDeleting: isDeletingAnyKey,
    } = useAllApiKeys(canViewAll && !isViewingOwnKeys);

    const apiKeys: ApiKeyItem[] = isViewingOwnKeys ? myApiKeys : allApiKeys;
    const isLoadingKeys = isViewingOwnKeys ? isLoadingMyKeys : isLoadingAllKeys;

    const usernamesById = useMemo(() => {
        const map = new Map<string, string>();
        for (const u of users) {
            map.set(u.id, u.username);
        }
        return map;
    }, [users]);

    const handleToggleApiKeyEnabled = useCallback(
        async (id: string, enabled: boolean) => {
            try {
                await toggleMyKeyEnabled(id, enabled);
                toast({
                    title: enabled ? 'API key enabled' : 'API key disabled',
                    variant: 'success',
                });
            } catch (error) {
                toast({
                    title: 'Failed to update API key',
                    description: getErrorMessage(error, 'An error occurred while updating the key.'),
                    variant: 'error',
                });
            }
        },
        [toggleMyKeyEnabled, toast],
    );

    const [isCreateDialogOpen, setIsCreateDialogOpen] = useState(false);
    const [plaintextKey, setPlaintextKey] = useState<string | null>(null);

    const openCreateDialog = useCallback(() => {
        setIsCreateDialogOpen(true);
    }, []);

    const handleCreateSubmit = useCallback(
        async (name: string, expiresAt: string | null) => {
            try {
                const result = await createApiKey({ name, expiresAt });
                setIsCreateDialogOpen(false);
                setPlaintextKey(result.plaintextKey);
            } catch (error) {
                toast({
                    title: 'Failed to create API key',
                    description: getErrorMessage(error),
                    variant: 'error',
                });
            }
        },
        [createApiKey, toast],
    );

    const handleDeleteApiKey = useCallback(
        (id: string) => {
            const key = apiKeys.find((k) => k.id === id);
            if (!key) {
                return;
            }
            setDeletingApiKey(key);
        },
        [apiKeys],
    );

    const handleConfirmDeleteApiKey = useCallback(async () => {
        if (!deletingApiKey) {
            return;
        }
        setIsDeleting(true);

        try {
            if (isViewingOwnKeys || deletingApiKey.userId === authUserId) {
                await deleteMyApiKey(deletingApiKey.id);
            } else {
                await deleteAnyApiKey({ userId: deletingApiKey.userId, id: deletingApiKey.id });
            }
            toast({
                title: 'API key deleted',
                description: `Magpie deleted ${deletingApiKey.name}.`,
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Failed to delete API key',
                description: 'An error occurred while deleting the key.',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingApiKey(null);
        }
    }, [deletingApiKey, isViewingOwnKeys, authUserId, deleteMyApiKey, deleteAnyApiKey, toast]);

    const handleCancelDeleteApiKey = useCallback(() => {
        setDeletingApiKey(null);
    }, []);

    return (
        <>
            <div className="list-toolbar__filters" data-component="api-key-toolbar">
                <div className="list-toolbar__filters-controls">
                    {canViewAll && (
                        <OwnershipToggle
                            value={activeScope}
                            onValueChange={onScopeChange}
                            aria-label="Filter API keys by ownership"
                            data-filter="scope"
                        />
                    )}
                </div>
                <div className="list-toolbar__filters-action">
                    <Button
                        variant="primary"
                        size="sm"
                        data-action="add-api-key"
                        onClick={openCreateDialog}
                        disabled={isCreatingKey}
                    >
                        <PlusIcon weight="bold" aria-hidden="true" />
                        <span className="users-page__add-label">Create Key</span>
                    </Button>
                </div>
            </div>

            <div className="users-page__list" role="list" aria-label="API Keys">
                {isLoadingKeys ? (
                    <EmptyState title="Loading..." size="sm" />
                ) : apiKeys.length === 0 ? (
                    <EmptyState
                        icon={<KeyIcon weight="duotone" />}
                        title="No API keys"
                        description={
                            isViewingOwnKeys ? 'An API key lets another application use Magpie.' : 'No API keys exist.'
                        }
                    />
                ) : (
                    apiKeys.map((key) => (
                        <ApiKeyRow
                            key={key.id}
                            apiKey={key}
                            ownerName={isViewingOwnKeys ? null : (usernamesById.get(key.userId) ?? null)}
                            isDeleting={isViewingOwnKeys ? isDeletingMyKey : isDeletingAnyKey}
                            isViewingOwnKeys={isViewingOwnKeys || key.userId === authUserId}
                            onToggleEnabled={handleToggleApiKeyEnabled}
                            onDelete={handleDeleteApiKey}
                        />
                    ))
                )}
            </div>

            {isViewingOwnKeys && (
                <QuickActionsButton
                    actions={[
                        {
                            id: 'add-api-key',
                            label: 'Add API Key',
                            icon: <PlusIcon weight="bold" />,
                            onClick: openCreateDialog,
                        },
                    ]}
                    title="Add API Key"
                />
            )}

            <DeleteConfirmDialog
                open={!!deletingApiKey}
                onConfirm={handleConfirmDeleteApiKey}
                onCancel={handleCancelDeleteApiKey}
                itemName={deletingApiKey?.name ?? ''}
                itemType="API Key"
                isDeleting={isDeleting}
            />

            <CreateApiKeyDialog
                open={isCreateDialogOpen}
                onOpenChange={setIsCreateDialogOpen}
                onSubmit={handleCreateSubmit}
                isCreating={isCreatingKey}
            />

            <ApiKeyRevealDialog plaintextKey={plaintextKey} onDone={() => setPlaintextKey(null)} />
        </>
    );
}

ApiKeyManagement.displayName = 'ApiKeyManagement';

interface CreateApiKeyDialogProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    onSubmit: (name: string, expiresAt: string | null) => void;
    isCreating: boolean;
}

function CreateApiKeyDialog({ open, onOpenChange, onSubmit, isCreating }: CreateApiKeyDialogProps) {
    const [name, setName] = useState('');
    const [expiresAt, setExpiresAt] = useState('');
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (open) {
            setName('');
            setExpiresAt('');
            setError(null);
        }
    }, [open]);

    const handleSubmit = useCallback(
        (event: SubmitEvent<HTMLFormElement>) => {
            event.preventDefault();
            const trimmed = name.trim();
            if (!trimmed) {
                setError('Name is required.');
                return;
            }
            const isoExpiry = expiresAt ? new Date(`${expiresAt}T23:59:59`).toISOString() : null;
            onSubmit(trimmed, isoExpiry);
        },
        [name, expiresAt, onSubmit],
    );

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="api-key-create-dialog" data-component="create-api-key-dialog">
                <DialogHeader>
                    <DialogTitle>Create API key</DialogTitle>
                    <DialogDescription>
                        Name the key so it is identifiable later. The full key is shown only once after creation.
                    </DialogDescription>
                </DialogHeader>

                <form onSubmit={handleSubmit} className="api-key-create-dialog__form">
                    <div className="api-key-create-dialog__field">
                        <Label htmlFor="api-key-name" error={!!error} required>
                            Name
                        </Label>
                        <Input
                            id="api-key-name"
                            value={name}
                            onChange={(e) => {
                                setName(e.target.value);
                                if (error) {
                                    setError(null);
                                }
                            }}
                            placeholder="Home automation"
                            error={!!error}
                            errorMessage={error || undefined}
                            disabled={isCreating}
                            autoFocus
                            data-field="api-key-name"
                        />
                    </div>

                    <div className="api-key-create-dialog__field">
                        <Label htmlFor="api-key-expires">Expires (optional)</Label>
                        <Input
                            id="api-key-expires"
                            type="date"
                            value={expiresAt}
                            onChange={(e) => setExpiresAt(e.target.value)}
                            disabled={isCreating}
                            data-field="api-key-expires"
                        />
                    </div>

                    <DialogFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            onClick={() => onOpenChange(false)}
                            disabled={isCreating}
                            data-action="cancel-create-key"
                        >
                            Cancel
                        </Button>
                        <Button type="submit" variant="primary" loading={isCreating} data-action="submit-create-key">
                            <PlusIcon weight="bold" aria-hidden="true" />
                            Create Key
                        </Button>
                    </DialogFooter>
                </form>
            </DialogContent>
        </Dialog>
    );
}

interface ApiKeyRevealDialogProps {
    plaintextKey: string | null;
    onDone: () => void;
}

function ApiKeyRevealDialog({ plaintextKey, onDone }: ApiKeyRevealDialogProps) {
    const { toast } = useToast();
    const [copied, setCopied] = useState(false);
    const copyTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    useEffect(() => {
        if (plaintextKey) {
            setCopied(false);
        }
    }, [plaintextKey]);

    useEffect(() => {
        return () => {
            if (copyTimeoutRef.current) {
                clearTimeout(copyTimeoutRef.current);
            }
        };
    }, []);

    const handleCopy = useCallback(async () => {
        if (!plaintextKey) {
            return;
        }
        try {
            await navigator.clipboard.writeText(plaintextKey);
            setCopied(true);
            if (copyTimeoutRef.current) {
                clearTimeout(copyTimeoutRef.current);
            }
            copyTimeoutRef.current = setTimeout(() => setCopied(false), 2000);
        } catch {
            toast({
                title: 'Copy failed',
                description: 'Select the key and copy it manually.',
                variant: 'error',
            });
        }
    }, [plaintextKey, toast]);

    return (
        <Dialog open={!!plaintextKey} onOpenChange={(open) => !open && onDone()}>
            <DialogContent
                className="api-key-reveal-dialog"
                data-component="reveal-api-key-dialog"
                hideCloseButton
                onEscapeKeyDown={(e) => e.preventDefault()}
                onPointerDownOutside={(e) => e.preventDefault()}
                onInteractOutside={(e) => e.preventDefault()}
            >
                <DialogHeader>
                    <DialogTitle>API key created</DialogTitle>
                    <DialogDescription>
                        For security, the key is shown only once and cannot be retrieved later.
                    </DialogDescription>
                </DialogHeader>

                <div className="api-key-reveal-dialog__key-row">
                    <code className="api-key-reveal-dialog__key" data-testid="reveal-api-key-value">
                        {plaintextKey}
                    </code>
                    <Tooltip content={copied ? 'Copied' : 'Copy to clipboard'}>
                        <button
                            type="button"
                            className="api-key-reveal-dialog__copy"
                            onClick={handleCopy}
                            data-action="copy-api-key"
                            aria-label="Copy API key to clipboard"
                        >
                            {copied ? (
                                <CheckIcon weight="bold" aria-hidden="true" />
                            ) : (
                                <CopyIcon weight="bold" aria-hidden="true" />
                            )}
                        </button>
                    </Tooltip>
                </div>

                <DialogFooter>
                    <Button type="button" variant="primary" onClick={onDone} data-action="dismiss-reveal-key">
                        Close
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

interface ApiKeyRowProps {
    apiKey: ApiKeyItem;
    ownerName: string | null;
    isDeleting: boolean;
    isViewingOwnKeys: boolean;
    onToggleEnabled: (id: string, enabled: boolean) => void;
    onDelete: (id: string) => void;
}

function ApiKeyRow({ apiKey, ownerName, isDeleting, isViewingOwnKeys, onToggleEnabled, onDelete }: ApiKeyRowProps) {
    const expired = isExpired(apiKey.expiresAt);

    return (
        <div
            className={`users-page__item${apiKey.enabled ? '' : ' users-page__item--disabled'}${expired ? ' users-page__item--expired' : ''}`}
            role="listitem"
            data-testid="users-page__item"
            data-api-key-id={apiKey.id}
            data-enabled={apiKey.enabled}
            data-expired={expired || undefined}
        >
            <div className="users-page__item-main">
                <div className="users-page__item-info">
                    <div className="users-page__item-header">
                        <span className="users-page__item-name">{apiKey.name}</span>
                        {ownerName && (
                            <Badge variant="info" size="sm" badgeStyle="outline" data-testid="api-key-owner-badge">
                                {ownerName}
                            </Badge>
                        )}
                        <Badge variant={apiKey.enabled && !expired ? 'healthy' : 'unknown'} size="sm" dot>
                            {expired ? 'Expired' : apiKey.enabled ? 'Active' : 'Disabled'}
                        </Badge>
                    </div>
                    <div className="users-page__item-meta">
                        <code className="users-page__item-prefix">{apiKey.keyPrefix}...</code>
                        <span className="users-page__item-login">
                            <ClockIcon weight="regular" aria-hidden="true" />
                            Used {formatRelativeTime(apiKey.lastUsedAt)}
                        </span>
                        {apiKey.expiresAt && (
                            <span
                                className={`users-page__item-expires${expired ? ' users-page__item-expires--expired' : ''}`}
                            >
                                {expired ? 'Expired' : 'Expires'} {formatShortDate(apiKey.expiresAt)}
                            </span>
                        )}
                    </div>
                </div>
                <div className="users-page__item-actions card-actions">
                    <Tooltip
                        content={
                            !isViewingOwnKeys
                                ? 'Only the key owner can toggle this'
                                : expired
                                  ? 'Key has expired and cannot be enabled'
                                  : apiKey.enabled
                                    ? 'Disable key'
                                    : 'Enable key'
                        }
                    >
                        <div className="users-page__item-toggle">
                            <span className="users-page__toggle-label">{apiKey.enabled ? 'Enabled' : 'Disabled'}</span>
                            <Switch
                                checked={apiKey.enabled}
                                onCheckedChange={(checked) => onToggleEnabled(apiKey.id, checked)}
                                aria-label={
                                    expired
                                        ? `${apiKey.name} has expired and cannot be enabled`
                                        : `${apiKey.enabled ? 'Disable' : 'Enable'} ${apiKey.name}`
                                }
                                data-action="toggle-enabled"
                                data-target={apiKey.id}
                                disabled={expired || !isViewingOwnKeys}
                            />
                        </div>
                    </Tooltip>
                    <Tooltip content={isDeleting ? 'Deleting...' : 'Delete'}>
                        <button
                            type="button"
                            className={`users-page__action-btn users-page__action-btn--danger card-actions__btn card-actions__btn--danger${isDeleting ? ' card-actions__btn--loading-danger' : ''}`}
                            data-testid="users-page__action-btn"
                            onClick={() => onDelete(apiKey.id)}
                            aria-label={isDeleting ? `Deleting ${apiKey.name}` : `Delete ${apiKey.name}`}
                            data-action="delete"
                            data-target={apiKey.id}
                            disabled={isDeleting}
                        >
                            {isDeleting ? (
                                <CircleNotchIcon weight="bold" className="card-actions__spinner" aria-hidden="true" />
                            ) : (
                                <TrashIcon weight="bold" />
                            )}
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}
