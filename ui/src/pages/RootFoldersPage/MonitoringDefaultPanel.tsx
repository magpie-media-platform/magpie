import { useState, useCallback } from 'react';
import { EyeIcon, CircleNotchIcon, FloppyDiskIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { parseSystemDefaultMonitoring } from '@/components/configuration/RootFolderForm';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { MonitoringToggle } from '@/components/ui/MonitoringToggle';
import { Skeleton } from '@/components/ui/Skeleton';
import { settingsToRecord, useSystemSettingsGroup } from '@/hooks/useSettings';
import { useSettingsGroupForm } from '@/hooks/useSettingsGroupForm';
import type { MonitoringMode } from '@/types';

import './MonitoringDefaultPanel.css';

const SETTINGS_GROUP = 'monitoring';
const SYSTEM_DEFAULT_KEY = 'system-default';
const MAX_SPREAD_KEY = 'max-spread';

type MonitoringFormValues = {
    systemDefault: MonitoringMode;
    maxSpread: string;
};

const defaultValues: MonitoringFormValues = {
    systemDefault: 'Monitored',
    maxSpread: '',
};

function fromRecord(record: Record<string, string>): MonitoringFormValues {
    const systemDefault = parseSystemDefaultMonitoring(record[SYSTEM_DEFAULT_KEY]);
    return {
        systemDefault,
        maxSpread: systemDefault === 'Shotgun' ? (record[MAX_SPREAD_KEY] ?? '0') : '',
    };
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
        fromRecord,
    });
    const { group, dataUpdatedAt } = useSystemSettingsGroup(SETTINGS_GROUP);
    const [savedMaxSpread, setSavedMaxSpread] = useState<{ value: string; dataUpdatedAt: number } | null>(null);
    const storedMaxSpread =
        savedMaxSpread?.dataUpdatedAt === dataUpdatedAt
            ? savedMaxSpread.value
            : (settingsToRecord(group?.settings ?? [])[MAX_SPREAD_KEY] ?? '0');
    const shotgun = values.systemDefault === 'Shotgun';
    const maxSpreadValid = !shotgun || /^\d+$/.test(values.maxSpread.trim());

    const handleChange = useCallback(
        (next: MonitoringMode) => {
            handleSettingChange('systemDefault', next);
            if ((next === 'Shotgun') !== shotgun) {
                handleSettingChange('maxSpread', next === 'Shotgun' ? storedMaxSpread : '');
            }
        },
        [handleSettingChange, shotgun, storedMaxSpread],
    );

    const saveSettings = useCallback(() => {
        void save(
            shotgun
                ? {
                      [SYSTEM_DEFAULT_KEY]: values.systemDefault,
                      [MAX_SPREAD_KEY]: String(Number(values.maxSpread.trim())),
                  }
                : { [SYSTEM_DEFAULT_KEY]: values.systemDefault },
            {
                successDescription: 'Monitoring settings updated.',
                onSuccess: () => setSavedMaxSpread(shotgun ? { value: values.maxSpread, dataUpdatedAt } : null),
            },
        );
    }, [save, shotgun, values.systemDefault, values.maxSpread, dataUpdatedAt]);

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
                                Monitored is used until a default is saved.
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
                            <span id="system-default-monitoring-label" className="label label--md">
                                Default monitoring
                            </span>
                            <MonitoringToggle<MonitoringMode>
                                value={values.systemDefault}
                                onChange={handleChange}
                                aria-labelledby="system-default-monitoring-label"
                                data-field="system-default-monitoring"
                                shotgun
                                inherit={false}
                            />
                        </div>

                        {shotgun && (
                            <>
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
                                    How many to grab at once per item. 0 means all.
                                </p>
                            </>
                        )}

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
