import { useState, useCallback, useMemo, useEffect, useRef, type ReactNode } from 'react';
import { FloppyDisk, X, Info, LockSimple } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import type { ContentFilterOption, QualityTierListOption, TagOption, WishlistFormValues } from '@/types';

import './WishlistForm.css';

export type { ContentFilterOption, QualityTierListOption, TagOption, WishlistFormValues };

export interface WishlistFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: WishlistFormValues | null;

    filters: ContentFilterOption[];

    qualityTierLists: QualityTierListOption[];

    tags: TagOption[];

    onSubmit: (values: WishlistFormValues) => void;

    isSubmitting?: boolean;

    isOwner?: boolean;

    canEdit?: boolean;
}

const NONE_VALUE = '__none__';

const emptyValues: WishlistFormValues = {
    name: '',
    filterId: null,
    qualityTierListId: null,
    monitored: true,
    tagIds: [],
};

export function valuesEqual(a: WishlistFormValues, b: WishlistFormValues): boolean {
    return (
        a.name === b.name &&
        a.filterId === b.filterId &&
        a.qualityTierListId === b.qualityTierListId &&
        a.monitored === b.monitored &&
        a.tagIds.length === b.tagIds.length &&
        a.tagIds.every((id) => b.tagIds.includes(id))
    );
}

