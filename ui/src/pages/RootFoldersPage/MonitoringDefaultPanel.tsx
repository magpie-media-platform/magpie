import { useState, useCallback } from 'react';
import { EyeIcon, CircleNotchIcon, FloppyDiskIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Skeleton } from '@/components/ui/Skeleton';
import { Switch } from '@/components/ui/Switch';
import { useSettingsGroupForm } from '@/hooks/useSettingsGroupForm';

import './MonitoringDefaultPanel.css';

const SETTINGS_GROUP = 'monitoring';
const SYSTEM_DEFAULT_KEY = 'system-default';
const MAX_SPREAD_KEY = 'max-spread';

const DEFAULT_MONITORING = true;

type MonitoringFormValues = {
    systemDefault: string;
    maxSpread: string;
};

const defaultValues: MonitoringFormValues = {
    systemDefault: String(DEFAULT_MONITORING),
    maxSpread: '0',
};

function parseMonitoring(raw: string | undefined): boolean {
    if (raw === 'false' || raw === '0') {
        return false;
    }
    return DEFAULT_MONITORING;
}

export function MonitoringDefaultPanel() {
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const {
        values,
        handleChange: handleSettingChange,
        hasChanges,
        isLoading,
        loadError,
        isUncreated,
        isUpdating,
        conflictState,
        save,
    } = useSettingsGroupForm<MonitoringFormValues>({
        settingsGroup: SETTINGS_GROUP,
        defaultValues,
        fromRecord: (record) => ({
            systemDefault: String(parseMonitoring(record[SYSTEM_DEFAULT_KEY])),
            maxSpread: record[MAX_SPREAD_KEY] ?? '0',
        }),
    });
    const value = parseMonitoring(values.systemDefault);
    const maxSpreadValid = /^\d+$/.test(values.maxSpread.trim());

    const handleChange = useCallback(
        (next: boolean) => {
            handleSettingChange('systemDefault', String(next));
        },
        [handleSettingChange],
    );

    const saveSettings = useCallback(() => {
        void save(
            { [SYSTEM_DEFAULT_KEY]: String(value), [MAX_SPREAD_KEY]: String(Number(values.maxSpread.trim())) },
            { successDescription: 'Monitoring settings updated.' },
        );
    }, [save, value, values.maxSpread]);

    const handleSave = useCallback(() => {
        if (conflictState === 'stale') {
            setShowStaleSaveConfirmation(true);
            return;
        }
        saveSettings();
    }, [conflictState, saveSettings]);

    return (
        <section
            className="root-folders-page__settings monitoring-default-panel"
            aria-labelledby="monitoring-default-title"
            data-component="monitoring-default-settings"
        >
            <header className="monitoring-default-panel__header">
                <EyeIcon weight="duotone" className="monitoring-default-panel__icon" aria-hidden="true" />
                <div className="monitoring-default-panel__heading">
                    <h2 id="monitoring-default-title" className="monitoring-default-panel__title">
                        Monitoring
                    </h2>
                    <p className="monitoring-default-panel__subtitle">
                        The default monitoring applies to new content when its root folder uses the system default.
                    </p>
                </div>
            </header>

            <div className="monitoring-default-panel__content">
                {isLoading ? (
                    <Skeleton width="220px" height="36px" data-testid="monitoring-default-loading" />
                ) : loadError ? (
                    <Alert
                        variant="warning"
                        icon={<WarningCircleIcon weight="duotone" />}
                        data-testid="monitoring-default-error"
                    >
                        Failed to load the monitoring default. If the settings group does not exist, save the setting to
                        create it.
                    </Alert>
                ) : (
                    <>
                        {isUncreated && (
                            <p className="monitoring-default-panel__info" data-testid="monitoring-default-info">
                                Active is used until a default is saved.
                            </p>
                        )}

                        {conflictState && (
                            <ExternalChangeConflictWarning
                                state={conflictState}
                                entityLabel="Settings"
                                id="monitoring-settings-external-change"
                            />
                        )}

                        <div className="monitoring-default-panel__row">
                            <Label htmlFor="system-default-monitoring">Default monitoring</Label>
                            <div className="monitoring-default-panel__toggle">
                                <Switch
                                    id="system-default-monitoring"
                                    checked={value}
                                    onCheckedChange={handleChange}
                                    aria-label="System default monitoring"
                                    data-setting="system-default-monitoring"
                                />
                                <span>{value ? 'Active' : 'Excluded'}</span>
                            </div>
                        </div>

                        <div className="monitoring-default-panel__row">
                            <Label htmlFor="shotgun-max-spread">Max spread</Label>
                            <Input
                                id="shotgun-max-spread"
                                type="number"
                                inputMode="numeric"
                                min={0}
                                step={1}
                                value={values.maxSpread}
                                onChange={(event) => handleSettingChange('maxSpread', event.target.value)}
                                className="monitoring-default-panel__number"
                                data-setting="max-spread"
                                error={!maxSpreadValid}
                                errorMessage={
                                    maxSpreadValid ? undefined : 'Max spread must be a whole number, 0 or more.'
                                }
                            />
                        </div>
                        <p className="monitoring-default-panel__info">
                            The most releases Shotgun grabs at the same time for one item. 0 grabs every acceptable
                            release.
                        </p>

                        <div className="monitoring-default-panel__row">
                            <Button
                                variant="primary"
                                size="sm"
                                onClick={handleSave}
                                disabled={!hasChanges || isUpdating || !maxSpreadValid}
                                data-action="save-monitoring-default"
                            >
                                {isUpdating ? (
                                    <>
                                        <CircleNotchIcon
                                            weight="bold"
                                            className="monitoring-default-panel__spinner"
                                            aria-hidden="true"
                                        />
                                        Saving...
                                    </>
                                ) : (
                                    <>
                                        <FloppyDiskIcon weight="bold" aria-hidden="true" />
                                        Save
                                    </>
                                )}
                            </Button>
                        </div>
                    </>
                )}
            </div>
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
        </section>
    );
}

MonitoringDefaultPanel.displayName = 'MonitoringDefaultPanel';

export default MonitoringDefaultPanel;
