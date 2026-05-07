import { useState, useMemo, useCallback } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
    Funnel,
    MagnifyingGlass,
    Plus,
    Trash,
    PencilSimple,
    User,
    Clock,
    LockSimple,
    LockSimpleOpen,
    CircleNotch,
    Copy,
    Star,
    DotsThreeVertical,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Tooltip } from '@/components/ui/Tooltip';
import { EmptyState } from '@/components/ui/EmptyState';
import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
} from '@/components/ui/DropdownMenu';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import {
    useContentFilters,
    useUpdateContentFilter,
    useDeleteContentFilter,
    useCreateContentFilter,
    type ContentFilterItem,
} from '@/hooks/useFilters';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useFormOptions } from '@/hooks/useFormOptions';
import { useToggle } from '@/hooks/useToggle';
import { useUserMap } from '@/hooks/useUserMap';
import { useWishlists } from '@/hooks/useWishlists';
import { useAuth, Role } from '@/auth';
import { formatRelativeTime } from '@/lib/formatTime';
import { canEditOwned } from '@/lib/ownership';
import type { WishlistFormValues } from '@/types';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { WishlistForm } from '@/pages/WishlistsPage';
import { FilterForm, type FilterFormValues } from './FilterForm';

import './FiltersPage.css';
import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';

export type { ContentFilterItem } from '@/hooks/useFilters';

interface EnrichedFilterItem extends ContentFilterItem {
    ownerName: string | null;
}

interface FilterPageFilterState {
    owner: string;
}

const ALL_VALUE = '__all__';
const SHARED_VALUE = '__shared__';

const OWNER_PARAM = 'owner';

interface OwnerOption {
    id: string;
    name: string;
}

