import { useState, useCallback, useEffect, useRef, useMemo, type ReactNode } from 'react';
import { FloppyDiskIcon, XIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Switch } from '@/components/ui/Switch';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { IntegrationSettingsForm } from '@/components/integrations/IntegrationSettingsForm/IntegrationSettingsForm';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';
import { useIndexerFeedProviderTypes } from '@/hooks/useProviderTypes';
import { omitUnchangedSecretSettings, secretsConfiguredForEdit, useIndexerFeedSchema } from '@/hooks/useProviderSchema';
import { foldDefaultsIntoBaseline } from '@/hooks/useFormDirty';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';

import './IndexerFeedForm.css';

export interface IndexerFeedFormValues {
    id?: string;
    name: string;
    description: string;
    provider: string;
    enabled: boolean;
    settings: Record<string, string>;
}

interface IndexerFeedFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: IndexerFeedFormValues | null;

    onSubmit: (values: IndexerFeedFormValues) => void | Promise<void>;

    isSubmitting?: boolean;
}

const emptyValues: IndexerFeedFormValues = {
    name: '',
    description: '',
    provider: '',
    enabled: true,
    settings: {},
};

export function IndexerFeedForm({
    open,
    onOpenChange,
    initialValues,
    onSubmit,
    isSubmitting = false,
}: IndexerFeedFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<IndexerFeedFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveDialog, setShowStaleSaveDialog] = useState(false);

    const cleanValuesRef = useRef<IndexerFeedFormValues>(initialValues ?? emptyValues);

    const { types: providerTypes, isLoading: isLoadingProviderTypes } = useIndexerFeedProviderTypes();
    const schemaQuery = useIndexerFeedSchema(values.provider || null);
    const schema = useMemo(() => schemaQuery.data ?? [], [schemaQuery.data]);
    const secretsConfigured = useMemo(() => secretsConfiguredForEdit(schema, isEditing), [schema, isEditing]);
    const seededProviderRef = useRef<string | null>(null);
    const resetKeyRef = useRef<IndexerFeedFormValues | null | undefined>(undefined);

    const isDirty = useMemo(() => {
        const clean = cleanValuesRef.current;
        return (
            values.name !== clean.name ||
            values.description !== clean.description ||
            values.provider !== clean.provider ||
            values.enabled !== clean.enabled ||
            JSON.stringify(values.settings) !== JSON.stringify(clean.settings)
        );
    }, [values]);

    const {
        state: conflictState,
        beginOwnSave,
        endOwnSave,
        clearConflict,
    } = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['indexerFeed.updated', 'indexerFeed.deleted'],
        deletedEventNames: ['indexerFeed.deleted'],
        contextKeys: ['indexerFeed.id'],
        enabled: open,
    });

    useEffect(() => {
        if (!open) {
            resetKeyRef.current = undefined;
            return;
        }
        if (resetKeyRef.current === initialValues) {
            return;
        }
        resetKeyRef.current = initialValues;
        if (isDirty && cleanValuesRef.current.id === initialValues?.id) {
            return;
        }
        const vals = initialValues ?? emptyValues;
        setValues(vals);
        cleanValuesRef.current = vals;
        setErrors({});
        setShowUnsavedDialog(false);
        setShowStaleSaveDialog(false);
        seededProviderRef.current = null;
        clearConflict();
    }, [clearConflict, initialValues, isDirty, open]);

    useEffect(() => {
        if (!values.provider || schema.length === 0 || seededProviderRef.current === values.provider) {
            return;
        }
        seededProviderRef.current = values.provider;
        setValues((prev) => {
            const visibleSettings = omitUnchangedSecretSettings(prev.settings, schema);
            const baseline = omitUnchangedSecretSettings(cleanValuesRef.current.settings, schema);
            const seeded = foldDefaultsIntoBaseline(visibleSettings, baseline, schema);
            cleanValuesRef.current = { ...cleanValuesRef.current, settings: seeded.baseline };
            return { ...prev, settings: seeded.next };
        });
    }, [values.provider, schema]);

    useUnsavedChangesGuard(open && isDirty && !isSubmitting);

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

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const handleDiscard = useCallback(() => {
        setShowUnsavedDialog(false);
        onOpenChange(false);
    }, [onOpenChange]);

    const handleKeepEditing = useCallback(() => {
        setShowUnsavedDialog(false);
    }, []);

    const handleNameChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, name: e.target.value }));
        setErrors((prev) => ({ ...prev, name: '' }));
    }, []);

    const handleDescriptionChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, description: e.target.value }));
    }, []);

    const handleProviderChange = useCallback((value: string) => {
        seededProviderRef.current = null;
        setValues((prev) => ({ ...prev, provider: value, settings: {} }));
        setErrors((prev) => ({ ...prev, provider: '' }));
    }, []);

    const handleSettingChange = useCallback((key: string, value: string) => {
        setValues((prev) => ({ ...prev, settings: { ...prev.settings, [key]: value } }));
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
            newErrors.provider = 'Integration is required';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    }, [values]);

    const save = useCallback(async () => {
        beginOwnSave();
        try {
            const submittedSettings = omitUnchangedSecretSettings(values.settings, schema);
            await Promise.resolve(onSubmit({ ...values, id: values.id, settings: submittedSettings }));
            endOwnSave(true);
            setShowStaleSaveDialog(false);
        } catch {
            endOwnSave(false);
        }
    }, [beginOwnSave, endOwnSave, onSubmit, schema, values]);

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

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="indexer-feed-form__sheet" data-component="indexer-feed-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Feed' : 'Add Feed'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the indexer feed configuration.'
                            : 'Configure a new feed to sync indexer definitions.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="indexer-feed-form">
                    <div className="indexer-feed-form__body">
                        {conflictState && (
                            <ExternalChangeConflictWarning
                                state={conflictState}
                                entityLabel="Indexer feed"
                                id="indexer-feed-external-change"
                            />
                        )}
                        <div className="indexer-feed-form__field">
                            <Label htmlFor="feed-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="feed-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Feed"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="indexer-feed-form__field">
                            <Label htmlFor="feed-description">Description</Label>
                            <Input
                                id="feed-description"
                                value={values.description}
                                onChange={handleDescriptionChange}
                                placeholder="Optional description"
                                disabled={isSubmitting}
                                data-field="description"
                            />
                        </div>

                        <div className="indexer-feed-form__field">
                            <Label htmlFor="feed-provider" error={!!errors.provider} required>
                                Integration
                            </Label>
                            {isEditing ? (
                                <Input
                                    id="feed-provider"
                                    value={values.provider}
                                    readOnly
                                    disabled
                                    data-field="provider"
                                />
                            ) : (
                                <Select
                                    value={values.provider || undefined}
                                    onValueChange={handleProviderChange}
                                    disabled={isSubmitting || (!isLoadingProviderTypes && providerTypes.length === 0)}
                                >
                                    <SelectTrigger
                                        id="feed-provider"
                                        error={!!errors.provider}
                                        aria-label="Integration"
                                        data-field="provider"
                                    >
                                        <SelectValue
                                            placeholder={
                                                isLoadingProviderTypes
                                                    ? 'Loading...'
                                                    : providerTypes.length === 0
                                                      ? 'No feed integrations available'
                                                      : 'Select a feed integration'
                                            }
                                        />
                                    </SelectTrigger>
                                    <SelectContent>
                                        {providerTypes.map((type) => (
                                            <SelectItem key={type} value={type}>
                                                {type}
                                            </SelectItem>
                                        ))}
                                    </SelectContent>
                                </Select>
                            )}
                            {errors.provider && (
                                <p className="indexer-feed-form__help" role="alert">
                                    {errors.provider}
                                </p>
                            )}
                            <p className="indexer-feed-form__help">
                                {!isLoadingProviderTypes && providerTypes.length === 0 && !isEditing
                                    ? 'No feed integrations are available. An extension that provides one must be installed.'
                                    : 'Choose the feed integration that supplies indexer definitions.'}
                            </p>
                        </div>

                        <div className="indexer-feed-form__field indexer-feed-form__field--toggle">
                            <div className="indexer-feed-form__toggle-wrapper">
                                <Switch
                                    id="feed-enabled"
                                    checked={values.enabled}
                                    onCheckedChange={handleEnabledChange}
                                    disabled={isSubmitting}
                                    data-field="enabled"
                                />
                                <Label htmlFor="feed-enabled">Enabled</Label>
                            </div>
                            <p className="indexer-feed-form__help">Disabled feeds will not be synced.</p>
                        </div>

                        {schema.length > 0 && (
                            <div className="indexer-feed-form__field">
                                <IntegrationSettingsForm
                                    schema={schema}
                                    values={values.settings}
                                    onChange={handleSettingChange}
                                    secretsConfigured={secretsConfigured}
                                    disabled={isSubmitting}
                                    idPrefix="indexer-feed-setting"
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

IndexerFeedForm.displayName = 'IndexerFeedForm';

export default IndexerFeedForm;
