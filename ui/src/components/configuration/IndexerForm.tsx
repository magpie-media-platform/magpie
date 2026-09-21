import { useState, useCallback, type ChangeEvent, type SubmitEvent, type ReactNode, type RefObject } from 'react';
import { FloppyDiskIcon, XIcon } from '@phosphor-icons/react';

import { useIntegrationSettingsFormController } from '@/hooks/useIntegrationSettingsFormController';
import { useIndexerProviderTypes } from '@/hooks/useProviderTypes';
import { useIndexerSchema } from '@/hooks/useProviderSchema';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { IntegrationSettingsForm } from '@/components/integrations/IntegrationSettingsForm';

import './IndexerForm.css';

export interface IndexerFormValues {
    name: string;
    providerType: string;
    description?: string;
    settings: Record<string, string>;
}

interface IndexerFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    onSubmit: (values: IndexerFormValues) => void | Promise<void>;

    isSubmitting?: boolean;

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
    onSubmit,
    isSubmitting = false,
    returnFocusRef,
}: IndexerFormProps): ReactNode {
    const [errors, setErrors] = useState<Record<string, string>>({});
    const { types: providerTypes, isLoading: isLoadingProviderTypes } = useIndexerProviderTypes();

    const submitIndexer = useCallback(
        (submittedValues: IndexerSettingsFormValues) => {
            const { provider, ...indexer } = submittedValues;
            return onSubmit({ ...indexer, providerType: provider });
        },
        [onSubmit],
    );

    const resetForm = useCallback(() => {
        setErrors({});
    }, []);

    const {
        values,
        setValues,
        settings,
        schema,
        combinedIssues,
        liveValidationFailed,
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
        emptyValues,
        onSubmit: submitIndexer,
        useSchema: useIndexerSchema,
        onReset: resetForm,
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

    const handleSubmit = useCallback(
        (e: SubmitEvent) => {
            void submitIntegrationForm(e, validateRequiredFields);
        },
        [submitIntegrationForm, validateRequiredFields],
    );

    const noProvidersAvailable = !isLoadingProviderTypes && providerTypes.length === 0;
    const saveDisabled = isSubmitting || liveValidationFailed;

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
                    <SheetTitle>Add Indexer</SheetTitle>
                    <SheetDescription>Configure a new indexer to search for content.</SheetDescription>
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
                                Integration
                            </Label>
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
                            {errors.providerType && (
                                <p className="indexer-form__help" role="alert">
                                    {errors.providerType}
                                </p>
                            )}
                            <p className="indexer-form__help">
                                {noProvidersAvailable
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
        </Sheet>
    );
}

IndexerForm.displayName = 'IndexerForm';

export default IndexerForm;
