import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
    ClockIcon,
    EyeIcon,
    EyeSlashIcon,
    FunnelSimpleIcon,
    LockSimpleIcon,
    LockSimpleOpenIcon,
    PencilSimpleIcon,
    TrashIcon,
    CircleNotchIcon,
    TreeViewIcon,
    UserIcon,
} from '@phosphor-icons/react';

import { Tooltip } from '@/components/ui/Tooltip';
import { Badge } from '@/components/ui/Badge';
import { useFormattedCriteria, type ContentFilterCriteria } from '@/hooks/useFilters';
import { formatRelativeTime } from '@/lib/formatTime';
import { formatDateTime } from '@/lib/dateFormatting';

import '@/styles/card-actions.css';

export interface WishlistItem {
    id: string;
    name: string;
    ownerId: string | null;
    ownerName: string | null;
    filterName: string | null;
    qualityTierListName: string | null;
    enabled: boolean;
    monitoring: boolean | null;
    ownerLock: boolean;
    lastSyncedAt: string | null;
    createdAt: string;
    contentFilterCriteria: ContentFilterCriteria | null;
}

interface WishlistRowProps {
    wishlist: WishlistItem;
    isOwner: boolean;
    canEdit: boolean;
    canDelete: boolean;
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
    onLock: (id: string) => Promise<void>;
    onUnlock: (id: string) => Promise<void>;
}

