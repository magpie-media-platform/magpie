import { type RefObject } from 'react';
import { ListNumbersIcon } from '@phosphor-icons/react';

import { TierCard } from './TierCard';
import { type TierWithCriteria } from './tierListDetailMappers';

interface TierListTiersSectionProps {
    canManage: boolean;
    displayTiers: TierWithCriteria[];
    deletingTierId: string | null;
    isDeleting: boolean;
    cloningTierId: string | null;
    draggedTierId: string | null;
    dropTargetId: string | null;
    dropPosition: 'before' | 'after' | null;
    dragImageRef: RefObject<HTMLDivElement | null>;
    onEditTier: (tierId: string) => void;
    onCloneTier: (tierId: string) => void;
    onDeleteTier: (tierId: string, tierName: string) => void;
    onToggleCutoff: (tierId: string) => void;
    onDragStart: (tierId: string, event: React.DragEvent) => void;
    onDragEnd: () => void;
    onDragOver: (tierId: string, event: React.DragEvent) => void;
    onDragLeave: () => void;
    onDrop: (tierId: string, event: React.DragEvent) => void;
}

export function TierListTiersSection({
    canManage,
    displayTiers,
    deletingTierId,
    isDeleting,
    cloningTierId,
    draggedTierId,
    dropTargetId,
    dropPosition,
    dragImageRef,
    onEditTier,
    onCloneTier,
    onDeleteTier,
    onToggleCutoff,
    onDragStart,
    onDragEnd,
    onDragOver,
    onDragLeave,
    onDrop,
}: TierListTiersSectionProps) {
    const reorderInstructions = canManage ? ' Drag tiers to reorder.' : '';

    return (
        <>
            <p className="tier-list-detail-page__description">
                Tiers are listed from highest to lowest priority. Higher tiers are preferred. The cutoff is the lowest
                acceptable tier.
                {reorderInstructions}
            </p>

            <div className="tier-list-detail-page__tiers" role="list" aria-label="Quality tiers">
                {displayTiers.length === 0 ? (
                    <div className="tier-list-detail-page__empty">
                        <ListNumbersIcon
                            weight="duotone"
                            className="tier-list-detail-page__empty-icon"
                            aria-hidden="true"
                        />
                        <p className="tier-list-detail-page__empty-text">No tiers configured</p>
                        <p className="tier-list-detail-page__empty-hint">Add tiers to define quality preferences</p>
                    </div>
                ) : (
                    displayTiers.map((tier, index) => (
                        <TierCard
                            key={tier.id}
                            tier={tier}
                            priority={index + 1}
                            totalTiers={displayTiers.length}
                            isDeleting={isDeleting && deletingTierId === tier.id}
                            isCloning={cloningTierId === tier.id}
                            canManage={canManage}
                            isDragging={draggedTierId === tier.id}
                            isDropTarget={dropTargetId === tier.id}
                            dropPosition={dropTargetId === tier.id ? dropPosition : null}
                            onEdit={onEditTier}
                            onClone={onCloneTier}
                            onDelete={onDeleteTier}
                            onToggleCutoff={onToggleCutoff}
                            onDragStart={onDragStart}
                            onDragEnd={onDragEnd}
                            onDragOver={onDragOver}
                            onDragLeave={onDragLeave}
                            onDrop={onDrop}
                        />
                    ))
                )}
            </div>

            <div ref={dragImageRef} className="tier-list-detail-page__drag-image" aria-hidden="true" />
        </>
    );
}

TierListTiersSection.displayName = 'TierListTiersSection';
