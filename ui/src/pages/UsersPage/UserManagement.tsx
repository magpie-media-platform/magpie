import { useState, useCallback, useMemo, useRef } from 'react';
import {
    UsersIcon,
    TrashIcon,
    PencilSimpleIcon,
    ClockIcon,
    PlusIcon,
    ShieldCheckIcon,
    WarningIcon,
    LockKeyOpenIcon,
} from '@phosphor-icons/react';

import { QuickActionsButton } from '@/components/ui/QuickActionsButton';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Switch';
import { useToast } from '@/components/ui/Toast';
import { useAuth, RoleName } from '@/auth';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToggle } from '@/hooks/useToggle';
import { formatRelativeTime } from '@/lib/formatTime';
import { parseUtcInstant } from '@/lib/dateParsing';
import { getErrorMessage } from '@/lib/errors';
import { useUsers, type UserQueryParams } from '@/hooks/useUsers';
import type { UserItem } from '@/types';
import { roleVariants } from '@/types';

import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { Pager } from '@/components/ui/Pager';
import { UserForm, type UserFormValues } from './UserForm';

const SYSTEM_PROVIDER_TYPE = 'system';

function isSystemUser(user: UserItem): boolean {
    return user.providerType === SYSTEM_PROVIDER_TYPE;
}

function isLocalProviderType(providerType: string): boolean {
    return providerType === 'local' || providerType === SYSTEM_PROVIDER_TYPE;
}

const SYSTEM_ACCOUNT_TOOLTIP = 'The system account cannot be modified';

function toUserFormValues(user: UserItem): UserFormValues {
    return {
        id: user.id,
        username: user.username,
        role: user.role,
        enabled: user.enabled,
        providerType: user.providerType,
        externalId: user.externalId ?? undefined,
        email: user.email ?? undefined,
    };
}

interface UserManagementProps {
    queryParams?: UserQueryParams;
    page?: number;
    pageCount?: number;
    onPageChange?: (page: number) => void;
}

