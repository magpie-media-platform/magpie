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
import { useDownloadClientProviderTypes } from '@/hooks/useProviderTypes';
import { secretsConfiguredForEdit, useDownloadClientSchema } from '@/hooks/useProviderSchema';
import type { IntegrationSettingsTestRequest } from '@/hooks/useTestConnection';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Switch } from '@/components/ui/Switch';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { IntegrationSettingsForm } from '@/components/integrations/IntegrationSettingsForm';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { returnFocusOnClose } from '@/lib/returnFocus';

import './DownloadClientForm.css';

export interface DownloadClientFormValues {
    id?: string;
    name: string;
    provider: string;
    enabled: boolean;
    cleanupDeleteFiles: boolean;
    keepFailedOnClient: boolean;
    settings: Record<string, string | number | boolean | undefined>;
}

interface DownloadClientFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: DownloadClientFormValues | null;

    onSubmit: (values: DownloadClientFormValues) => void | Promise<void>;

    isSubmitting?: boolean;

    onTest?: (request: IntegrationSettingsTestRequest) => void | Promise<void>;

    isTesting?: boolean;

    returnFocusRef?: RefObject<HTMLElement | null>;
}

const emptyValues: DownloadClientFormValues = {
    name: '',
    provider: '',
    enabled: true,
    cleanupDeleteFiles: true,
    keepFailedOnClient: false,
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
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [showStaleSaveDialog, setShowStaleSaveDialog] = useState(false);

    const {
        types: providerTypes,
        labels: providerLabels,
        isLoading: isLoadingProviderTypes,
    } = useDownloadClientProviderTypes();

    const resetFormErrors = useCallback(() => {
        setErrors({});
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
        handleTest,
        handleSubmit: submitIntegrationForm,
    } = useIntegrationSettingsFormController<DownloadClientFormValues>({
        open,
        onOpenChange,
        initialValues,
        emptyValues,
        onSubmit,
        onTest,
        useSchema: useDownloadClientSchema,
        onReset: resetFormErrors,
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
        eventNames: ['downloadClient.updated', 'downloadClient.deleted'],
        deletedEventNames: ['downloadClient.deleted'],
        contextKeys: ['downloadClient.id'],
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

    const handleProviderChange = useCallback(
        (value: string) => {
            changeProvider(value);
            setErrors((prev) => ({ ...prev, provider: '' }));
        },
        [changeProvider],
    );

    const handleEnabledChange = useCallback(
        (enabled: boolean) => {
            setValues((prev) => ({ ...prev, enabled }));
        },
        [setValues],
    );

    const handleCleanupToggle = useCallback(
        (key: 'cleanupDeleteFiles' | 'keepFailedOnClient', value: boolean) => {
            setValues((prev) => ({ ...prev, [key]: value }));
        },
        [setValues],
    );

    const validateRequiredFields = useCallback((): boolean => {
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
                className="download-client-form__sheet"
                data-component="download-client-form"
                onCloseAutoFocus={returnFocusOnClose(returnFocusRef)}
            >
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Download Client' : 'Add Download Client'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the download client configuration.'
                            : 'Download client that receives releases from Magpie.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="download-client-form">
                    <div className="download-client-form__body">
                        {conflictState && (
                            <ExternalChangeConflictWarning
                                state={conflictState}
                                entityLabel="Download client"
                                id="download-client-external-change"
                            />
                        )}
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
                                Integration
                            </Label>
                            {isEditing ? (
                                <Input
                                    id="client-provider"
                                    value={providerLabels[values.provider] ?? values.provider}
                                    readOnly
                                    disabled
                                    data-field="provider"
                                />
                            ) : (
                                <Select
                                    value={values.provider || undefined}
                                    onValueChange={handleProviderChange}
                                    disabled={isSubmitting || noProvidersAvailable}
                                >
                                    <SelectTrigger
                                        id="client-provider"
                                        error={!!errors.provider}
                                        aria-label="Integration"
                                        data-field="provider"
                                    >
                                        <SelectValue
                                            placeholder={
                                                isLoadingProviderTypes
                                                    ? 'Loading...'
                                                    : noProvidersAvailable
                                                      ? 'No integrations available'
                                                      : 'Select a download client integration'
                                            }
                                        />
                                    </SelectTrigger>
                                    <SelectContent>
                                        {providerTypes.map((type) => (
                                            <SelectItem key={type} value={type}>
                                                {providerLabels[type]}
                                            </SelectItem>
                                        ))}
                                    </SelectContent>
                                </Select>
                            )}
                            {errors.provider && (
                                <p className="download-client-form__help" role="alert">
                                    {errors.provider}
                                </p>
                            )}
                            <p className="download-client-form__help">
                                {noProvidersAvailable && !isEditing
                                    ? 'No download client integrations are registered. Install an extension that provides one.'
                                    : 'Magpie sends downloads through this integration.'}
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

                        <div className="download-client-form__settings" data-field="cleanup">
                            <h3 className="download-client-form__settings-title">Cleanup</h3>

                            <div className="download-client-form__field download-client-form__field--toggle">
                                <div className="download-client-form__toggle-wrapper">
                                    <Switch
                                        id="cleanup-delete-files"
                                        checked={values.cleanupDeleteFiles}
                                        onCheckedChange={(checked) =>
                                            handleCleanupToggle('cleanupDeleteFiles', checked)
                                        }
                                        disabled={isSubmitting}
                                        data-field="cleanup-delete-files"
                                    />
                                    <Label htmlFor="cleanup-delete-files">Delete files from client</Label>
                                </div>
                                <p className="download-client-form__help">
                                    Delete the client&apos;s files during cleanup.
                                </p>
                            </div>

                            <div className="download-client-form__field download-client-form__field--toggle">
                                <div className="download-client-form__toggle-wrapper">
                                    <Switch
                                        id="cleanup-keep-failed"
                                        checked={values.keepFailedOnClient}
                                        onCheckedChange={(checked) =>
                                            handleCleanupToggle('keepFailedOnClient', checked)
                                        }
                                        disabled={isSubmitting}
                                        data-field="keep-failed-on-client"
                                    />
                                    <Label htmlFor="cleanup-keep-failed">Keep failed downloads</Label>
                                </div>
                                <p className="download-client-form__help">
                                    Keep failed downloads in the client for troubleshooting.
                                </p>
                            </div>
                        </div>

                        {schema.length > 0 && (
                            <div className="download-client-form__settings" data-field="settings">
                                <h3 className="download-client-form__settings-title">Settings</h3>
                                <IntegrationSettingsForm
                                    schema={schema}
                                    values={settings}
                                    onChange={handleSettingChange}
                                    secretsConfigured={secretsConfigured}
                                    issues={combinedIssues}
                                    disabled={isSubmitting}
                                    idPrefix="client-setting"
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
                        {onTest && values.provider && (
                            <Button
                                type="button"
                                variant="outline"
                                disabled={isSubmitting || isTesting}
                                loading={isTesting}
                                onClick={handleTest}
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
                title="Save changes based on older data?"
                description="This item changed elsewhere after editing began. Saving will overwrite those newer changes with the values in this form."
                saving={isSubmitting}
                onCancel={() => setShowStaleSaveDialog(false)}
                onConfirm={() => void save(null)}
            />
        </Sheet>
    );
}

DownloadClientForm.displayName = 'DownloadClientForm';

export default DownloadClientForm;
