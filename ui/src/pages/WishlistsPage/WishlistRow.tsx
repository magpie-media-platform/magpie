import { useCallback } from 'react';
import {
    ClockIcon,
    FunnelSimpleIcon,
    LockSimpleIcon,
    LockSimpleOpenIcon,
    PencilSimpleIcon,
    TrashIcon,
    CircleNotchIcon,
    UserIcon,
} from '@phosphor-icons/react';

import { Tooltip } from '@/components/ui/Tooltip';
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
    monitored: boolean;
    ownerLock: boolean;
    lastSyncedAt: string | null;
    createdAt: string;
    contentFilterCriteria: ContentFilterCriteria | null;
}

interface WishlistRowProps {
    wishlist: WishlistItem;
    isLocking: boolean;
    isOwner: boolean;
    canEdit: boolean;
    canDelete: boolean;
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
    onLock: (id: string) => void;
    onUnlock: (id: string) => void;
    onClick: (id: string) => void;
}

export function WishlistRow({
    wishlist,
    isLocking,
    isOwner,
    canEdit,
    canDelete,
    onEdit,
    onDelete,
    onLock,
    onUnlock,
    onClick,
}: WishlistRowProps) {
    const formattedCriteriaQuery = useFormattedCriteria(wishlist.contentFilterCriteria);
    const criteriaQuery = formattedCriteriaQuery.data ?? '';

    const handleRowClick = useCallback(
        (e: React.MouseEvent) => {
            if ((e.target as HTMLElement | null)?.closest('.wishlists-page__item-actions')) {
                return;
            }
            onClick(wishlist.id);
        },
        [wishlist.id, onClick],
    );

    const handleKeyDown = useCallback(
        (e: React.KeyboardEvent) => {
            if (e.key === 'Enter' || e.key === ' ') {
                if ((e.target as HTMLElement | null)?.closest('.wishlists-page__item-actions')) {
                    return;
                }
                e.preventDefault();
                onClick(wishlist.id);
            }
        },
        [wishlist.id, onClick],
    );

    return (
        <div
            className={`wishlists-page__item${wishlist.monitored ? '' : ' wishlists-page__item--unmonitored'}`}
            role="listitem"
            tabIndex={0}
            onClick={handleRowClick}
            onKeyDown={handleKeyDown}
            data-wishlist-id={wishlist.id}
            data-monitored={wishlist.monitored}
            data-owner={isOwner || undefined}
            data-locked={wishlist.ownerLock || undefined}
            data-testid={wishlist.monitored ? 'wishlists-page__item' : 'wishlists-page__item--unmonitored'}
        >
            <div className="wishlists-page__item-main">
                <div className="wishlists-page__item-info">
                    <div className="wishlists-page__item-header">
                        <span className="wishlists-page__item-name">{wishlist.name}</span>
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
                            onClick={() => (wishlist.ownerLock ? onUnlock(wishlist.id) : onLock(wishlist.id))}
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
