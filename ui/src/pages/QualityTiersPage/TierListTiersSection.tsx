import { ListNumbersIcon } from '@phosphor-icons/react';

import { type useSortableList } from '@/components/ui/SortableList';

import { TierCard } from './TierCard';
import { type TierWithCriteria } from './tierListDetailMappers';

interface TierListTiersSectionProps {
    canManage: boolean;
    sortable: ReturnType<typeof useSortableList<TierWithCriteria>>;
    deletingTierId: string | null;
    isDeleting: boolean;
    cloningTierId: string | null;
    sharedListNames: ReadonlyMap<string, string[]> | null;
    onEditTier: (tierId: string) => void;
    onCloneTier: (tierId: string) => void;
    onDeleteTier: (tierId: string, tierName: string) => void;
    onToggleCutoff: (tierId: string) => void;
}

export function TierListTiersSection({
    canManage,
    sortable,
    deletingTierId,
    isDeleting,
    cloningTierId,
    sharedListNames,
    onEditTier,
    onCloneTier,
    onDeleteTier,
    onToggleCutoff,
}: TierListTiersSectionProps) {
    const reorderInstructions = canManage ? ' Drag tiers or use the move buttons to reorder.' : '';
    const displayTiers = sortable.items;

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
                            sharedWith={sharedListNames?.get(tier.id)}
                            isMoving={sortable.movingId === tier.id}
                            isReorderDisabled={sortable.isMoving}
                            isDragging={sortable.draggedId === tier.id}
                            isDropTarget={sortable.dropTargetId === tier.id}
                            dropPosition={sortable.dropTargetId === tier.id ? sortable.dropPosition : null}
                            focusDirection={
                                sortable.keyboardFocusTarget?.id === tier.id
                                    ? sortable.keyboardFocusTarget.direction
                                    : null
                            }
                            onEdit={onEditTier}
                            onClone={onCloneTier}
                            onDelete={onDeleteTier}
                            onToggleCutoff={onToggleCutoff}
                            onDragStart={sortable.handleDragStart}
                            onDragEnd={sortable.handleDragEnd}
                            onDragOver={sortable.handleDragOver}
                            onDragLeave={sortable.handleDragLeave}
                            onDrop={sortable.handleDrop}
                            onTouchStart={sortable.handleTouchStart}
                            onTouchMove={sortable.handleTouchMove}
                            onTouchEnd={sortable.handleTouchEnd}
                            onTouchCancel={sortable.handleTouchCancel}
                            onMove={sortable.handleMove}
                            onFocusRestored={sortable.handleFocusRestored}
                        />
                    ))
                )}
            </div>

            <div ref={sortable.dragImageRef} className="sortable-list__drag-image" aria-hidden="true" />
        </>
    );
}

TierListTiersSection.displayName = 'TierListTiersSection';
