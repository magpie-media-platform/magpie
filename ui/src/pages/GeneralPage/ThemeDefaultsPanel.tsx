import { useCallback, useState } from 'react';
import { CircleNotchIcon, FloppyDiskIcon, PaletteIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { LoadErrorAlert } from '@/components/ui/LoadErrorAlert';
import { Skeleton } from '@/components/ui/Skeleton';
import { ThemeSettingsFields } from '@/components/ui/ThemeDialog';
import { useSettingsGroupForm } from '@/hooks/useSettingsGroupForm';
import { useThemeCatalog } from '@/hooks/useThemeCatalog';
import type { ThemeCatalog, ThemeMode, ThemeSettings } from '@/lib/theme';

const SETTINGS_GROUP = 'theme';

function ThemeDefaultsForm({ catalog }: { catalog: ThemeCatalog }) {
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const { values, handleChange, hasChanges, isLoading, loadError, refetch, isUpdating, conflictState, save } =
        useSettingsGroupForm<ThemeSettings>({
            settingsGroup: SETTINGS_GROUP,
            defaultValues: catalog.defaults,
            fromRecord: (record) => ({
                mode: (record.mode as ThemeMode | undefined) ?? catalog.defaults.mode,
                darkTheme: record.darkTheme ?? catalog.defaults.darkTheme,
                lightTheme: record.lightTheme ?? catalog.defaults.lightTheme,
            }),
        });

    const saveSettings = useCallback(() => {
        void save(values, { successDescription: 'Theme defaults updated.' });
    }, [save, values]);

    const handleSave = useCallback(() => {
        if (conflictState === 'stale') {
            setShowStaleSaveConfirmation(true);
            return;
        }
        saveSettings();
    }, [conflictState, saveSettings]);

    if (isLoading) {
        return <Skeleton height="140px" />;
    }

    if (loadError) {
        return <LoadErrorAlert onRetry={() => void refetch()} />;
    }

    return (
        <>
            <p className="general-page__settings-hint">Used when a browser has no theme override.</p>
            {conflictState && (
                <ExternalChangeConflictWarning
                    state={conflictState}
                    entityLabel="Settings"
                    id="theme-defaults-external-change"
                />
            )}
            <div className="general-page__settings-input">
                <ThemeSettingsFields
                    themes={catalog.themes}
                    value={values}
                    disabled={isUpdating}
                    onChange={handleChange}
                />
            </div>
            <div className="general-page__settings-actions">
                <Button
                    variant="primary"
                    size="sm"
                    onClick={handleSave}
                    disabled={!hasChanges || isUpdating}
                    data-action="save-theme-defaults"
                >
                    {isUpdating ? (
                        <>
                            <CircleNotchIcon weight="bold" className="general-page__spinner" aria-hidden="true" />
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
        </>
    );
}

export function ThemeDefaultsPanel() {
    const catalog = useThemeCatalog();

    return (
        <section
            className="general-page__settings"
            aria-labelledby="theme-defaults-title"
            data-component="theme-defaults"
        >
            <header className="general-page__settings-header">
                <PaletteIcon weight="duotone" className="general-page__settings-icon" aria-hidden="true" />
                <h2 id="theme-defaults-title" className="general-page__settings-title">
                    Theme defaults
                </h2>
            </header>
            <div className="general-page__settings-content">
                {catalog.data ? (
                    <ThemeDefaultsForm catalog={catalog.data} />
                ) : catalog.isError ? (
                    <LoadErrorAlert onRetry={() => void catalog.refetch()} />
                ) : (
                    <Skeleton height="140px" />
                )}
            </div>
        </section>
    );
}
