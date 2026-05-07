import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode, type RefObject } from 'react';
import { FloppyDisk, X, Flask } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';

import './MediaServerForm.css';

export interface MediaServerFormValues {
    id?: string;
    name: string;
    provider: string;
    enabled: boolean;
    settings: Record<string, string | number | boolean | undefined>;
}

export interface MediaServerFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: MediaServerFormValues | null;

    onSubmit: (values: MediaServerFormValues) => void;

    isSubmitting?: boolean;

    onTest?: () => void;

    isTesting?: boolean;

    returnFocusRef?: RefObject<HTMLElement | null>;
}

const emptyValues: MediaServerFormValues = {
    name: '',
    provider: '',
    enabled: true,
    settings: {},
};

export function MediaServerForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    isSubmitting = false,
    onTest,
    isTesting = false,
    returnFocusRef,
}: MediaServerFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<MediaServerFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<MediaServerFormValues>(initialValues ?? emptyValues);
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
                className="media-server-form__sheet"
                data-component="media-server-form"
                onCloseAutoFocus={(e) => {
                    if (returnFocusRef?.current) {
                        e.preventDefault();
                        returnFocusRef.current.focus();
                    }
                }}
            >
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Media Server' : 'Add Media Server'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the media server configuration.'
                            : 'Configure a new media server integration.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="media-server-form">
                    <div className="media-server-form__body">
                        <div className="media-server-form__field">
                            <Label htmlFor="server-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="server-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Media Server"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="media-server-form__field">
                            <Label htmlFor="server-provider" error={!!errors.provider} required>
                                Provider Type
                            </Label>
                            <Input
                                id="server-provider"
                                value={values.provider}
                                onChange={handleProviderChange}
                                placeholder="plex"
                                error={!!errors.provider}
                                errorMessage={errors.provider || undefined}
                                disabled={isSubmitting || isEditing}
                                data-field="provider"
                            />
                            <p className="media-server-form__help">
                                The media server type (e.g., plex, jellyfin, emby).
                            </p>
                        </div>

                        <div className="media-server-form__field media-server-form__field--toggle">
                            <div className="media-server-form__toggle-wrapper">
                                <Switch
                                    id="server-enabled"
                                    checked={values.enabled}
                                    onCheckedChange={handleEnabledChange}
                                    disabled={isSubmitting}
                                    data-field="enabled"
                                />
                                <Label htmlFor="server-enabled">Enabled</Label>
                            </div>
                            <p className="media-server-form__help">Disabled servers will not be synced or queried.</p>
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

MediaServerForm.displayName = 'MediaServerForm';

export default MediaServerForm;
