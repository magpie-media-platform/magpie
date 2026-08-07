import {
    useState,
    useCallback,
    useMemo,
    type ChangeEvent,
    type SubmitEvent,
    type ReactNode,
    type RefObject,
} from 'react';
import { FloppyDiskIcon, XIcon, FlaskIcon } from '@phosphor-icons/react';

import { useIntegrationSettingsFormController } from '@/hooks/useIntegrationSettingsFormController';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';
import { useIndexerProviderTypes } from '@/hooks/useProviderTypes';
import { secretsConfiguredForEdit, useIndexerSchema } from '@/hooks/useProviderSchema';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { IntegrationSettingsForm } from '@/components/integrations/IntegrationSettingsForm';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';

import './IndexerForm.css';

export interface IndexerFormValues {
    id?: string;
    name: string;
    providerType: string;
    description?: string;
    settings: Record<string, string>;
}

interface IndexerFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: IndexerFormValues | null;

    onSubmit: (values: IndexerFormValues) => void | Promise<void>;

    isSubmitting?: boolean;

    onTest?: () => void;

    isTesting?: boolean;

    returnFocusRef?: RefObject<HTMLElement | null>;
}

type IndexerSettingsFormValues = Omit<IndexerFormValues, 'providerType'> & { provider: string };

const emptyValues: IndexerSettingsFormValues = {
    name: '',
    provider: '',
    settings: {},
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
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [showStaleSaveDialog, setShowStaleSaveDialog] = useState(false);
    const { types: providerTypes, isLoading: isLoadingProviderTypes } = useIndexerProviderTypes();

    const controllerInitialValues = useMemo<IndexerSettingsFormValues | null | undefined>(
        () =>
            initialValues
                ? {
                      id: initialValues.id,
                      name: initialValues.name,
                      provider: initialValues.providerType,
                      description: initialValues.description,
                      settings: initialValues.settings,
                  }
                : initialValues,
        [initialValues],
    );

    const submitIndexer = useCallback(
        (submittedValues: IndexerSettingsFormValues) => {
            const { provider, ...indexer } = submittedValues;
            return onSubmit({ ...indexer, providerType: provider });
        },
        [onSubmit],
    );

    const resetForm = useCallback(() => {
        setErrors({});
        setShowStaleSaveDialog(false);
    }, []);

    const {
        values,
        setValues,
        settings,
        schema,
        combinedIssues,
        liveValidationFailed,
        isDirty,
        showUnsavedDialog,
        handleOpenChange,
        handleDiscard,
        handleKeepEditing,
        handleProviderChange: changeProvider,
        handleSettingChange,
        handleSettingBlur,
        handleSubmit: submitIntegrationForm,
    } = useIntegrationSettingsFormController<IndexerSettingsFormValues>({
        open,
        onOpenChange,
        initialValues: controllerInitialValues,
        emptyValues,
        onSubmit: submitIndexer,
        useSchema: useIndexerSchema,
        onReset: resetForm,
    });

    const isEditing = !!values.id;
    const secretsConfigured = useMemo(() => secretsConfiguredForEdit(schema, isEditing), [schema, isEditing]);

    const {
        state: conflictState,
        beginOwnSave,
        endOwnSave,
    } = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['indexer.updated', 'indexer.deleted'],
        deletedEventNames: ['indexer.deleted'],
        contextKeys: ['indexer.id'],
        enabled: open,
    });

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const handleNameChange = useCallback(
        (e: ChangeEvent<HTMLInputElement>) => {
            setValues((prev) => ({ ...prev, name: e.target.value }));
            setErrors((prev) => ({ ...prev, name: '' }));
        },
        [setValues],
    );

    const handleProviderTypeChange = useCallback(
        (value: string) => {
            changeProvider(value);
            setErrors((prev) => ({ ...prev, providerType: '' }));
        },
        [changeProvider],
    );

    const handleDescriptionChange = useCallback(
        (e: ChangeEvent<HTMLInputElement>) => {
            setValues((prev) => ({ ...prev, description: e.target.value }));
        },
        [setValues],
    );

    const validateRequiredFields = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        if (!values.provider.trim()) {
            newErrors.providerType = 'Integration is required';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    }, [values]);

    const save = useCallback(
        async (e: SubmitEvent | null) => {
            beginOwnSave();
            const saved = await submitIntegrationForm(e, validateRequiredFields);
            endOwnSave(saved);
            if (saved) {
                setShowStaleSaveDialog(false);
            }
        },
        [beginOwnSave, endOwnSave, submitIntegrationForm, validateRequiredFields],
    );

    const handleSubmit = useCallback(
        (e: SubmitEvent) => {
            e.preventDefault();
            if (conflictState === 'stale') {
                if (validateRequiredFields()) {
                    setShowStaleSaveDialog(true);
                }
                return;
            }
            void save(e);
        },
        [conflictState, save, validateRequiredFields],
    );

    const noProvidersAvailable = !isLoadingProviderTypes && providerTypes.length === 0;
    const saveDisabled = isSubmitting || isTesting || liveValidationFailed || conflictState === 'deleted';

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
                        {conflictState && (
                            <ExternalChangeConflictWarning
                                state={conflictState}
                                entityLabel="Indexer"
                                id="indexer-external-change"
                            />
                        )}
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
                                Integration
                            </Label>
                            {isEditing ? (
                                <Input
                                    id="indexer-provider-type"
                                    value={values.provider}
                                    readOnly
                                    disabled
                                    data-field="providerType"
                                />
                            ) : (
                                <Select
                                    value={values.provider || undefined}
                                    onValueChange={handleProviderTypeChange}
                                    disabled={isSubmitting || noProvidersAvailable}
                                >
                                    <SelectTrigger
                                        id="indexer-provider-type"
                                        error={!!errors.providerType}
                                        aria-label="Integration"
                                        data-field="providerType"
                                    >
                                        <SelectValue
                                            placeholder={
                                                isLoadingProviderTypes
                                                    ? 'Loading...'
                                                    : noProvidersAvailable
                                                      ? 'No integrations available'
                                                      : 'Select an indexer integration'
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
                            {errors.providerType && (
                                <p className="indexer-form__help" role="alert">
                                    {errors.providerType}
                                </p>
                            )}
                            <p className="indexer-form__help">
                                {noProvidersAvailable && !isEditing
                                    ? 'No indexer integrations are available. Install an extension that provides one.'
                                    : 'The indexer protocol, such as Newznab or Torznab.'}
                            </p>
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

                        {schema.length > 0 && (
                            <div className="indexer-form__settings" data-field="settings">
                                <h3 className="indexer-form__settings-title">Settings</h3>
                                <IntegrationSettingsForm
                                    schema={schema}
                                    values={settings}
                                    onChange={handleSettingChange}
                                    secretsConfigured={secretsConfigured}
                                    issues={combinedIssues}
                                    disabled={isSubmitting}
                                    idPrefix="indexer-setting"
                                    onFieldBlur={handleSettingBlur}
                                />
                            </div>
                        )}
                    </div>

                    <SheetFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            disabled={isSubmitting || isTesting}
                            onClick={handleCancel}
                            data-action="cancel"
                        >
                            <XIcon weight="bold" aria-hidden="true" />
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
                                <FlaskIcon weight="bold" aria-hidden="true" />
                                Test
                            </Button>
                        )}
                        <Button
                            type="submit"
                            variant="primary"
                            disabled={saveDisabled}
                            loading={isSubmitting}
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
                entityLabel="Indexer"
                saving={isSubmitting}
                onCancel={() => setShowStaleSaveDialog(false)}
                onConfirm={() => void save(null)}
            />
        </Sheet>
    );
}

IndexerForm.displayName = 'IndexerForm';

export default IndexerForm;
