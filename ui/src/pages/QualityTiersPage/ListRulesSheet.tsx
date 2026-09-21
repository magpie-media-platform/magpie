import { useCallback, useState } from 'react';
import { FloppyDiskIcon, XIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';

import { CriteriaEditor, type Criterion } from './CriteriaEditor';
import { hasBlankField } from './criteriaEditorUtils';
import { ExclusionsEditor, type ExclusionDraft } from './RulesEditor';

interface ListRulesSheetProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    tierListName: string;
    criteria: Criterion[];
    onCriteriaChange: (criteria: Criterion[]) => void;
    exclusions: ExclusionDraft[];
    onExclusionsChange: (exclusions: ExclusionDraft[]) => void;
    isSubmitting: boolean;
    onClose: () => void;
    onSave: () => void;
    conflictState: 'stale' | 'deleted' | null;
    showStaleSaveConfirmation: boolean;
    onCancelStaleSave: () => void;
    onConfirmStaleSave: () => void;
}

export function ListRulesSheet({
    open,
    onOpenChange,
    tierListName,
    criteria,
    onCriteriaChange,
    exclusions,
    onExclusionsChange,
    isSubmitting,
    onClose,
    onSave,
    conflictState,
    showStaleSaveConfirmation,
    onCancelStaleSave,
    onConfirmStaleSave,
}: ListRulesSheetProps) {
    const [showBlankFieldError, setShowBlankFieldError] = useState(false);
    const hasBlankCriterionField =
        criteria.some(hasBlankField) || exclusions.some((exclusion) => exclusion.criteria.some(hasBlankField));

    const handleSave = useCallback(() => {
        if (hasBlankCriterionField) {
            setShowBlankFieldError(true);
            return;
        }
        setShowBlankFieldError(false);
        onSave();
    }, [hasBlankCriterionField, onSave]);

    return (
        <Sheet open={open} onOpenChange={onOpenChange}>
            <SheetContent side="right" className="tier-form__sheet" data-component="list-rules-form">
                <SheetHeader>
                    <SheetTitle>Criteria and Exclusions for Every Tier</SheetTitle>
                    <SheetDescription>
                        These criteria and exclusions apply to every tier in "{tierListName}" before any tier is
                        evaluated.
                    </SheetDescription>
                </SheetHeader>

                <div className="tier-list-detail-page__list-rules-body">
                    {conflictState && (
                        <ExternalChangeConflictWarning
                            state={conflictState}
                            entityLabel="Tier list"
                            id="list-rules-external-change"
                        />
                    )}
                    <CriteriaEditor
                        criteria={criteria}
                        onChange={onCriteriaChange}
                        label="Criteria for the entire list"
                        scopeNoun="every tier in this list"
                        helpText="Every release must match all criteria before any tier in this list is considered."
                        disabled={isSubmitting}
                    />
                    <ExclusionsEditor
                        exclusions={exclusions}
                        onChange={onExclusionsChange}
                        label="Exclusions for the entire list"
                        scopeLabel="the entire list"
                        helpText="Matching any exclusion rejects the release before any tier in this list is evaluated."
                        disabled={isSubmitting}
                    />
                    {showBlankFieldError && hasBlankCriterionField && (
                        <p className="tier-form__error" role="alert">
                            Every criterion needs a field name
                        </p>
                    )}
                </div>

                <SheetFooter>
                    <Button
                        type="button"
                        variant="ghost"
                        disabled={isSubmitting}
                        onClick={onClose}
                        data-action="cancel"
                    >
                        <XIcon weight="bold" aria-hidden="true" />
                        Cancel
                    </Button>
                    <Button
                        type="button"
                        variant="primary"
                        loading={isSubmitting}
                        disabled={conflictState === 'deleted'}
                        onClick={handleSave}
                        data-action="save-list-criteria-exclusions"
                    >
                        <FloppyDiskIcon weight="bold" aria-hidden="true" />
                        Save Criteria and Exclusions for Every Tier
                    </Button>
                </SheetFooter>
            </SheetContent>
            <StaleSaveConfirmationDialog
                open={showStaleSaveConfirmation}
                title="Save changes based on older data?"
                description="This item changed after editing began. Saving will overwrite the newer changes."
                saving={isSubmitting}
                onCancel={onCancelStaleSave}
                onConfirm={onConfirmStaleSave}
            />
        </Sheet>
    );
}

ListRulesSheet.displayName = 'ListRulesSheet';
