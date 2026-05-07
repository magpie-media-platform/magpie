import { useState, useMemo, useCallback } from 'react';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { useNavigate } from 'react-router-dom';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { Star, Plus } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { useWishlists, type ApiWishlist, type WishlistPatch } from '@/hooks/useWishlists';
import { useTags } from '@/hooks/useTags';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import { useFormOptions } from '@/hooks/useFormOptions';
import { useUserMap } from '@/hooks/useUserMap';
import { useAuth, Role } from '@/auth';
import { canEditOwned } from '@/lib/ownership';
import type { WishlistFormValues } from '@/types';
import { WishlistForm } from './WishlistForm';
import { WishlistRow, type WishlistItem } from './WishlistRow';

import './WishlistsPage.css';
import '@/styles/list-toolbar.css';

const ALL_VALUE = '__all__';

interface OwnerOption {
    id: string;
    name: string;
}

const monitoredOptions = [
    { value: 'all', label: 'All Monitored' },
    { value: 'true', label: 'Monitored' },
    { value: 'false', label: 'Unmonitored' },
];

function mapApiWishlistToItem(apiWishlist: ApiWishlist): WishlistItem {
    return {
        id: apiWishlist.id,
        name: apiWishlist.name,
        ownerId: apiWishlist.ownerUserId ?? null,
        ownerName: null,
        filterName: apiWishlist.contentFilter?.name ?? null,
        qualityTierListName: apiWishlist.qualityTierList?.name ?? null,
        monitored: apiWishlist.enabled,
        ownerLock: apiWishlist.ownerLock,
        lastSyncedAt: apiWishlist.lastSyncedAt ?? null,
        createdAt: apiWishlist.createdAt,
    };
}

