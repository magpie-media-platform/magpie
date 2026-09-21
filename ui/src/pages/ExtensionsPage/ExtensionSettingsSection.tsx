import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { GearSixIcon, FloppyDiskIcon, PlugsConnectedIcon } from '@phosphor-icons/react';

import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { getErrorMessage } from '@/lib/errors';
import { Button } from '@/components/ui/Button';
import { IntegrationSettingsForm } from '@/components/integrations/IntegrationSettingsForm/IntegrationSettingsForm';
import { useToast } from '@/components/ui/Toast';
import {
    useExtensionSettingsGroups,
    useUpdateExtensionSettingsGroup,
    useTestExtensionSettingsGroup,
    settingsToRecord,
    type ApiSettingsGroup,
} from '@/hooks/useSettings';
import { omitUnchangedSecretSettings, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

interface ExtensionSettingsSectionProps {
    extensionId: string;
}

export function ExtensionSettingsSection({ extensionId }: ExtensionSettingsSectionProps): ReactNode {
    const { groups, dataUpdatedAt, isLoading, error } = useExtensionSettingsGroups(extensionId);

    const configurable = useMemo(
        () => groups.filter((group) => (group.schema?.length ?? 0) > 0).sort((a, b) => a.name.localeCompare(b.name)),
        [groups],
    );

    return (
        <section className="extension-detail__section" data-section="settings">
            <h2 className="extension-detail__section-title">
                <GearSixIcon weight="duotone" aria-hidden="true" />
                Settings
            </h2>
            {error ? (
                <p className="extension-detail__section-note extension-detail__section-note--error" role="alert">
                    Failed to load settings. {getErrorMessage(error)}
                </p>
            ) : isLoading ? (
                <p className="extension-detail__section-note" role="status">
                    Loading settings...
                </p>
            ) : configurable.length === 0 ? (
                <p className="extension-detail__section-note">This extension has no configurable settings.</p>
            ) : null}
            {configurable.map((group) => (
                <ExtensionSettingsGroupForm
                    key={`${extensionId}:${group.name}`}
                    extensionId={extensionId}
                    group={group}
                    serverVersion={dataUpdatedAt}
                />
            ))}
        </section>
    );
}

ExtensionSettingsSection.displayName = 'ExtensionSettingsSection';

interface ExtensionSettingsGroupFormProps {
    extensionId: string;
    group: ApiSettingsGroup;
    serverVersion: number;
}

function buildInitialValues(group: ApiSettingsGroup, schema: IntegrationSettingDefinition[]): Record<string, string> {
    const record = settingsToRecord(group.settings);
    for (const entry of schema) {
        if (!(entry.key in record) && !entry.secret && entry.defaultValue != null) {
            record[entry.key] = entry.defaultValue;
        }
    }
    return record;
}

function normalizeValues(
    values: Record<string, string>,
    schema: IntegrationSettingDefinition[],
): Record<string, string> {
    const normalized = { ...values };
    for (const entry of schema) {
        if (entry.secret && !normalized[entry.key]) {
            delete normalized[entry.key];
        }
    }
    return normalized;
}

function withoutSecretValues(
    values: Record<string, string>,
    schema: IntegrationSettingDefinition[],
): Record<string, string> {
    const normalized = { ...values };
    for (const entry of schema) {
        if (entry.secret) {
            delete normalized[entry.key];
        }
    }
    return normalized;
}

function valuesEqual(left: Record<string, string>, right: Record<string, string>): boolean {
    const leftKeys = Object.keys(left);
    const rightKeys = Object.keys(right);
    return (
        leftKeys.length === rightKeys.length &&
        leftKeys.every((key) => Object.prototype.hasOwnProperty.call(right, key) && left[key] === right[key])
    );
}

function secretStatesEqual(
    left: ApiSettingsGroup['secretSettings'],
    right: ApiSettingsGroup['secretSettings'],
): boolean {
    const leftStates = left ?? {};
    const rightStates = right ?? {};
    const keys = new Set([...Object.keys(leftStates), ...Object.keys(rightStates)]);
    return [...keys].every((key) => leftStates[key]?.isSet === rightStates[key]?.isSet);
}

function ExtensionSettingsGroupForm({ extensionId, group, serverVersion }: ExtensionSettingsGroupFormProps): ReactNode {
    const { toast } = useToast();
    const schema = useMemo(() => group.schema ?? [], [group.schema]);
    const secretsConfigured = useMemo(() => {
        const result: Record<string, boolean> = {};
        for (const [key, state] of Object.entries(group.secretSettings ?? {})) {
            result[key] = state.isSet;
        }
        return result;
    }, [group.secretSettings]);
    const serverValues = useMemo(() => normalizeValues(buildInitialValues(group, schema), schema), [group, schema]);
    const [values, setValues] = useState<Record<string, string>>(() => serverValues);
    const valuesRef = useRef(values);
    const cleanValuesRef = useRef(serverValues);
    const observedServerValuesRef = useRef(serverValues);
    const cleanSecretSettingsRef = useRef(group.secretSettings ?? {});
    const observedSecretSettingsRef = useRef(group.secretSettings ?? {});
    const [conflictState, setConflictState] = useState<'stale' | null>(null);
    const [showStaleSaveDialog, setShowStaleSaveDialog] = useState(false);
    valuesRef.current = values;

    const update = useUpdateExtensionSettingsGroup(extensionId, group.name);
    const connectionTest = useTestExtensionSettingsGroup(extensionId, group.name);
    const isDirty = !valuesEqual(normalizeValues(values, schema), cleanValuesRef.current);

    useUnsavedChangesGuard(isDirty);

    useEffect(() => {
        observedServerValuesRef.current = serverValues;
        observedSecretSettingsRef.current = group.secretSettings ?? {};
        const currentValues = normalizeValues(valuesRef.current, schema);
        if (valuesEqual(currentValues, cleanValuesRef.current)) {
            cleanValuesRef.current = serverValues;
            cleanSecretSettingsRef.current = group.secretSettings ?? {};
            valuesRef.current = serverValues;
            setValues(serverValues);
            setConflictState(null);
            return;
        }

        const serverMatchesBaseline =
            valuesEqual(serverValues, cleanValuesRef.current) &&
            secretStatesEqual(group.secretSettings, cleanSecretSettingsRef.current);
        setConflictState(serverMatchesBaseline ? null : 'stale');
    }, [group.secretSettings, schema, serverValues, serverVersion]);

    const handleChange = useCallback((key: string, value: string) => {
        setValues((prev) => {
            const next = { ...prev, [key]: value };
            valuesRef.current = next;
            return next;
        });
    }, []);

    const save = useCallback(async () => {
        const submittedValues = valuesRef.current;
        if (valuesEqual(normalizeValues(submittedValues, schema), cleanValuesRef.current)) {
            setShowStaleSaveDialog(false);
            return;
        }
        const existingKeys = new Set(group.settings.map((s) => s.key));
        const payload = omitUnchangedSecretSettings(submittedValues, schema);
        try {
            await update.updateSettings({ values: payload, existingKeys });
            const savedValues = withoutSecretValues(submittedValues, schema);
            const savedSecretSettings = { ...observedSecretSettingsRef.current };
            for (const entry of schema) {
                if (entry.secret && submittedValues[entry.key]) {
                    savedSecretSettings[entry.key] = { isSet: true };
                }
            }
            cleanValuesRef.current = savedValues;
            cleanSecretSettingsRef.current = savedSecretSettings;

            const nextValues = { ...valuesRef.current };
            for (const entry of schema) {
                if (entry.secret && nextValues[entry.key] === submittedValues[entry.key]) {
                    delete nextValues[entry.key];
                }
            }
            valuesRef.current = nextValues;
            setValues(nextValues);

            const hasNewerEdits = !valuesEqual(normalizeValues(nextValues, schema), savedValues);
            const observedServerMatchesSaved =
                valuesEqual(observedServerValuesRef.current, savedValues) &&
                secretStatesEqual(observedSecretSettingsRef.current, savedSecretSettings);
            if (!hasNewerEdits || observedServerMatchesSaved) {
                setConflictState(null);
            }
            setShowStaleSaveDialog(false);
            toast({ title: 'Settings Saved', description: `${group.name} settings saved.`, variant: 'success' });
        } catch (err) {
            toast({
                title: 'Save Failed',
                description: getErrorMessage(err, 'Failed to save settings.'),
                variant: 'error',
            });
        }
    }, [group.name, group.settings, schema, toast, update]);

    const handleSave = useCallback(() => {
        if (conflictState === 'stale') {
            setShowStaleSaveDialog(true);
            return;
        }
        void save();
    }, [conflictState, save]);

    const handleTest = useCallback(async () => {
        const payload = omitUnchangedSecretSettings(values, schema);
        try {
            const result = await connectionTest.test(payload);
            toast({
                title: result.success ? 'Connection Succeeded' : 'Connection Failed',
                description: result.message,
                variant: result.success ? 'success' : 'error',
            });
        } catch (err) {
            toast({
                title: 'Connection Failed',
                description: getErrorMessage(err, 'Failed to run the connection test.'),
                variant: 'error',
            });
        }
    }, [connectionTest, schema, toast, values]);

    return (
        <div
            className="extension-detail__settings-group"
            data-settings-group={group.name}
            data-testid="extension-settings-group"
        >
            {conflictState && (
                <ExternalChangeConflictWarning
                    state={conflictState}
                    entityLabel="Configuration"
                    id={`extension-settings-external-change-${encodeURIComponent(group.name)}`}
                />
            )}
            <IntegrationSettingsForm
                schema={schema}
                values={values}
                onChange={handleChange}
                secretsConfigured={secretsConfigured}
                extensionId={extensionId}
                idPrefix={`ext-setting-${group.name}`}
                disabled={update.isUpdating}
            />
            <div className="extension-detail__settings-actions">
                <Button
                    type="button"
                    variant="primary"
                    size="md"
                    onClick={handleSave}
                    loading={update.isUpdating}
                    disabled={!isDirty}
                    data-action="save-settings"
                >
                    <FloppyDiskIcon weight="bold" aria-hidden="true" />
                    Save
                </Button>
                {group.canTest ? (
                    <Button
                        type="button"
                        variant="secondary"
                        size="md"
                        onClick={handleTest}
                        loading={connectionTest.isTesting}
                        data-action="test-connection"
                    >
                        <PlugsConnectedIcon weight="bold" aria-hidden="true" />
                        Test
                    </Button>
                ) : null}
            </div>
            <StaleSaveConfirmationDialog
                open={showStaleSaveDialog}
                entityLabel="configuration"
                saving={update.isUpdating}
                onCancel={() => setShowStaleSaveDialog(false)}
                onConfirm={() => void save()}
            />
        </div>
    );
}
