import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { FloppyDiskIcon, XIcon } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { SecretInput } from '@/components/ui/SecretInput';
import { Switch } from '@/components/ui/Switch';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import type { PackageSourceSearchFilterKind, PackageSourceCredentialKind } from '@/hooks/usePackageSources';

import './PackageSourceForm.css';

export interface PackageSourceFormValues {
    id?: string;
    name: string;
    sourceUrl: string;
    enabled: boolean;
    searchFilterKind: PackageSourceSearchFilterKind;
    searchFilterValue: string;
    credentialKind: PackageSourceCredentialKind;
    username: string;
    secret: string;
    secretReference: string;
    hasSecret: boolean;
}

interface PackageSourceFormProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    initialValues?: PackageSourceFormValues | null;
    onSubmit: (values: PackageSourceFormValues) => void | Promise<void>;
    isSubmitting?: boolean;
}

const emptyValues: PackageSourceFormValues = {
    name: '',
    sourceUrl: 'https://api.nuget.org/v3/index.json',
    enabled: true,
    searchFilterKind: 'Tag',
    searchFilterValue: 'magpie',
    credentialKind: 'None',
    username: '',
    secret: '',
    secretReference: '',
    hasSecret: false,
};

const filterKindLabels: Record<PackageSourceSearchFilterKind, string> = {
    Tag: 'Require NuGet tag',
    Prefix: 'Require package id prefix',
    None: 'No discovery filter',
};

const credentialKindLabels: Record<PackageSourceCredentialKind, string> = {
    None: 'None',
    Basic: 'Basic authentication',
    SecretReference: 'External secret reference',
};

function validateSourceUrl(sourceUrl: string): string | undefined {
    let url: URL;
    try {
        url = new URL(sourceUrl.trim());
    } catch {
        return 'sourceUrl must be an absolute HTTP or HTTPS URL.';
    }

    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
        return 'sourceUrl must be an absolute HTTP or HTTPS URL.';
    }

    if (url.username || url.password) {
        return 'sourceUrl must not include credentials.';
    }

    return undefined;
}

