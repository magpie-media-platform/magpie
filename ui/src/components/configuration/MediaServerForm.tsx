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
import { useMediaServerProviderTypes } from '@/hooks/useProviderTypes';
import { secretsConfiguredForEdit, useMediaServerSchema } from '@/hooks/useProviderSchema';
import type { IntegrationSettingsTestRequest } from '@/hooks/useTestConnection';
import { useInstanceName } from '@/hooks/useInstanceName';

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

import './MediaServerForm.css';

export interface MediaServerFormValues {
    id?: string;
    name: string;
    provider: string;
    enabled: boolean;
    settings: Record<string, string | number | boolean | undefined>;
}

interface MediaServerFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: MediaServerFormValues | null;

    onSubmit: (values: MediaServerFormValues) => void | Promise<void>;

    isSubmitting?: boolean;

    onTest?: (request: IntegrationSettingsTestRequest) => void | Promise<void>;

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
    const instanceName = useInstanceName();
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [showStaleSaveDialog, setShowStaleSaveDialog] = useState(false);

    const {
        types: providerTypes,
        labels: providerLabels,
        isLoading: isLoadingProviderTypes,
    } = useMediaServerProviderTypes();

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
    } = useIntegrationSettingsFormController<MediaServerFormValues>({
        open,
        onOpenChange,
        initialValues,
        emptyValues,
        onSubmit,
        onTest,
        useSchema: useMediaServerSchema,
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
        eventNames: ['mediaServer.updated', 'mediaServer.deleted'],
        deletedEventNames: ['mediaServer.deleted'],
        contextKeys: ['mediaServer.id'],
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
                className="media-server-form__sheet"
                data-component="media-server-form"
                onCloseAutoFocus={returnFocusOnClose(returnFocusRef)}
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
                        {conflictState && (
                            <ExternalChangeConflictWarning
                                state={conflictState}
                                entityLabel="Media server"
                                id="media-server-external-change"
                            />
                        )}
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
                                Integration
                            </Label>
                            {isEditing ? (
                                <Input
                                    id="server-provider"
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
                                        id="server-provider"
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
                                                      : 'Select a media server integration'
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
                                <p className="media-server-form__help" role="alert">
                                    {errors.provider}
                                </p>
                            )}
                            <p className="media-server-form__help">
                                {noProvidersAvailable && !isEditing
                                    ? 'No media server integrations are registered. Install an extension that provides one.'
                                    : `${instanceName} imports content into this integration.`}
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

                        {schema.length > 0 && (
                            <div className="media-server-form__settings" data-field="settings">
                                <h3 className="media-server-form__settings-title">Settings</h3>
                                <IntegrationSettingsForm
                                    schema={schema}
                                    values={settings}
                                    onChange={handleSettingChange}
                                    secretsConfigured={secretsConfigured}
                                    issues={combinedIssues}
                                    disabled={isSubmitting}
                                    idPrefix="server-setting"
                                    entityId={values.id ?? null}
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

MediaServerForm.displayName = 'MediaServerForm';

export default MediaServerForm;
