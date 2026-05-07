import { useState, useMemo, useCallback } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import {
    ArrowLeft,
    Star,
    User,
    Clock,
    FunnelSimple,
    SlidersHorizontal,
    LockSimple,
    LockSimpleOpen,
    Cube,
    ImageBroken,
    PencilSimple,
    Warning,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { Button } from '@/components/ui/Button';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tooltip } from '@/components/ui/Tooltip';
import { EmptyState } from '@/components/ui/EmptyState';
import { useToast } from '@/components/ui/Toast';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { useWishlist, useWishlists, type ApiWishlist, type WishlistPatch } from '@/hooks/useWishlists';
import { useTags } from '@/hooks/useTags';
import { useFormOptions } from '@/hooks/useFormOptions';
import { useUserMap } from '@/hooks/useUserMap';
import { useAuth, Role } from '@/auth';
import { formatRelativeTime } from '@/lib/formatTime';
import { canEditOwned } from '@/lib/ownership';

import type { WishlistFormValues } from '@/types';
import { WishlistForm } from './WishlistForm';

import './WishlistDetailPage.css';

type ContentKind = 'episode' | 'movie' | 'music' | 'book' | 'unknown';

type FulfillmentStatus = 'missing' | 'upgradable' | 'fulfilled' | 'fulfilledExternally';

interface WishlistContentItem {
    id: string;

    title: string;

    subtitle?: string;

    posterUrl?: string;

    kind: ContentKind;

    fulfillmentStatus: FulfillmentStatus;

    addedDate: string;

    lastSearched: string | null;

    monitored: boolean;

    subjectId?: string;
}

const fulfillmentLabels: Record<FulfillmentStatus, string> = {
    missing: 'Missing',
    upgradable: 'Upgradable',
    fulfilled: 'Fulfilled',
    fulfilledExternally: 'External',
};

const fulfillmentVariants: Record<FulfillmentStatus, string> = {
    missing: 'missing',
    upgradable: 'degraded',
    fulfilled: 'fulfilled',
    fulfilledExternally: 'stabilizing',
};

const EMPTY_CONTENT_ITEMS: WishlistContentItem[] = [];

interface WishlistDisplayItem {
    id: string;
    name: string;
    ownerId: string | null;
    ownerName: string | null;
    filterName: string | null;
    qualityTierListName: string | null;
    monitored: boolean;
    ownerLock: boolean;
    lastSyncedAt: string | null;
    createdAt: string;
    contentFilterId: string | null;
    qualityTierListId: string | null;
}

function mapApiWishlistToDisplayItem(apiWishlist: ApiWishlist): WishlistDisplayItem {
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
        contentFilterId: apiWishlist.contentFilterId ?? null,
        qualityTierListId: apiWishlist.qualityTierListId ?? null,
    };
}

