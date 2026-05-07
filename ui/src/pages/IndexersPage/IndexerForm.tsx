import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode, type RefObject } from 'react';
import { FloppyDisk, X, Flask } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import './IndexerForm.css';

export interface IndexerFormValues {
    id?: string;
    name: string;
    providerType: string;
    description?: string;
}

export interface IndexerFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: IndexerFormValues | null;

    onSubmit: (values: IndexerFormValues) => void;

    isSubmitting?: boolean;

    onTest?: () => void;

    isTesting?: boolean;

    returnFocusRef?: RefObject<HTMLElement | null>;
}

const emptyValues: IndexerFormValues = {
    name: '',
    providerType: '',
};

export function IndexerForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    isSubmitting = false,
    onTest,
    isTesting = false,
    returnFocusRef,
}: IndexerFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<IndexerFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<IndexerFormValues>(initialValues ?? emptyValues);
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
            values.providerType !== cleanValuesRef.current.providerType ||
            values.description !== cleanValuesRef.current.description,
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

    const handleProviderTypeChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, providerType: e.target.value }));
        setErrors((prev) => ({ ...prev, providerType: '' }));
    }, []);

    const handleDescriptionChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, description: e.target.value }));
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        if (!values.providerType.trim()) {
            newErrors.providerType = 'Provider type is required';
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
            <SheetContent
                side="right"
                className="indexer-form__sheet"
                data-component="indexer-form"
                onCloseAutoFocus={(e) => {
                    if (returnFocusRef?.current) {
                        e.preventDefault();
                        returnFocusRef.current.focus();
                    }
                }}
            >
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Indexer' : 'Add Indexer'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the indexer configuration.'
                            : 'Configure a new indexer to search for content.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="indexer-form">
                    <div className="indexer-form__body">
                        <div className="indexer-form__field">
                            <Label htmlFor="indexer-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="indexer-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Indexer"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="indexer-form__field">
                            <Label htmlFor="indexer-provider-type" error={!!errors.providerType} required>
                                Provider Type
                            </Label>
                            <Input
                                id="indexer-provider-type"
                                value={values.providerType}
                                onChange={handleProviderTypeChange}
                                placeholder="newznab"
                                error={!!errors.providerType}
                                errorMessage={errors.providerType || undefined}
                                disabled={isSubmitting}
                                data-field="providerType"
                            />
                            <p className="indexer-form__help">The indexer protocol (e.g., newznab, torznab).</p>
                        </div>

                        <div className="indexer-form__field">
                            <Label htmlFor="indexer-description">Description</Label>
                            <Input
                                id="indexer-description"
                                value={values.description ?? ''}
                                onChange={handleDescriptionChange}
                                placeholder="Optional description"
                                disabled={isSubmitting}
                                data-field="description"
                            />
                        </div>
                    </div>

                    <SheetFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            disabled={isSubmitting || isTesting}
                            onClick={handleCancel}
                            data-action="cancel"
                        >
                            <X weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                        {isEditing && onTest && (
                            <Button
                                type="button"
                                variant="outline"
                                disabled={isSubmitting || isTesting}
                                loading={isTesting}
                                onClick={onTest}
                                data-action="test"
                            >
                                <Flask weight="bold" aria-hidden="true" />
                                Test
                            </Button>
                        )}
                        <Button
                            type="submit"
                            variant="primary"
                            disabled={isSubmitting || isTesting}
                            loading={isSubmitting}
                            data-action="save"
                        >
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

IndexerForm.displayName = 'IndexerForm';

export default IndexerForm;
