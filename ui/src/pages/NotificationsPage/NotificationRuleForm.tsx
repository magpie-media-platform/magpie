import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { FloppyDiskIcon, InfoIcon, PlusIcon, TrashIcon, XIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { IntegrationSettingsForm } from '@/components/integrations/IntegrationSettingsForm';
import { Label } from '@/components/ui/Label';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/Sheet';
import { Switch } from '@/components/ui/Switch';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';
import { useNotificationProviderSchema } from '@/hooks/useNotificationRules';
import { deepEqual, foldDefaultsIntoBaseline, stripBlankRows, useFormDirty } from '@/hooks/useFormDirty';
import { omitUnchangedSecretSettings, secretsConfiguredForEdit } from '@/hooks/useProviderSchema';
import {
    extractValidationResultFromError,
    validateIntegrationSettings,
    type IntegrationSettingsValidationIssue,
} from '@/hooks/useValidateSettings';
import type { EditableEventContextCriterion } from '@/lib/eventContextCriteria';
import { focusFirstInvalidField } from '@/lib/focusFirstInvalidField';

import './NotificationRuleForm.css';

export type NotificationContextFilter = EditableEventContextCriterion;

export interface NotificationRuleFormValues {
    id?: string;
    name: string;
    providerType: string;
    eventNamePattern: string;
    contextFilters: NotificationContextFilter[];
    settings: Record<string, string>;
    enabled: boolean;
}

interface NotificationRuleFormProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    initialValues?: NotificationRuleFormValues | null;
    providers: string[];
    providerLabels: Record<string, string>;
    isProvidersLoading?: boolean;
    providersError?: Error | null;
    onRetryProviders?: () => void;
    onSubmit: (values: NotificationRuleFormValues) => void | Promise<void>;
    isSubmitting?: boolean;
}

const emptyValues: NotificationRuleFormValues = {
    name: '',
    providerType: '',
    eventNamePattern: '',
    contextFilters: [],
    settings: {},
    enabled: true,
};

function isBlankContextFilter(filter: NotificationContextFilter): boolean {
    return filter.key.trim() === '' && (filter.pattern === null || filter.pattern.trim() === '');
}

function normalizeRule(values: NotificationRuleFormValues): unknown {
    return {
        name: values.name,
        providerType: values.providerType,
        eventNamePattern: values.eventNamePattern,
        enabled: values.enabled,
        contextFilters: stripBlankRows(values.contextFilters, isBlankContextFilter),
        settings: values.settings,
    };
}

function validateRegex(pattern: string): string {
    if (!pattern.trim()) {
        return '';
    }
    try {
        new RegExp(pattern);
        return '';
    } catch {
        return 'Invalid regex pattern';
    }
}