export function WishlistDetailPage() {
    useDocumentTitle('Wishlist');
    const { wishlistId } = useParams<{ wishlistId: string }>();
    const navigate = useNavigate();
    const { hasRole, user } = useAuth();
    const canManage = hasRole(Role.Manager);
    const { toast } = useToast();

    const [formOpen, , openForm, closeForm] = useToggle();

    const { data: apiWishlist, isLoading, refetch } = useWishlist(wishlistId);
    const { updateWishlist, isUpdating } = useWishlists();

    const { filterOptions, qualityTierListOptions, tagOptions } = useFormOptions();
    const userMap = useUserMap();
    const { tags: entityTags } = useTags(wishlistId ? { entityId: wishlistId } : undefined);

    const wishlist = useMemo(() => {
        if (!apiWishlist) {
            return null;
        }
        const display = mapApiWishlistToDisplayItem(apiWishlist);
        return {
            ...display,
            ownerName: display.ownerId ? (userMap.get(display.ownerId) ?? null) : null,
        };
    }, [apiWishlist, userMap]);

    const contentItems = EMPTY_CONTENT_ITEMS;

    const isOwner = wishlist?.ownerId === user?.id;

    const canEdit = useMemo(
        () => (wishlist ? canEditOwned(wishlist, user?.id, canManage, hasRole, Role.Requester) : false),
        [wishlist, user?.id, canManage, hasRole],
    );

    const breadcrumbs: BreadcrumbItem[] = useMemo(() => {
        const items: BreadcrumbItem[] = [{ id: 'wishlists', label: 'Wishlists', to: '/wishlists' }];

        if (wishlist) {
            items.push({
                id: wishlist.id,
                label: wishlist.name,
                to: `/wishlists/${wishlist.id}`,
            });
        }

        return items;
    }, [wishlist]);

    const statusCounts = useMemo(() => {
        return {
            missing: contentItems.filter((i) => i.fulfillmentStatus === 'missing').length,
            upgradable: contentItems.filter((i) => i.fulfillmentStatus === 'upgradable').length,
            fulfilled: contentItems.filter((i) => i.fulfillmentStatus === 'fulfilled').length,
            fulfilledExternally: contentItems.filter((i) => i.fulfillmentStatus === 'fulfilledExternally').length,
        };
    }, [contentItems]);

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const handleEdit = useCallback(() => {
        openForm();
    }, [openForm]);

    const handleFormSubmit = useCallback(
        async (values: WishlistFormValues) => {
            if (!wishlist) {
                return;
            }

            try {
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

                await updateWishlist(wishlist.id, patch, isOwner);
                await refetch();
                closeForm();

                toast({
                    title: 'Wishlist Updated',
                    description: `${values.name} has been updated successfully.`,
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Update Failed',
                    description: 'Failed to update wishlist.',
                    variant: 'error',
                });
            }
        },
        [wishlist, isOwner, entityTags, updateWishlist, refetch, closeForm, toast],
    );

    const handleItemClick = useCallback(
        (item: WishlistContentItem) => {
            navigate(`/library/${item.id}`);
        },
        [navigate],
    );

    const getFormInitialValues = useCallback((): WishlistFormValues | null => {
        if (!wishlist) {
            return null;
        }

        return {
            id: wishlist.id,
            name: wishlist.name,
            filterId: wishlist.contentFilterId,
            qualityTierListId: wishlist.qualityTierListId,
            monitored: wishlist.monitored,
            tagIds: entityTags.map((t) => t.name),
        };
    }, [wishlist, entityTags]);

    if (isLoading) {
        return (
            <div className="wishlist-detail-page" data-page="wishlist-detail">
                <div className="wishlist-detail-page__loading">
                    <Skeleton className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--breadcrumb" />
                    <div className="wishlist-detail-page__loading-header">
                        <Skeleton
                            className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--icon"
                            variant="circle"
                        />
                        <Skeleton className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--title" />
                        <Skeleton className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--badge" />
                    </div>
                    <Skeleton className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--meta" />
                    <div className="wishlist-detail-page__loading-summary">
                        <Skeleton className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--summary-badge" />
                        <Skeleton className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--summary-badge" />
                        <Skeleton className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--summary-badge" />
                        <Skeleton className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--summary-badge" />
                    </div>
                    <Skeleton className="wishlist-detail-page__skeleton wishlist-detail-page__skeleton--content" />
                </div>
            </div>
        );
    }

    if (!wishlist) {
        return (
            <div className="wishlist-detail-page" data-page="wishlist-detail">
                <div className="wishlist-detail-page__not-found">
                    <Star weight="duotone" className="wishlist-detail-page__not-found-icon" aria-hidden="true" />
                    <h1>Wishlist Not Found</h1>
                    <p>The wishlist you are looking for does not exist.</p>
                    <Link to="/wishlists" className="wishlist-detail-page__back-link">
                        <ArrowLeft weight="bold" aria-hidden="true" />
                        Back to Wishlists
                    </Link>
                </div>
            </div>
        );
    }

    return (
        <div className="wishlist-detail-page" data-page="wishlist-detail">
            <PullToRefresh onRefresh={handleRefresh} className="wishlist-detail-page__pull-to-refresh">
                <Breadcrumb items={breadcrumbs} className="wishlist-detail-page__breadcrumb" />

                <header
                    className="wishlist-detail-page__header"
                    data-component="wishlist-detail-header"
                    data-wishlist-id={wishlist.id}
                >
                    <div className="wishlist-detail-page__header-main">
                        <Star weight="duotone" className="wishlist-detail-page__icon" aria-hidden="true" />
                        <div className="wishlist-detail-page__title-row">
                            <h1 className="wishlist-detail-page__title">{wishlist.name}</h1>
                            {wishlist.ownerLock && (
                                <Tooltip
                                    content={isOwner ? 'Locked - Only you can edit' : 'Locked - Only owner can edit'}
                                    side="top"
                                >
                                    <span
                                        className="wishlist-detail-page__lock wishlist-detail-page__lock--locked"
                                        aria-label={
                                            isOwner ? 'Locked - Only you can edit' : 'Locked - Only owner can edit'
                                        }
                                        data-testid="wishlist-detail-page__lock--locked"
                                    >
                                        <LockSimple weight="fill" aria-hidden="true" />
                                    </span>
                                </Tooltip>
                            )}
                            {!wishlist.ownerLock && !isOwner && (
                                <Tooltip content="Unlocked - Anyone with permission can edit" side="top">
                                    <span className="wishlist-detail-page__lock" aria-label="Unlocked">
                                        <LockSimpleOpen weight="fill" aria-hidden="true" />
                                    </span>
                                </Tooltip>
                            )}
                            {!wishlist.monitored && (
                                <Badge variant="default" size="sm">
                                    Unmonitored
                                </Badge>
                            )}
                            {wishlist.monitored && (
                                <Badge variant="fulfilled" size="sm">
                                    Monitored
                                </Badge>
                            )}
                        </div>
                    </div>
                    <div className="wishlist-detail-page__header-actions">
                        {canEdit && (
                            <Button variant="secondary" size="sm" onClick={handleEdit} data-action="edit">
                                <PencilSimple weight="bold" aria-hidden="true" />
                                <span className="wishlist-detail-page__action-label">Edit</span>
                            </Button>
                        )}
                    </div>
                </header>

                <div className="wishlist-detail-page__meta">
                    {wishlist.ownerId && (
                        <span className="wishlist-detail-page__meta-item">
                            <User weight="regular" aria-hidden="true" />
                            <span className="wishlist-detail-page__meta-label">Owner:</span>
                            <span
                                className="wishlist-detail-page__meta-value"
                                data-testid="wishlist-detail-page__meta-value"
                            >
                                {wishlist.ownerName ?? 'Unknown'}
                            </span>
                        </span>
                    )}
                    {wishlist.filterName && (
                        <span className="wishlist-detail-page__meta-item">
                            <FunnelSimple weight="regular" aria-hidden="true" />
                            <span className="wishlist-detail-page__meta-label">Filter:</span>
                            <span
                                className="wishlist-detail-page__meta-value"
                                data-testid="wishlist-detail-page__meta-value"
                            >
                                {wishlist.filterName}
                            </span>
                        </span>
                    )}
                    {wishlist.qualityTierListName && (
                        <span className="wishlist-detail-page__meta-item">
                            <SlidersHorizontal weight="regular" aria-hidden="true" />
                            <span className="wishlist-detail-page__meta-label">Quality:</span>
                            <span
                                className="wishlist-detail-page__meta-value"
                                data-testid="wishlist-detail-page__meta-value"
                            >
                                {wishlist.qualityTierListName}
                            </span>
                        </span>
                    )}
                    <span className="wishlist-detail-page__meta-item">
                        <Clock weight="regular" aria-hidden="true" />
                        <span className="wishlist-detail-page__meta-label">Created:</span>
                        <span
                            className="wishlist-detail-page__meta-value"
                            data-testid="wishlist-detail-page__meta-value"
                        >
                            {formatRelativeTime(wishlist.createdAt)}
                        </span>
                    </span>
                </div>

                <div
                    className="wishlist-detail-page__summary"
                    aria-label="Content status summary"
                    data-testid="wishlist-detail-page__summary"
                >
                    <Badge variant="missing" size="sm" badgeStyle="outline" dot count={statusCounts.missing}>
                        Missing
                    </Badge>
                    <Badge variant="upgradable" size="sm" badgeStyle="outline" dot count={statusCounts.upgradable}>
                        Upgradable
                    </Badge>
                    <Badge variant="fulfilled" size="sm" badgeStyle="outline" dot count={statusCounts.fulfilled}>
                        Fulfilled
                    </Badge>
                    <Badge variant="fulfilledExternally" size="sm" badgeStyle="outline" dot count={statusCounts.fulfilledExternally}>
                        External
                    </Badge>
                </div>

                {contentItems.length === 0 ? (
                    <EmptyState
                        icon={<Star weight="duotone" />}
                        title="No content"
                        description="Content tracking will be available in a future update."
                        data-component="wishlist-detail-empty"
                    />
                ) : (
                    <div className="wishlist-detail-page__list" role="list" aria-label="Wishlist content">
                        {contentItems.map((item) => (
                            <WishlistContentRow key={item.id} item={item} onClick={handleItemClick} />
                        ))}
                    </div>
                )}
            </PullToRefresh>

            <WishlistForm
                open={formOpen}
                onOpenChange={(open) => {
                    if (!open) closeForm();
                }}
                initialValues={getFormInitialValues()}
                filters={filterOptions}
                qualityTierLists={qualityTierListOptions}
                tags={tagOptions}
                onSubmit={handleFormSubmit}
                isSubmitting={isUpdating}
                isOwner={isOwner}
                canEdit={canEdit}
            />
        </div>
    );
}

