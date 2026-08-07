import { useState, useCallback } from 'react';
import { GearIcon, FloppyDiskIcon, CircleNotchIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { useSettingsGroupForm } from '@/hooks/useSettingsGroupForm';

import { JobRetryPolicyPanel } from './JobRetryPolicyPanel';

import './GeneralPage.css';

const SETTINGS_GROUP = 'general';

type GeneralFormValues = {
    instanceName: string;
    baseUrl: string;
};

const defaultValues: GeneralFormValues = {
    instanceName: '',
    baseUrl: '',
};

export function GeneralPage() {
    useDocumentTitle('Advanced Settings');
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const { values, handleChange, hasChanges, isLoading, loadError, isUncreated, isUpdating, conflictState, save } =
        useSettingsGroupForm<GeneralFormValues>({
            settingsGroup: SETTINGS_GROUP,
            defaultValues,
            fromRecord: (record) => ({
                instanceName: record.instanceName ?? '',
                baseUrl: record.baseUrl ?? '',
            }),
        });

    const saveSettings = useCallback(() => {
        void save(
            {
                instanceName: values.instanceName,
                baseUrl: values.baseUrl,
            },
            { successDescription: 'Magpie updated the general settings.' },
        );
    }, [save, values]);

    const handleSave = useCallback(() => {
        if (conflictState === 'stale') {
            setShowStaleSaveConfirmation(true);
            return;
        }
        saveSettings();
    }, [conflictState, saveSettings]);

    return (
        <div className="general-page" data-page="general">
            <PageHeader
                className="general-page__header"
                title="Advanced"
                icon={<GearIcon weight="duotone" aria-hidden="true" data-testid="general-page__icon" />}
            />

            <section
                className="general-page__settings"
                aria-labelledby="general-settings-title"
                data-component="general-settings"
            >
                <header className="general-page__settings-header">
                    <GearIcon weight="duotone" className="general-page__settings-icon" aria-hidden="true" />
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
                        <Alert
                            variant="warning"
                            icon={<WarningCircleIcon weight="duotone" />}
                            data-testid="settings-error"
                        >
                            Failed to load settings. If the settings group does not exist, save the settings to create
                            it.
                        </Alert>
                    ) : (
                        <>
                            {isUncreated && (
                                <div className="general-page__info" data-testid="settings-info">
                                    <p className="general-page__info-text">Settings will be created on first save.</p>
                                </div>
                            )}

                            {conflictState && (
                                <ExternalChangeConflictWarning
                                    state={conflictState}
                                    entityLabel="Settings"
                                    id="general-settings-external-change"
                                />
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
                                            <CircleNotchIcon
                                                weight="bold"
                                                className="general-page__spinner"
                                                aria-hidden="true"
                                            />
                                            Saving...
                                        </>
                                    ) : (
                                        <>
                                            <FloppyDiskIcon weight="bold" aria-hidden="true" />
                                            Save Settings
                                        </>
                                    )}
                                </Button>
                            </div>
                        </>
                    )}
                </div>
            </section>

            <JobRetryPolicyPanel />

            <StaleSaveConfirmationDialog
                open={showStaleSaveConfirmation}
                entityLabel="settings"
                saving={isUpdating}
                onCancel={() => setShowStaleSaveConfirmation(false)}
                onConfirm={() => {
                    setShowStaleSaveConfirmation(false);
                    saveSettings();
                }}
            />
        </div>
    );
}

GeneralPage.displayName = 'GeneralPage';

export default GeneralPage;
