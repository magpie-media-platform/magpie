import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react';

import { settingsToRecord, useSystemSettingsGroup, useUpdateSystemSettingsGroup } from '@/hooks/useSettings';
import { deepEqual } from '@/hooks/useFormDirty';
import { useToast } from '@/hooks/ui/useToast';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

interface UseSettingsGroupFormOptions<T extends Record<string, string>> {
    settingsGroup: string;
    defaultValues: T;
    fromRecord: (record: Record<string, string>) => T;
    onLoaded?: () => void;
}

interface SaveSettingsOptions {
    successDescription: string;
    onSuccess?: () => void;
    onError?: (error: unknown) => void;
}

interface SettingsGroupForm<T extends Record<string, string>> {
    values: T;
    setValues: Dispatch<SetStateAction<T>>;
    hasChanges: boolean;
    setHasChanges: Dispatch<SetStateAction<boolean>>;
    handleChange: (key: keyof T, value: string) => void;
    isLoading: boolean;
    loadError: unknown;
    isUncreated: boolean;
    isUpdating: boolean;
    conflictState: 'stale' | null;
    save: (settingsValues: Record<string, string>, options: SaveSettingsOptions) => Promise<void>;
}

export function useSettingsGroupForm<T extends Record<string, string>>(
    options: UseSettingsGroupFormOptions<T>,
): SettingsGroupForm<T> {
    const { settingsGroup, defaultValues, fromRecord, onLoaded } = options;

    const { group, isLoading, error: loadError, dataUpdatedAt } = useSystemSettingsGroup(settingsGroup);
    const isUncreated = !isLoading && !loadError && group === null;
    const { updateSettings, isUpdating } = useUpdateSystemSettingsGroup(settingsGroup);
    const { toast } = useToast();

    const [values, setValues] = useState<T>(defaultValues);
    const [hasChanges, setHasChanges] = useState(false);
    const [conflictState, setConflictState] = useState<'stale' | null>(null);
    const hasChangesRef = useRef(false);
    const valuesRef = useRef(values);
    const baselineRef = useRef<T>(defaultValues);
    const observedServerValuesRef = useRef<T | null>(null);

    useUnsavedChangesGuard(hasChanges);

    useEffect(() => {
        hasChangesRef.current = hasChanges;
    }, [hasChanges]);

    useEffect(() => {
        valuesRef.current = values;
    }, [values]);

    const fromRecordRef = useRef(fromRecord);
    fromRecordRef.current = fromRecord;
    const onLoadedRef = useRef(onLoaded);
    onLoadedRef.current = onLoaded;

    useEffect(() => {
        if (!group) {
            return;
        }

        const record = settingsToRecord(group.settings);
        const serverValues = fromRecordRef.current(record);
        observedServerValuesRef.current = serverValues;

        if (hasChangesRef.current) {
            if (!deepEqual(serverValues, baselineRef.current)) {
                setConflictState('stale');
            } else {
                setConflictState(null);
            }
            return;
        }

        baselineRef.current = serverValues;
        valuesRef.current = serverValues;
        setValues(serverValues);
        setConflictState(null);
        onLoadedRef.current?.();
        hasChangesRef.current = false;
        setHasChanges(false);
    }, [dataUpdatedAt, group]);

    const handleChange = useCallback((key: keyof T, value: string) => {
        let next = { ...valuesRef.current, [key]: value } as T;
        const latestServerValues = observedServerValuesRef.current;

        if (latestServerValues && deepEqual(next, latestServerValues)) {
            baselineRef.current = latestServerValues;
        } else if (
            latestServerValues &&
            deepEqual(next, baselineRef.current) &&
            !deepEqual(latestServerValues, baselineRef.current)
        ) {
            baselineRef.current = latestServerValues;
            next = latestServerValues;
        }

        const dirty = !deepEqual(next, baselineRef.current);
        valuesRef.current = next;
        hasChangesRef.current = dirty;
        setValues(next);
        setHasChanges(dirty);
        if (!dirty) {
            setConflictState(null);
        }
    }, []);

    const save = useCallback(
        async (settingsValues: Record<string, string>, saveOptions: SaveSettingsOptions) => {
            const existingKeys = new Set(group?.settings.map((s) => s.key) ?? []);
            const submittedValues = valuesRef.current;
            try {
                await updateSettings({ values: settingsValues, existingKeys });
                baselineRef.current = submittedValues;

                const hasNewerEdits = !deepEqual(valuesRef.current, submittedValues);
                const observedServerValues = observedServerValuesRef.current;
                if (
                    !hasNewerEdits ||
                    (observedServerValues !== null && deepEqual(observedServerValues, submittedValues))
                ) {
                    setConflictState(null);
                }
                hasChangesRef.current = hasNewerEdits;
                setHasChanges(hasNewerEdits);
                saveOptions.onSuccess?.();
                toast({
                    title: 'Settings Saved',
                    description: saveOptions.successDescription,
                    variant: 'success',
                });
            } catch (err) {
                saveOptions.onError?.(err);
                toast({
                    title: 'Save Failed',
                    description: err instanceof Error ? err.message : 'Failed to save settings. Please try again.',
                    variant: 'error',
                });
            }
        },
        [group, updateSettings, toast],
    );

    return {
        values,
        setValues,
        hasChanges,
        setHasChanges,
        handleChange,
        isLoading,
        loadError,
        isUncreated,
        isUpdating,
        conflictState,
        save,
    };
}