WishlistDetailPage.displayName = 'WishlistDetailPage';

interface WishlistContentRowProps {
    item: WishlistContentItem;
    onClick: (item: WishlistContentItem) => void;
}

function WishlistContentRow({ item, onClick }: WishlistContentRowProps) {
    const [imageError, setImageError] = useState(false);
    const KindIcon = Cube;

    const handleRowClick = useCallback(() => {
        onClick(item);
    }, [item, onClick]);

    const handleKeyDown = useCallback(
        (e: React.KeyboardEvent) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onClick(item);
            }
        },
        [item, onClick],
    );

    return (
        <div
            className={`wishlist-detail-page__row ${!item.monitored ? 'wishlist-detail-page__row--unmonitored' : ''}`}
            role="listitem"
            tabIndex={0}
            onClick={handleRowClick}
            onKeyDown={handleKeyDown}
            data-content-id={item.id}
            data-status={item.fulfillmentStatus}
            data-monitored={item.monitored}
        >
            <div className="wishlist-detail-page__row-poster">
                {item.posterUrl && !imageError ? (
                    <img src={item.posterUrl} alt="" loading="lazy" onError={() => setImageError(true)} />
                ) : (
                    <div className="wishlist-detail-page__poster-placeholder">
                        <ImageBroken weight="duotone" aria-hidden="true" />
                    </div>
                )}
            </div>
            <div className="wishlist-detail-page__row-info">
                <div className="wishlist-detail-page__row-title-row">
                    <span className="wishlist-detail-page__row-title">{item.title}</span>
                    {!item.monitored && (
                        <Badge variant="default" size="sm">
                            Unmonitored
                        </Badge>
                    )}
                </div>
                {item.subtitle && <span className="wishlist-detail-page__row-subtitle">{item.subtitle}</span>}
                <div className="wishlist-detail-page__row-meta">
                    <Badge variant="default" size="sm">
                        <KindIcon weight="duotone" aria-hidden="true" />
                        <span className="wishlist-detail-page__kind-label">{item.kind}</span>
                    </Badge>
                    <span className="wishlist-detail-page__searched-time">
                        <Clock weight="regular" aria-hidden="true" />
                        {formatRelativeTime(item.lastSearched)}
                    </span>
                </div>
            </div>
            <div className="wishlist-detail-page__row-status">
                <Badge
                    variant={fulfillmentVariants[item.fulfillmentStatus] as Parameters<typeof Badge>[0]['variant']}
                    size="sm"
                    dot={item.fulfillmentStatus === 'missing'}
                >
                    {item.fulfillmentStatus === 'missing' && (
                        <Warning weight="bold" aria-hidden="true" className="wishlist-detail-page__status-icon" />
                    )}
                    {fulfillmentLabels[item.fulfillmentStatus]}
                </Badge>
            </div>
        </div>
    );
}

export default WishlistDetailPage;
