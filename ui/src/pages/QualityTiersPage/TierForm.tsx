import { useState, useCallback, useEffect, useRef, type ReactNode } from 'react';
import { FloppyDiskIcon, XIcon, InfoIcon, CheckFatIcon, CopyIcon } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { stripBlankRows, useFormDirty } from '@/hooks/useFormDirty';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';
import type { ApiQualityTierRule } from '@/hooks/useQualityTierLists';

import { Alert } from '@/components/ui/Alert';
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

    sharedWith?: readonly string[] | null;

    sharedWithFailed?: boolean;

    onRetrySharedWith?: () => void;

    onClone?: () => void;

    isCloning?: boolean;
}

const emptyValues: TierFormValues = {
    name: '',
    isCutoff: false,
    criteria: [],
    exclusions: [],
    sourceRules: [],
};

function normalizeSharedFields(values: TierFormValues) {
    return {
        name: values.name,
        criteria: stripBlankRows(values.criteria, isBlankCriterion),
        exclusions: normalizeExclusions(values.exclusions ?? []),
    };
}

function normalizeTier(values: TierFormValues): unknown {
    return { ...normalizeSharedFields(values), isCutoff: values.isCutoff };
}

function quoteNames(names: readonly string[]): string {
    return names.map((name) => `"${name}"`).join(', ');
}

export function TierForm({
    open,
    onOpenChange,
    initialValues,
    tierListName,
    tierListId,
    onSubmit,
    isSubmitting = false,
    sharedWith,
    sharedWithFailed = false,
    onRetrySharedWith,
    onClone,
    isCloning = false,
}: TierFormProps): ReactNode {
    const isEditing = !!initialValues?.id;
    const sharingUnknown = isEditing && sharedWith === null;
    const otherLists = (isEditing && sharedWith) || [];
    const isShared = otherLists.length > 0;

    const [values, setValues] = useState<TierFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<TierFormValues>(initialValues ?? emptyValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const [showSharedSaveConfirmation, setShowSharedSaveConfirmation] = useState(false);
    const [showCloneDiscard, setShowCloneDiscard] = useState(false);
    const loadedEntityIdRef = useRef<string | null | undefined>(undefined);
    const isDirty = useFormDirty(values, cleanValuesRef.current, normalizeTier);
    const sharedFieldsDirty = useFormDirty(values, cleanValuesRef.current, normalizeSharedFields);
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
        setShowCloneDiscard(false);
        setShowStaleSaveConfirmation(false);
        setShowSharedSaveConfirmation(false);
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

    const handleClone = useCallback(() => {
        if (isDirty) {
            setShowCloneDiscard(true);
            return;
        }
        onClone?.();
    }, [isDirty, onClone]);

    const handleDiscardAndClone = useCallback(() => {
        setShowCloneDiscard(false);
        onClone?.();
    }, [onClone]);

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

    const confirmSharedOrSave = useCallback(() => {
        if (isShared && sharedFieldsDirty) {
            setShowSharedSaveConfirmation(true);
            return;
        }
        void save();
    }, [isShared, save, sharedFieldsDirty]);

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            if (conflict.state === 'deleted' || sharingUnknown || !validate()) {
                return;
            }
            if (conflict.state === 'stale') {
                setShowStaleSaveConfirmation(true);
                return;
            }
            confirmSharedOrSave();
        },
        [conflict.state, confirmSharedOrSave, sharingUnknown, validate],
    );

    const listScope = isShared ? ` and ${otherLists.length} other ${otherLists.length === 1 ? 'list' : 'lists'}` : '';
    const editScope = tierListName && !sharingUnknown ? ` in "${tierListName}"${listScope}` : '';

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="tier-form__sheet" data-component="tier-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Tier' : 'Add Tier'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? `Update the tier configuration${editScope}.`
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
                        {sharingUnknown && sharedWithFailed && (
                            <Alert
                                variant="error"
                                title="Failed to load the tier lists"
                                data-testid="shared-tier-error"
                            >
                                <p className="tier-form__notice-text">The lists that use this tier are unknown.</p>
                                <Button type="button" variant="secondary" size="sm" onClick={onRetrySharedWith}>
                                    Retry
                                </Button>
                            </Alert>
                        )}
                        {isShared && (
                            <Alert variant="info" title="Shared tier" data-testid="shared-tier-notice">
                                <p className="tier-form__notice-text">
                                    Name, criteria and exclusions also change in {quoteNames(otherLists)}. The cutoff
                                    applies only to {tierListName ? `"${tierListName}"` : 'this list'}. Clone Tier adds
                                    a copy that only this list uses and keeps the shared tier until you remove it.
                                </p>
                                {onClone && (
                                    <Button
                                        type="button"
                                        size="sm"
                                        onClick={handleClone}
                                        loading={isCloning}
                                        disabled={isSubmitting}
                                        data-action="clone-tier"
                                    >
                                        <CopyIcon weight="bold" aria-hidden="true" />
                                        Clone Tier
                                    </Button>
                                )}
                            </Alert>
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
                            disabled={conflict.state === 'deleted' || sharingUnknown}
                            data-action="save"
                        >
                            <FloppyDiskIcon weight="bold" aria-hidden="true" />
                            Save
                        </Button>
                    </SheetFooter>
                </form>
            </SheetContent>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
            <UnsavedChangesDialog
                open={showCloneDiscard}
                title="Discard Changes and Clone?"
                description="Clone Tier copies the saved tier. It does not copy your unsaved changes."
                discardLabel="Discard and Clone"
                onDiscard={handleDiscardAndClone}
                onCancel={() => setShowCloneDiscard(false)}
            />
            <StaleSaveConfirmationDialog
                open={showStaleSaveConfirmation}
                title="Save changes based on older data?"
                description="This item changed after editing began. Saving will overwrite the newer changes."
                saving={isSubmitting}
                onCancel={() => setShowStaleSaveConfirmation(false)}
                onConfirm={() => {
                    setShowStaleSaveConfirmation(false);
                    confirmSharedOrSave();
                }}
            />
            <StaleSaveConfirmationDialog
                open={showSharedSaveConfirmation}
                title={`Save changes to ${otherLists.length + 1} lists?`}
                description={`Name, criteria and exclusions change in every list that uses this tier: ${quoteNames(
                    tierListName ? [tierListName, ...otherLists] : otherLists,
                )}.`}
                saving={isSubmitting}
                onCancel={() => setShowSharedSaveConfirmation(false)}
                onConfirm={() => {
                    setShowSharedSaveConfirmation(false);
                    void save();
                }}
            />
        </Sheet>
    );
}

TierForm.displayName = 'TierForm';

export default TierForm;