export function WishlistRow({
    wishlist,
    isOwner,
    canEdit,
    canDelete,
    onEdit,
    onDelete,
    onLock,
    onUnlock,
}: WishlistRowProps) {
    const formattedCriteriaQuery = useFormattedCriteria(wishlist.contentFilterCriteria);
    const criteriaQuery = formattedCriteriaQuery.data ?? '';

    const MonitoringIcon = wishlist.monitoring === null ? TreeViewIcon : wishlist.monitoring ? EyeIcon : EyeSlashIcon;
    const monitoringLabel =
        wishlist.monitoring === null ? 'Inherit' : wishlist.monitoring ? 'Monitored' : 'Not monitored';

    const [isLocking, setIsLocking] = useState(false);
    const handleToggleLock = async () => {
        setIsLocking(true);
        try {
            await (wishlist.ownerLock ? onUnlock(wishlist.id) : onLock(wishlist.id));
        } finally {
            setIsLocking(false);
        }
    };

    return (
        <div
            className={`wishlists-page__item${wishlist.enabled ? '' : ' wishlists-page__item--disabled'}`}
            role="listitem"
            data-wishlist-id={wishlist.id}
            data-enabled={wishlist.enabled}
            data-owner={isOwner || undefined}
            data-locked={wishlist.ownerLock || undefined}
            data-testid={wishlist.enabled ? 'wishlists-page__item' : 'wishlists-page__item--disabled'}
        >
            <div className="wishlists-page__item-main">
                <div className="wishlists-page__item-info">
                    <div className="wishlists-page__item-header">
                        <Link to={`/wishlists/${wishlist.id}`} className="wishlists-page__item-name">
                            {wishlist.name}
                        </Link>
                        <Badge variant={wishlist.enabled ? 'fulfilled' : 'default'} size="sm">
                            {wishlist.enabled ? 'Enabled' : 'Disabled'}
                        </Badge>
                        {wishlist.ownerLock && (
                            <Tooltip
                                content={isOwner ? 'Locked. Only you can edit.' : 'Locked. Only the owner can edit.'}
                                side="top"
                            >
                                <span
                                    className="wishlists-page__owner-lock wishlists-page__owner-lock--locked"
                                    data-owner-lock="true"
                                    aria-label={
                                        isOwner ? 'Locked. Only you can edit.' : 'Locked. Only the owner can edit.'
                                    }
                                >
                                    <LockSimpleIcon weight="fill" aria-hidden="true" />
                                </span>
                            </Tooltip>
                        )}
                        {!wishlist.ownerLock && !isOwner && (
                            <Tooltip content="Unlocked. Anyone with permission can edit." side="top">
                                <span
                                    className="wishlists-page__owner-lock"
                                    aria-label="Unlocked. Anyone with permission can edit."
                                >
                                    <LockSimpleOpenIcon weight="fill" aria-hidden="true" />
                                </span>
                            </Tooltip>
                        )}
                    </div>
                    <div className="wishlists-page__item-meta">
                        {wishlist.ownerId && (
                            <span className="wishlists-page__item-owner" data-testid="wishlists-page__item-owner">
                                <UserIcon weight="regular" aria-hidden="true" />
                                {wishlist.ownerName ?? 'Unknown'}
                            </span>
                        )}
                        {wishlist.filterName && (
                            <span className="wishlists-page__item-filter">
                                <FunnelSimpleIcon weight="regular" aria-hidden="true" />
                                {wishlist.filterName}
                            </span>
                        )}
                        <span
                            className="wishlists-page__item-synced"
                            title={wishlist.lastSyncedAt ? formatDateTime(wishlist.lastSyncedAt) : undefined}
                        >
                            <ClockIcon weight="regular" aria-hidden="true" />
                            {formatRelativeTime(wishlist.lastSyncedAt)}
                        </span>
                        <span className="wishlists-page__item-monitoring" data-testid="wishlists-page__item-monitoring">
                            <MonitoringIcon weight="regular" aria-hidden="true" />
                            {monitoringLabel}
                        </span>
                    </div>
                    {criteriaQuery && (
                        <span
                            className="wishlists-page__item-query"
                            title={criteriaQuery}
                            data-testid="wishlists-page__item-query"
                        >
                            {criteriaQuery}
                        </span>
                    )}
                </div>
                <div className="card-actions wishlists-page__item-actions">
                    {isOwner && (
                        <button
                            type="button"
                            className={`card-actions__btn${isLocking ? ' card-actions__btn--loading' : ''}${wishlist.ownerLock ? ' wishlists-page__action-btn--locked' : ''}`}
                            onClick={() => void handleToggleLock()}
                            disabled={isLocking}
                            aria-label={
                                isLocking
                                    ? `Updating access for "${wishlist.name}".`
                                    : wishlist.ownerLock
                                      ? `Unlock ${wishlist.name}`
                                      : `Lock ${wishlist.name}`
                            }
                            data-action={wishlist.ownerLock ? 'unlock' : 'lock'}
                            data-target={wishlist.id}
                            title={
                                isLocking
                                    ? 'Updating...'
                                    : wishlist.ownerLock
                                      ? 'Unlock so others can edit'
                                      : 'Lock so only you can edit'
                            }
                        >
                            {isLocking ? (
                                <CircleNotchIcon weight="bold" className="card-actions__spinner" aria-hidden="true" />
                            ) : wishlist.ownerLock ? (
                                <LockSimpleIcon weight="bold" />
                            ) : (
                                <LockSimpleOpenIcon weight="bold" />
                            )}
                        </button>
                    )}
                    {canEdit && (
                        <button
                            type="button"
                            className="card-actions__btn"
                            onClick={() => onEdit(wishlist.id)}
                            aria-label={`Edit ${wishlist.name}`}
                            data-action="edit"
                            data-target={wishlist.id}
                            title="Edit"
                        >
                            <PencilSimpleIcon weight="bold" />
                        </button>
                    )}
                    {canDelete && (
                        <button
                            type="button"
                            className="card-actions__btn card-actions__btn--danger"
                            onClick={() => onDelete(wishlist.id)}
                            aria-label={`Delete ${wishlist.name}`}
                            data-action="delete"
                            data-target={wishlist.id}
                            title="Delete"
                        >
                            <TrashIcon weight="bold" />
                        </button>
                    )}
                </div>
            </div>
        </div>
    );
}
