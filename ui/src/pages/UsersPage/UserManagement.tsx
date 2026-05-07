import { useState, useMemo, useCallback, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Users, Trash, PencilSimple, Clock, Plus, ShieldCheck, Warning } from '@phosphor-icons/react';

import { QuickActionsButton } from '@/components/ui/QuickActionsButton';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Switch';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { useAuth, Role, RoleName } from '@/auth';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToggle } from '@/hooks/useToggle';
import { formatRelativeTime } from '@/lib/formatTime';
import { useUsers } from '@/hooks/useUsers';
import type { UserItem } from '@/types';
import { roleVariants } from '@/types';

import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { UserForm, type UserFormValues } from './UserForm';

interface UserFilterState {
    role: string;
    enabled: string;
}

const ALL_VALUE = '__all__';

const roleOptions = [
    { value: 'all', label: 'All Roles' },
    { value: String(Role.Viewer), label: 'Viewer' },
    { value: String(Role.Requester), label: 'Requester' },
    { value: String(Role.Manager), label: 'Manager' },
    { value: String(Role.Admin), label: 'Admin' },
];

const enabledOptions = [
    { value: 'all', label: 'All Enabled' },
    { value: 'true', label: 'Enabled' },
    { value: 'false', label: 'Disabled' },
];

