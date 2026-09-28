import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
    FunnelIcon,
    MagnifyingGlassIcon,
    PlusIcon,
    TrashIcon,
    PencilSimpleIcon,
    UserIcon,
    ClockIcon,
    LockSimpleIcon,
    LockSimpleOpenIcon,
    CircleNotchIcon,
    CopyIcon,
    StarIcon,
    MagnifyingGlassPlusIcon,
    DotsThreeVerticalIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { PageHeader } from '@/components/ui/PageHeader';
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
import { OwnershipToggle, type OwnershipScope } from '@/components/ui/OwnershipToggle';
import { useToast } from '@/components/ui/Toast';
import {
    useContentFilters,
    useUpdateContentFilter,
    useDeleteContentFilter,
    useCreateContentFilter,
    type ContentFilterItem,
} from '@/hooks/useFilters';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import { useUserMap } from '@/hooks/useUserMap';
import { useWishlists } from '@/hooks/useWishlists';
import { useAuth, Role } from '@/auth';
import { getErrorMessage } from '@/lib/errors';
import { formatRelativeTime } from '@/lib/formatTime';
import { canEditOwned } from '@/lib/ownership';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import type { FiltersPageCreateState, LibraryAddPreloadState, WishlistsPageCreateState } from '@/types/navigation';
import { FilterForm, type FilterFormValues } from './FilterForm';

import './FiltersPage.css';
import '@/styles/card-actions.css';
import '@/styles/list-item-card.css';
import '@/styles/list-toolbar.css';

export type { ContentFilterItem } from '@/hooks/useFilters';

interface EnrichedFilterItem extends ContentFilterItem {
    ownerName: string | null;
    wishlistId: string | null;
    wishlistName: string | null;
}

const EDIT_PARAM = 'edit';

