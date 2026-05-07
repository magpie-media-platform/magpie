import { useMemo, useCallback } from 'react';

import {
    useSystemSettingsGroup,
    useUpdateSystemSettingsGroup,
    settingsToRecord,
    type UpdateSettingsVars,
} from './useSettings';

export type BackupInterval = 'daily' | 'weekly' | 'biweekly' | 'monthly';

export interface BackupSettings {
    enabled: boolean;

    interval: BackupInterval;

    retention: number;
}

const defaultSettings: BackupSettings = {
    enabled: false,
    interval: 'daily',
    retention: 7,
};

function parseBoolean(value: string | undefined): boolean {
    return value?.toLowerCase() === 'true';
}

function parseIntOrDefault(value: string | undefined, defaultValue: number): number {
    if (!value) return defaultValue;
    const parsed = Number.parseInt(value, 10);
    return Number.isNaN(parsed) ? defaultValue : parsed;
}

function parseInterval(value: string | undefined, defaultValue: BackupInterval): BackupInterval {
    if (!value) return defaultValue;
    const intervals: BackupInterval[] = ['daily', 'weekly', 'biweekly', 'monthly'];
    return intervals.includes(value as BackupInterval) ? (value as BackupInterval) : defaultValue;
}

function apiSettingsToBackupSettings(record: Record<string, string>): BackupSettings {
    return {
        enabled: parseBoolean(record.enabled),
        interval: parseInterval(record.interval, defaultSettings.interval),
        retention: parseIntOrDefault(record.retention, defaultSettings.retention),
    };
}

function backupSettingsToApiSettings(settings: BackupSettings): Record<string, string> {
    return {
        enabled: settings.enabled.toString(),
        interval: settings.interval,
        retention: settings.retention.toString(),
    };
}

export function useBackupSettings() {
    const { group, isLoading, error } = useSystemSettingsGroup('backups');
    const { updateSettings, isUpdating, error: updateError } = useUpdateSystemSettingsGroup('backups');

    const settings = useMemo<BackupSettings>(() => {
        if (!group?.settings) return defaultSettings;
        const record = settingsToRecord(group.settings);
        return apiSettingsToBackupSettings(record);
    }, [group]);

    const existingKeys = useMemo(() => {
        if (!group?.settings) return new Set<string>();
        return new Set(group.settings.map((s) => s.key));
    }, [group]);

    const saveSettings = useCallback(
        async (newSettings: BackupSettings): Promise<void> => {
            const apiSettings = backupSettingsToApiSettings(newSettings);
            const vars: UpdateSettingsVars = {
                values: apiSettings,
                existingKeys,
            };
            await updateSettings(vars);
        },
        [updateSettings, existingKeys],
    );

    return {
        settings,
        isLoading,
        error,
        saveSettings,
        isSaving: isUpdating,
        saveError: updateError,
    };
}
