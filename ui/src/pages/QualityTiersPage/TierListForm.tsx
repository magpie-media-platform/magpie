import { useState, useCallback, type ReactNode } from 'react';
import { FloppyDiskIcon, XIcon, InfoIcon, ListNumbersIcon } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { useFormDirty } from '@/hooks/useFormDirty';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { TagInput } from '@/components/ui/TagInput';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import type { ApiTag } from '@/hooks/useTags';
import { normalizeMimePatterns } from './MimeTypesEditor';

import './TierListForm.css';
import './TierForms.css';

export interface TierListFormValues {
    id?: string;
    name: string;
    mimeTypes: string[];
    upgradeAllowed: boolean;
    tags: string[];
}

interface TierListFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: TierListFormValues | null;

    tags: ApiTag[];

    onSubmit: (values: TierListFormValues) => void;

    isSubmitting?: boolean;
}

const emptyValues: TierListFormValues = {
    name: '',
    mimeTypes: [],
    upgradeAllowed: true,
    tags: [],
};

function normalizeTierList(values: TierListFormValues): unknown {
    return {
        name: values.name,
        mimeTypes: [...values.mimeTypes].sort(),
        upgradeAllowed: values.upgradeAllowed,
        tags: [...values.tags].sort(),
    };
}

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

    const [cleanValues, setCleanValues] = useState<TierListFormValues>(initialValues ?? emptyValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);

    const isDirty = useFormDirty(values, cleanValues, normalizeTierList);

    const resetKey = `${open ? 'open' : 'closed'}:${initialValues?.id ?? 'new'}`;
    const [seededKey, setSeededKey] = useState(resetKey);
    if (seededKey !== resetKey) {
        const clean = initialValues ?? emptyValues;
        setSeededKey(resetKey);
        setValues(clean);
        setCleanValues(clean);
        setErrors({});
        setShowUnsavedDialog(false);
    }

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

    const handleUpgradeAllowedChange = useCallback((upgradeAllowed: boolean) => {
        setValues((prev) => ({ ...prev, upgradeAllowed }));
    }, []);

    const handleTagToggle = useCallback((tagName: string) => {
        setValues((prev) => ({
            ...prev,
            tags: prev.tags.includes(tagName) ? prev.tags.filter((name) => name !== tagName) : [...prev.tags, tagName],
        }));
    }, []);

    const validate = useCallback((): TierListFormValues | null => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        const normalizedMimeTypes = normalizeMimePatterns(values.mimeTypes);
        if (normalizedMimeTypes.error) {
            newErrors.mimeTypes = normalizedMimeTypes.error;
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0
            ? { ...values, name: values.name.trim(), mimeTypes: normalizedMimeTypes.patterns }
            : null;
    }, [values]);

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            const normalized = validate();
            if (normalized) {
                onSubmit(normalized);
            }
        },
        [validate, onSubmit],
    );

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="tier-list-form__sheet" data-component="tier-list-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Tier List' : 'Add Tier List'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the tier list configuration.'
                            : 'Configure a new tier list for content searches.'}
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

                        <div className="tier-list-form__field" data-field="mimeTypes">
                            <Label htmlFor="tier-list-mime-types" error={!!errors.mimeTypes}>
                                MIME Types
                            </Label>
                            <TagInput
                                id="tier-list-mime-types"
                                value={values.mimeTypes}
                                onChange={(mimeTypes) => {
                                    setValues((previous) => ({ ...previous, mimeTypes }));
                                    setErrors((previous) => ({ ...previous, mimeTypes: '' }));
                                }}
                                allowCreate
                                placeholder="audio/*"
                                disabled={isSubmitting}
                                aria-invalid={!!errors.mimeTypes}
                                aria-describedby={errors.mimeTypes ? 'tier-list-mime-types-error' : undefined}
                            />
                            {errors.mimeTypes ? (
                                <p id="tier-list-mime-types-error" className="tier-list-form__help" role="alert">
                                    {errors.mimeTypes}
                                </p>
                            ) : (
                                <p className="tier-list-form__help">Leave empty to allow any MIME type.</p>
                            )}
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
                                            <InfoIcon weight="fill" aria-hidden="true" />
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
                                <ListNumbersIcon
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
                            <XIcon weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                        <Button type="submit" variant="primary" loading={isSubmitting} data-action="save">
                            <FloppyDiskIcon weight="bold" aria-hidden="true" />
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
