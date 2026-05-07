import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { FloppyDisk, X } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { PathInput } from '@/components/ui/PathInput';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import type { LibraryTypeDefinition } from '@/hooks/useLibraryTypes';

import './RootFolderForm.css';

export interface RootFolderFormValues {
    id?: string;
    name: string;
    path: string;
    contentTypeId: string;
}

export interface RootFolderFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: RootFolderFormValues | null;

    contentTypes: LibraryTypeDefinition[];

    onSubmit: (values: RootFolderFormValues) => void;

    isSubmitting?: boolean;
}

const emptyValues: RootFolderFormValues = {
    name: '',
    path: '',
    contentTypeId: '',
};

export function RootFolderForm({
    open,
    onOpenChange,
    initialValues,
    contentTypes,
    onSubmit,
    isSubmitting = false,
}: RootFolderFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<RootFolderFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<RootFolderFormValues>(initialValues ?? emptyValues);
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
            values.path !== cleanValuesRef.current.path ||
            values.contentTypeId !== cleanValuesRef.current.contentTypeId,
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

    const handlePathChange = useCallback((path: string) => {
        setValues((prev) => ({ ...prev, path }));
        setErrors((prev) => ({ ...prev, path: '' }));
    }, []);

    const handleContentTypeChange = useCallback((contentTypeId: string) => {
        setValues((prev) => ({ ...prev, contentTypeId }));
        setErrors((prev) => ({ ...prev, contentTypeId: '' }));
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        if (!values.path.trim()) {
            newErrors.path = 'Path is required';
        }

        if (!values.contentTypeId) {
            newErrors.contentTypeId = 'Content type is required';
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

    const selectedContentType = contentTypes.find((ct) => ct.id === values.contentTypeId);

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="root-folder-form__sheet" data-component="root-folder-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Root Folder' : 'Add Root Folder'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the root folder configuration.'
                            : 'Configure a new root folder for content storage.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="root-folder-form">
                    <div className="root-folder-form__body">
                        <div className="root-folder-form__field">
                            <Label htmlFor="folder-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="folder-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Media Folder"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="root-folder-form__field">
                            <Label htmlFor="folder-path" error={!!errors.path} required>
                                Path
                            </Label>
                            <PathInput
                                id="folder-path"
                                value={values.path}
                                onChange={handlePathChange}
                                placeholder="/media/movies"
                                mode="folder"
                                error={!!errors.path}
                                errorMessage={errors.path || undefined}
                                disabled={isSubmitting}
                                data-field="path"
                            />
                            <p className="root-folder-form__help">The filesystem path where content will be stored.</p>
                        </div>

                        <div className="root-folder-form__field">
                            <Label htmlFor="folder-content-type" error={!!errors.contentTypeId} required>
                                Content Type
                            </Label>
                            <Select
                                value={values.contentTypeId}
                                onValueChange={handleContentTypeChange}
                                disabled={isSubmitting}
                            >
                                <SelectTrigger
                                    id="folder-content-type"
                                    placeholder="Select a content type..."
                                    error={!!errors.contentTypeId}
                                    aria-describedby={errors.contentTypeId ? 'folder-content-type-error' : undefined}
                                    data-field="content-type"
                                >
                                    {selectedContentType?.name ?? 'Select a content type...'}
                                </SelectTrigger>
                                <SelectContent>
                                    {contentTypes.map((ct) => (
                                        <SelectItem key={ct.id} value={ct.id}>
                                            {ct.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                            <p className="root-folder-form__help">
                                The type of content that will be stored in this folder.
                            </p>
                            {errors.contentTypeId && (
                                <p id="folder-content-type-error" className="form-error" role="alert">
                                    {errors.contentTypeId}
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

RootFolderForm.displayName = 'RootFolderForm';

export default RootFolderForm;
