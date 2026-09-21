import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { useLocation, useNavigate } from 'react-router-dom';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { StarIcon, PlusIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { EmptyState } from '@/components/ui/EmptyState';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { OwnershipToggle, type OwnershipScope } from '@/components/ui/OwnershipToggle';
import { useToast } from '@/components/ui/Toast';
import { useWishlists, type ApiWishlist } from '@/hooks/useWishlists';
import { useTags } from '@/hooks/useTags';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import { useFormOptions } from '@/hooks/useFormOptions';
import { useUserMap } from '@/hooks/useUserMap';
import { useAuth, Role } from '@/auth';
import { canEditOwned } from '@/lib/ownership';
import { getErrorMessage } from '@/lib/errors';
import { WishlistForm, type WishlistFormValues } from './WishlistForm';
import { resolveFilterContentType } from './wishlistFormUtils';
import { WishlistRow, type WishlistItem } from './WishlistRow';
import { buildWishlistPatch } from './buildWishlistPatch';

import './WishlistsPage.css';
import '@/styles/list-toolbar.css';
import type { WishlistsPageCreateState } from '@/types/navigation';

function mapApiWishlistToItem(apiWishlist: ApiWishlist): WishlistItem {
    return {
        id: apiWishlist.id,
        name: apiWishlist.name,
        ownerId: apiWishlist.ownerUserId ?? null,
        ownerName: null,
        filterName: apiWishlist.contentFilter?.name ?? null,
        qualityTierListName: apiWishlist.qualityTierList?.name ?? null,
        enabled: apiWishlist.enabled,
        monitoring: apiWishlist.monitoring ?? null,
        ownerLock: apiWishlist.ownerLock,
        lastSyncedAt: apiWishlist.lastSyncedAt ?? null,
        createdAt: apiWishlist.createdAt,
        contentFilterCriteria: apiWishlist.contentFilter?.criteria ?? null,
    };
}

export function WishlistsPage() {
    useDocumentTitle('Wishlists');
    const navigate = useNavigate();
    const location = useLocation();
    const [filters, setFilter] = useUrlFilterState({ scope: '' });
    const scope = filters.scope === 'mine' ? 'mine' : 'all';

    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingWishlist, setEditingWishlist] = useState<ApiWishlist | null>(null);
    const [createSeed, setCreateSeed] = useState<WishlistsPageCreateState | null>(null);
    const [deletingWishlist, setDeletingWishlist] = useState<ApiWishlist | null>(null);
    const [deleteFilterToo, setDeleteFilterToo] = useState(false);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();
    const { hasRole, user } = useAuth();
    const canManage = hasRole(Role.Manager);
    const canCreate = hasRole(Role.Requester);

    const {
        wishlists: apiWishlists,
        isLoading,
        isError,
        error,
        createWishlist,
        isCreating,
        updateWishlist,
        isUpdating,
        toggleLock,
        deleteWishlist,
        refetch,
    } = useWishlists();
    const currentEditingWishlist = useMemo(() => {
        if (!editingWishlist) {
            return null;
        }
        return apiWishlists.find((candidate) => candidate.id === editingWishlist.id) ?? editingWishlist;
    }, [apiWishlists, editingWishlist]);

    const { rootFolderOptions, tagOptions } = useFormOptions();
    const userMap = useUserMap();
    const { tags: entityTags } = useTags(currentEditingWishlist ? { entityId: currentEditingWishlist.id } : undefined);

    const wishlists = useMemo(
        () =>
            apiWishlists.map((wl) => ({
                ...mapApiWishlistToItem(wl),
                ownerName: wl.ownerUserId ? (userMap.get(wl.ownerUserId) ?? null) : null,
            })),
        [apiWishlists, userMap],
    );

    const filteredWishlists = useMemo(() => {
        if (scope === 'mine') {
            return wishlists.filter((wl) => wl.ownerId === user?.id);
        }
        return wishlists;
    }, [wishlists, scope, user?.id]);

    const handleScopeChange = useCallback(
        (value: OwnershipScope) => {
            setFilter({ scope: value === 'mine' ? 'mine' : '' });
        },
        [setFilter],
    );

    const handleAddWishlist = useCallback(() => {
        setEditingWishlist(null);
        setCreateSeed(null);
        openForm();
    }, [openForm]);

    const createState = location.state as WishlistsPageCreateState | null;
    const createStateAppliedRef = useRef(false);
    useEffect(() => {
        if (!createState?.createCriteria || createStateAppliedRef.current || !canCreate) {
            return;
        }
        createStateAppliedRef.current = true;
        setEditingWishlist(null);
        setCreateSeed(createState);
        openForm();
        void navigate(
            { pathname: location.pathname, search: location.search, hash: location.hash },
            { replace: true, state: null },
        );
    }, [createState, canCreate, openForm, navigate, location.pathname, location.search, location.hash]);

    const addWishlistActions: QuickAction[] = useMemo(
        () => [
            { id: 'add-wishlist', label: 'Add Wishlist', icon: <PlusIcon weight="bold" />, onClick: handleAddWishlist },
        ],
        [handleAddWishlist],
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
            setDeleteFilterToo(false);
            setDeletingWishlist(wishlist);
        },
        [apiWishlists],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingWishlist) {
            return;
        }

        const hasBackingFilter = !!deletingWishlist.contentFilter;
        setIsDeleting(true);

        try {
            await deleteWishlist(deletingWishlist.id, hasBackingFilter && deleteFilterToo);
            toast({
                title: 'Wishlist Deleted',
                description: `Magpie deleted "${deletingWishlist.name}".`,
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
    }, [deletingWishlist, deleteFilterToo, deleteWishlist, toast]);

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
                setCreateSeed(null);
            }
        },
        [closeForm],
    );

    const handleFormSubmit = useCallback(
        async (values: WishlistFormValues) => {
            const isEditing = !!currentEditingWishlist;

            try {
                if (isEditing && currentEditingWishlist) {
                    const patch = buildWishlistPatch(
                        values,
                        entityTags.map((t) => t.name),
                    );

                    await updateWishlist(currentEditingWishlist.id, patch);
                    toast({
                        title: 'Wishlist Updated',
                        description: `"${values.name}" updated.`,
                        variant: 'success',
                    });
                } else {
                    const created = await createWishlist({
                        name: values.name,
                        criteria: values.criteria,
                        qualityTierListId: values.qualityTierListId,
                        rootFolderId: values.rootFolderId,
                        enabled: values.enabled,
                        monitoring: values.monitoring,
                    });
                    if (values.tagIds.length > 0) {
                        await updateWishlist(created.id, { tags: { add: values.tagIds } });
                    }
                    toast({
                        title: 'Wishlist Created',
                        description: `"${values.name}" created.`,
                        variant: 'success',
                    });
                }
            } catch (error) {
                const action = isEditing ? 'update' : 'create';
                toast({
                    title: isEditing ? 'Update Failed' : 'Create Failed',
                    description: `Failed to ${action} wishlist.`,
                    variant: 'error',
                });
                throw error;
            }
        },
        [currentEditingWishlist, entityTags, createWishlist, updateWishlist, toast],
    );

    const getFormInitialValues = useCallback((): WishlistFormValues | null => {
        if (!currentEditingWishlist) {
            if (createSeed) {
                return {
                    name: createSeed.createName ?? '',
                    filterId: null,
                    contentType: resolveFilterContentType(createSeed.createCriteria) ?? '',
                    criteria: createSeed.createCriteria,
                    qualityTierListId: null,
                    rootFolderId: null,
                    enabled: true,
                    monitoring: null,
                    tagIds: [],
                };
            }
            return null;
        }

        const criteria = currentEditingWishlist.contentFilter?.criteria ?? {};

        return {
            id: currentEditingWishlist.id,
            name: currentEditingWishlist.name,
            filterId: currentEditingWishlist.contentFilterId ?? null,
            contentType: resolveFilterContentType(criteria) ?? '',
            criteria,
            qualityTierListId: currentEditingWishlist.qualityTierListId ?? null,
            rootFolderId: currentEditingWishlist.rootFolderId ?? null,
            enabled: currentEditingWishlist.enabled,
            monitoring: currentEditingWishlist.monitoring ?? null,
            tagIds: entityTags.map((t) => t.name),
        };
    }, [currentEditingWishlist, entityTags, createSeed]);

    const isEditingOwner = currentEditingWishlist ? currentEditingWishlist.ownerUserId === user?.id : true;

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const hasActiveFilters = scope === 'mine';

    const canEditWishlist = useCallback(
        (wishlist: WishlistItem): boolean => canEditOwned(wishlist, user?.id, canManage, hasRole, Role.Requester),
        [canManage, hasRole, user?.id],
    );

    const canDeleteWishlist = useCallback(
        (wishlist: WishlistItem): boolean => canManage && canEditWishlist(wishlist),
        [canEditWishlist, canManage],
    );

    const wishlistsDescription = 'Saved filters Magpie uses to automatically monitor for matching content.';

    if (isLoading) {
        return (
            <div className="wishlists-page" data-page="wishlists">
                <PageHeader
                    className="wishlists-page__header"
                    title="Wishlists"
                    description={wishlistsDescription}
                    icon={<StarIcon weight="duotone" aria-hidden="true" data-testid="wishlists-page__icon" />}
                />
                <div className="wishlists-page__loading">
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
                <PageHeader
                    className="wishlists-page__header"
                    title="Wishlists"
                    description={wishlistsDescription}
                    icon={<StarIcon weight="duotone" aria-hidden="true" data-testid="wishlists-page__icon" />}
                    actions={
                        canCreate && (
                            <Button variant="primary" size="sm" data-action="add-wishlist" onClick={handleAddWishlist}>
                                <PlusIcon weight="bold" aria-hidden="true" />
                                <span className="wishlists-page__add-label">Add Wishlist</span>
                            </Button>
                        )
                    }
                />

                <div className="wishlists-page__ownership" data-component="wishlist-filters">
                    <OwnershipToggle
                        value={scope}
                        onValueChange={handleScopeChange}
                        aria-label="Filter wishlists by ownership"
                    />
                </div>

                <div className="wishlists-page__list" role="list" aria-label="Wishlists">
                    {isError ? (
                        <EmptyState
                            icon={<WarningCircleIcon weight="duotone" />}
                            title="Failed to load wishlists"
                            description={getErrorMessage(error)}
                            action={
                                <Button variant="secondary" size="sm" onClick={() => refetch()}>
                                    Retry
                                </Button>
                            }
                        />
                    ) : filteredWishlists.length === 0 ? (
                        <EmptyState
                            icon={<StarIcon weight="duotone" />}
                            title="No wishlists found"
                            description={
                                hasActiveFilters
                                    ? 'No wishlists match the active filters.'
                                    : canCreate
                                      ? 'A wishlist is a saved filter Magpie uses to automatically monitor for matching content.'
                                      : 'Wishlists are saved filters Magpie uses to automatically monitor for matching content.'
                            }
                        />
                    ) : (
                        filteredWishlists.map((wishlist) => (
                            <WishlistRow
                                key={wishlist.id}
                                wishlist={wishlist}
                                isOwner={wishlist.ownerId === user?.id}
                                canEdit={canEditWishlist(wishlist)}
                                canDelete={canDeleteWishlist(wishlist)}
                                onEdit={handleEditWishlist}
                                onDelete={handleDeleteWishlist}
                                onLock={handleLockWishlist}
                                onUnlock={handleUnlockWishlist}
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
                rootFolders={rootFolderOptions}
                tags={tagOptions}
                onSubmit={handleFormSubmit}
                isSubmitting={isCreating || isUpdating}
                isOwner={isEditingOwner}
                canEdit={currentEditingWishlist ? canEditWishlist(mapApiWishlistToItem(currentEditingWishlist)) : true}
            />

            <DeleteConfirmDialog
                open={!!deletingWishlist}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingWishlist?.name ?? ''}
                itemType="Wishlist"
                isDeleting={isDeleting}
                extraToggle={
                    deletingWishlist?.contentFilter
                        ? {
                              label: `Also delete its filter "${deletingWishlist.contentFilter.name}"`,
                              checked: deleteFilterToo,
                              onChange: setDeleteFilterToo,
                          }
                        : undefined
                }
            />
        </div>
    );
}

WishlistsPage.displayName = 'WishlistsPage';

export default WishlistsPage;
