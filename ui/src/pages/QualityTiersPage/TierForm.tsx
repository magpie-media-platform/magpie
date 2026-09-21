import { useState, useCallback, useEffect, useRef, type ReactNode } from 'react';
import { FloppyDiskIcon, XIcon, InfoIcon, CheckFatIcon } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { stripBlankRows, useFormDirty } from '@/hooks/useFormDirty';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';
import type { ApiQualityTierRule } from '@/hooks/useQualityTierLists';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { CriteriaEditor, type Criterion, type CriterionOperator } from './CriteriaEditor';
import { hasBlankField, isBlankCriterion } from './criteriaEditorUtils';
import { ExclusionsEditor, type ExclusionDraft } from './RulesEditor';
import { normalizeExclusions } from './rulesEditorUtils';

export type { Criterion, CriterionOperator };
export type { ExclusionDraft };

import './TierForm.css';
import './TierForms.css';

export interface TierFormValues {
    id?: string;
    name: string;
    isCutoff: boolean;
    criteria: Criterion[];
    exclusions?: ExclusionDraft[];
    sourceRules?: ApiQualityTierRule[];
}

interface TierFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: TierFormValues | null;

    tierListName?: string;

    tierListId?: string;

    onSubmit: (values: TierFormValues) => void | Promise<void>;

    isSubmitting?: boolean;
}

const emptyValues: TierFormValues = {
    name: '',
    isCutoff: false,
    criteria: [],
    exclusions: [],
    sourceRules: [],
};

function normalizeTier(values: TierFormValues): unknown {
    return {
        name: values.name,
        isCutoff: values.isCutoff,
        criteria: stripBlankRows(values.criteria, isBlankCriterion),
        exclusions: normalizeExclusions(values.exclusions ?? []),
    };
}

