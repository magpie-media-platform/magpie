import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { FloppyDisk, X, Info, CheckFat } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { CriteriaEditor, type Criterion, type CriterionOperator } from './CriteriaEditor';

export type { Criterion, CriterionOperator };

import './TierForm.css';

export interface TierFormValues {
    id?: string;
    name: string;
    isCutoff: boolean;
    criteria: Criterion[];
}

export interface TierFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: TierFormValues | null;

    tierListName?: string;

    onSubmit: (values: TierFormValues) => void;

    isSubmitting?: boolean;
}

const emptyValues: TierFormValues = {
    name: '',
    isCutoff: false,
    criteria: [],
};

export function TierForm({
    open,
    onOpenChange,
    initialValues,
    tierListName,
    onSubmit,
    isSubmitting = false,
}: TierFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<TierFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<TierFormValues>(initialValues ?? emptyValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);

    useEffect(() => {
        if (open) {
            const clean = initialValues ?? emptyValues;
            setValues(clean);
            cleanValuesRef.current = clean;
            setErrors({});
            setShowUnsavedDialog(false);
        }
    }, [open, initialValues]);

    const isDirty = useMemo(
        () =>
            values.name !== cleanValuesRef.current.name ||
            values.isCutoff !== cleanValuesRef.current.isCutoff ||
            JSON.stringify(values.criteria) !== JSON.stringify(cleanValuesRef.current.criteria),
        [values],
    );

    useUnsavedChangesGuard(open && isDirty);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen && isDirty) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(nextOpen);
        },
        [isDirty, onOpenChange],
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
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    }, [values]);

    const handleSubmit = useCallback(
        (e: React.FormEvent) => {
            e.preventDefault();
            if (validate()) {
                onSubmit(values);
            }
        },
        [values, validate, onSubmit],
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
                                A descriptive name for this quality tier, such as "Bluray-1080p" or "WEB-720p".
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
                                            <Info weight="fill" aria-hidden="true" />
                                        </button>
                                    </Tooltip>
                                </div>
                            </div>
                            <p className="tier-form__help">
                                <CheckFat weight="fill" className="tier-form__help-icon" aria-hidden="true" />
                                When set as cutoff, content meeting this tier or higher will not be upgraded.
                            </p>
                        </div>

                        <div className="tier-form__field">
                            <CriteriaEditor
                                criteria={values.criteria}
                                onChange={handleCriteriaChange}
                                disabled={isSubmitting}
                            />
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
                            <X weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                        <Button type="submit" variant="primary" loading={isSubmitting} data-action="save">
                            <FloppyDisk weight="bold" aria-hidden="true" />
                            Save
                        </Button>
                    </SheetFooter>
                </form>
            </SheetContent>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
        </Sheet>
    );
}

TierForm.displayName = 'TierForm';

export default TierForm;
