import { type DragEvent, type TouchEvent } from 'react';
import { TrashIcon, PencilSimpleIcon, CopyIcon, CheckFatIcon, CircleNotchIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import {
    SortableListControls,
    sortableListItemClassNames,
    type SortableDropPosition,
    type SortableMoveDirection,
} from '@/components/ui/SortableList';
import { Tooltip } from '@/components/ui/Tooltip';

import { formatOperator, type TierWithCriteria } from './tierListDetailMappers';

interface TierCardProps {
    tier: TierWithCriteria;
    priority: number;
    totalTiers: number;
    isDeleting: boolean;
    isCloning: boolean;
    canManage: boolean;
    isMoving: boolean;
    isReorderDisabled: boolean;
    isDragging: boolean;
    isDropTarget: boolean;
    dropPosition: SortableDropPosition | null;
    focusDirection: SortableMoveDirection | null;
    onEdit: (tierId: string) => void;
    onClone: (tierId: string) => void;
    onDelete: (tierId: string, tierName: string) => void;
    onToggleCutoff: (tierId: string) => void;
    onDragStart: (tier: TierWithCriteria, event: DragEvent) => void;
    onDragEnd: () => void;
    onDragOver: (tierId: string, event: DragEvent) => void;
    onDragLeave: () => void;
    onDrop: (tierId: string, event: DragEvent) => void;
    onTouchStart: (tier: TierWithCriteria, event: TouchEvent) => void;
    onTouchMove: (event: TouchEvent) => void;
    onTouchEnd: () => void;
    onTouchCancel: () => void;
    onMove: (tier: TierWithCriteria, direction: SortableMoveDirection) => void;
    onFocusRestored: () => void;
}

export function TierCard({
    tier,
    priority,
    totalTiers,
    isDeleting,
    isCloning,
    canManage,
    isMoving,
    isReorderDisabled,
    isDragging,
    isDropTarget,
    dropPosition,
    focusDirection,
    onEdit,
    onClone,
    onDelete,
    onToggleCutoff,
    onDragStart,
    onDragEnd,
    onDragOver,
    onDragLeave,
    onDrop,
    onTouchStart,
    onTouchMove,
    onTouchEnd,
    onTouchCancel,
    onMove,
    onFocusRestored,
}: TierCardProps) {
    const tierClasses = [
        'tier-list-detail-page__tier',
        sortableListItemClassNames(isDragging, isDropTarget, dropPosition),
        tier.isCutoff && 'tier-list-detail-page__tier--cutoff',
    ]
        .filter(Boolean)
        .join(' ');

    return (
        <div
            className={tierClasses}
            role="listitem"
            data-tier-id={tier.id}
            data-sortable-list-item-id={tier.id}
            data-cutoff={tier.isCutoff || undefined}
            data-deleting={isDeleting || undefined}
            data-dragging={isDragging || undefined}
            onDragOver={(e) => onDragOver(tier.id, e)}
            onDragLeave={onDragLeave}
            onDrop={(e) => onDrop(tier.id, e)}
        >
            <div className="tier-list-detail-page__tier-header">
                {canManage && (
                    <SortableListControls
                        label={tier.name}
                        position={priority - 1}
                        itemCount={totalTiers}
                        moving={isMoving}
                        disabled={isReorderDisabled}
                        focusDirection={focusDirection}
                        dragHandleClassName="tier-list-detail-page__drag-handle"
                        dragHandleTestId="tier-list-detail-page__drag-handle"
                        actionName="tier"
                        onDragStart={(event) => onDragStart(tier, event)}
                        onDragEnd={onDragEnd}
                        onTouchStart={(event) => onTouchStart(tier, event)}
                        onTouchMove={onTouchMove}
                        onTouchEnd={onTouchEnd}
                        onTouchCancel={onTouchCancel}
                        onMove={(direction) => onMove(tier, direction)}
                        onFocusRestored={onFocusRestored}
                    />
                )}
                <div className="tier-list-detail-page__tier-priority">
                    <span className="tier-list-detail-page__priority-number">{priority}</span>
                    <span className="tier-list-detail-page__priority-of">of {totalTiers}</span>
                </div>
                <div className="tier-list-detail-page__tier-info">
                    <div className="tier-list-detail-page__tier-title-row">
                        <span className="tier-list-detail-page__tier-name">{tier.name}</span>
                        {tier.isCutoff && (
                            <Badge variant="healthy" size="sm">
                                <CheckFatIcon
                                    weight="fill"
                                    className="tier-list-detail-page__cutoff-icon"
                                    aria-hidden="true"
                                />
                                Cutoff
                            </Badge>
                        )}
                    </div>
                    <div className="tier-list-detail-page__tier-meta">
                        <span className="tier-list-detail-page__criteria-count">
                            {tier.criteria.length} {tier.criteria.length === 1 ? 'criterion' : 'criteria'}
                        </span>
                        {tier.exclusions.length > 0 && (
                            <span className="tier-list-detail-page__criteria-count" data-testid="tier-exclusion-count">
                                {tier.exclusions.length} {tier.exclusions.length === 1 ? 'exclusion' : 'exclusions'}
                            </span>
                        )}
                    </div>
                </div>
                {canManage && (
                    <div className="tier-list-detail-page__tier-actions card-actions">
                        <Tooltip content={tier.isCutoff ? 'Clear cutoff' : 'Set as cutoff'}>
                            <button
                                type="button"
                                className={`card-actions__btn${tier.isCutoff ? ' card-actions__btn--active' : ''}`}
                                onClick={() => onToggleCutoff(tier.id)}
                                aria-label={
                                    tier.isCutoff ? `Clear cutoff on ${tier.name}` : `Set ${tier.name} as cutoff`
                                }
                                aria-pressed={tier.isCutoff}
                                data-action="set-cutoff"
                                data-target={tier.id}
                            >
                                <CheckFatIcon weight={tier.isCutoff ? 'fill' : 'bold'} />
                            </button>
                        </Tooltip>
                        <Tooltip content="Edit">
                            <button
                                type="button"
                                className="card-actions__btn"
                                onClick={() => onEdit(tier.id)}
                                aria-label={`Edit ${tier.name}`}
                                data-action="edit"
                                data-target={tier.id}
                            >
                                <PencilSimpleIcon weight="bold" />
                            </button>
                        </Tooltip>
                        <Tooltip content="Clone tier">
                            <button
                                type="button"
                                className={`card-actions__btn${isCloning ? ' card-actions__btn--loading' : ''}`}
                                onClick={() => onClone(tier.id)}
                                disabled={isCloning}
                                aria-label={isCloning ? `Cloning ${tier.name}` : `Clone ${tier.name}`}
                                data-action="clone-tier"
                                data-target={tier.id}
                            >
                                {isCloning ? (
                                    <CircleNotchIcon
                                        weight="bold"
                                        className="card-actions__spinner"
                                        aria-hidden="true"
                                    />
                                ) : (
                                    <CopyIcon weight="bold" />
                                )}
                            </button>
                        </Tooltip>
                        <Tooltip content="Delete">
                            <button
                                type="button"
                                className={`card-actions__btn card-actions__btn--danger${isDeleting ? ' card-actions__btn--loading-danger' : ''}`}
                                onClick={() => onDelete(tier.id, tier.name)}
                                disabled={isDeleting}
                                aria-label={isDeleting ? `Deleting ${tier.name}` : `Delete ${tier.name}`}
                                data-action="delete"
                                data-target={tier.id}
                            >
                                {isDeleting ? (
                                    <CircleNotchIcon
                                        weight="bold"
                                        className="card-actions__spinner"
                                        aria-hidden="true"
                                    />
                                ) : (
                                    <TrashIcon weight="bold" />
                                )}
                            </button>
                        </Tooltip>
                    </div>
                )}
            </div>

            {tier.criteria.length > 0 && (
                <div className="tier-list-detail-page__criteria">
                    {tier.criteria.map((criterion) => (
                        <span
                            key={criterion.id}
                            className="tier-list-detail-page__criterion"
                            data-criterion-id={criterion.id}
                        >
                            <span className="tier-list-detail-page__criterion-field">{criterion.field}</span>
                            <span className="tier-list-detail-page__criterion-operator">
                                {formatOperator(criterion.operator)}
                            </span>
                            <span className="tier-list-detail-page__criterion-value">{criterion.value || '(any)'}</span>
                        </span>
                    ))}
                </div>
            )}
        </div>
    );
}

TierCard.displayName = 'TierCard';