export function PackageSourceForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    isSubmitting = false,
}: PackageSourceFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<PackageSourceFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});

    const cleanValuesRef = useRef<PackageSourceFormValues>(initialValues ?? emptyValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const loadedEntityIdRef = useRef<string | null | undefined>(undefined);

    const isDirty = useMemo(() => {
        const clean = cleanValuesRef.current;
        return (
            values.name !== clean.name ||
            values.sourceUrl !== clean.sourceUrl ||
            values.enabled !== clean.enabled ||
            values.searchFilterKind !== clean.searchFilterKind ||
            values.searchFilterValue !== clean.searchFilterValue ||
            values.credentialKind !== clean.credentialKind ||
            values.username !== clean.username ||
            values.secret !== clean.secret ||
            values.secretReference !== clean.secretReference
        );
    }, [values]);
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
        setShowStaleSaveConfirmation(false);
    }, [open, initialValues]);

    const conflict = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['extensionPackageSource.updated', 'extensionPackageSource.deleted'],
        deletedEventNames: ['extensionPackageSource.deleted'],
        contextKeys: ['extensionPackageSource.id'],
        enabled: open && isEditing,
    });

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

    const handleKeepEditing = useCallback(() => setShowUnsavedDialog(false), []);
    const handleCancel = useCallback(() => handleOpenChange(false), [handleOpenChange]);

    const update = useCallback(<K extends keyof PackageSourceFormValues>(key: K, value: PackageSourceFormValues[K]) => {
        setValues((prev) => ({ ...prev, [key]: value }));
        setErrors((prev) => ({ ...prev, [key]: '' }));
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};
        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }
        const sourceUrlError = validateSourceUrl(values.sourceUrl);
        if (sourceUrlError) {
            newErrors.sourceUrl = sourceUrlError;
        }
        if (values.searchFilterKind !== 'None' && !values.searchFilterValue.trim()) {
            newErrors.searchFilterValue = 'Filter value is required';
        }
        if (values.credentialKind === 'Basic' && (!values.hasSecret || values.secret.length > 0)) {
            if (!values.secret.trim()) {
                newErrors.secret = 'secret is required for basic credentials.';
            }
        }
        if (values.credentialKind === 'SecretReference' && !values.secretReference.trim()) {
            newErrors.secretReference = 'Secret reference is required';
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

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            if (conflict.state === 'deleted' || !validate()) {
                return;
            }
            if (conflict.state === 'stale') {
                setShowStaleSaveConfirmation(true);
                return;
            }
            void save();
        },
        [conflict.state, save, validate],
    );

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="package-source-form__sheet" data-component="package-source-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Feed' : 'Add NuGet Feed'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the NuGet feed used to discover and install extensions.'
                            : 'Configure a NuGet feed to discover and install extensions from.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="package-source-form">
                    <div className="package-source-form__body">
                        {conflict.state && (
                            <ExternalChangeConflictWarning
                                state={conflict.state}
                                entityLabel="NuGet feed"
                                id="package-source-external-change"
                            />
                        )}
                        <div className="package-source-form__field">
                            <Label htmlFor="package-source-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="package-source-name"
                                value={values.name}
                                onChange={(e) => update('name', e.target.value)}
                                placeholder="nuget.org"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="package-source-form__field">
                            <Label htmlFor="package-source-url" error={!!errors.sourceUrl} required>
                                Source URL
                            </Label>
                            <Input
                                id="package-source-url"
                                value={values.sourceUrl}
                                onChange={(e) => update('sourceUrl', e.target.value)}
                                placeholder="https://api.nuget.org/v3/index.json"
                                error={!!errors.sourceUrl}
                                errorMessage={errors.sourceUrl || undefined}
                                disabled={isSubmitting}
                                data-field="sourceUrl"
                            />
                            <p className="package-source-form__help">The v3 service index URL of the NuGet feed.</p>
                        </div>

                        <div className="package-source-form__field package-source-form__field--toggle">
                            <Switch
                                id="package-source-enabled"
                                checked={values.enabled}
                                onCheckedChange={(enabled) => update('enabled', enabled)}
                                disabled={isSubmitting}
                                data-field="enabled"
                            />
                            <Label htmlFor="package-source-enabled">Enabled</Label>
                        </div>

                        <div className="package-source-form__field">
                            <Label htmlFor="package-source-discovery-filter">Discovery filter</Label>
                            <Select
                                value={values.searchFilterKind}
                                onValueChange={(value) =>
                                    update('searchFilterKind', value as PackageSourceSearchFilterKind)
                                }
                                disabled={isSubmitting}
                            >
                                <SelectTrigger id="package-source-discovery-filter" data-field="searchFilterKind">
                                    {filterKindLabels[values.searchFilterKind]}
                                </SelectTrigger>
                                <SelectContent>
                                    {(Object.keys(filterKindLabels) as PackageSourceSearchFilterKind[]).map((kind) => (
                                        <SelectItem key={kind} value={kind}>
                                            {filterKindLabels[kind]}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        </div>

                        {values.searchFilterKind !== 'None' && (
                            <div className="package-source-form__field">
                                <Label
                                    htmlFor="package-source-filter-value"
                                    error={!!errors.searchFilterValue}
                                    required
                                >
                                    Filter value
                                </Label>
                                <Input
                                    id="package-source-filter-value"
                                    value={values.searchFilterValue}
                                    onChange={(e) => update('searchFilterValue', e.target.value)}
                                    placeholder={values.searchFilterKind === 'Tag' ? 'magpie' : 'Magpie.Extensions'}
                                    error={!!errors.searchFilterValue}
                                    errorMessage={errors.searchFilterValue || undefined}
                                    disabled={isSubmitting}
                                    data-field="searchFilterValue"
                                />
                            </div>
                        )}

                        <div className="package-source-form__field">
                            <Label htmlFor="package-source-credentials">Credentials</Label>
                            <Select
                                value={values.credentialKind}
                                onValueChange={(value) =>
                                    update('credentialKind', value as PackageSourceCredentialKind)
                                }
                                disabled={isSubmitting}
                            >
                                <SelectTrigger id="package-source-credentials" data-field="credentialKind">
                                    {credentialKindLabels[values.credentialKind]}
                                </SelectTrigger>
                                <SelectContent>
                                    {(Object.keys(credentialKindLabels) as PackageSourceCredentialKind[]).map(
                                        (kind) => (
                                            <SelectItem key={kind} value={kind}>
                                                {credentialKindLabels[kind]}
                                            </SelectItem>
                                        ),
                                    )}
                                </SelectContent>
                            </Select>
                        </div>

                        {values.credentialKind === 'Basic' && (
                            <>
                                <div className="package-source-form__field">
                                    <Label htmlFor="package-source-username">Username</Label>
                                    <Input
                                        id="package-source-username"
                                        value={values.username}
                                        onChange={(e) => update('username', e.target.value)}
                                        disabled={isSubmitting}
                                        data-field="username"
                                    />
                                </div>
                                <div className="package-source-form__field">
                                    <Label
                                        htmlFor="package-source-secret"
                                        error={!!errors.secret}
                                        required={!values.hasSecret}
                                    >
                                        Secret
                                    </Label>
                                    <SecretInput
                                        id="package-source-secret"
                                        configured={values.hasSecret}
                                        fieldLabel="Secret"
                                        value={values.secret}
                                        onChange={(e) => update('secret', e.target.value)}
                                        error={!!errors.secret}
                                        errorMessage={errors.secret || undefined}
                                        disabled={isSubmitting}
                                        data-field="secret"
                                    />
                                </div>
                            </>
                        )}

                        {values.credentialKind === 'SecretReference' && (
                            <div className="package-source-form__field">
                                <Label
                                    htmlFor="package-source-secret-reference"
                                    error={!!errors.secretReference}
                                    required
                                >
                                    Secret reference
                                </Label>
                                <Input
                                    id="package-source-secret-reference"
                                    value={values.secretReference}
                                    onChange={(e) => update('secretReference', e.target.value)}
                                    placeholder="env:MAGPIE_FEED_TOKEN"
                                    error={!!errors.secretReference}
                                    errorMessage={errors.secretReference || undefined}
                                    disabled={isSubmitting}
                                    data-field="secretReference"
                                />
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
                        <Button
                            type="submit"
                            variant="primary"
                            loading={isSubmitting}
                            disabled={conflict.state === 'deleted'}
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
                open={showStaleSaveConfirmation}
                title="Save changes based on older data?"
                description="This item changed after editing began. Saving will overwrite the newer changes."
                saving={isSubmitting}
                onCancel={() => setShowStaleSaveConfirmation(false)}
                onConfirm={() => {
                    setShowStaleSaveConfirmation(false);
                    void save();
                }}
            />
        </Sheet>
    );
}

PackageSourceForm.displayName = 'PackageSourceForm';

export default PackageSourceForm;
