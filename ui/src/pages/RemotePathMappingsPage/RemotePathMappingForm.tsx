import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { FlaskIcon, FloppyDiskIcon, XIcon } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';

import './RemotePathMappingForm.css';

export interface RemotePathMappingFormValues {
    id?: string;
    host: string;
    remotePath: string;
    localPath: string;
}

interface RemotePathMappingFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: RemotePathMappingFormValues | null;

    onSubmit: (values: RemotePathMappingFormValues) => void | Promise<void>;

    onTestLocalPath?: (localPath: string) => Promise<void>;

    isSubmitting?: boolean;

    isTestingLocalPath?: boolean;
}

const emptyValues: RemotePathMappingFormValues = {
    host: '',
    remotePath: '',
    localPath: '',
};

const absoluteUrlPattern = /^[a-z][a-z\d+\-.]*:\/\//i;
const ipv6HostPattern = /^[a-f\d:.]+$/i;

function normalizeMappingHost(raw: string): string | null {
    const value = raw.trim();

    if (!value) {
        return null;
    }

    if (absoluteUrlPattern.test(value)) {
        try {
            const url = new URL(value);
            return normalizeMappingHost(url.hostname);
        } catch {
            return null;
        }
    }

    let hostPart = value;
    const slash = hostPart.indexOf('/');
    if (slash >= 0) {
        hostPart = hostPart.slice(0, slash);
    }

    if (hostPart.startsWith('[')) {
        const end = hostPart.indexOf(']');
        if (end > 1) {
            return hostPart.slice(1, end);
        }

        return null;
    }

    const colonCount = hostPart.split(':').length - 1;
    if (colonCount > 1 && ipv6HostPattern.test(hostPart)) {
        return hostPart;
    }

    const colon = hostPart.indexOf(':');
    if (colon >= 0) {
        hostPart = hostPart.slice(0, colon);
    }

    const normalized = hostPart.trim();
    return normalized || null;
}