export function WishlistsPage() {
    useDocumentTitle('Wishlists');
    const navigate = useNavigate();
    const [filters, setFilter] = useUrlFilterState({ owner: '', monitored: '' });

    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingWishlist, setEditingWishlist] = useState<ApiWishlist | null>(null);
    const [deletingWishlist, setDeletingWishlist] = useState<ApiWishlist | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();
    const { hasRole, user } = useAuth();
    const canManage = hasRole(Role.Manager);
    const canCreate = hasRole(Role.Requester);

    const {
        wishlists: apiWishlists,
        isLoading,
        createWishlist,
        isCreating,
        updateWishlist,
        isUpdating,
        toggleLock,
        isLocking,
        deleteWishlist,
        refetch,
    } = useWishlists();

    const { filterOptions, qualityTierListOptions, tagOptions } = useFormOptions();
    const userMap = useUserMap();
    const { tags: entityTags } = useTags(editingWishlist ? { entityId: editingWishlist.id } : undefined);

    const wishlists = useMemo(
        () =>
            apiWishlists.map((wl) => ({
                ...mapApiWishlistToItem(wl),
                ownerName: wl.ownerUserId ? (userMap.get(wl.ownerUserId) ?? null) : null,
            })),
        [apiWishlists, userMap],
    );

    const totalWishlists = wishlists.length;

    const ownerOptions: OwnerOption[] = useMemo(() => {
        const owners = new Map<string, string>();
        for (const wl of wishlists) {
            if (wl.ownerId) {
                owners.set(wl.ownerId, wl.ownerName ?? 'Unknown');
            }
        }
        return Array.from(owners.entries()).map(([id, name]) => ({ id, name }));
    }, [wishlists]);

    const filteredWishlists = useMemo(() => {
        let items = [...wishlists];

        if (filters.owner) {
            items = items.filter((wl) => wl.ownerId === filters.owner);
        }

        if (filters.monitored) {
            items = items.filter((wl) => wl.monitored === (filters.monitored === 'true'));
        }

        return items;
    }, [wishlists, filters]);

    const statusCounts = useMemo(
        () => ({
            monitored: wishlists.filter((wl) => wl.monitored).length,
            unmonitored: wishlists.filter((wl) => !wl.monitored).length,
            locked: wishlists.filter((wl) => wl.ownerLock).length,
        }),
        [wishlists],
    );

    const handleOwnerChange = useCallback(
        (value: string) => {
            setFilter({ owner: value === ALL_VALUE ? '' : value });
        },
        [setFilter],
    );

    const handleMonitoredChange = useCallback(
        (value: string) => {
            setFilter({ monitored: value === ALL_VALUE ? '' : value });
        },
        [setFilter],
    );

    const handleClearFilters = useCallback(() => {
        setFilter({ owner: '', monitored: '' });
    }, [setFilter]);

    const handleAddWishlist = useCallback(() => {
        setEditingWishlist(null);
        openForm();
    }, [openForm]);

    const addWishlistActions: QuickAction[] = useMemo(
        () => [{ id: 'add-wishlist', label: 'Add Wishlist', icon: <Plus weight="bold" />, onClick: handleAddWishlist }],
        [handleAddWishlist],
    );

    const handleWishlistClick = useCallback(
        (id: string) => {
            navigate(`/wishlists/${id}`);
        },
        [navigate],
    );

    const handleEditWishlist = useCallback(
        (id: string) => {
            const wishlist = apiWishlists.find((wl) => wl.id === id);
            if (wishlist) {
                setEditingWishlist(wishlist);
                openForm();
            }
        },
        [apiWishlists, openForm],
    );

    const handleDeleteWishlist = useCallback(
        (id: string) => {
            const wishlist = apiWishlists.find((wl) => wl.id === id);
            if (!wishlist) {
                return;
            }
            setDeletingWishlist(wishlist);
        },
        [apiWishlists],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingWishlist) {
            return;
        }

        const isOwner = deletingWishlist.ownerUserId === user?.id;
        setIsDeleting(true);

        try {
            await deleteWishlist(deletingWishlist.id, isOwner, deletingWishlist.ownerUserId);
            toast({
                title: 'Wishlist Deleted',
                description: `"${deletingWishlist.name}" has been deleted.`,
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Delete Failed',
                description: `Failed to delete "${deletingWishlist.name}".`,
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingWishlist(null);
        }
    }, [deletingWishlist, user?.id, deleteWishlist, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingWishlist(null);
    }, []);

    const handleLockWishlist = useCallback(
        async (id: string) => {
            const wishlist = apiWishlists.find((wl) => wl.id === id);
            if (!wishlist) {
                return;
            }

            try {
                await toggleLock(id, true);
                toast({
                    title: wishlist.name,
                    description: 'Wishlist locked. Only you can edit it now.',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: wishlist.name,
                    description: 'Failed to lock wishlist',
                    variant: 'error',
                });
            }
        },
        [apiWishlists, toggleLock, toast],
    );

    const handleUnlockWishlist = useCallback(
        async (id: string) => {
            const wishlist = apiWishlists.find((wl) => wl.id === id);
            if (!wishlist) {
                return;
            }

            try {
                await toggleLock(id, false);
                toast({
                    title: wishlist.name,
                    description: 'Wishlist unlocked. Others with permission can now edit.',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: wishlist.name,
                    description: 'Failed to unlock wishlist',
                    variant: 'error',
                });
            }
        },
        [apiWishlists, toggleLock, toast],
    );

    const handleFormOpenChange = useCallback(
        (open: boolean) => {
            if (!open) {
                closeForm();
                setEditingWishlist(null);
            }
        },
        [closeForm],
    );

    const handleFormSubmit = useCallback(
        async (values: WishlistFormValues) => {
            const isEditing = !!editingWishlist;
            const isOwner = editingWishlist ? editingWishlist.ownerUserId === user?.id : true;

            try {
                if (isEditing && editingWishlist) {
                    const patch: WishlistPatch = {
                        name: { value: values.name },
                        contentFilterId: { value: values.filterId ?? '' },
                        enabled: { value: values.monitored },
                    };
                    if (values.qualityTierListId !== undefined) {
                        patch.qualityTierListId = { value: values.qualityTierListId };
                    }
                    const originalTagIds = entityTags.map((t) => t.name);
                    const tagsToAdd = values.tagIds.filter((id) => !originalTagIds.includes(id));
                    const tagsToRemove = originalTagIds.filter((id) => !values.tagIds.includes(id));
                    if (tagsToAdd.length > 0 || tagsToRemove.length > 0) {
                        patch.tags = {};
                        if (tagsToAdd.length > 0) {
                            patch.tags.add = tagsToAdd;
                        }
                        if (tagsToRemove.length > 0) {
                            patch.tags.remove = tagsToRemove;
                        }
                    }

                    await updateWishlist(editingWishlist.id, patch, isOwner);
                    toast({
                        title: 'Wishlist Updated',
                        description: `${values.name} has been updated successfully.`,
                        variant: 'success',
                    });
                } else {
                    await createWishlist({
                        name: values.name,
                        contentFilterId: values.filterId ?? '',
                        qualityTierListId: values.qualityTierListId,
                        enabled: values.monitored,
                    });
                    toast({
                        title: 'Wishlist Created',
                        description: `${values.name} has been created successfully.`,
                        variant: 'success',
                    });
                }

                closeForm();
                setEditingWishlist(null);
            } catch {
                toast({
                    title: isEditing ? 'Update Failed' : 'Create Failed',
                    description: `Failed to ${isEditing ? 'update' : 'create'} wishlist.`,
                    variant: 'error',
                });
            }
        },
        [editingWishlist, user?.id, entityTags, createWishlist, updateWishlist, toast, closeForm],
    );

    const getFormInitialValues = useCallback((): WishlistFormValues | null => {
        if (!editingWishlist) {
            return null;
        }

        return {
            id: editingWishlist.id,
            name: editingWishlist.name,
            filterId: editingWishlist.contentFilterId ?? null,
            qualityTierListId: editingWishlist.qualityTierListId ?? null,
            monitored: editingWishlist.enabled,
            tagIds: entityTags.map((t) => t.name),
        };
    }, [editingWishlist, entityTags]);

    const isEditingOwner = editingWishlist ? editingWishlist.ownerUserId === user?.id : true;

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const hasActiveFilters = filters.owner !== '' || filters.monitored !== '';

    const ownerLabel = filters.owner
        ? (ownerOptions.find((o) => o.id === filters.owner)?.name ?? 'Unknown')
        : 'All Owners';

    const monitoredLabel = filters.monitored
        ? (monitoredOptions.find((o) => o.value === filters.monitored)?.label ?? 'Unknown')
        : 'All Monitored';

    const canEditWishlist = useCallback(
        (wishlist: WishlistItem): boolean => canEditOwned(wishlist, user?.id, canManage, hasRole, Role.Requester),
        [canManage, hasRole, user?.id],
    );

    if (isLoading) {
        return (
            <div className="wishlists-page" data-page="wishlists">
                <header className="wishlists-page__header">
                    <div className="wishlists-page__header-left">
                        <Star
                            weight="duotone"
                            className="wishlists-page__icon"
                            aria-hidden="true"
                            data-testid="wishlists-page__icon"
                        />
                        <h1 className="wishlists-page__title">Wishlists</h1>
                    </div>
                </header>
                <div className="wishlists-page__loading">
                    <div className="wishlists-page__loading-summary">
                        <Skeleton className="wishlists-page__skeleton wishlists-page__skeleton--badge" />
                        <Skeleton className="wishlists-page__skeleton wishlists-page__skeleton--badge" />
                        <Skeleton className="wishlists-page__skeleton wishlists-page__skeleton--badge" />
                    </div>
                    <Skeleton className="wishlists-page__skeleton wishlists-page__skeleton--row" />
                    <Skeleton className="wishlists-page__skeleton wishlists-page__skeleton--row" />
                    <Skeleton className="wishlists-page__skeleton wishlists-page__skeleton--row" />
                </div>
            </div>
        );
    }

    return (
        <div className="wishlists-page" data-page="wishlists">
            <PullToRefresh onRefresh={handleRefresh} className="wishlists-page__pull-to-refresh">
                <header className="wishlists-page__header">
                    <div className="wishlists-page__header-left">
                        <Star
                            weight="duotone"
                            className="wishlists-page__icon"
                            aria-hidden="true"
                            data-testid="wishlists-page__icon"
                        />
                        <h1 className="wishlists-page__title">Wishlists</h1>
                        <span className="page-count wishlists-page__count" data-testid="wishlists-page__count">
                            {totalWishlists}
                        </span>
                    </div>
                    <div className="wishlists-page__header-actions">
                        {canCreate && (
                            <Button variant="primary" size="sm" data-action="add-wishlist" onClick={handleAddWishlist}>
                                <Plus weight="bold" aria-hidden="true" />
                                <span className="wishlists-page__add-label">Add Wishlist</span>
                            </Button>
                        )}
                    </div>
                </header>

                <div className="list-toolbar__summary" aria-label="Status summary">
                    <Badge variant="healthy" size="sm" badgeStyle="outline" dot count={statusCounts.monitored}>
                        Monitored
                    </Badge>
                    <Badge variant="default" size="sm" badgeStyle="outline" dot count={statusCounts.unmonitored}>
                        Unmonitored
                    </Badge>
                    <Badge variant="paused" size="sm" badgeStyle="outline" dot count={statusCounts.locked}>
                        Locked
                    </Badge>
                </div>

                <div className="list-toolbar__filters" data-component="wishlist-filters">
                    <div className="list-toolbar__filters-controls">
                        <Select value={filters.owner || ALL_VALUE} onValueChange={handleOwnerChange}>
                            <SelectTrigger
                                size="sm"
                                placeholder="All Owners"
                                aria-label="Filter by owner"
                                data-filter="owner"
                            >
                                {ownerLabel}
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value={ALL_VALUE}>All Owners</SelectItem>
                                {ownerOptions.map((opt) => (
                                    <SelectItem key={opt.id} value={opt.id}>
                                        {opt.name}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>

                        <Select value={filters.monitored || ALL_VALUE} onValueChange={handleMonitoredChange}>
                            <SelectTrigger
                                size="sm"
                                placeholder="All"
                                aria-label="Filter by monitored status"
                                data-filter="monitored"
                            >
                                {monitoredLabel}
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value={ALL_VALUE}>All</SelectItem>
                                {monitoredOptions
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

                <div className="wishlists-page__list" role="list" aria-label="Wishlists">
                    {filteredWishlists.length === 0 ? (
                        <EmptyState
                            icon={<Star weight="duotone" />}
                            title="No wishlists found"
                            description={
                                hasActiveFilters
                                    ? 'Try adjusting your filters'
                                    : 'Create a wishlist to start tracking content'
                            }
                            action={
                                canCreate && !hasActiveFilters ? (
                                    <Button
                                        variant="primary"
                                        size="sm"
                                        onClick={handleAddWishlist}
                                        data-action="add-wishlist-empty"
                                    >
                                        <Plus weight="bold" aria-hidden="true" />
                                        <span>Add Wishlist</span>
                                    </Button>
                                ) : undefined
                            }
                        />
                    ) : (
                        filteredWishlists.map((wishlist) => (
                            <WishlistRow
                                key={wishlist.id}
                                wishlist={wishlist}
                                isLocking={isLocking}
                                isOwner={wishlist.ownerId === user?.id}
                                canEdit={canEditWishlist(wishlist)}
                                canManage={canManage}
                                onEdit={handleEditWishlist}
                                onDelete={handleDeleteWishlist}
                                onLock={handleLockWishlist}
                                onUnlock={handleUnlockWishlist}
                                onClick={handleWishlistClick}
                            />
                        ))
                    )}
                </div>
            </PullToRefresh>

            <QuickActionsButton actions={addWishlistActions} title="Add Wishlist" visible={canCreate} />

            <WishlistForm
                open={formOpen}
                onOpenChange={handleFormOpenChange}
                initialValues={getFormInitialValues()}
                filters={filterOptions}
                qualityTierLists={qualityTierListOptions}
                tags={tagOptions}
                onSubmit={handleFormSubmit}
                isSubmitting={isCreating || isUpdating}
                isOwner={isEditingOwner}
                canEdit={editingWishlist ? canEditWishlist(mapApiWishlistToItem(editingWishlist)) : true}
            />

            <DeleteConfirmDialog
                open={!!deletingWishlist}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingWishlist?.name ?? ''}
                itemType="Wishlist"
                isDeleting={isDeleting}
            />
        </div>
    );
}

WishlistsPage.displayName = 'WishlistsPage';

export default WishlistsPage;