export function UserManagement({ queryParams, page = 1, pageCount = 1, onPageChange }: UserManagementProps = {}) {
    const { user: authUser } = useAuth();
    const authUserId = authUser?.id;
    const [userFormOpen, , openUserForm, closeUserForm] = useToggle();
    const [editingUser, setEditingUser] = useState<UserFormValues | null>(null);
    const returnFocusRef = useRef<HTMLElement | null>(null);
    const [deletingUser, setDeletingUser] = useState<UserItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();

    const {
        users,
        isLoading: isLoadingUsers,
        isCreating,
        isCreatingExternal,
        isUpdating,
        createUser,
        createExternalUser,
        updateUser,
        toggleUserEnabled,
        deleteUser,
        unlockUser,
        isUnlocking,
    } = useUsers(queryParams);
    const currentEditingUser = useMemo(() => {
        if (!editingUser?.id) {
            return editingUser;
        }
        const user = users.find((candidate) => candidate.id === editingUser.id);
        return user ? toUserFormValues(user) : editingUser;
    }, [editingUser, users]);

    const isSubmitting = isCreating || isCreatingExternal || isUpdating;

    const handleAddUser = useCallback(() => {
        returnFocusRef.current = document.activeElement as HTMLElement;
        setEditingUser(null);
        openUserForm();
    }, [openUserForm]);

    const handleEditUser = useCallback(
        (id: string) => {
            const user = users.find((u) => u.id === id);
            if (user) {
                returnFocusRef.current = document.activeElement as HTMLElement;
                setEditingUser(toUserFormValues(user));
                openUserForm();
            }
        },
        [users, openUserForm],
    );

    const handleDeleteUser = useCallback(
        (id: string) => {
            const user = users.find((u) => u.id === id);
            if (!user) {
                return;
            }

            if (authUserId === id) {
                toast({
                    title: 'Cannot delete yourself',
                    description: 'You cannot delete your own account.',
                    variant: 'error',
                });
                return;
            }

            if (isSystemUser(user)) {
                toast({
                    title: 'Cannot delete the system account',
                    description: SYSTEM_ACCOUNT_TOOLTIP,
                    variant: 'error',
                });
                return;
            }

            setDeletingUser(user);
        },
        [users, authUserId, toast],
    );

    const handleConfirmDeleteUser = useCallback(async () => {
        if (!deletingUser) {
            return;
        }
        setIsDeleting(true);

        try {
            await deleteUser(deletingUser.id);
            toast({
                title: 'User deleted',
                description: `Magpie deleted ${deletingUser.username}.`,
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Failed to delete user',
                description: 'An error occurred while deleting the user.',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingUser(null);
        }
    }, [deletingUser, deleteUser, toast]);

    const handleCancelDeleteUser = useCallback(() => {
        setDeletingUser(null);
    }, []);

    const handleUserFormSubmit = useCallback(
        async (values: UserFormValues) => {
            try {
                if (values.id) {
                    await updateUser(values.id, {
                        role: values.role,
                        enabled: values.enabled,
                    });
                    toast({
                        title: 'User updated',
                        description: `Magpie updated ${values.username}.`,
                        variant: 'success',
                    });
                } else if (values.providerType && values.providerType !== 'local') {
                    await createExternalUser({
                        username: values.username,
                        providerType: values.providerType,
                        externalId: values.externalId ?? '',
                        role: values.role,
                        email: values.email ?? null,
                    });
                    toast({
                        title: 'External user created',
                        description: `Magpie created ${values.username}.`,
                        variant: 'success',
                    });
                } else {
                    await createUser({
                        username: values.username,
                        password: values.password ?? '',
                        role: values.role,
                        enabled: values.enabled,
                    });
                    toast({
                        title: 'User created',
                        description: `Magpie created ${values.username}.`,
                        variant: 'success',
                    });
                }
                closeUserForm();
            } catch (error) {
                toast({
                    title: values.id ? 'Failed to update user' : 'Failed to create user',
                    description: getErrorMessage(error),
                    variant: 'error',
                });
                throw error;
            }
        },
        [createUser, createExternalUser, updateUser, toast, closeUserForm],
    );

    const handleToggleUserEnabled = useCallback(
        async (id: string, enabled: boolean) => {
            try {
                await toggleUserEnabled(id, enabled);
                toast({
                    title: enabled ? 'User enabled' : 'User disabled',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Failed to update user',
                    description: 'An error occurred while updating the user.',
                    variant: 'error',
                });
            }
        },
        [toggleUserEnabled, toast],
    );

    const handleUnlockUser = useCallback(
        async (id: string) => {
            const user = users.find((candidate) => candidate.id === id);
            try {
                await unlockUser(id);
                toast({
                    title: 'User unlocked',
                    description: `${user?.username ?? 'The user'} can sign in again.`,
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Failed to unlock user',
                    description: 'An error occurred while clearing the lockout.',
                    variant: 'error',
                });
            }
        },
        [users, unlockUser, toast],
    );

    return (
        <>
            <div className="users-page__user-actions" data-component="user-toolbar">
                <Button variant="primary" size="sm" data-action="add-user" onClick={handleAddUser}>
                    <PlusIcon weight="bold" aria-hidden="true" />
                    <span className="users-page__add-label">Add User</span>
                </Button>
            </div>

            <div className="users-page__list" role="list" aria-label="Users">
                {isLoadingUsers ? (
                    <EmptyState title="Loading..." size="sm" />
                ) : users.length === 0 ? (
                    <EmptyState
                        icon={<UsersIcon weight="duotone" />}
                        title="No users found"
                        description="Add a user to get started"
                    />
                ) : (
                    users.map((user) => (
                        <UserRow
                            key={user.id}
                            user={user}
                            isCurrentUser={authUserId === user.id}
                            isSystem={isSystemUser(user)}
                            onToggleEnabled={handleToggleUserEnabled}
                            onEdit={handleEditUser}
                            onDelete={handleDeleteUser}
                            onUnlock={handleUnlockUser}
                            isUnlocking={isUnlocking}
                        />
                    ))
                )}
            </div>

            {onPageChange && pageCount > 1 && (
                <Pager
                    className="users-page__pager"
                    page={page}
                    pageCount={pageCount}
                    onFirst={() => onPageChange(1)}
                    onPrevious={() => onPageChange(page - 1)}
                    onNext={() => onPageChange(page + 1)}
                    onLast={() => onPageChange(pageCount)}
                    disabled={isLoadingUsers}
                />
            )}

            <QuickActionsButton
                actions={[
                    { id: 'add-user', label: 'Add User', icon: <PlusIcon weight="bold" />, onClick: handleAddUser },
                ]}
                title="Add User"
            />

            <UserForm
                open={userFormOpen}
                onOpenChange={(open) => {
                    if (!open) closeUserForm();
                }}
                initialValues={currentEditingUser}
                onSubmit={handleUserFormSubmit}
                isSubmitting={isSubmitting}
                returnFocusRef={returnFocusRef}
            />

            <DeleteConfirmDialog
                open={!!deletingUser}
                onConfirm={handleConfirmDeleteUser}
                onCancel={handleCancelDeleteUser}
                itemName={deletingUser?.username ?? ''}
                itemType="User"
                isDeleting={isDeleting}
            />
        </>
    );
}

UserManagement.displayName = 'UserManagement';

interface UserRowProps {
    user: UserItem;
    isCurrentUser: boolean;
    isSystem: boolean;
    onToggleEnabled: (id: string, enabled: boolean) => void;
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
    onUnlock: (id: string) => void;
    isUnlocking: boolean;
}

function UserRow({
    user,
    isCurrentUser,
    isSystem,
    onToggleEnabled,
    onEdit,
    onDelete,
    onUnlock,
    isUnlocking,
}: UserRowProps) {
    const variant = roleVariants[user.role] ?? 'default';
    const roleLabelText = RoleName[user.role] ?? 'Unknown';
    const lockedUntil = user.lockedUntil ? parseUtcInstant(user.lockedUntil) : null;
    const isLocked = lockedUntil !== null && lockedUntil > new Date();

    const isProtected = isCurrentUser || isSystem;

    return (
        <div
            className={`users-page__item${user.enabled ? '' : ' users-page__item--disabled'}`}
            role="listitem"
            data-testid="users-page__item"
            data-user-id={user.id}
            data-role={variant}
            data-enabled={user.enabled}
            data-locked={isLocked}
        >
            <div className="users-page__item-main">
                <div className="users-page__item-info">
                    <div className="users-page__item-header">
                        <span className="users-page__item-name">
                            {user.username}
                            {isCurrentUser && (
                                <span
                                    className="users-page__current-user-badge"
                                    data-testid="users-page__current-user-badge"
                                    title="This is you"
                                >
                                    <ShieldCheckIcon weight="fill" aria-hidden="true" />
                                </span>
                            )}
                        </span>
                        <Badge variant={variant} size="sm">
                            {roleLabelText}
                        </Badge>
                        {!isLocalProviderType(user.providerType) && (
                            <Badge
                                variant="info"
                                size="sm"
                                badgeStyle="outline"
                                data-testid="users-page__provider-badge"
                            >
                                {user.providerType}
                            </Badge>
                        )}
                        {isLocked && (
                            <Badge variant="unhealthy" size="sm">
                                <WarningIcon weight="bold" aria-hidden="true" />
                                Locked
                            </Badge>
                        )}
                    </div>
                    <div className="users-page__item-meta">
                        {user.email && <span className="users-page__item-email">{user.email}</span>}
                        <span className="users-page__item-login">
                            <ClockIcon weight="regular" aria-hidden="true" />
                            Login {formatRelativeTime(user.lastLoginAt)}
                        </span>
                        {isLocked && (
                            <span className="users-page__item-expires" data-testid="users-page__lock-expiry">
                                Locked until{' '}
                                {lockedUntil.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' })}
                                {user.failedLoginAttempts > 0 && ` after ${user.failedLoginAttempts} failed sign-ins`}
                            </span>
                        )}
                    </div>
                </div>
                <div className="users-page__item-actions card-actions">
                    {isLocked && (
                        <Tooltip
                            content={
                                isSystem
                                    ? SYSTEM_ACCOUNT_TOOLTIP
                                    : isCurrentUser
                                      ? 'Cannot modify your own account'
                                      : 'Clear the lockout'
                            }
                        >
                            <button
                                type="button"
                                className="users-page__action-btn card-actions__btn"
                                data-testid="users-page__action-btn"
                                onClick={() => onUnlock(user.id)}
                                aria-label={`Unlock ${user.username}`}
                                data-action="unlock"
                                data-target={user.id}
                                disabled={isProtected || isUnlocking}
                            >
                                <LockKeyOpenIcon weight="bold" />
                            </button>
                        </Tooltip>
                    )}
                    <Tooltip
                        content={
                            isSystem
                                ? SYSTEM_ACCOUNT_TOOLTIP
                                : isCurrentUser
                                  ? 'Cannot modify your own account'
                                  : user.enabled
                                    ? 'Disable user'
                                    : 'Enable user'
                        }
                    >
                        <div className="users-page__item-toggle">
                            <span className="users-page__toggle-label">{user.enabled ? 'Enabled' : 'Disabled'}</span>
                            <Switch
                                checked={user.enabled}
                                onCheckedChange={(checked) => onToggleEnabled(user.id, checked)}
                                aria-label={`${user.enabled ? 'Disable' : 'Enable'} ${user.username}`}
                                data-action="toggle-enabled"
                                data-target={user.id}
                                disabled={isProtected}
                            />
                        </div>
                    </Tooltip>
                    <Tooltip
                        content={
                            isSystem
                                ? SYSTEM_ACCOUNT_TOOLTIP
                                : isCurrentUser
                                  ? 'Cannot modify your own account'
                                  : 'Edit'
                        }
                    >
                        <button
                            type="button"
                            className="users-page__action-btn card-actions__btn"
                            data-testid="users-page__action-btn"
                            onClick={() => onEdit(user.id)}
                            aria-label={`Edit ${user.username}`}
                            data-action="edit"
                            data-target={user.id}
                            disabled={isProtected}
                        >
                            <PencilSimpleIcon weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip
                        content={
                            isSystem
                                ? SYSTEM_ACCOUNT_TOOLTIP
                                : isCurrentUser
                                  ? 'Cannot modify your own account'
                                  : 'Delete'
                        }
                    >
                        <button
                            type="button"
                            className="users-page__action-btn users-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="users-page__action-btn"
                            onClick={() => onDelete(user.id)}
                            aria-label={`Delete ${user.username}`}
                            data-action="delete"
                            data-target={user.id}
                            disabled={isProtected}
                        >
                            <TrashIcon weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}