export function NotificationRuleForm({
    open,
    onOpenChange,
    initialValues,
    providers,
    providerLabels,
    isProvidersLoading = false,
    providersError = null,
    onRetryProviders,
    onSubmit,
    isSubmitting = false,
}: NotificationRuleFormProps): ReactNode {
    const isEditing = !!initialValues?.id;
    const initial = initialValues ?? emptyValues;

    const [values, setValues] = useState<NotificationRuleFormValues>(initial);
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [serverIssues, setServerIssues] = useState<IntegrationSettingsValidationIssue[]>([]);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveDialog, setShowStaleSaveDialog] = useState(false);

    const cleanValuesRef = useRef<NotificationRuleFormValues>(initial);
    const filterKeyCounter = useRef(0);
    const [filterKeys, setFilterKeys] = useState<string[]>(() =>
        initial.contextFilters.map(() => `cf-${filterKeyCounter.current++}`),
    );
    const seededProviderRef = useRef<string | null>(null);
    const wasOpenRef = useRef(false);
    const prevInitialValuesRef = useRef<NotificationRuleFormValues | null | undefined>(initialValues);

    const schemaQuery = useNotificationProviderSchema(values.providerType || null);
    const schema = useMemo(() => schemaQuery.data ?? [], [schemaQuery.data]);
    const secretsConfigured = useMemo(() => secretsConfiguredForEdit(schema, isEditing), [schema, isEditing]);

    useEffect(() => {
        const justOpened = open && !wasOpenRef.current;
        const initialValuesChanged = open && prevInitialValuesRef.current !== initialValues;
        wasOpenRef.current = open;
        prevInitialValuesRef.current = initialValues;

        if (!open) {
            return;
        }

        const currentDirty = !deepEqual(normalizeRule(values), normalizeRule(cleanValuesRef.current));
        const preserveDirtyDraft =
            initialValuesChanged && currentDirty && cleanValuesRef.current.id === initialValues?.id;

        if (justOpened || (initialValuesChanged && !preserveDirtyDraft)) {
            const next = initialValues ?? emptyValues;
            const visibleSettings = omitUnchangedSecretSettings(next.settings, schema);
            const visibleValues = { ...next, settings: visibleSettings };
            setValues(visibleValues);
            cleanValuesRef.current = visibleValues;
            setErrors({});
            setServerIssues([]);
            setShowUnsavedDialog(false);
            setShowStaleSaveDialog(false);
            setFilterKeys(next.contextFilters.map(() => `cf-${filterKeyCounter.current++}`));
            seededProviderRef.current = null;
            return;
        }

        setValues((prev) => {
            const visibleSettings = omitUnchangedSecretSettings(prev.settings, schema);
            if (deepEqual(visibleSettings, prev.settings)) {
                return prev;
            }
            cleanValuesRef.current = {
                ...cleanValuesRef.current,
                settings: omitUnchangedSecretSettings(cleanValuesRef.current.settings, schema),
            };
            return { ...prev, settings: visibleSettings };
        });
    }, [open, initialValues, schema, values]);

    useEffect(() => {
        if (!open || !values.providerType || schema.length === 0) {
            return;
        }
        if (seededProviderRef.current === values.providerType) {
            return;
        }
        seededProviderRef.current = values.providerType;
        setValues((prev) => {
            const seeded = foldDefaultsIntoBaseline(prev.settings, cleanValuesRef.current.settings, schema);
            cleanValuesRef.current = { ...cleanValuesRef.current, settings: seeded.baseline };
            return seeded.changed ? { ...prev, settings: seeded.next } : prev;
        });
    }, [open, schema, values.providerType]);

    const isDirty = useFormDirty(values, cleanValuesRef.current, normalizeRule);
    const conflict = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['notificationRule.updated', 'notificationRule.deleted'],
        deletedEventNames: ['notificationRule.deleted'],
        contextKeys: ['notificationRule.id'],
        enabled: open,
    });

    useUnsavedChangesGuard(open && isDirty && !showUnsavedDialog);

    const handleOpenChange = useCallback(
        (next: boolean) => {
            if (!next && isDirty) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(next);
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

    const handleNameChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const value = e.target.value;
        setValues((prev) => ({ ...prev, name: value }));
        setErrors((prev) => ({ ...prev, name: '' }));
    }, []);

    const handleProviderChange = useCallback((value: string) => {
        setValues((prev) => ({ ...prev, providerType: value, settings: {} }));
        setErrors((prev) => ({ ...prev, providerType: '' }));
        setServerIssues([]);
        seededProviderRef.current = null;
    }, []);

    useEffect(() => {
        if (!open || isEditing || values.providerType || providers.length !== 1) {
            return;
        }
        const provider = providers[0];
        handleProviderChange(provider);
        cleanValuesRef.current = { ...cleanValuesRef.current, providerType: provider };
    }, [open, isEditing, values.providerType, providers, handleProviderChange]);

    const handleEventNameChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const value = e.target.value;
        setValues((prev) => ({ ...prev, eventNamePattern: value }));
        setErrors((prev) => ({ ...prev, eventNamePattern: '' }));
    }, []);

    const handleEnabledChange = useCallback((enabled: boolean) => {
        setValues((prev) => ({ ...prev, enabled }));
    }, []);

    const handleSettingChange = useCallback((key: string, value: string) => {
        setValues((prev) => ({ ...prev, settings: { ...prev.settings, [key]: value } }));
        setServerIssues((prev) => prev.filter((issue) => issue.key !== key));
    }, []);

    const handleAddFilter = useCallback(() => {
        setValues((prev) => ({
            ...prev,
            contextFilters: [...prev.contextFilters, { key: '', pattern: '', negate: false }],
        }));
        setFilterKeys((prev) => [...prev, `cf-${filterKeyCounter.current++}`]);
    }, []);

    const handleRemoveFilter = useCallback((index: number) => {
        setValues((prev) => ({
            ...prev,
            contextFilters: prev.contextFilters.filter((_, i) => i !== index),
        }));
        setFilterKeys((prev) => prev.filter((_, i) => i !== index));
    }, []);

    const handleFilterKeyChange = useCallback((index: number, key: string) => {
        setValues((prev) => ({
            ...prev,
            contextFilters: prev.contextFilters.map((f, i) => (i === index ? { ...f, key } : f)),
        }));
    }, []);

    const handleFilterPatternChange = useCallback((index: number, pattern: string) => {
        setValues((prev) => ({
            ...prev,
            contextFilters: prev.contextFilters.map((f, i) => (i === index ? { ...f, pattern } : f)),
        }));
    }, []);

    const handleFilterNegateChange = useCallback((index: number, negate: boolean) => {
        setValues((prev) => ({
            ...prev,
            contextFilters: prev.contextFilters.map((f, i) => (i === index ? { ...f, negate } : f)),
        }));
    }, []);

    const validate = useCallback((): boolean => {
        const next: Record<string, string> = {};
        if (!values.name.trim()) {
            next.name = 'Name is required';
        }
        if (!values.providerType) {
            next.providerType = 'Provider is required';
        }
        const patternError = validateRegex(values.eventNamePattern);
        if (patternError) {
            next.eventNamePattern = patternError;
        }
        values.contextFilters.forEach((filter, index) => {
            if (!filter.key.trim()) {
                next[`filter-${index}-key`] = 'Key is required';
            }
            if (filter.pattern) {
                const e = validateRegex(filter.pattern);
                if (e) {
                    next[`filter-${index}-pattern`] = e;
                }
            }
        });
        const settingsValidation = validateIntegrationSettings(schema, values.settings, isEditing);
        setErrors(next);
        setServerIssues(settingsValidation.issues);
        return Object.keys(next).length === 0 && settingsValidation.isValid;
    }, [isEditing, schema, values]);

    const save = useCallback(async () => {
        conflict.beginOwnSave();
        const submittedSettings = omitUnchangedSecretSettings(values.settings, schema);
        try {
            await onSubmit({ ...values, settings: submittedSettings });
            setServerIssues([]);
            setShowStaleSaveDialog(false);
            conflict.endOwnSave(true);
        } catch (error) {
            conflict.endOwnSave(false);
            const validation = extractValidationResultFromError(error);
            if (validation) {
                setServerIssues(validation.issues);
            }
        }
    }, [conflict, onSubmit, schema, values]);

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            if (!validate()) {
                focusFirstInvalidField(e.currentTarget);
                return;
            }
            if (conflict.state === 'stale') {
                setShowStaleSaveDialog(true);
                return;
            }
            void save();
        },
        [conflict.state, save, validate],
    );

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const hasProvidersError = !isEditing && providersError !== null;
    const noProviders = !isProvidersLoading && !hasProvidersError && providers.length === 0;
    const hasSchemaError = values.providerType !== '' && schemaQuery.isError;
    const cannotSubmit = isSubmitting || hasProvidersError || hasSchemaError || conflict.state === 'deleted';

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent
                side="right"
                className="notification-rule-form__sheet"
                data-component="notification-rule-form"
            >
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Notification Rule' : 'Add Notification Rule'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the notification rule.'
                            : 'Configure a new rule to route matching events through a notification provider.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="notification-rule-form">
                    <div className="notification-rule-form__body">
                        {conflict.state && (
                            <ExternalChangeConflictWarning
                                state={conflict.state}
                                entityLabel="Notification rule"
                                id="notification-rule-external-change"
                            />
                        )}
                        <div className="notification-rule-form__field">
                            <Label htmlFor="notif-rule-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="notif-rule-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My notification"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="notification-rule-form__field">
                            <Label htmlFor="notif-rule-provider" error={!!errors.providerType} required>
                                Provider
                            </Label>
                            {isEditing ? (
                                <Input
                                    id="notif-rule-provider"
                                    value={providerLabels[values.providerType] ?? values.providerType}
                                    readOnly
                                    disabled
                                    data-field="providerType"
                                />
                            ) : (
                                <Select
                                    value={values.providerType || undefined}
                                    onValueChange={handleProviderChange}
                                    disabled={isSubmitting || noProviders || hasProvidersError}
                                >
                                    <SelectTrigger
                                        id="notif-rule-provider"
                                        error={!!errors.providerType}
                                        aria-label="Provider"
                                        data-field="providerType"
                                        placeholder={
                                            isProvidersLoading
                                                ? 'Loading...'
                                                : hasProvidersError
                                                  ? 'Provider catalog unavailable'
                                                  : noProviders
                                                    ? 'No providers available'
                                                    : 'Select a provider'
                                        }
                                    />

                                    <SelectContent>
                                        {providers.map((p) => (
                                            <SelectItem key={p} value={p}>
                                                {providerLabels[p]}
                                            </SelectItem>
                                        ))}
                                    </SelectContent>
                                </Select>
                            )}
                            {hasProvidersError && (
                                <div className="notification-rule-form__query-error" role="alert">
                                    <span>Failed to load notification providers.</span>
                                    {onRetryProviders && (
                                        <Button
                                            type="button"
                                            variant="secondary"
                                            size="sm"
                                            onClick={onRetryProviders}
                                            disabled={isProvidersLoading}
                                            aria-label="Retry notification providers"
                                        >
                                            Retry
                                        </Button>
                                    )}
                                </div>
                            )}
                            {errors.providerType && (
                                <p className="notification-rule-form__help" role="alert">
                                    {errors.providerType}
                                </p>
                            )}
                        </div>

                        <div className="notification-rule-form__field">
                            <div className="notification-rule-form__label-row">
                                <Label htmlFor="notif-rule-event-pattern" error={!!errors.eventNamePattern}>
                                    Event Name Pattern
                                </Label>
                                <Tooltip
                                    content={
                                        <p>
                                            Match event names with a regular expression. For example,{' '}
                                            <code>content\.added</code> matches the event name content.added. Leave
                                            blank to match every event.
                                        </p>
                                    }
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="notification-rule-form__info-button"
                                        aria-label="Event pattern help"
                                    >
                                        <InfoIcon weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <Input
                                id="notif-rule-event-pattern"
                                value={values.eventNamePattern}
                                onChange={handleEventNameChange}
                                placeholder=".*"
                                error={!!errors.eventNamePattern}
                                errorMessage={errors.eventNamePattern || undefined}
                                disabled={isSubmitting}
                                data-field="eventNamePattern"
                            />
                        </div>

                        <div className="notification-rule-form__field">
                            <div className="notification-rule-form__label-row">
                                <Label>Context Filters</Label>
                                <Tooltip
                                    content={<p>All filters must match the event context for the rule to fire.</p>}
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="notification-rule-form__info-button"
                                        aria-label="Context filters help"
                                    >
                                        <InfoIcon weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <div className="notification-rule-form__filters">
                                {values.contextFilters.length === 0 && (
                                    <p className="notification-rule-form__filters-empty">
                                        No filters; rule fires for any matching event name.
                                    </p>
                                )}
                                {values.contextFilters.map((filter, index) => (
                                    <div
                                        key={filterKeys[index]}
                                        className="notification-rule-form__filter"
                                        data-filter-index={index}
                                    >
                                        <Input
                                            value={filter.key}
                                            onChange={(e) => handleFilterKeyChange(index, e.target.value)}
                                            placeholder="Key"
                                            error={!!errors[`filter-${index}-key`]}
                                            errorMessage={errors[`filter-${index}-key`] || undefined}
                                            disabled={isSubmitting}
                                            aria-label={`Filter ${index + 1} key`}
                                            data-field={`filter-${index}-key`}
                                            className="notification-rule-form__filter-input"
                                        />
                                        <Input
                                            value={filter.pattern ?? ''}
                                            onChange={(e) => handleFilterPatternChange(index, e.target.value)}
                                            placeholder="Pattern (regex)"
                                            error={!!errors[`filter-${index}-pattern`]}
                                            errorMessage={errors[`filter-${index}-pattern`] || undefined}
                                            disabled={isSubmitting}
                                            aria-label={`Filter ${index + 1} pattern`}
                                            data-field={`filter-${index}-pattern`}
                                            className="notification-rule-form__filter-input"
                                        />
                                        <div className="notification-rule-form__filter-negate">
                                            <Switch
                                                checked={filter.negate}
                                                onCheckedChange={(negate) => handleFilterNegateChange(index, negate)}
                                                disabled={isSubmitting}
                                                aria-label={`Filter ${index + 1} negate`}
                                                data-field={`filter-${index}-negate`}
                                            />
                                            <span className="notification-rule-form__filter-negate-label">Negate</span>
                                        </div>
                                        <Button
                                            type="button"
                                            variant="ghost"
                                            size="sm"
                                            onClick={() => handleRemoveFilter(index)}
                                            disabled={isSubmitting}
                                            aria-label={`Remove filter ${index + 1}`}
                                            data-action={`remove-filter-${index}`}
                                        >
                                            <TrashIcon weight="bold" aria-hidden="true" />
                                        </Button>
                                    </div>
                                ))}
                                <Button
                                    type="button"
                                    variant="secondary"
                                    size="sm"
                                    onClick={handleAddFilter}
                                    disabled={isSubmitting}
                                    data-action="add-filter"
                                >
                                    <PlusIcon weight="bold" aria-hidden="true" />
                                    Add Filter
                                </Button>
                            </div>
                        </div>

                        {schema.length > 0 && (
                            <div className="notification-rule-form__settings" data-field="settings">
                                <h3 className="notification-rule-form__settings-title">Provider Settings</h3>
                                <IntegrationSettingsForm
                                    schema={schema}
                                    values={values.settings}
                                    onChange={handleSettingChange}
                                    secretsConfigured={secretsConfigured}
                                    issues={serverIssues}
                                    disabled={isSubmitting}
                                    idPrefix="notif-rule-setting"
                                />
                            </div>
                        )}

                        {hasSchemaError && (
                            <div className="notification-rule-form__query-error" role="alert">
                                <span>Failed to load settings for {values.providerType}.</span>
                                <Button
                                    type="button"
                                    variant="secondary"
                                    size="sm"
                                    onClick={() => void schemaQuery.refetch()}
                                    disabled={schemaQuery.isFetching}
                                    aria-label="Retry provider settings"
                                >
                                    Retry
                                </Button>
                            </div>
                        )}

                        <div className="notification-rule-form__field notification-rule-form__field--toggle">
                            <div className="notification-rule-form__toggle-wrapper">
                                <Switch
                                    id="notif-rule-enabled"
                                    checked={values.enabled}
                                    onCheckedChange={handleEnabledChange}
                                    disabled={isSubmitting}
                                    data-field="enabled"
                                />
                                <Label htmlFor="notif-rule-enabled">Enabled</Label>
                            </div>
                            <p className="notification-rule-form__help">Disabled rules do not send notifications.</p>
                        </div>
                    </div>

                    <SheetFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            disabled={isSubmitting}
                            onClick={handleCancel}
                            data-action="cancel"
                        >
                            <XIcon weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                        <Button
                            type="submit"
                            variant="primary"
                            loading={isSubmitting}
                            disabled={cannotSubmit}
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
                onConfirm={() => void save()}
            />
        </Sheet>
    );
}

NotificationRuleForm.displayName = 'NotificationRuleForm';

export default NotificationRuleForm;
