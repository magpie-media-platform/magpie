import { useState, useMemo, useCallback } from 'react';
import { Key, Trash, Clock, Plus, CircleNotch, CaretDown, User } from '@phosphor-icons/react';

import { QuickActionsButton } from '@/components/ui/QuickActionsButton';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Switch';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { Tooltip } from '@/components/ui/Tooltip';
import { useAuth, RoleName } from '@/auth';
import { useApiKeys, useUserApiKeys } from '@/hooks/useApiKeys';
import { useUsers } from '@/hooks/useUsers';
import { formatRelativeTime } from '@/lib/formatTime';
import { formatShortDate } from '@/lib/dateFormatting';
import { getErrorMessage } from '@/lib/errors';
import type { ApiKeyItem } from '@/types';
import { roleVariants } from '@/types';

import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { EmptyState } from '@/components/ui/EmptyState';

function isExpired(dateStr: string | null): boolean {
    if (!dateStr) {
        return false;
    }
    try {
        return new Date(dateStr) < new Date();
    } catch {
        return false;
    }
}

export function ApiKeyManagement() {
    const { user: authUser } = useAuth();
    const authUserId = authUser?.id;
    const { users } = useUsers();
    const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
    const [deletingApiKey, setDeletingApiKey] = useState<ApiKeyItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();

    const {
        apiKeys: myApiKeys,
        isLoading: isLoadingMyKeys,
        createApiKey,
        isCreating: isCreatingKey,
        deleteApiKey: deleteMyApiKey,
        isDeleting: isDeletingMyKey,
        toggleEnabled: toggleMyKeyEnabled,
    } = useApiKeys();

    const isViewingOwnKeys = selectedUserId === null || selectedUserId === authUserId;
    const {
        apiKeys: otherUserApiKeys,
        isLoading: isLoadingOtherKeys,
        deleteApiKey: deleteOtherUserApiKey,
        isDeleting: isDeletingOtherKey,
    } = useUserApiKeys(isViewingOwnKeys ? null : selectedUserId);

    const apiKeys: ApiKeyItem[] = isViewingOwnKeys ? myApiKeys : otherUserApiKeys;
    const isLoadingKeys = isViewingOwnKeys ? isLoadingMyKeys : isLoadingOtherKeys;

    const selectedUser = useMemo(() => {
        const targetUserId = selectedUserId ?? authUserId ?? '';
        return users.find((u) => u.id === targetUserId) ?? null;
    }, [users, selectedUserId, authUserId]);

    const handleUserSelect = useCallback(
        (userId: string) => {
            setSelectedUserId(userId === authUserId ? null : userId);
        },
        [authUserId],
    );

    const handleToggleApiKeyEnabled = useCallback(
        async (id: string, enabled: boolean) => {
            try {
                await toggleMyKeyEnabled(id, enabled);
                toast({
                    title: enabled ? 'API key enabled' : 'API key disabled',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Failed to update API key',
                    description: 'An error occurred while updating the key.',
                    variant: 'error',
                });
            }
        },
        [toggleMyKeyEnabled, toast],
    );

    const handleAddApiKey = useCallback(async () => {
        try {
            const result = await createApiKey({ name: `API Key ${apiKeys.length + 1}` });
            toast({
                title: 'API key created',
                description: `Key prefix: ${result.keyPrefix}... Copy the full key now -- it will not be shown again.`,
                variant: 'success',
            });
        } catch (error) {
            toast({
                title: 'Failed to create API key',
                description: getErrorMessage(error),
                variant: 'error',
            });
        }
    }, [createApiKey, apiKeys.length, toast]);

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
            if (isViewingOwnKeys) {
                await deleteMyApiKey(deletingApiKey.id);
            } else if (selectedUserId) {
                await deleteOtherUserApiKey({ userId: selectedUserId, id: deletingApiKey.id });
            }
            toast({
                title: 'API key deleted',
                description: `${deletingApiKey.name} has been deleted.`,
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
    }, [deletingApiKey, isViewingOwnKeys, selectedUserId, deleteMyApiKey, deleteOtherUserApiKey, toast]);

    const handleCancelDeleteApiKey = useCallback(() => {
        setDeletingApiKey(null);
    }, []);

    return (
        <>
            <div className="users-page__user-selector" data-component="user-selector">
                <label className="users-page__user-selector-label">
                    <User weight="bold" aria-hidden="true" />
                    Viewing API keys for:
                </label>
                <Select value={selectedUserId ?? authUserId ?? ''} onValueChange={handleUserSelect}>
                    <SelectTrigger
                        size="sm"
                        aria-label="Select user"
                        data-filter="user"
                        className="users-page__user-selector-trigger"
                    >
                        <span className="users-page__user-selector-value">
                            {selectedUser?.username ?? 'Select user'}
                            {selectedUser && (
                                <Badge variant={roleVariants[selectedUser.role]} size="sm">
                                    {RoleName[selectedUser.role]}
                                </Badge>
                            )}
                        </span>
                        <CaretDown weight="bold" aria-hidden="true" />
                    </SelectTrigger>
                    <SelectContent>
                        {users.map((u) => (
                            <SelectItem key={u.id} value={u.id}>
                                <span className="users-page__user-option">
                                    {u.username}
                                    {u.id === authUserId && <span className="users-page__user-option-you">(you)</span>}
                                </span>
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>
            </div>

            <div className="users-page__tab-header users-page__tab-header--api-keys">
                <p className="users-page__api-keys-description">
                    API keys allow programmatic access to Magpie.
                    {selectedUser && selectedUser.id !== authUserId
                        ? ` Managing keys for ${selectedUser.username}.`
                        : ' Keys are scoped to your user account.'}
                </p>
                {isViewingOwnKeys && (
                    <Button
                        variant="primary"
                        size="sm"
                        data-action="add-api-key"
                        onClick={handleAddApiKey}
                        disabled={isCreatingKey}
                    >
                        <Plus weight="bold" aria-hidden="true" />
                        <span className="users-page__add-label">Create Key</span>
                    </Button>
                )}
            </div>

            <div className="users-page__list" role="list" aria-label="API Keys">
                {isLoadingKeys ? (
                    <EmptyState title="Loading..." size="sm" />
                ) : apiKeys.length === 0 ? (
                    <EmptyState
                        icon={<Key weight="duotone" />}
                        title="No API keys"
                        description={
                            selectedUser && selectedUser.id !== authUserId
                                ? `${selectedUser.username} has no API keys`
                                : 'Create an API key to access Magpie programmatically'
                        }
                    />
                ) : (
                    apiKeys.map((key) => (
                        <ApiKeyRow
                            key={key.id}
                            apiKey={key}
                            isDeleting={isViewingOwnKeys ? isDeletingMyKey : isDeletingOtherKey}
                            isViewingOwnKeys={isViewingOwnKeys}
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
                            icon: <Plus weight="bold" />,
                            onClick: handleAddApiKey,
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
        </>
    );
}

ApiKeyManagement.displayName = 'ApiKeyManagement';

interface ApiKeyRowProps {
    apiKey: ApiKeyItem;
    isDeleting: boolean;
    isViewingOwnKeys: boolean;
    onToggleEnabled: (id: string, enabled: boolean) => void;
    onDelete: (id: string) => void;
}

function ApiKeyRow({ apiKey, isDeleting, isViewingOwnKeys, onToggleEnabled, onDelete }: ApiKeyRowProps) {
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
                        <Badge variant={apiKey.enabled && !expired ? 'healthy' : 'unknown'} size="sm" dot>
                            {expired ? 'Expired' : apiKey.enabled ? 'Active' : 'Disabled'}
                        </Badge>
                    </div>
                    <div className="users-page__item-meta">
                        <code className="users-page__item-prefix">{apiKey.keyPrefix}...</code>
                        <span className="users-page__item-login">
                            <Clock weight="regular" aria-hidden="true" />
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
                                <CircleNotch weight="bold" className="card-actions__spinner" aria-hidden="true" />
                            ) : (
                                <Trash weight="bold" />
                            )}
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}
