import { useMemo, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import {
    ArrowLeftIcon,
    StarIcon,
    CalendarBlankIcon,
    ClockIcon,
    FunnelSimpleIcon,
    LockSimpleIcon,
    LockSimpleOpenIcon,
    PencilSimpleIcon,
    ArrowsClockwiseIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { Button } from '@/components/ui/Button';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { useWishlist, useWishlists, useWishlistRefresh, type ApiWishlist } from '@/hooks/useWishlists';
import { useTags } from '@/hooks/useTags';
import { useFormattedCriteria, isCriteriaEmpty, type ContentFilterCriteria } from '@/hooks/useFilters';
import { useFormOptions } from '@/hooks/useFormOptions';
import { useAuth, Role } from '@/auth';
import { formatDateTime } from '@/lib/dateFormatting';
import { canEditOwned } from '@/lib/ownership';
import { getErrorMessage, isNotFoundError } from '@/lib/errors';
import { EmptyState } from '@/components/ui/EmptyState';

import { WishlistForm, type WishlistFormValues } from './WishlistForm';
import { WishlistDiscoveredList } from './WishlistDiscoveredList';
import { resolveFilterContentType } from './wishlistFormUtils';
import { buildWishlistPatch } from './buildWishlistPatch';

import './WishlistDetailPage.css';

interface WishlistDisplayItem {
    id: string;
    name: string;
    ownerId: string | null;
    enabled: boolean;
    monitoring: boolean | null;
    ownerLock: boolean;
    lastSyncedAt: string | null;
    createdAt: string;
    contentFilterId: string | null;
    qualityTierListId: string | null;
    rootFolderId: string | null;
    contentFilterCriteria: ContentFilterCriteria | null;
}

function mapApiWishlistToDisplayItem(apiWishlist: ApiWishlist): WishlistDisplayItem {
    return {
        id: apiWishlist.id,
        name: apiWishlist.name,
        ownerId: apiWishlist.ownerUserId ?? null,
        enabled: apiWishlist.enabled,
        monitoring: apiWishlist.monitoring ?? null,
        ownerLock: apiWishlist.ownerLock,
        lastSyncedAt: apiWishlist.lastSyncedAt ?? null,
        createdAt: apiWishlist.createdAt,
        contentFilterId: apiWishlist.contentFilterId ?? null,
        qualityTierListId: apiWishlist.qualityTierListId ?? null,
        rootFolderId: apiWishlist.rootFolderId ?? null,
        contentFilterCriteria: apiWishlist.contentFilter?.criteria ?? null,
    };
}

export function WishlistDetailPage() {
    useDocumentTitle('Wishlist');
    const { wishlistId } = useParams<{ wishlistId: string }>();
    const { hasRole, user } = useAuth();
    const canManage = hasRole(Role.Manager);
    const { toast } = useToast();

    const [formOpen, , openForm, closeForm] = useToggle();

    const { data: apiWishlist, isLoading, isError, error, refetch } = useWishlist(wishlistId);
    const { updateWishlist, isUpdating } = useWishlists();
    const { mutateAsync: refreshWishlist, isPending: isRefreshing } = useWishlistRefresh();

    const { rootFolderOptions, tagOptions } = useFormOptions();
    const { tags: entityTags } = useTags(wishlistId ? { entityId: wishlistId } : undefined);

    const wishlist = useMemo(() => (apiWishlist ? mapApiWishlistToDisplayItem(apiWishlist) : null), [apiWishlist]);

    const criteria = wishlist?.contentFilterCriteria ?? null;
    const hasCriteria = !isCriteriaEmpty(criteria);
    const formattedCriteriaQuery = useFormattedCriteria(criteria);
    const criteriaQuery = formattedCriteriaQuery.data ?? '';
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

    const handlePageRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const handleEdit = useCallback(() => {
        openForm();
    }, [openForm]);

    const handleRefresh = useCallback(async () => {
        if (!wishlist) {
            return;
        }
        try {
            await refreshWishlist(wishlist.id);
            toast({
                title: 'Refresh Queued',
                description: 'Searching for matches.',
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Refresh Failed',
                description: 'Failed to queue a refresh for this wishlist.',
                variant: 'error',
            });
        }
    }, [wishlist, refreshWishlist, toast]);

    const handleFormSubmit = useCallback(
        async (values: WishlistFormValues) => {
            if (!wishlist) {
                return;
            }

            try {
                const patch = buildWishlistPatch(
                    values,
                    entityTags.map((t) => t.name),
                );

                await updateWishlist(wishlist.id, patch);
                await refetch();

                toast({
                    title: 'Wishlist Updated',
                    description: `"${values.name}" updated.`,
                    variant: 'success',
                });
            } catch (error) {
                toast({
                    title: 'Update Failed',
                    description: 'Failed to update wishlist.',
                    variant: 'error',
                });
                throw error;
            }
        },
        [wishlist, entityTags, updateWishlist, refetch, toast],
    );

    const getFormInitialValues = useCallback((): WishlistFormValues | null => {
        if (!wishlist) {
            return null;
        }

        const formCriteria = wishlist.contentFilterCriteria ?? {};

        return {
            id: wishlist.id,
            name: wishlist.name,
            filterId: wishlist.contentFilterId,
            contentType: resolveFilterContentType(formCriteria) ?? '',
            criteria: formCriteria,
            qualityTierListId: wishlist.qualityTierListId,
            rootFolderId: wishlist.rootFolderId,
            enabled: wishlist.enabled,
            monitoring: wishlist.monitoring,
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

    const isNotFound = isNotFoundError(error);

    if (isError && !isNotFound) {
        return (
            <div className="wishlist-detail-page" data-page="wishlist-detail">
                <Breadcrumb items={breadcrumbs} className="wishlist-detail-page__breadcrumb" />
                <EmptyState
                    icon={<WarningCircleIcon weight="duotone" />}
                    title="Failed to load wishlist"
                    description={getErrorMessage(error)}
                    action={
                        <Button variant="secondary" size="sm" onClick={() => refetch()}>
                            Retry
                        </Button>
                    }
                />
            </div>
        );
    }

    if (!wishlist) {
        return (
            <div className="wishlist-detail-page" data-page="wishlist-detail">
                <div className="wishlist-detail-page__not-found">
                    <StarIcon weight="duotone" className="wishlist-detail-page__not-found-icon" aria-hidden="true" />
                    <h1>Wishlist Not Found</h1>
                    <p>The wishlist you are looking for does not exist.</p>
                    <Link to="/wishlists" className="wishlist-detail-page__back-link">
                        <ArrowLeftIcon weight="bold" aria-hidden="true" />
                        Back to Wishlists
                    </Link>
                </div>
            </div>
        );
    }

    return (
        <div className="wishlist-detail-page" data-page="wishlist-detail">
            <PullToRefresh onRefresh={handlePageRefresh} className="wishlist-detail-page__pull-to-refresh">
                <Breadcrumb items={breadcrumbs} className="wishlist-detail-page__breadcrumb" />

                <header
                    className="wishlist-detail-page__header"
                    data-component="wishlist-detail-header"
                    data-wishlist-id={wishlist.id}
                >
                    <div className="wishlist-detail-page__header-main">
                        <StarIcon weight="duotone" className="wishlist-detail-page__icon" aria-hidden="true" />
                        <div className="wishlist-detail-page__title-row">
                            <h1 className="wishlist-detail-page__title">{wishlist.name}</h1>
                            {wishlist.ownerLock && (
                                <Tooltip
                                    content={
                                        isOwner ? 'Locked. Only you can edit.' : 'Locked. Only the owner can edit.'
                                    }
                                    side="top"
                                >
                                    <span
                                        className="wishlist-detail-page__lock wishlist-detail-page__lock--locked"
                                        aria-label={
                                            isOwner ? 'Locked. Only you can edit.' : 'Locked. Only the owner can edit.'
                                        }
                                        data-testid="wishlist-detail-page__lock--locked"
                                    >
                                        <LockSimpleIcon weight="fill" aria-hidden="true" />
                                    </span>
                                </Tooltip>
                            )}
                            {!wishlist.ownerLock && !isOwner && (
                                <Tooltip content="Unlocked - Anyone with permission can edit" side="top">
                                    <span className="wishlist-detail-page__lock" aria-label="Unlocked">
                                        <LockSimpleOpenIcon weight="fill" aria-hidden="true" />
                                    </span>
                                </Tooltip>
                            )}
                            {!wishlist.enabled && (
                                <Badge variant="default" size="sm">
                                    Disabled
                                </Badge>
                            )}
                            {wishlist.enabled && (
                                <Badge variant="fulfilled" size="sm">
                                    Enabled
                                </Badge>
                            )}
                            <Badge variant="default" size="sm">
                                {wishlist.monitoring === null
                                    ? 'Inherit'
                                    : wishlist.monitoring
                                      ? 'Monitored'
                                      : 'Not monitored'}
                            </Badge>
                        </div>
                    </div>
                    <div className="wishlist-detail-page__header-actions">
                        {isOwner && (
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={handleRefresh}
                                disabled={isRefreshing}
                                data-action="refresh"
                            >
                                <ArrowsClockwiseIcon
                                    weight="bold"
                                    aria-hidden="true"
                                    className={
                                        isRefreshing ? 'wishlist-detail-page__refresh-icon--spinning' : undefined
                                    }
                                />
                                <span className="wishlist-detail-page__action-label">Refresh</span>
                            </Button>
                        )}
                        {canEdit && (
                            <Button variant="secondary" size="sm" onClick={handleEdit} data-action="edit">
                                <PencilSimpleIcon weight="bold" aria-hidden="true" />
                                <span className="wishlist-detail-page__action-label">Edit</span>
                            </Button>
                        )}
                    </div>
                </header>

                <div className="wishlist-detail-page__meta">
                    <span className="wishlist-detail-page__meta-item">
                        <CalendarBlankIcon weight="regular" aria-hidden="true" />
                        <span className="wishlist-detail-page__meta-label">Created:</span>
                        <time
                            className="wishlist-detail-page__meta-value"
                            dateTime={wishlist.createdAt}
                            data-testid="wishlist-detail-page__created-at"
                        >
                            {formatDateTime(wishlist.createdAt)}
                        </time>
                    </span>
                    <span className="wishlist-detail-page__meta-item">
                        <ClockIcon weight="regular" aria-hidden="true" />
                        <span className="wishlist-detail-page__meta-label">Last refresh:</span>
                        {wishlist.lastSyncedAt ? (
                            <time
                                className="wishlist-detail-page__meta-value"
                                dateTime={wishlist.lastSyncedAt}
                                data-testid="wishlist-detail-page__last-refresh"
                            >
                                {formatDateTime(wishlist.lastSyncedAt)}
                            </time>
                        ) : (
                            <span
                                className="wishlist-detail-page__meta-value"
                                data-testid="wishlist-detail-page__last-refresh"
                            >
                                Never
                            </span>
                        )}
                    </span>
                </div>

                <div className="wishlist-detail-page__criteria" data-component="wishlist-detail-criteria">
                    <span className="wishlist-detail-page__criteria-label">
                        <FunnelSimpleIcon weight="regular" aria-hidden="true" />
                        Monitoring
                    </span>
                    {hasCriteria ? (
                        <code
                            className="wishlist-detail-page__criteria-query"
                            data-testid="wishlist-detail-page__query"
                        >
                            {criteriaQuery || '…'}
                        </code>
                    ) : (
                        <span
                            className="wishlist-detail-page__criteria-empty"
                            data-testid="wishlist-detail-page__criteria-empty"
                        >
                            No criteria set yet
                        </span>
                    )}
                </div>

                <WishlistDiscoveredList wishlistId={wishlist.id} onOpenFilter={handleEdit} canEditFilter={canEdit} />
            </PullToRefresh>

            <WishlistForm
                open={formOpen}
                onOpenChange={(open) => {
                    if (!open) closeForm();
                }}
                initialValues={getFormInitialValues()}
                rootFolders={rootFolderOptions}
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

export default WishlistDetailPage;