export function UserManagement() {
    const { user: authUser } = useAuth();
    const authUserId = authUser?.id;
    const [searchParams, setSearchParams] = useSearchParams();
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
        isUpdating,
        createUser,
        updateUser,
        toggleUserEnabled,
        deleteUser,
    } = useUsers();

    const filters: UserFilterState = useMemo(
        () => ({
            role: searchParams.get('role') || '',
            enabled: searchParams.get('enabled') || '',
        }),
        [searchParams],
    );

    const filteredUsers = useMemo(() => {
        let items = [...users];

        if (filters.role) {
            items = items.filter((u) => u.role === Number(filters.role));
        }

        if (filters.enabled) {
            items = items.filter((u) => u.enabled === (filters.enabled === 'true'));
        }

        return items;
    }, [users, filters]);

    const roleCounts = useMemo(
        () => ({
            admin: users.filter((u) => u.role === Role.Admin).length,
            manager: users.filter((u) => u.role === Role.Manager).length,
            requester: users.filter((u) => u.role === Role.Requester).length,
            viewer: users.filter((u) => u.role === Role.Viewer).length,
        }),
        [users],
    );

    const hasActiveFilters = filters.role !== '' || filters.enabled !== '';
    const isSubmitting = isCreating || isUpdating;

    const roleLabel = filters.role
        ? (roleOptions.find((o) => o.value === filters.role)?.label ?? 'Unknown')
        : 'All Roles';

    const enabledLabel = filters.enabled
        ? (enabledOptions.find((o) => o.value === filters.enabled)?.label ?? 'Unknown')
        : 'All Enabled';

    const handleRoleChange = useCallback(
        (value: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (value === ALL_VALUE) {
                        next.delete('role');
                    } else {
                        next.set('role', value);
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const handleEnabledChange = useCallback(
        (value: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (value === ALL_VALUE) {
                        next.delete('enabled');
                    } else {
                        next.set('enabled', value);
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const handleClearFilters = useCallback(() => {
        setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev);
                next.delete('role');
                next.delete('enabled');
                return next;
            },
            { replace: true },
        );
    }, [setSearchParams]);

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
                setEditingUser({
                    id: user.id,
                    username: user.username,
                    role: user.role,
                    enabled: user.enabled,
                });
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
                description: `${deletingUser.username} has been deleted.`,
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
                        description: `${values.username} has been updated.`,
                        variant: 'success',
                    });
                } else {
                    await createUser({
                        username: values.username,
                        password: values.password ?? '',
                        role: values.role,
                    });
                    toast({
                        title: 'User created',
                        description: `${values.username} has been created.`,
                        variant: 'success',
                    });
                }
                closeUserForm();
            } catch {
                toast({
                    title: values.id ? 'Failed to update user' : 'Failed to create user',
                    description: 'An error occurred. Please try again.',
                    variant: 'error',
                });
            }
        },
        [createUser, updateUser, toast, closeUserForm],
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

    return (
        <>
            <div className="users-page__tab-header">
                <div className="list-toolbar__summary" aria-label="Role summary">
                    <Badge variant="primary" size="sm" badgeStyle="outline" dot count={roleCounts.admin}>
                        Admin
                    </Badge>
                    <Badge variant="warning" size="sm" badgeStyle="outline" dot count={roleCounts.manager}>
                        Manager
                    </Badge>
                    <Badge variant="info" size="sm" badgeStyle="outline" dot count={roleCounts.requester}>
                        Requester
                    </Badge>
                    <Badge variant="default" size="sm" badgeStyle="outline" dot count={roleCounts.viewer}>
                        Viewer
                    </Badge>
                </div>
            </div>

            <div className="list-toolbar__filters" data-component="user-filters">
                <div className="list-toolbar__filters-controls">
                    <Select value={filters.role || ALL_VALUE} onValueChange={handleRoleChange}>
                        <SelectTrigger size="sm" placeholder="All Roles" aria-label="Filter by role" data-filter="role">
                            {roleLabel}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value={ALL_VALUE}>All Roles</SelectItem>
                            {roleOptions
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

            <div className="users-page__list" role="list" aria-label="Users">
                {isLoadingUsers ? (
                    <EmptyState title="Loading..." size="sm" />
                ) : filteredUsers.length === 0 ? (
                    <EmptyState
                        icon={<Users weight="duotone" />}
                        title="No users found"
                        description={hasActiveFilters ? 'Try adjusting your filters' : 'Add a user to get started'}
                    />
                ) : (
                    filteredUsers.map((user) => (
                        <UserRow
                            key={user.id}
                            user={user}
                            isCurrentUser={authUserId === user.id}
                            onToggleEnabled={handleToggleUserEnabled}
                            onEdit={handleEditUser}
                            onDelete={handleDeleteUser}
                        />
                    ))
                )}
            </div>

            <QuickActionsButton
                actions={[{ id: 'add-user', label: 'Add User', icon: <Plus weight="bold" />, onClick: handleAddUser }]}
                title="Add User"
            />

            <UserForm
                open={userFormOpen}
                onOpenChange={(open) => {
                    if (!open) closeUserForm();
                }}
                initialValues={editingUser}
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
    onToggleEnabled: (id: string, enabled: boolean) => void;
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
}

function UserRow({ user, isCurrentUser, onToggleEnabled, onEdit, onDelete }: UserRowProps) {
    const variant = roleVariants[user.role] ?? 'default';
    const roleLabelText = RoleName[user.role] ?? 'Unknown';
    const isLocked = user.lockedUntil && new Date(user.lockedUntil) > new Date();

    return (
        <div
            className={`users-page__item${user.enabled ? '' : ' users-page__item--disabled'}`}
            role="listitem"
            data-testid="users-page__item"
            data-user-id={user.id}
            data-role={variant}
            data-enabled={user.enabled}
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
                                    <ShieldCheck weight="fill" aria-hidden="true" />
                                </span>
                            )}
                        </span>
                        <Badge variant={variant} size="sm">
                            {roleLabelText}
                        </Badge>
                        {isLocked && (
                            <Badge variant="unhealthy" size="sm">
                                <Warning weight="bold" aria-hidden="true" />
                                Locked
                            </Badge>
                        )}
                    </div>
                    <div className="users-page__item-meta">
                        {user.email && <span className="users-page__item-email">{user.email}</span>}
                        <span className="users-page__item-login">
                            <Clock weight="regular" aria-hidden="true" />
                            Login {formatRelativeTime(user.lastLoginAt)}
                        </span>
                    </div>
                </div>
                <div className="users-page__item-actions card-actions">
                    <Tooltip
                        content={
                            isCurrentUser
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
                                disabled={isCurrentUser}
                            />
                        </div>
                    </Tooltip>
                    <Tooltip content={isCurrentUser ? 'Cannot modify your own account' : 'Edit'}>
                        <button
                            type="button"
                            className="users-page__action-btn card-actions__btn"
                            data-testid="users-page__action-btn"
                            onClick={() => onEdit(user.id)}
                            aria-label={`Edit ${user.username}`}
                            data-action="edit"
                            data-target={user.id}
                            disabled={isCurrentUser}
                        >
                            <PencilSimple weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip content={isCurrentUser ? 'Cannot modify your own account' : 'Delete'}>
                        <button
                            type="button"
                            className="users-page__action-btn users-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="users-page__action-btn"
                            onClick={() => onDelete(user.id)}
                            aria-label={`Delete ${user.username}`}
                            data-action="delete"
                            data-target={user.id}
                            disabled={isCurrentUser}
                        >
                            <Trash weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}
