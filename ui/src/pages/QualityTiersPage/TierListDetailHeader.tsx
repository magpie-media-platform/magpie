import { SlidersHorizontalIcon, PlusIcon, FunnelIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { MimeTypesEditor } from './MimeTypesEditor';

interface TierListDetailHeaderProps {
    tierListId: string;
    name: string;
    mimeTypes: string[];
    criteriaCount: number;
    exclusionCount: number;
    canManage: boolean;
    onOpenListRules: () => void;
    onAddTier: () => void;
    onSaveMimeTypes: (patterns: string[]) => Promise<void>;
}

export function TierListDetailHeader({
    tierListId,
    name,
    mimeTypes,
    criteriaCount,
    exclusionCount,
    canManage,
    onOpenListRules,
    onAddTier,
    onSaveMimeTypes,
}: TierListDetailHeaderProps) {
    const criteriaSummary = `${criteriaCount} ${criteriaCount === 1 ? 'criterion' : 'criteria'}`;
    const exclusionsSummary = `${exclusionCount} ${exclusionCount === 1 ? 'exclusion' : 'exclusions'}`;

    return (
        <header
            className="tier-list-detail-page__header"
            data-component="tier-list-detail-header"
            data-tier-list-id={tierListId}
        >
            <div className="tier-list-detail-page__header-main">
                <SlidersHorizontalIcon weight="duotone" className="tier-list-detail-page__icon" aria-hidden="true" />
                <div className="tier-list-detail-page__title-row">
                    <h1 className="tier-list-detail-page__title">{name}</h1>
                    <MimeTypesEditor value={mimeTypes} canEdit={canManage} onSave={onSaveMimeTypes} label={name} />
                </div>
            </div>
            {canManage && (
                <div className="tier-list-detail-page__header-actions">
                    <Button variant="secondary" size="sm" onClick={onOpenListRules} data-action="list-rules">
                        <FunnelIcon weight="bold" aria-hidden="true" />
                        <span className="tier-list-detail-page__add-label">
                            Criteria + Exclusions
                            {criteriaCount + exclusionCount > 0 ? ` (${criteriaSummary}, ${exclusionsSummary})` : ''}
                        </span>
                    </Button>
                    <Button variant="primary" size="sm" onClick={onAddTier} data-action="add-tier">
                        <PlusIcon weight="bold" aria-hidden="true" />
                        <span className="tier-list-detail-page__add-label">Add Tier</span>
                    </Button>
                </div>
            )}
        </header>
    );
}

TierListDetailHeader.displayName = 'TierListDetailHeader';
