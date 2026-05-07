import { useState, useCallback, useEffect } from 'react';
import { Gear, FloppyDisk, CircleNotch, WarningCircle } from '@phosphor-icons/react';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/Toast';
import { isApiError } from '@/lib/api';
import { useSystemSettingsGroup, useUpdateSystemSettingsGroup, settingsToRecord } from '@/hooks/useSettings';

import './GeneralPage.css';

const SETTINGS_GROUP = 'general';

interface GeneralFormValues {
    instanceName: string;
    baseUrl: string;
}

const defaultValues: GeneralFormValues = {
    instanceName: '',
    baseUrl: '',
};

export function GeneralPage() {
    useDocumentTitle('General Settings');
    const { group, isLoading, error: rawLoadError } = useSystemSettingsGroup(SETTINGS_GROUP);

    const is404 = isApiError(rawLoadError) && rawLoadError.status === 404;
    const loadError = is404 ? null : rawLoadError;
    const { updateSettings, isUpdating } = useUpdateSystemSettingsGroup(SETTINGS_GROUP);
    const { toast } = useToast();

    const [values, setValues] = useState<GeneralFormValues>(defaultValues);
    const [hasChanges, setHasChanges] = useState(false);

    useUnsavedChangesGuard(hasChanges);

    useEffect(() => {
        if (group) {
            const record = settingsToRecord(group.settings);
            setValues({
                instanceName: record.instanceName ?? '',
                baseUrl: record.baseUrl ?? '',
            });
            setHasChanges(false);
        }
    }, [group]);

    const handleChange = useCallback((key: keyof GeneralFormValues, value: string) => {
        setValues((prev) => ({ ...prev, [key]: value }));
        setHasChanges(true);
    }, []);

    const handleSave = useCallback(async () => {
        const existingKeys = new Set(group?.settings.map((s) => s.key) ?? []);
        try {
            await updateSettings({
                values: {
                    instanceName: values.instanceName,
                    baseUrl: values.baseUrl,
                },
                existingKeys,
            });
            setHasChanges(false);
            toast({
                title: 'Settings Saved',
                description: 'General settings have been updated successfully.',
                variant: 'success',
            });
        } catch (err) {
            toast({
                title: 'Save Failed',
                description: err instanceof Error ? err.message : 'Failed to save settings. Please try again.',
                variant: 'error',
            });
        }
    }, [group, values, updateSettings, toast]);

    return (
        <div className="general-page" data-page="general">
            <header className="general-page__header">
                <div className="general-page__header-left">
                    <Gear
                        weight="duotone"
                        className="general-page__icon"
                        aria-hidden="true"
                        data-testid="general-page__icon"
                    />
                    <h1 className="general-page__title">General</h1>
                </div>
            </header>

            <section
                className="general-page__settings"
                aria-labelledby="general-settings-title"
                data-component="general-settings"
            >
                <header className="general-page__settings-header">
                    <Gear weight="duotone" className="general-page__settings-icon" aria-hidden="true" />
                    <h2 id="general-settings-title" className="general-page__settings-title">
                        Instance Settings
                    </h2>
                </header>

                <div className="general-page__settings-content">
                    {isLoading ? (
                        <div className="general-page__loading" data-testid="settings-loading">
                            <div className="general-page__settings-row">
                                <div className="general-page__settings-field">
                                    <Skeleton width="120px" height="1em" />
                                    <Skeleton width="200px" height="1em" />
                                </div>
                                <Skeleton width="100%" height="36px" />
                            </div>
                            <div className="general-page__settings-row">
                                <div className="general-page__settings-field">
                                    <Skeleton width="80px" height="1em" />
                                    <Skeleton width="240px" height="1em" />
                                </div>
                                <Skeleton width="100%" height="36px" />
                            </div>
                        </div>
                    ) : loadError ? (
                        <Alert variant="warning" icon={<WarningCircle weight="duotone" />} data-testid="settings-error">
                            Failed to load settings. The settings group may not exist yet and will be created on first
                            save.
                        </Alert>
                    ) : (
                        <>
                            {is404 && (
                                <div className="general-page__info" data-testid="settings-info">
                                    <p className="general-page__info-text">Settings will be created on first save.</p>
                                </div>
                            )}

                            <div className="general-page__settings-row">
                                <div className="general-page__settings-field">
                                    <Label htmlFor="instance-name">Instance Name</Label>
                                    <p className="general-page__settings-hint">
                                        A friendly name for this Magpie instance
                                    </p>
                                </div>
                                <Input
                                    id="instance-name"
                                    value={values.instanceName}
                                    onChange={(e) => handleChange('instanceName', e.target.value)}
                                    className="general-page__settings-input"
                                    data-setting="instanceName"
                                />
                            </div>

                            <div className="general-page__settings-row">
                                <div className="general-page__settings-field">
                                    <Label htmlFor="base-url">Base URL</Label>
                                    <p className="general-page__settings-hint">
                                        The external URL used to access this instance
                                    </p>
                                </div>
                                <Input
                                    id="base-url"
                                    value={values.baseUrl}
                                    onChange={(e) => handleChange('baseUrl', e.target.value)}
                                    placeholder="https://magpie.example.com"
                                    className="general-page__settings-input"
                                    data-setting="baseUrl"
                                />
                            </div>

                            <div className="general-page__settings-actions">
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={handleSave}
                                    disabled={!hasChanges || isUpdating}
                                    data-action="save-settings"
                                >
                                    {isUpdating ? (
                                        <>
                                            <CircleNotch
                                                weight="bold"
                                                className="general-page__spinner"
                                                aria-hidden="true"
                                            />
                                            Saving...
                                        </>
                                    ) : (
                                        <>
                                            <FloppyDisk weight="bold" aria-hidden="true" />
                                            Save Settings
                                        </>
                                    )}
                                </Button>
                            </div>
                        </>
                    )}
                </div>
            </section>
        </div>
    );
}

GeneralPage.displayName = 'GeneralPage';

export default GeneralPage;