export function TierForm({
    open,
    onOpenChange,
    initialValues,
    tierListName,
    tierListId,
    onSubmit,
    isSubmitting = false,
}: TierFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<TierFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<TierFormValues>(initialValues ?? emptyValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const loadedEntityIdRef = useRef<string | null | undefined>(undefined);
    const isDirty = useFormDirty(values, cleanValuesRef.current, normalizeTier);
    const isDirtyRef = useRef(isDirty);
    isDirtyRef.current = isDirty;

    useEffect(() => {
        if (!open) {
            loadedEntityIdRef.current = undefined;
            return;
        }
        const nextEntityId = initialValues?.id ?? null;
        if (loadedEntityIdRef.current === nextEntityId && isDirtyRef.current) {
            return;
        }
        const clean = initialValues ?? emptyValues;
        setValues(clean);
        cleanValuesRef.current = clean;
        loadedEntityIdRef.current = nextEntityId;
        setErrors({});
        setShowUnsavedDialog(false);
        setShowStaleSaveConfirmation(false);
    }, [open, initialValues]);

    const conflict = useExternalChangeConflict({
        dirty: isDirty,
        entityId: tierListId,
        eventNames: ['qualityTierList.updated', 'qualityTierList.deleted'],
        deletedEventNames: ['qualityTierList.deleted'],
        contextKeys: ['qualityTierList.id'],
        enabled: open && isEditing && !!tierListId,
    });

    useUnsavedChangesGuard(open && isDirty && !isSubmitting);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen && isDirty && !isSubmitting) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(nextOpen);
        },
        [isDirty, isSubmitting, onOpenChange],
    );

    const handleDiscard = useCallback(() => {
        setShowUnsavedDialog(false);
        onOpenChange(false);
    }, [onOpenChange]);

    const handleKeepEditing = useCallback(() => {
        setShowUnsavedDialog(false);
    }, []);

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const handleNameChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, name: e.target.value }));
        setErrors((prev) => ({ ...prev, name: '' }));
    }, []);

    const handleCutoffChange = useCallback((isCutoff: boolean) => {
        setValues((prev) => ({ ...prev, isCutoff }));
    }, []);

    const handleCriteriaChange = useCallback((criteria: Criterion[]) => {
        setValues((prev) => ({ ...prev, criteria }));
        setErrors((prev) => ({ ...prev, criteria: '' }));
    }, []);

    const handleExclusionsChange = useCallback((exclusions: ExclusionDraft[]) => {
        setValues((prev) => ({ ...prev, exclusions }));
        setErrors((prev) => ({ ...prev, exclusions: '' }));
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        if (values.criteria.some(hasBlankField)) {
            newErrors.criteria = 'Every criterion needs a field name';
        }

        if ((values.exclusions ?? []).some((exclusion) => exclusion.criteria.some(hasBlankField))) {
            newErrors.exclusions = 'Every exclusion criterion needs a field name';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    }, [values]);

    const save = useCallback(async () => {
        conflict.beginOwnSave();
        try {
            await onSubmit(values);
            conflict.endOwnSave(true);
        } catch {
            conflict.endOwnSave(false);
        }
    }, [conflict, onSubmit, values]);

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            if (conflict.state === 'deleted' || !validate()) {
                return;
            }
            if (conflict.state === 'stale') {
                setShowStaleSaveConfirmation(true);
                return;
            }
            void save();
        },
        [conflict.state, save, validate],
    );

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="tier-form__sheet" data-component="tier-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Tier' : 'Add Tier'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? `Update the tier configuration${tierListName ? ` in "${tierListName}".` : '.'}`
                            : `Add a new tier${tierListName ? ` to "${tierListName}".` : '.'}`}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="tier-form">
                    <div className="tier-form__body">
                        {conflict.state && (
                            <ExternalChangeConflictWarning
                                state={conflict.state}
                                entityLabel="Tier list"
                                id="tier-external-change"
                            />
                        )}
                        <div className="tier-form__field">
                            <Label htmlFor="tier-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="tier-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="Bluray-1080p"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                            <p className="tier-form__help">
                                A descriptive name for this quality tier, such as "High" or "Standard".
                            </p>
                        </div>

                        <div className="tier-form__field tier-form__field--toggle">
                            <div className="tier-form__toggle-wrapper">
                                <Switch
                                    id="tier-cutoff"
                                    checked={values.isCutoff}
                                    onCheckedChange={handleCutoffChange}
                                    disabled={isSubmitting}
                                    data-field="isCutoff"
                                />
                                <div className="tier-form__label-row">
                                    <Label htmlFor="tier-cutoff">Set as Cutoff</Label>
                                    <Tooltip
                                        content={
                                            <p className="tier-form__tooltip">
                                                The cutoff tier is the minimum acceptable quality level. Content at or
                                                above this tier will not be upgraded further.
                                            </p>
                                        }
                                        side="top"
                                    >
                                        <button
                                            type="button"
                                            className="tier-form__info-button"
                                            aria-label="Cutoff help"
                                        >
                                            <InfoIcon weight="fill" aria-hidden="true" />
                                        </button>
                                    </Tooltip>
                                </div>
                            </div>
                            <p className="tier-form__help">
                                <CheckFatIcon weight="fill" className="tier-form__help-icon" aria-hidden="true" />
                                When set as cutoff, content meeting this tier or higher will not be upgraded.
                            </p>
                        </div>

                        <div className="tier-form__field">
                            <CriteriaEditor
                                criteria={values.criteria}
                                onChange={handleCriteriaChange}
                                disabled={isSubmitting}
                            />
                            {errors.criteria && (
                                <p className="tier-form__error" role="alert">
                                    {errors.criteria}
                                </p>
                            )}
                        </div>

                        <div className="tier-form__field">
                            <ExclusionsEditor
                                exclusions={values.exclusions ?? []}
                                onChange={handleExclusionsChange}
                                scopeLabel="this tier"
                                disabled={isSubmitting}
                            />
                            {errors.exclusions && (
                                <p className="tier-form__error" role="alert">
                                    {errors.exclusions}
                                </p>
                            )}
                        </div>
                    </div>

                    <SheetFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            disabled={isSubmitting}
                            onClick={handleCancel}
                            data-action="cancel"
                        >
                            <XIcon weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                        <Button
                            type="submit"
                            variant="primary"
                            loading={isSubmitting}
                            disabled={conflict.state === 'deleted'}
                            data-action="save"
                        >
                            <FloppyDiskIcon weight="bold" aria-hidden="true" />
                            Save
                        </Button>
                    </SheetFooter>
                </form>
            </SheetContent>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
            <StaleSaveConfirmationDialog
                open={showStaleSaveConfirmation}
                title="Save changes based on older data?"
                description="This item changed after editing began. Saving will overwrite the newer changes."
                saving={isSubmitting}
                onCancel={() => setShowStaleSaveConfirmation(false)}
                onConfirm={() => {
                    setShowStaleSaveConfirmation(false);
                    void save();
                }}
            />
        </Sheet>
    );
}

TierForm.displayName = 'TierForm';

export default TierForm;