export function FiltersPage() {
    useDocumentTitle('Filters');
    const [searchParams, setSearchParams] = useSearchParams();
    const location = useLocation();

    const [filterState, setFilterState] = useUrlFilterState({ scope: '' });
    const scope: OwnershipScope = filterState.scope === 'mine' ? 'mine' : 'all';

    const [lockingFilters, setLockingFilters] = useState<Set<string>>(new Set());
    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingFilter, setEditingFilter] = useState<EnrichedFilterItem | null>(null);
    const [deletingFilter, setDeletingFilter] = useState<ContentFilterItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();
    const navigate = useNavigate();
    const { hasPermission, hasRole, user } = useAuth();
    const isAdmin = hasRole(Role.Admin);
    const canWriteFilters = hasPermission('filters.own');

    const { filters: contentFilters, isLoading, error, refetch } = useContentFilters();
    const { wishlists, refetch: refetchWishlists } = useWishlists();

    const userMap = useUserMap();

    const createMutation = useCreateContentFilter();
    const updateMutation = useUpdateContentFilter();
    const deleteMutation = useDeleteContentFilter();

    const isSubmitting = createMutation.isPending || updateMutation.isPending;

    const enrichedFilters = useMemo(
        () =>
            contentFilters.map((filter) => {
                const wishlist = wishlists.find((candidate) => candidate.contentFilterId === filter.id);
                return {
                    ...filter,
                    ownerName: filter.ownerId ? (userMap.get(filter.ownerId) ?? null) : null,
                    wishlistId: wishlist?.id ?? null,
                    wishlistName: wishlist?.name ?? null,
                };
            }),
        [contentFilters, userMap, wishlists],
    );
    const currentEditingFilter = useMemo(() => {
        if (!editingFilter?.id) {
            return editingFilter;
        }
        return enrichedFilters.find((candidate) => candidate.id === editingFilter.id) ?? editingFilter;
    }, [editingFilter, enrichedFilters]);

    const filteredFilters = useMemo(() => {
        if (scope === 'mine') {
            return enrichedFilters.filter((f) => f.ownerId === user?.id);
        }
        return enrichedFilters;
    }, [enrichedFilters, scope, user?.id]);

    const handleScopeChange = useCallback(
        (value: OwnershipScope) => {
            setFilterState({ scope: value === 'mine' ? 'mine' : '' });
        },
        [setFilterState],
    );

    const handleAddFilter = useCallback(() => {
        setEditingFilter(null);
        openForm();
    }, [openForm]);

    const addFilterActions: QuickAction[] = useMemo(
        () => [{ id: 'add-filter', label: 'Add Filter', icon: <PlusIcon weight="bold" />, onClick: handleAddFilter }],
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

    const editLinkAppliedRef = useRef<string | null>(null);
    useEffect(() => {
        const editId = searchParams.get(EDIT_PARAM);
        if (!editId || editLinkAppliedRef.current === editId) {
            return;
        }
        const filter = enrichedFilters.find((f) => f.id === editId);
        if (!filter) {
            return;
        }
        editLinkAppliedRef.current = editId;
        setEditingFilter(filter);
        openForm();
        setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev);
                next.delete(EDIT_PARAM);
                return next;
            },
            { replace: true },
        );
    }, [searchParams, enrichedFilters, openForm, setSearchParams]);

    const createState = location.state as FiltersPageCreateState | null;
    const createStateAppliedRef = useRef(false);
    useEffect(() => {
        if (!createState?.createQuery || createStateAppliedRef.current || !hasRole(Role.Requester)) {
            return;
        }
        createStateAppliedRef.current = true;
        setEditingFilter({
            id: '',
            name: createState.createName ?? '',
            ownerId: user?.id ?? null,
            ownerName: user?.username ?? null,
            criteriaCount: 0,
            ownerLock: false,
            query: createState.createQuery,
            criteria: {},
            createdAt: '',
            modifiedAt: null,
            wishlistId: null,
            wishlistName: null,
        });
        openForm();
    }, [createState, hasRole, openForm, user?.id, user?.username]);

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
            await deleteMutation.mutateAsync({ id: deletingFilter.id });
            toast({
                title: 'Filter Deleted',
                description: `Magpie deleted "${deletingFilter.name}".`,
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Delete Failed',
                description: `Failed to delete "${deletingFilter.name}".`,
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

    const handleSetOwnerLock = useCallback(
        async (id: string, ownerLock: boolean) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (!filter) {
                return;
            }

            setLockingFilters((prev) => new Set([...prev, id]));

            try {
                await updateMutation.mutateAsync({
                    id: filter.id,
                    ownerLock: { value: ownerLock },
                });

                toast({
                    title: filter.name,
                    description: ownerLock
                        ? 'Filter locked. Only you can edit it now.'
                        : 'Filter unlocked. Others with permission can now edit.',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: filter.name,
                    description: ownerLock ? 'Failed to lock filter' : 'Failed to unlock filter',
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

    const handleViewInLibrary = useCallback(
        (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (!filter?.query) {
                return;
            }
            const search = new URLSearchParams({ savedFilter: filter.id });
            void navigate(`/library/all?${search}`);
        },
        [enrichedFilters, navigate],
    );

    const handleSearchProviders = useCallback(
        (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (!filter?.query) {
                return;
            }
            const preload: LibraryAddPreloadState = {
                filterQuery: filter.query,
                savedFilterId: filter.id,
            };
            void navigate('/library/add', { state: preload });
        },
        [enrichedFilters, navigate],
    );

    const handleConvertToWishlist = useCallback(
        (id: string) => {
            const filter = enrichedFilters.find((f) => f.id === id);
            if (!filter) {
                return;
            }
            const createState: WishlistsPageCreateState = {
                createCriteria: filter.criteria,
                createName: filter.name,
            };
            void navigate('/wishlists', { state: createState });
        },
        [enrichedFilters, navigate],
    );

    const handleFormSubmit = useCallback(
        async (values: FilterFormValues) => {
            const isNew = !currentEditingFilter?.id;

            if (!isNew && !enrichedFilters.some((f) => f.id === currentEditingFilter.id)) {
                toast({
                    title: 'Filter Unavailable',
                    description: 'This filter no longer exists. Reload and try again.',
                    variant: 'error',
                });
                throw new Error('Filter is unavailable');
            }

            try {
                if (isNew) {
                    await createMutation.mutateAsync({
                        name: values.name,
                        criteria: values.criteria,
                    });
                } else {
                    await updateMutation.mutateAsync({
                        id: currentEditingFilter.id,
                        name: { value: values.name },
                        criteria: { value: values.criteria },
                    });
                }

                closeForm();
                setEditingFilter(null);

                toast({
                    title: isNew ? 'Filter Created' : 'Filter Updated',
                    description: `Magpie ${isNew ? 'created' : 'updated'} "${values.name}".`,
                    variant: 'success',
                });
            } catch (error) {
                toast({
                    title: isNew ? 'Create Failed' : 'Update Failed',
                    description: `Failed to ${isNew ? 'create' : 'update'} the filter.`,
                    variant: 'error',
                });
                throw error;
            }
        },
        [currentEditingFilter, enrichedFilters, createMutation, updateMutation, closeForm, toast],
    );

    const isEditingOwner = currentEditingFilter ? currentEditingFilter.ownerId === user?.id : true;

    const existingFilterNames = useMemo(() => {
        const editingId = currentEditingFilter?.id;
        return enrichedFilters.filter((f) => f.id !== editingId).map((f) => f.name);
    }, [enrichedFilters, currentEditingFilter?.id]);

    const canEditFilter = useCallback(
        (filter: EnrichedFilterItem): boolean =>
            !filter.wishlistName && canEditOwned(filter, user?.id, isAdmin, canWriteFilters),
        [canWriteFilters, isAdmin, user?.id],
    );

    const canEditIfUnbound = useCallback(
        (filter: ContentFilterItem): boolean => canEditOwned(filter, user?.id, isAdmin, canWriteFilters),
        [canWriteFilters, isAdmin, user?.id],
    );

    const handleRefresh = useCallback(async () => {
        await Promise.all([refetch(), refetchWishlists()]);
    }, [refetch, refetchWishlists]);

    const hasActiveFilters = scope === 'mine';

    return (
        <div className="filters-page" data-page="filters">
            <PullToRefresh onRefresh={handleRefresh} className="filters-page__pull-to-refresh">
                <PageHeader
                    className="filters-page__header"
                    title="Filters"
                    description="Reusable filters for the library and wishlists."
                    icon={<FunnelIcon weight="duotone" aria-hidden="true" data-testid="filters-page__icon" />}
                    actions={
                        hasRole(Role.Requester) && (
                            <Button variant="primary" size="sm" data-action="add-filter" onClick={handleAddFilter}>
                                <PlusIcon weight="bold" aria-hidden="true" />
                                <span className="filters-page__add-label">Add Filter</span>
                            </Button>
                        )
                    }
                />

                <div className="filters-page__ownership" data-component="filter-filters">
                    <OwnershipToggle value={scope} onValueChange={handleScopeChange} aria-label="Filter by ownership" />
                </div>

                {error && (
                    <EmptyState
                        icon={<WarningCircleIcon weight="duotone" />}
                        title="Failed to load filters"
                        description={getErrorMessage(error)}
                        action={
                            <Button variant="secondary" size="sm" onClick={() => refetch()}>
                                Retry
                            </Button>
                        }
                    />
                )}
                <div
                    className="filters-page__list"
                    role={filteredFilters.length > 0 ? 'list' : undefined}
                    aria-label={filteredFilters.length > 0 ? 'Content filters' : undefined}
                >
                    {filteredFilters.length === 0 && isLoading ? (
                        <EmptyState title="Loading filters..." size="sm" />
                    ) : filteredFilters.length === 0 && !error ? (
                        <EmptyState
                            icon={<FunnelIcon weight="duotone" />}
                            title="No filters found"
                            description={
                                hasActiveFilters
                                    ? 'Try adjusting your filters'
                                    : 'Reusable filters can be applied in the library and wishlists.'
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
                                canEditIfUnbound={canEditIfUnbound(filter)}
                                canDelete={canEditFilter(filter)}
                                onEdit={handleEditFilter}
                                onDuplicate={handleDuplicateFilter}
                                onDelete={handleDeleteFilter}
                                onSetOwnerLock={handleSetOwnerLock}
                                onViewInLibrary={handleViewInLibrary}
                                onSearchProviders={handleSearchProviders}
                                onConvertToWishlist={handleConvertToWishlist}
                                canCreateWishlist={hasRole(Role.Requester)}
                                canDuplicate={hasRole(Role.Requester)}
                                canSearchProviders={hasPermission('content.add')}
                            />
                        ))
                    )}
                </div>
            </PullToRefresh>

            {hasRole(Role.Requester) && <QuickActionsButton actions={addFilterActions} title="Add Filter" />}

            <FilterForm
                open={formOpen}
                onOpenChange={handleFormOpenChange}
                initialValues={currentEditingFilter}
                onSubmit={handleFormSubmit}
                isSubmitting={isSubmitting}
                isOwner={isEditingOwner}
                canEdit={currentEditingFilter ? canEditFilter(currentEditingFilter) : true}
                isShared={currentEditingFilter ? currentEditingFilter.ownerId === null : false}
                existingNames={existingFilterNames}
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
    canEditIfUnbound: boolean;
    canDelete: boolean;
    onEdit: (id: string) => void;
    onDuplicate: (id: string) => void;
    onDelete: (id: string) => void;
    onSetOwnerLock: (id: string, ownerLock: boolean) => void;
    onViewInLibrary: (id: string) => void;
    onSearchProviders: (id: string) => void;
    onConvertToWishlist: (id: string) => void;
    canCreateWishlist: boolean;
    canDuplicate: boolean;
    canSearchProviders: boolean;
}

function FilterRow({
    filter,
    isLocking,
    isOwner,
    canEdit,
    canEditIfUnbound,
    canDelete,
    onEdit,
    onDuplicate,
    onDelete,
    onSetOwnerLock,
    onViewInLibrary,
    onSearchProviders,
    onConvertToWishlist,
    canCreateWishlist,
    canDuplicate,
    canSearchProviders,
}: FilterRowProps) {
    const isShared = filter.ownerId === null;

    return (
        <div
            className="filters-page__item list-item-card"
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
                                    <LockSimpleIcon weight="fill" aria-hidden="true" />
                                </span>
                            </Tooltip>
                        )}
                        {!isShared && !filter.ownerLock && !isOwner && (
                            <Tooltip content="Unlocked - Anyone with permission can edit" side="top">
                                <span
                                    className="filters-page__owner-lock"
                                    aria-label="Unlocked - Anyone with permission can edit"
                                >
                                    <LockSimpleOpenIcon weight="fill" aria-hidden="true" />
                                </span>
                            </Tooltip>
                        )}
                        <Badge variant={isShared ? 'info' : 'stabilizing'} size="sm">
                            {isShared ? 'Shared' : 'Personal'}
                        </Badge>
                        {filter.wishlistName && filter.wishlistId && (
                            <Link
                                to={`/wishlists/${filter.wishlistId}`}
                                className="badge badge--default badge--sm filters-page__wishlist-badge"
                                data-wishlist-id={filter.wishlistId}
                                data-action="open-wishlist"
                            >
                                <StarIcon weight="fill" aria-hidden="true" />
                                <span className="filters-page__wishlist-badge-label">
                                    Part of {filter.wishlistName}
                                </span>
                            </Link>
                        )}
                    </div>
                    <div className="filters-page__item-meta">
                        {!isShared && filter.ownerName && (
                            <span className="filters-page__item-owner">
                                <UserIcon weight="regular" aria-hidden="true" />
                                {filter.ownerName}
                            </span>
                        )}
                        <span className="filters-page__item-modified">
                            <ClockIcon weight="regular" aria-hidden="true" />
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
                        onClick={() => onViewInLibrary(filter.id)}
                        aria-label={`View ${filter.name} in Library`}
                        data-action="view-in-library"
                        data-target={filter.id}
                    >
                        <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                        <span>View</span>
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
                                <PencilSimpleIcon weight="bold" />
                            </button>
                        </Tooltip>
                    )}

                    {!canEdit && filter.wishlistName && canEditIfUnbound && (
                        <Tooltip content="Edit this filter from its wishlist" side="top">
                            <button
                                type="button"
                                className="filters-page__action-btn card-actions__btn"
                                aria-disabled="true"
                                onClick={(e) => e.preventDefault()}
                                aria-label={`Edit ${filter.name} from its wishlist`}
                                data-action="edit"
                                data-target={filter.id}
                                data-disabled-reason="wishlist-bound"
                            >
                                <PencilSimpleIcon weight="bold" />
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
                                <DotsThreeVerticalIcon weight="bold" />
                            </button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end" sideOffset={4}>
                            {canDuplicate && (
                                <DropdownMenuItem
                                    onSelect={() => onDuplicate(filter.id)}
                                    data-action="duplicate"
                                    data-target={filter.id}
                                >
                                    <CopyIcon weight="bold" aria-hidden="true" />
                                    Duplicate
                                </DropdownMenuItem>
                            )}
                            {filter.query && canSearchProviders && (
                                <DropdownMenuItem
                                    onSelect={() => onSearchProviders(filter.id)}
                                    data-action="search-providers"
                                    data-target={filter.id}
                                >
                                    <MagnifyingGlassPlusIcon weight="bold" aria-hidden="true" />
                                    Search providers
                                </DropdownMenuItem>
                            )}
                            {canCreateWishlist && filter.criteriaCount > 0 && !filter.wishlistName && (
                                <DropdownMenuItem
                                    onSelect={() => onConvertToWishlist(filter.id)}
                                    data-action="convert-to-wishlist"
                                    data-target={filter.id}
                                >
                                    <StarIcon weight="bold" aria-hidden="true" />
                                    Convert to wishlist
                                </DropdownMenuItem>
                            )}
                            {!isShared && isOwner && !filter.wishlistName && (
                                <>
                                    <DropdownMenuSeparator />
                                    <DropdownMenuItem
                                        onSelect={() => onSetOwnerLock(filter.id, !filter.ownerLock)}
                                        disabled={isLocking}
                                        data-action={filter.ownerLock ? 'unlock' : 'lock'}
                                        data-target={filter.id}
                                    >
                                        {isLocking ? (
                                            <CircleNotchIcon
                                                weight="bold"
                                                className="card-actions__spinner"
                                                aria-hidden="true"
                                            />
                                        ) : filter.ownerLock ? (
                                            <LockSimpleOpenIcon weight="bold" aria-hidden="true" />
                                        ) : (
                                            <LockSimpleIcon weight="bold" aria-hidden="true" />
                                        )}
                                        {isLocking ? 'Updating...' : filter.ownerLock ? 'Unlock' : 'Lock'}
                                    </DropdownMenuItem>
                                </>
                            )}
                            {canDelete && (
                                <>
                                    <DropdownMenuSeparator />
                                    <DropdownMenuItem
                                        onSelect={() => onDelete(filter.id)}
                                        className="filters-page__menu-item--danger"
                                        data-action="delete"
                                        data-target={filter.id}
                                    >
                                        <TrashIcon weight="bold" aria-hidden="true" />
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
