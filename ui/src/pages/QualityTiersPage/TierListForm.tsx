import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { FloppyDisk, X, Info, ListNumbers } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import type { ApiTag } from '@/hooks/useTags';

import './TierListForm.css';

export interface TierListFormValues {
    id?: string;
    name: string;
    upgradeAllowed: boolean;
    tags: string[];
}

export interface TierListFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: TierListFormValues | null;

    tags: ApiTag[];

    onSubmit: (values: TierListFormValues) => void;

    isSubmitting?: boolean;
}

const emptyValues: TierListFormValues = {
    name: '',
    upgradeAllowed: true,
    tags: [],
};

export function TierListForm({
    open,
    onOpenChange,
    initialValues,
    tags,
    onSubmit,
    isSubmitting = false,
}: TierListFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<TierListFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<TierListFormValues>(initialValues ?? emptyValues);
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
            values.upgradeAllowed !== cleanValuesRef.current.upgradeAllowed ||
            values.tags.length !== cleanValuesRef.current.tags.length ||
            values.tags.some((tag, i) => tag !== cleanValuesRef.current.tags[i]),
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

    const handleUpgradeAllowedChange = useCallback((upgradeAllowed: boolean) => {
        setValues((prev) => ({ ...prev, upgradeAllowed }));
    }, []);

    const handleTagToggle = useCallback((tagName: string) => {
        setValues((prev) => ({
            ...prev,
            tags: prev.tags.includes(tagName) ? prev.tags.filter((name) => name !== tagName) : [...prev.tags, tagName],
        }));
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
            <SheetContent side="right" className="tier-list-form__sheet" data-component="tier-list-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Tier List' : 'Add Tier List'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the tier list configuration.'
                            : 'Configure a new quality tier list for content searches.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="tier-list-form">
                    <div className="tier-list-form__body">
                        <div className="tier-list-form__field">
                            <Label htmlFor="tier-list-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="tier-list-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="HD Standard"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="tier-list-form__field tier-list-form__field--toggle">
                            <div className="tier-list-form__toggle-wrapper">
                                <Switch
                                    id="tier-list-upgrade-allowed"
                                    checked={values.upgradeAllowed}
                                    onCheckedChange={handleUpgradeAllowedChange}
                                    disabled={isSubmitting}
                                    data-field="upgradeAllowed"
                                />
                                <div className="tier-list-form__label-row">
                                    <Label htmlFor="tier-list-upgrade-allowed">Upgrade Allowed</Label>
                                    <Tooltip
                                        content={
                                            <p className="tier-list-form__tooltip">
                                                When enabled, content can be upgraded to higher quality tiers when
                                                better releases become available.
                                            </p>
                                        }
                                        side="top"
                                    >
                                        <button
                                            type="button"
                                            className="tier-list-form__info-button"
                                            aria-label="Upgrade allowed help"
                                        >
                                            <Info weight="fill" aria-hidden="true" />
                                        </button>
                                    </Tooltip>
                                </div>
                            </div>
                            <p className="tier-list-form__help">
                                When disabled, content will not be replaced with higher quality releases.
                            </p>
                        </div>

                        {tags.length > 0 && (
                            <div className="tier-list-form__field">
                                <Label>Tags</Label>
                                <div className="tier-list-form__tags" data-field="tags">
                                    {tags.map((tag) => (
                                        <button
                                            key={tag.name}
                                            type="button"
                                            className={`tier-list-form__tag${values.tags.includes(tag.name) ? ' tier-list-form__tag--selected' : ''}`}
                                            onClick={() => handleTagToggle(tag.name)}
                                            disabled={isSubmitting}
                                            data-tag-name={tag.name}
                                            data-selected={values.tags.includes(tag.name) || undefined}
                                            aria-pressed={values.tags.includes(tag.name)}
                                        >
                                            {tag.name}
                                        </button>
                                    ))}
                                </div>
                                <p className="tier-list-form__help">Assign tags to organize and filter tier lists.</p>
                            </div>
                        )}

                        {!isEditing && (
                            <div className="tier-list-form__hint" data-hint="tiers">
                                <ListNumbers
                                    weight="duotone"
                                    className="tier-list-form__hint-icon"
                                    aria-hidden="true"
                                />
                                <p className="tier-list-form__hint-text">
                                    Quality tiers can be configured after the tier list is created.
                                </p>
                            </div>
                        )}
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

TierListForm.displayName = 'TierListForm';

export default TierListForm;
