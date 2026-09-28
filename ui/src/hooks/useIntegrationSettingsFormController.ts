import {
    useCallback,
    useEffect,
    useMemo,
    useRef,
    useState,
    type Dispatch,
    type SubmitEvent,
    type SetStateAction,
} from 'react';
import type { UseQueryResult } from '@tanstack/react-query';

import { deepEqual, foldDefaultsIntoBaseline, useFormDirty } from '@/hooks/useFormDirty';
import { omitUnchangedSecretSettings, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import {
    extractValidationResultFromError,
    validateIntegrationSettings,
    type IntegrationSettingsValidationIssue,
} from '@/hooks/useValidateSettings';
import type { IntegrationSettingsTestRequest } from '@/hooks/useTestConnection';
import { toStringSettings } from '@/lib/integrationSettings';
import { focusFirstInvalidField } from '@/lib/focusFirstInvalidField';

interface IntegrationSettingsFormValues {
    id?: string;

    provider: string;

    settings: Record<string, string | number | boolean | undefined>;
}

interface UseIntegrationSettingsFormControllerOptions<TValues extends IntegrationSettingsFormValues> {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: TValues | null;

    emptyValues: TValues;

    onSubmit: (values: TValues) => void | Promise<void>;

    onTest?: (request: IntegrationSettingsTestRequest) => void | Promise<void>;

    useSchema: (providerType: string | null) => Pick<UseQueryResult<IntegrationSettingDefinition[]>, 'data'>;

    onReset?: () => void;

    getDirtySnapshot?: (values: TValues, settings: Record<string, string>) => unknown;
}

interface IntegrationSettingsFormController<TValues extends IntegrationSettingsFormValues> {
    values: TValues;

    setValues: Dispatch<SetStateAction<TValues>>;

    settings: Record<string, string>;

    schema: IntegrationSettingDefinition[];

    combinedIssues: IntegrationSettingsValidationIssue[];

    isDirty: boolean;

    liveValidationFailed: boolean;

    showUnsavedDialog: boolean;

    handleOpenChange: (open: boolean) => void;

    handleDiscard: () => void;

    handleKeepEditing: () => void;

    handleProviderChange: (value: string) => void;

    handleSettingChange: (key: string, value: string) => void;

    handleSettingBlur: (key: string) => void;

    handleTest: () => void;

    handleSubmit: (e: SubmitEvent | null, validateRequiredFields: () => boolean) => Promise<boolean>;
}

const closedForm = Symbol('closed form');

function withStringSettings<TValues extends IntegrationSettingsFormValues>(
    values: TValues,
    settings: Record<string, string> = toStringSettings(values.settings),
): TValues {
    return { ...values, settings };
}

function defaultDirtySnapshot<TValues extends IntegrationSettingsFormValues>(
    values: TValues,
    settings: Record<string, string>,
): unknown {
    return { ...values, settings };
}

export function useIntegrationSettingsFormController<TValues extends IntegrationSettingsFormValues>({
    open,
    onOpenChange,
    initialValues,
    emptyValues,
    onSubmit,
    onTest,
    useSchema,
    onReset,
    getDirtySnapshot = defaultDirtySnapshot,
}: UseIntegrationSettingsFormControllerOptions<TValues>): IntegrationSettingsFormController<TValues> {
    const initialCleanValues = withStringSettings(initialValues ?? emptyValues);
    const initialCleanSettings = toStringSettings((initialValues ?? emptyValues).settings);

    const [values, setValues] = useState<TValues>(initialCleanValues);
    const [settings, setSettings] = useState<Record<string, string>>(initialCleanSettings);
    const [serverIssues, setServerIssues] = useState<IntegrationSettingsValidationIssue[]>([]);
    const [touchedSettings, setTouchedSettings] = useState<Set<string>>(new Set());
    const [submitted, setSubmitted] = useState(false);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);

    const cleanValuesRef = useRef<TValues>(initialCleanValues);
    const cleanSettingsRef = useRef<Record<string, string>>(initialCleanSettings);
    const submitInFlightRef = useRef(false);
    const seededProviderRef = useRef<string | null>(null);
    const resetKeyRef = useRef<TValues | null | undefined | typeof closedForm>(closedForm);
    const valuesRef = useRef(values);
    const settingsRef = useRef(settings);
    valuesRef.current = values;
    settingsRef.current = settings;

    const schemaQuery = useSchema(values.provider || null);
    const schema = useMemo(() => schemaQuery.data ?? [], [schemaQuery.data]);

    useEffect(() => {
        if (!open) {
            resetKeyRef.current = closedForm;
            return;
        }
        const reopened = resetKeyRef.current === closedForm;
        if (!reopened && resetKeyRef.current === initialValues) {
            return;
        }
        resetKeyRef.current = initialValues;
        if (!reopened) {
            const currentDirty = !deepEqual(
                getDirtySnapshot(valuesRef.current, settingsRef.current),
                getDirtySnapshot(cleanValuesRef.current, cleanSettingsRef.current),
            );
            if (currentDirty && cleanValuesRef.current.id === initialValues?.id) {
                return;
            }
        }
        const baseValues = initialValues ?? emptyValues;
        const cleanSettings = toStringSettings(baseValues.settings);
        const cleanValues = withStringSettings(baseValues, cleanSettings);
        setValues(cleanValues);
        setSettings(cleanSettings);
        cleanValuesRef.current = cleanValues;
        cleanSettingsRef.current = cleanSettings;
        setServerIssues([]);
        setTouchedSettings(new Set());
        setSubmitted(false);
        setShowUnsavedDialog(false);
        seededProviderRef.current = null;
        onReset?.();
    }, [emptyValues, getDirtySnapshot, initialValues, onReset, open]);

    const isDirty = useFormDirty(
        getDirtySnapshot(values, settings),
        getDirtySnapshot(cleanValuesRef.current, cleanSettingsRef.current),
    );

    useUnsavedChangesGuard(open && isDirty && !showUnsavedDialog);

    const schemaValidation = useMemo(
        () => validateIntegrationSettings(schema, settings, !!values.id),
        [schema, settings, values.id],
    );

    useEffect(() => {
        if (!open || !values.provider || schema.length === 0) {
            return;
        }
        if (seededProviderRef.current === values.provider) {
            return;
        }
        seededProviderRef.current = values.provider;
        setSettings((prev) => {
            const visibleSettings = omitUnchangedSecretSettings(prev, schema);
            const baseline = omitUnchangedSecretSettings(cleanSettingsRef.current, schema);
            const seeded = foldDefaultsIntoBaseline(visibleSettings, baseline, schema);
            cleanSettingsRef.current = seeded.baseline;
            return seeded.next;
        });
    }, [open, schema, values.provider]);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen && isDirty) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(nextOpen);
        },
        [isDirty, onOpenChange],
    );

    const handleDiscard = useCallback(() => {
        setShowUnsavedDialog(false);
        onOpenChange(false);
    }, [onOpenChange]);

    const handleKeepEditing = useCallback(() => {
        setShowUnsavedDialog(false);
    }, []);

    const handleProviderChange = useCallback((value: string) => {
        setValues((prev) => ({ ...prev, provider: value }));
        setSettings({});
        setServerIssues([]);
        setTouchedSettings(new Set());
        seededProviderRef.current = null;
    }, []);

    const handleSettingChange = useCallback((key: string, value: string) => {
        setSettings((prev) => ({ ...prev, [key]: value }));
        setServerIssues((prev) => prev.filter((issue) => issue.key !== key));
    }, []);

    const handleSettingBlur = useCallback((key: string) => {
        setTouchedSettings((prev) => {
            if (prev.has(key)) {
                return prev;
            }
            const next = new Set(prev);
            next.add(key);
            return next;
        });
    }, []);

    const handleTest = useCallback(() => {
        if (!values.provider) {
            return;
        }
        const submittedSettings = omitUnchangedSecretSettings(settings, schema);
        void onTest?.({ providerType: values.provider, id: values.id, settings: submittedSettings });
    }, [onTest, values.provider, values.id, settings, schema]);

    const handleSubmit = useCallback(
        async (e: SubmitEvent | null, validateRequiredFields: () => boolean) => {
            e?.preventDefault();
            if (submitInFlightRef.current) {
                return false;
            }
            setSubmitted(true);
            if (!validateRequiredFields()) {
                focusFirstInvalidField(e?.currentTarget ?? null);
                return false;
            }

            submitInFlightRef.current = true;
            try {
                const submittedSettings = omitUnchangedSecretSettings(settings, schema);
                await onSubmit(withStringSettings(values, submittedSettings));
                setServerIssues([]);
                return true;
            } catch (error) {
                const validation = extractValidationResultFromError(error);
                if (validation) {
                    setServerIssues(validation.issues);
                }
                return false;
            } finally {
                submitInFlightRef.current = false;
            }
        },
        [onSubmit, schema, settings, values],
    );

    const combinedIssues = useMemo<IntegrationSettingsValidationIssue[]>(() => {
        const seen = new Set<string>();
        const merged: IntegrationSettingsValidationIssue[] = [];
        for (const issue of serverIssues) {
            const id = `${issue.key ?? '__form__'}::${issue.message}`;
            if (!seen.has(id)) {
                seen.add(id);
                merged.push(issue);
            }
        }
        if (!schemaValidation.isValid) {
            for (const issue of schemaValidation.issues) {
                const surfaced = submitted || (issue.key != null && touchedSettings.has(issue.key));
                if (!surfaced) {
                    continue;
                }
                const id = `${issue.key ?? '__form__'}::${issue.message}`;
                if (!seen.has(id)) {
                    seen.add(id);
                    merged.push(issue);
                }
            }
        }
        return merged;
    }, [serverIssues, schemaValidation, submitted, touchedSettings]);

    const liveValidationFailed = !schemaValidation.isValid;

    return {
        values,
        setValues,
        settings,
        schema,
        combinedIssues,
        isDirty,
        liveValidationFailed,
        showUnsavedDialog,
        handleOpenChange,
        handleDiscard,
        handleKeepEditing,
        handleProviderChange,
        handleSettingChange,
        handleSettingBlur,
        handleTest,
        handleSubmit,
    };
}