export function WishlistForm({
    open,
    onOpenChange,
    initialValues,
    filters,
    qualityTierLists,
    tags,
    onSubmit,
    isSubmitting = false,
    isOwner = true,
    canEdit = true,
}: WishlistFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<WishlistFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<WishlistFormValues>(initialValues ?? emptyValues);

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

    const isDirty = useMemo(() => !valuesEqual(values, cleanValuesRef.current), [values]);

    const readOnly = isEditing && !canEdit;

    useUnsavedChangesGuard(open && isDirty && !readOnly);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen && isDirty && !readOnly) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(nextOpen);
        },
        [isDirty, readOnly, onOpenChange],
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

    const handleFilterChange = useCallback((filterId: string) => {
        setValues((prev) => ({
            ...prev,
            filterId: filterId === NONE_VALUE ? null : filterId,
        }));
    }, []);

    const handleQualityTierListChange = useCallback((qualityTierListId: string) => {
        setValues((prev) => ({
            ...prev,
            qualityTierListId: qualityTierListId === NONE_VALUE ? null : qualityTierListId,
        }));
    }, []);

    const handleMonitoredChange = useCallback((monitored: boolean) => {
        setValues((prev) => ({ ...prev, monitored }));
    }, []);

    const handleTagToggle = useCallback((tagId: string) => {
        setValues((prev) => ({
            ...prev,
            tagIds: prev.tagIds.includes(tagId) ? prev.tagIds.filter((id) => id !== tagId) : [...prev.tagIds, tagId],
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

    const selectedFilter = useMemo(() => filters.find((f) => f.id === values.filterId), [filters, values.filterId]);

    const selectedQualityTierList = useMemo(
        () => qualityTierLists.find((q) => q.id === values.qualityTierListId),
        [qualityTierLists, values.qualityTierListId],
    );

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="wishlist-form__sheet" data-component="wishlist-form">
                <SheetHeader>
                    <SheetTitle>
                        {isEditing ? 'Edit Wishlist' : 'Add Wishlist'}
                        {isEditing && !isOwner && (
                            <Tooltip content="This wishlist is owned by another user" side="bottom">
                                <span className="wishlist-form__owner-lock" data-testid="wishlist-form__owner-lock">
                                    <LockSimple weight="fill" aria-hidden="true" />
                                </span>
                            </Tooltip>
                        )}
                    </SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? readOnly
                                ? 'View wishlist configuration.'
                                : 'Update the wishlist configuration.'
                            : 'Configure a new wishlist for content discovery.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="wishlist-form">
                    <div className="wishlist-form__body">
                        <div className="wishlist-form__field">
                            <Label htmlFor="wishlist-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="wishlist-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Wishlist"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting || readOnly}
                                data-field="name"
                            />
                        </div>

                        <div className="wishlist-form__field">
                            <div className="wishlist-form__label-row">
                                <Label htmlFor="wishlist-filter">Content Filter</Label>
                                <Tooltip
                                    content={
                                        <p className="wishlist-form__tooltip">
                                            Optional filter to automatically match content.
                                            <br />
                                            If set, matching content will be added to this wishlist.
                                        </p>
                                    }
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="wishlist-form__info-button"
                                        aria-label="Content filter help"
                                    >
                                        <Info weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <Select
                                value={values.filterId ?? NONE_VALUE}
                                onValueChange={handleFilterChange}
                                disabled={isSubmitting || readOnly}
                            >
                                <SelectTrigger id="wishlist-filter" placeholder="No filter" data-field="filterId">
                                    {selectedFilter?.name ?? 'No filter'}
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value={NONE_VALUE}>No filter</SelectItem>
                                    {filters.map((filter) => (
                                        <SelectItem key={filter.id} value={filter.id}>
                                            {filter.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        </div>

                        <div className="wishlist-form__field">
                            <div className="wishlist-form__label-row">
                                <Label htmlFor="wishlist-quality-tier">Quality Tier List</Label>
                                <Tooltip
                                    content={
                                        <p className="wishlist-form__tooltip">
                                            Quality preferences for searching.
                                            <br />
                                            Determines acceptable quality levels for content.
                                        </p>
                                    }
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="wishlist-form__info-button"
                                        aria-label="Quality tier help"
                                    >
                                        <Info weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <Select
                                value={values.qualityTierListId ?? NONE_VALUE}
                                onValueChange={handleQualityTierListChange}
                                disabled={isSubmitting || readOnly}
                            >
                                <SelectTrigger
                                    id="wishlist-quality-tier"
                                    placeholder="Default quality"
                                    data-field="qualityTierListId"
                                >
                                    {selectedQualityTierList?.name ?? 'Default quality'}
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value={NONE_VALUE}>Default quality</SelectItem>
                                    {qualityTierLists.map((qtl) => (
                                        <SelectItem key={qtl.id} value={qtl.id}>
                                            {qtl.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        </div>

                        <div className="wishlist-form__field wishlist-form__field--toggle">
                            <div className="wishlist-form__toggle-wrapper">
                                <Switch
                                    id="wishlist-monitored"
                                    checked={values.monitored}
                                    onCheckedChange={handleMonitoredChange}
                                    disabled={isSubmitting || readOnly}
                                    data-field="monitored"
                                />
                                <Label htmlFor="wishlist-monitored">Monitored</Label>
                            </div>
                            <p className="wishlist-form__help">
                                Monitored wishlists will be searched for missing content automatically.
                            </p>
                        </div>

                        {tags.length > 0 && (
                            <div className="wishlist-form__field">
                                <Label>Tags</Label>
                                <div className="wishlist-form__tags" data-field="tags">
                                    {tags.map((tag) => (
                                        <button
                                            key={tag.id}
                                            type="button"
                                            className={`wishlist-form__tag${values.tagIds.includes(tag.id) ? ' wishlist-form__tag--selected' : ''}`}
                                            onClick={() => handleTagToggle(tag.id)}
                                            disabled={isSubmitting || readOnly}
                                            data-tag-id={tag.id}
                                            aria-pressed={values.tagIds.includes(tag.id)}
                                        >
                                            {tag.name}
                                        </button>
                                    ))}
                                </div>
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
                            {readOnly ? 'Close' : 'Cancel'}
                        </Button>
                        {!readOnly && (
                            <Button type="submit" variant="primary" loading={isSubmitting} data-action="save">
                                <FloppyDisk weight="bold" aria-hidden="true" />
                                Save
                            </Button>
                        )}
                    </SheetFooter>
                </form>
            </SheetContent>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
        </Sheet>
    );
}

WishlistForm.displayName = 'WishlistForm';

export default WishlistForm;