export function FiltersPage() {
    useDocumentTitle('Filters');
    const [searchParams, setSearchParams] = useSearchParams();

    const filters: FilterPageFilterState = useMemo(
        () => ({
            owner: searchParams.get(OWNER_PARAM) || '',
        }),
        [searchParams],
    );

    const [lockingFilters, setLockingFilters] = useState<Set<string>>(new Set());
    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingFilter, setEditingFilter] = useState<ContentFilterItem | null>(null);
    const [wishlistFormOpen, , openWishlistForm, closeWishlistForm] = useToggle();
    const [convertingFilter, setConvertingFilter] = useState<ContentFilterItem | null>(null);
    const [deletingFilter, setDeletingFilter] = useState<ContentFilterItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();
    const navigate = useNavigate();
    const { hasRole, user } = useAuth();
    const canManage = hasRole(Role.Manager);

    const { filters: contentFilters, refetch } = useContentFilters();

    const { createWishlist, isCreating: isWishlistSubmitting } = useWishlists();

    const { qualityTierListOptions, tagOptions } = useFormOptions();
    const userMap = useUserMap();

    const createMutation = useCreateContentFilter();
    const updateMutation = useUpdateContentFilter();
    const deleteMutation = useDeleteContentFilter();

    const isSubmitting = createMutation.isPending || updateMutation.isPending;

    const enrichedFilters = useMemo(
        () =>
            contentFilters.map((f) => ({
                ...f,
                ownerName: f.ownerId ? (userMap.get(f.ownerId) ?? null) : null,
            })),
        [contentFilters, userMap],
    );

    const totalFilters = enrichedFilters.length;

    const ownerOptions: OwnerOption[] = useMemo(() => {
        const owners = new Map<string, string>();
        for (const f of enrichedFilters) {
            if (f.ownerId && f.ownerName) {
                owners.set(f.ownerId, f.ownerName);
            }
        }
        return Array.from(owners.entries()).map(([id, name]) => ({ id, name }));
    }, [enrichedFilters]);

    const filteredFilters = useMemo(() => {
        let items = [...enrichedFilters];

        if (filters.owner === SHARED_VALUE) {
            items = items.filter((f) => f.ownerId === null);
        } else if (filters.owner) {
            items = items.filter((f) => f.ownerId === filters.owner);
        }

        return items;
    }, [enrichedFilters, filters]);

    const typeCounts = useMemo(
        () => ({
            shared: enrichedFilters.filter((f) => f.ownerId === null).length,
            personal: enrichedFilters.filter((f) => f.ownerId !== null).length,
        }),
        [enrichedFilters],
    );

    const handleOwnerChange = useCallback(
        (value: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    const ownerValue = value === ALL_VALUE ? '' : value;
                    if (ownerValue) {
                        next.set(OWNER_PARAM, ownerValue);
                    } else {
                        next.delete(OWNER_PARAM);
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
                next.delete(OWNER_PARAM);
                return next;
            },
            { replace: true },
        );
    }, [setSearchParams]);

    const handleAddFilter = useCallback(() => {
        setEditingFilter(null);
        openForm();
    }, [openForm]);

    const addFilterActions: QuickAction[] = useMemo(
        () => [{ id: 'add-filter', label: 'Add Filter', icon: <Plus weight="bold" />, onClick: handleAddFilter }],
        [handleAddFilter],
    );

    const handleEditFilter = useCallback(
        (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (filter) {
                setEditingFilter(filter);
                openForm();
            }
        },
        [enrichedFilters, openForm],
    );

    const handleDuplicateFilter = useCallback(
        (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (filter) {
                setEditingFilter({
                    ...filter,
                    id: '',
                    name: `${filter.name} (Copy)`,
                });
                openForm();
            }
        },
        [enrichedFilters, openForm],
    );

    const handleDeleteFilter = useCallback(
        (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (!filter) {
                return;
            }
            setDeletingFilter(filter);
        },
        [enrichedFilters],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingFilter) {
            return;
        }

        setIsDeleting(true);

        try {
            await deleteMutation.mutateAsync({
                id: deletingFilter.id,
                isShared: deletingFilter.ownerId === null,
            });
            toast({
                title: 'Filter Deleted',
                description: `"${deletingFilter.name}" has been deleted.`,
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Delete Failed',
                description: `Could not delete "${deletingFilter.name}".`,
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingFilter(null);
        }
    }, [deletingFilter, deleteMutation, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingFilter(null);
    }, []);

    const handleLockFilter = useCallback(
        async (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (!filter) {
                return;
            }

            setLockingFilters((prev) => new Set([...prev, id]));

            try {
                await updateMutation.mutateAsync({
                    id: filter.id,
                    isShared: filter.ownerId === null,
                    ownerLock: { value: true },
                });

                toast({
                    title: filter.name,
                    description: 'Filter locked. Only you can edit it now.',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: filter.name,
                    description: 'Failed to lock filter',
                    variant: 'error',
                });
            } finally {
                setLockingFilters((prev) => {
                    const next = new Set(prev);
                    next.delete(id);
                    return next;
                });
            }
        },
        [enrichedFilters, updateMutation, toast],
    );

    const handleUnlockFilter = useCallback(
        async (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (!filter) {
                return;
            }

            setLockingFilters((prev) => new Set([...prev, id]));

            try {
                await updateMutation.mutateAsync({
                    id: filter.id,
                    isShared: filter.ownerId === null,
                    ownerLock: { value: false },
                });

                toast({
                    title: filter.name,
                    description: 'Filter unlocked. Others with permission can now edit.',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: filter.name,
                    description: 'Failed to unlock filter',
                    variant: 'error',
                });
            } finally {
                setLockingFilters((prev) => {
                    const next = new Set(prev);
                    next.delete(id);
                    return next;
                });
            }
        },
        [enrichedFilters, updateMutation, toast],
    );

    const handleFormOpenChange = useCallback(
        (open: boolean) => {
            if (!open) {
                closeForm();
                setEditingFilter(null);
            }
        },
        [closeForm],
    );

    const handleConvertToWishlist = useCallback(
        (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (filter) {
                setConvertingFilter(filter);
                openWishlistForm();
            }
        },
        [enrichedFilters, openWishlistForm],
    );

    const handleApplyInLibrary = useCallback(
        (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (!filter?.query) {
                return;
            }
            navigate(`/library/all?filter=${encodeURIComponent(filter.query)}`);
        },
        [enrichedFilters, navigate],
    );

    const handleWishlistFormOpenChange = useCallback(
        (open: boolean) => {
            if (!open) {
                closeWishlistForm();
                setConvertingFilter(null);
            }
        },
        [closeWishlistForm],
    );

    const handleWishlistSubmit = useCallback(
        async (values: WishlistFormValues) => {
            try {
                await createWishlist({
                    name: values.name,
                    contentFilterId: values.filterId ?? '',
                    qualityTierListId: values.qualityTierListId,
                    enabled: values.monitored,
                });

                closeWishlistForm();
                setConvertingFilter(null);

                toast({
                    title: 'Wishlist Created',
                    description: `"${values.name}" has been created from filter "${convertingFilter?.name}".`,
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Create Failed',
                    description: `Could not create wishlist "${values.name}".`,
                    variant: 'error',
                });
            }
        },
        [convertingFilter, createWishlist, closeWishlistForm, toast],
    );

    const enrichedFilterOptions = useMemo(() => {
        return enrichedFilters.map((f) => ({ id: f.id, name: f.name }));
    }, [enrichedFilters]);

    const getWishlistFormInitialValues = useCallback((): WishlistFormValues | null => {
        if (!convertingFilter) {
            return null;
        }

        return {
            name: `${convertingFilter.name} Wishlist`,
            filterId: convertingFilter.id,
            qualityTierListId: null,
            monitored: true,
            tagIds: [],
        };
    }, [convertingFilter]);

    const handleFormSubmit = useCallback(
        async (values: FilterFormValues) => {
            const isNew = !editingFilter?.id;

            try {
                if (isNew) {
                    await createMutation.mutateAsync({
                        name: values.name,
                        criteria: values.criteria,
                        isShared: values.isShared,
                    });
                } else {
                    await updateMutation.mutateAsync({
                        id: editingFilter.id,
                        isShared: editingFilter.ownerId === null,
                        name: { value: values.name },
                        criteria: { value: values.criteria },
                    });
                }

                closeForm();
                setEditingFilter(null);

                toast({
                    title: isNew ? 'Filter Created' : 'Filter Updated',
                    description: `${values.name} has been ${isNew ? 'created' : 'updated'} successfully.`,
                    variant: 'success',
                });
            } catch {
                toast({
                    title: isNew ? 'Create Failed' : 'Update Failed',
                    description: `Could not ${isNew ? 'create' : 'update'} filter.`,
                    variant: 'error',
                });
            }
        },
        [editingFilter, createMutation, updateMutation, closeForm, toast],
    );

    const isEditingOwner = editingFilter ? editingFilter.ownerId === user?.id : true;

    const canEditFilter = useCallback(
        (filter: ContentFilterItem): boolean => canEditOwned(filter, user?.id, canManage, hasRole, Role.Requester),
        [canManage, hasRole, user?.id],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const hasActiveFilters = filters.owner !== '';

    const ownerLabel =
        filters.owner === SHARED_VALUE
            ? 'Shared'
            : filters.owner
              ? (ownerOptions.find((o) => o.id === filters.owner)?.name ?? 'Unknown')
              : 'All Owners';

    return (
        <div className="filters-page" data-page="filters">
            <PullToRefresh onRefresh={handleRefresh} className="filters-page__pull-to-refresh">
                <header className="filters-page__header">
                    <div className="filters-page__header-left">
                        <Funnel
                            weight="duotone"
                            className="filters-page__icon"
                            aria-hidden="true"
                            data-testid="filters-page__icon"
                        />
                        <h1 className="filters-page__title">Filters</h1>
                        <span className="page-count filters-page__count" data-testid="filters-page__count">
                            {totalFilters}
                        </span>
                    </div>
                    <div className="filters-page__header-actions">
                        {hasRole(Role.Requester) && (
                            <Button variant="primary" size="sm" data-action="add-filter" onClick={handleAddFilter}>
                                <Plus weight="bold" aria-hidden="true" />
                                <span className="filters-page__add-label">Add Filter</span>
                            </Button>
                        )}
                    </div>
                </header>

                <div className="list-toolbar__summary" aria-label="Filter summary">
                    <Badge variant="info" size="sm" badgeStyle="outline" dot count={typeCounts.shared}>
                        Shared
                    </Badge>
                    <Badge variant="stabilizing" size="sm" badgeStyle="outline" dot count={typeCounts.personal}>
                        Personal
                    </Badge>
                </div>

                <div className="list-toolbar__filters" data-component="filter-filters">
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
                                <SelectItem value={SHARED_VALUE}>Shared</SelectItem>
                                {ownerOptions.map((opt) => (
                                    <SelectItem key={opt.id} value={opt.id}>
                                        {opt.name}
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

                <div className="filters-page__list" role="list" aria-label="Content filters">
                    {filteredFilters.length === 0 ? (
                        <EmptyState
                            icon={<Funnel weight="duotone" />}
                            title="No filters found"
                            description={
                                hasActiveFilters
                                    ? 'Try adjusting your filters'
                                    : 'Create a filter to start matching content'
                            }
                            action={
                                hasRole(Role.Requester) && !hasActiveFilters ? (
                                    <Button
                                        variant="primary"
                                        size="sm"
                                        onClick={handleAddFilter}
                                        data-action="add-filter-empty"
                                    >
                                        <Plus weight="bold" aria-hidden="true" />
                                        <span>Add Filter</span>
                                    </Button>
                                ) : undefined
                            }
                        />
                    ) : (
                        filteredFilters.map((filter) => (
                            <FilterRow
                                key={filter.id}
                                filter={filter}
                                isLocking={lockingFilters.has(filter.id)}
                                isOwner={filter.ownerId === user?.id}
                                canEdit={canEditFilter(filter)}
                                canManage={canManage}
                                canCreateWishlist={hasRole(Role.Requester)}
                                onEdit={handleEditFilter}
                                onDuplicate={handleDuplicateFilter}
                                onDelete={handleDeleteFilter}
                                onLock={handleLockFilter}
                                onUnlock={handleUnlockFilter}
                                onConvertToWishlist={handleConvertToWishlist}
                                onApplyInLibrary={handleApplyInLibrary}
                            />
                        ))
                    )}
                </div>
            </PullToRefresh>

            {hasRole(Role.Requester) && <QuickActionsButton actions={addFilterActions} title="Add Filter" />}

            <FilterForm
                open={formOpen}
                onOpenChange={handleFormOpenChange}
                initialValues={editingFilter}
                onSubmit={handleFormSubmit}
                isSubmitting={isSubmitting}
                isOwner={isEditingOwner}
                canEdit={editingFilter ? canEditFilter(editingFilter) : true}
                isShared={editingFilter ? editingFilter.ownerId === null : false}
            />

            <WishlistForm
                open={wishlistFormOpen}
                onOpenChange={handleWishlistFormOpenChange}
                initialValues={getWishlistFormInitialValues()}
                filters={enrichedFilterOptions}
                qualityTierLists={qualityTierListOptions}
                tags={tagOptions}
                onSubmit={handleWishlistSubmit}
                isSubmitting={isWishlistSubmitting}
            />

            <DeleteConfirmDialog
                open={!!deletingFilter}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingFilter?.name ?? ''}
                itemType="Filter"
                isDeleting={isDeleting}
            />
        </div>
    );
}

FiltersPage.displayName = 'FiltersPage';

interface FilterRowProps {
    filter: EnrichedFilterItem;
    isLocking: boolean;
    isOwner: boolean;
    canEdit: boolean;
    canManage: boolean;
    canCreateWishlist: boolean;
    onEdit: (id: string) => void;
    onDuplicate: (id: string) => void;
    onDelete: (id: string) => void;
    onLock: (id: string) => void;
    onUnlock: (id: string) => void;
    onConvertToWishlist: (id: string) => void;
    onApplyInLibrary: (id: string) => void;
}

function FilterRow({
    filter,
    isLocking,
    isOwner,
    canEdit,
    canManage,
    canCreateWishlist,
    onEdit,
    onDuplicate,
    onDelete,
    onLock,
    onUnlock,
    onConvertToWishlist,
    onApplyInLibrary,
}: FilterRowProps) {
    const isShared = filter.ownerId === null;

    return (
        <div
            className="filters-page__item"
            role="listitem"
            data-filter-id={filter.id}
            data-shared={isShared || undefined}
            data-owner={isOwner || undefined}
            data-locked={filter.ownerLock || undefined}
        >
            <div className="filters-page__item-main">
                <div className="filters-page__item-info">
                    <div className="filters-page__item-header">
                        <span className="filters-page__item-name">{filter.name}</span>
                        {!isShared && filter.ownerLock && (
                            <Tooltip
                                content={isOwner ? 'Locked - Only you can edit' : 'Locked - Only owner can edit'}
                                side="top"
                            >
                                <span
                                    className="filters-page__owner-lock filters-page__owner-lock--locked"
                                    data-owner-lock="true"
                                    aria-label={isOwner ? 'Locked - Only you can edit' : 'Locked - Only owner can edit'}
                                >
                                    <LockSimple weight="fill" aria-hidden="true" />
                                </span>
                            </Tooltip>
                        )}
                        {!isShared && !filter.ownerLock && !isOwner && (
                            <Tooltip content="Unlocked - Anyone with permission can edit" side="top">
                                <span
                                    className="filters-page__owner-lock"
                                    aria-label="Unlocked - Anyone with permission can edit"
                                >
                                    <LockSimpleOpen weight="fill" aria-hidden="true" />
                                </span>
                            </Tooltip>
                        )}
                        <Badge variant={isShared ? 'info' : 'stabilizing'} size="sm">
                            {isShared ? 'Shared' : 'Personal'}
                        </Badge>
                    </div>
                    <div className="filters-page__item-meta">
                        {!isShared && filter.ownerName && (
                            <span className="filters-page__item-owner">
                                <User weight="regular" aria-hidden="true" />
                                {filter.ownerName}
                            </span>
                        )}
                        <span className="filters-page__item-modified">
                            <Clock weight="regular" aria-hidden="true" />
                            {formatRelativeTime(filter.modifiedAt || filter.createdAt)}
                        </span>
                    </div>
                    {filter.query && (
                        <code
                            className="filters-page__item-query"
                            title={filter.query}
                            data-query
                            data-testid="filters-page__item-query"
                        >
                            {filter.query}
                        </code>
                    )}
                </div>
                <div className="filters-page__item-actions card-actions">
                    <button
                        type="button"
                        className="filters-page__apply-btn"
                        onClick={() => onApplyInLibrary(filter.id)}
                        aria-label={`Apply ${filter.name} in Library`}
                        data-action="apply-in-library"
                        data-target={filter.id}
                    >
                        <MagnifyingGlass weight="bold" aria-hidden="true" />
                        <span>Apply</span>
                    </button>

                    {canEdit && (
                        <Tooltip content="Edit" side="top">
                            <button
                                type="button"
                                className="filters-page__action-btn card-actions__btn"
                                onClick={() => onEdit(filter.id)}
                                aria-label={`Edit ${filter.name}`}
                                data-action="edit"
                                data-target={filter.id}
                            >
                                <PencilSimple weight="bold" />
                            </button>
                        </Tooltip>
                    )}

                    <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                            <button
                                type="button"
                                className="filters-page__action-btn card-actions__btn"
                                aria-label={`More actions for ${filter.name}`}
                                data-action="more-actions"
                                data-target={filter.id}
                            >
                                <DotsThreeVertical weight="bold" />
                            </button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end" sideOffset={4}>
                            {canCreateWishlist && (
                                <DropdownMenuItem
                                    onSelect={() => onConvertToWishlist(filter.id)}
                                    data-action="convert-to-wishlist"
                                    data-target={filter.id}
                                >
                                    <Star weight="bold" aria-hidden="true" />
                                    Create Wishlist
                                </DropdownMenuItem>
                            )}
                            <DropdownMenuItem
                                onSelect={() => onDuplicate(filter.id)}
                                data-action="duplicate"
                                data-target={filter.id}
                            >
                                <Copy weight="bold" aria-hidden="true" />
                                Duplicate
                            </DropdownMenuItem>
                            {!isShared && isOwner && (
                                <>
                                    <DropdownMenuSeparator />
                                    <DropdownMenuItem
                                        onSelect={() => (filter.ownerLock ? onUnlock(filter.id) : onLock(filter.id))}
                                        disabled={isLocking}
                                        data-action={filter.ownerLock ? 'unlock' : 'lock'}
                                        data-target={filter.id}
                                    >
                                        {isLocking ? (
                                            <CircleNotch
                                                weight="bold"
                                                className="card-actions__spinner"
                                                aria-hidden="true"
                                            />
                                        ) : filter.ownerLock ? (
                                            <LockSimpleOpen weight="bold" aria-hidden="true" />
                                        ) : (
                                            <LockSimple weight="bold" aria-hidden="true" />
                                        )}
                                        {isLocking ? 'Updating...' : filter.ownerLock ? 'Unlock' : 'Lock'}
                                    </DropdownMenuItem>
                                </>
                            )}
                            {canManage && (
                                <>
                                    <DropdownMenuSeparator />
                                    <DropdownMenuItem
                                        onSelect={() => onDelete(filter.id)}
                                        className="filters-page__menu-item--danger"
                                        data-action="delete"
                                        data-target={filter.id}
                                    >
                                        <Trash weight="bold" aria-hidden="true" />
                                        Delete
                                    </DropdownMenuItem>
                                </>
                            )}
                        </DropdownMenuContent>
                    </DropdownMenu>
                </div>
            </div>
        </div>
    );
}

export default FiltersPage;