export function RemotePathMappingForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    onTestLocalPath,
    isSubmitting = false,
    isTestingLocalPath = false,
}: RemotePathMappingFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<RemotePathMappingFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<RemotePathMappingFormValues>(initialValues ?? emptyValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveDialog, setShowStaleSaveDialog] = useState(false);
    const wasOpenRef = useRef(false);

    const isDirty = useMemo(
        () =>
            values.host !== cleanValuesRef.current.host ||
            values.remotePath !== cleanValuesRef.current.remotePath ||
            values.localPath !== cleanValuesRef.current.localPath,
        [values],
    );

    const {
        state: conflictState,
        beginOwnSave,
        endOwnSave,
        clearConflict,
    } = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['remotePathMapping.updated', 'remotePathMapping.deleted'],
        deletedEventNames: ['remotePathMapping.deleted'],
        contextKeys: ['remotePathMapping.id'],
        enabled: open,
    });

    useEffect(() => {
        const justOpened = open && !wasOpenRef.current;
        wasOpenRef.current = open;
        if (!open) {
            return;
        }
        if (!justOpened && isDirty && cleanValuesRef.current.id === initialValues?.id) {
            return;
        }
        const clean = initialValues ?? emptyValues;
        setValues(clean);
        cleanValuesRef.current = clean;
        setErrors({});
        setShowUnsavedDialog(false);
        setShowStaleSaveDialog(false);
        clearConflict();
    }, [clearConflict, initialValues, isDirty, open]);

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

    const handleHostChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, host: e.target.value }));
        setErrors((prev) => ({ ...prev, host: '' }));
    }, []);

    const handleRemotePathChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, remotePath: e.target.value }));
        setErrors((prev) => ({ ...prev, remotePath: '' }));
    }, []);

    const handleLocalPathChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, localPath: e.target.value }));
        setErrors((prev) => ({ ...prev, localPath: '' }));
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};
        const host = normalizeMappingHost(values.host);

        if (!values.host.trim()) {
            newErrors.host = 'Host is required';
        } else if (!host) {
            newErrors.host = 'Host must include a hostname';
        }

        if (!values.remotePath.trim()) {
            newErrors.remotePath = 'Remote path is required';
        }

        if (!values.localPath.trim()) {
            newErrors.localPath = 'Local path is required';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    }, [values]);

    const save = useCallback(async () => {
        const host = normalizeMappingHost(values.host);
        if (!host) {
            return;
        }
        beginOwnSave();
        try {
            await Promise.resolve(
                onSubmit({
                    ...values,
                    host,
                    remotePath: values.remotePath.trim(),
                    localPath: values.localPath.trim(),
                }),
            );
            endOwnSave(true);
            setShowStaleSaveDialog(false);
        } catch {
            endOwnSave(false);
        }
    }, [beginOwnSave, endOwnSave, onSubmit, values]);

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            if (!validate()) {
                return;
            }
            if (conflictState === 'stale') {
                setShowStaleSaveDialog(true);
                return;
            }
            void save();
        },
        [conflictState, save, validate],
    );

    const handleTestLocalPath = useCallback(async () => {
        const localPath = values.localPath.trim();
        if (!localPath) {
            setErrors((prev) => ({ ...prev, localPath: 'Local path is required' }));
            return;
        }

        await onTestLocalPath?.(localPath);
    }, [onTestLocalPath, values.localPath]);

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent
                side="right"
                className="remote-path-mapping-form__sheet"
                data-component="remote-path-mapping-form"
            >
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Remote Path Mapping' : 'Add Remote Path Mapping'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the remote path mapping.'
                            : 'Translate a download client path into one Magpie can read at import time.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="remote-path-mapping-form">
                    <div className="remote-path-mapping-form__body">
                        {conflictState && (
                            <ExternalChangeConflictWarning
                                state={conflictState}
                                entityLabel="Remote path mapping"
                                id="remote-path-mapping-external-change"
                            />
                        )}
                        <div className="remote-path-mapping-form__field">
                            <Label htmlFor="mapping-host" error={!!errors.host} required>
                                Host
                            </Label>
                            <Input
                                id="mapping-host"
                                value={values.host}
                                onChange={handleHostChange}
                                placeholder="downloadbox.example.test"
                                error={!!errors.host}
                                errorMessage={errors.host || undefined}
                                disabled={isSubmitting}
                                data-field="host"
                            />
                            <p className="remote-path-mapping-form__help">
                                Bare host used for matching. Full URLs are saved as the host only.
                            </p>
                        </div>

                        <div className="remote-path-mapping-form__field">
                            <Label htmlFor="mapping-remote-path" error={!!errors.remotePath} required>
                                Remote Path
                            </Label>
                            <Input
                                id="mapping-remote-path"
                                value={values.remotePath}
                                onChange={handleRemotePathChange}
                                placeholder="E:\Transmission"
                                error={!!errors.remotePath}
                                errorMessage={errors.remotePath || undefined}
                                disabled={isSubmitting}
                                data-field="remote-path"
                            />
                            <p className="remote-path-mapping-form__help">
                                The path the download client reports for completed downloads.
                            </p>
                        </div>

                        <div className="remote-path-mapping-form__field">
                            <Label htmlFor="mapping-local-path" error={!!errors.localPath} required>
                                Local Path
                            </Label>
                            <Input
                                id="mapping-local-path"
                                value={values.localPath}
                                onChange={handleLocalPathChange}
                                placeholder="\\downloadbox.example.test\share\Downloads"
                                error={!!errors.localPath}
                                errorMessage={errors.localPath || undefined}
                                disabled={isSubmitting}
                                data-field="local-path"
                            />
                            <p className="remote-path-mapping-form__help">
                                Magpie uses this path to read the files. Magpie replaces the remote prefix with this
                                value during import.
                            </p>
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
                        {onTestLocalPath && (
                            <Button
                                type="button"
                                variant="outline"
                                disabled={isSubmitting || isTestingLocalPath}
                                loading={isTestingLocalPath}
                                onClick={handleTestLocalPath}
                                data-action="test-local-path"
                            >
                                <FlaskIcon weight="bold" aria-hidden="true" />
                                Test Path
                            </Button>
                        )}
                        <Button
                            type="submit"
                            variant="primary"
                            loading={isSubmitting}
                            disabled={conflictState === 'deleted'}
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
                open={showStaleSaveDialog}
                title="Save changes based on older data?"
                description="This item changed elsewhere after editing began. Saving will overwrite those newer changes with the values in this form."
                saving={isSubmitting}
                onCancel={() => setShowStaleSaveDialog(false)}
                onConfirm={() => void save()}
            />
        </Sheet>
    );
}

RemotePathMappingForm.displayName = 'RemotePathMappingForm';

export default RemotePathMappingForm;
