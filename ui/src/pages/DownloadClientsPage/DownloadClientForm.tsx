import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode, type RefObject } from 'react';
import { FloppyDisk, X, Flask } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';

import './DownloadClientForm.css';

export interface DownloadClientFormValues {
    id?: string;
    name: string;
    provider: string;
    enabled: boolean;
    settings: Record<string, string | number | boolean | undefined>;
}

export interface DownloadClientFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: DownloadClientFormValues | null;

    onSubmit: (values: DownloadClientFormValues) => void;

    isSubmitting?: boolean;

    onTest?: () => void;

    isTesting?: boolean;

    returnFocusRef?: RefObject<HTMLElement | null>;
}

const emptyValues: DownloadClientFormValues = {
    name: '',
    provider: '',
    enabled: true,
    settings: {},
};

export function DownloadClientForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    isSubmitting = false,
    onTest,
    isTesting = false,
    returnFocusRef,
}: DownloadClientFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<DownloadClientFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<DownloadClientFormValues>(initialValues ?? emptyValues);
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
            values.provider !== cleanValuesRef.current.provider ||
            values.enabled !== cleanValuesRef.current.enabled,
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

    const handleProviderChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, provider: e.target.value }));
        setErrors((prev) => ({ ...prev, provider: '' }));
    }, []);

    const handleEnabledChange = useCallback((enabled: boolean) => {
        setValues((prev) => ({ ...prev, enabled }));
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        if (!values.provider.trim()) {
            newErrors.provider = 'Provider type is required';
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
                className="download-client-form__sheet"
                data-component="download-client-form"
                onCloseAutoFocus={(e) => {
                    if (returnFocusRef?.current) {
                        e.preventDefault();
                        returnFocusRef.current.focus();
                    }
                }}
            >
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Download Client' : 'Add Download Client'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the download client configuration.'
                            : 'Configure a new download client for downloading content.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="download-client-form">
                    <div className="download-client-form__body">
                        <div className="download-client-form__field">
                            <Label htmlFor="client-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="client-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Download Client"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="download-client-form__field">
                            <Label htmlFor="client-provider" error={!!errors.provider} required>
                                Provider Type
                            </Label>
                            <Input
                                id="client-provider"
                                value={values.provider}
                                onChange={handleProviderChange}
                                placeholder="sabnzbd"
                                error={!!errors.provider}
                                errorMessage={errors.provider || undefined}
                                disabled={isSubmitting || isEditing}
                                data-field="provider"
                            />
                            <p className="download-client-form__help">
                                The download client type (e.g., sabnzbd, qbittorrent).
                            </p>
                        </div>

                        <div className="download-client-form__field download-client-form__field--toggle">
                            <div className="download-client-form__toggle-wrapper">
                                <Switch
                                    id="client-enabled"
                                    checked={values.enabled}
                                    onCheckedChange={handleEnabledChange}
                                    disabled={isSubmitting}
                                    data-field="enabled"
                                />
                                <Label htmlFor="client-enabled">Enabled</Label>
                            </div>
                            <p className="download-client-form__help">Disabled clients will not receive downloads.</p>
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

DownloadClientForm.displayName = 'DownloadClientForm';

export default DownloadClientForm;
