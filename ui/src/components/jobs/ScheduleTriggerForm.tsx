import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode, type ChangeEvent } from 'react';
import { FloppyDiskIcon, XIcon, InfoIcon, LockSimpleIcon, WarningIcon } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { useFormDirty } from '@/hooks/useFormDirty';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import type { JobParameterDefinition } from '@/hooks/useJobs';
import { useInstanceName } from '@/hooks/useInstanceName';

import { cronPresets, describeCron, describeCronUtc } from './cronDescription';
import { JobParameterFieldset } from './JobParameterFieldset';
import {
    areJobParameterValuesEqual,
    jobParameterErrorKey,
    normalizeJobParameterValues,
    seedJobParameters,
    validateJobParameters,
} from './jobParameters';

import './ScheduleTriggerForm.css';
import './TriggerForm.css';

export interface JobTemplateOption {
    id: string;
    name: string;
    onDemand?: boolean;
    enabled?: boolean;
    defaultSchedule?: string | null;
    parameters?: JobParameterDefinition[];
}

export interface ScheduleTriggerFormValues {
    id?: string;
    name: string;
    templateId: string;
    cronExpression: string;
    enabled: boolean;
    parameters: Record<string, string>;
}

interface ScheduleTriggerFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: ScheduleTriggerFormValues | null;

    templates: JobTemplateOption[];

    currentTemplateId?: string | null;

    readOnly?: boolean;

    onSubmit: (values: ScheduleTriggerFormValues) => void | Promise<void>;

    isSubmitting?: boolean;
}

const emptyValues: ScheduleTriggerFormValues = {
    name: '',
    templateId: '',
    cronExpression: '',
    enabled: true,
    parameters: {},
};

const CUSTOM_PRESET = 'custom';

function validateCronExpression(expression: string): string {
    if (!expression.trim()) {
        return 'Cron expression is required';
    }

    const parts = expression.trim().split(/\s+/);
    if (parts.length !== 5) {
        return 'Cron expression must have 5 parts (minute hour day month weekday)';
    }

    return '';
}

function normalizeInitialValues(values: ScheduleTriggerFormValues | null | undefined): ScheduleTriggerFormValues {
    if (!values) {
        return emptyValues;
    }
    if (values.parameters) {
        return values;
    }
    return { ...values, parameters: {} };
}

function normalizeSchedule(values: ScheduleTriggerFormValues): unknown {
    return {
        name: values.name,
        templateId: values.templateId,
        cronExpression: values.cronExpression,
        enabled: values.enabled,
        parameters: values.parameters,
    };
}

function lowercaseFirstCharacter(value: string): string {
    return value.length === 0 ? value : value.charAt(0).toLowerCase() + value.slice(1);
}

export function ScheduleTriggerForm({
    open,
    onOpenChange,
    initialValues,
    templates,
    currentTemplateId = null,
    readOnly = false,
    onSubmit,
    isSubmitting = false,
}: ScheduleTriggerFormProps): ReactNode {
    const instanceName = useInstanceName();
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<ScheduleTriggerFormValues>(() => normalizeInitialValues(initialValues));
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [selectedPreset, setSelectedPreset] = useState<string>(() => {
        if (!initialValues?.cronExpression) {
            return CUSTOM_PRESET;
        }
        const preset = cronPresets.find((p) => p.value === initialValues.cronExpression);
        return preset ? preset.value : CUSTOM_PRESET;
    });

    const cleanValuesRef = useRef<ScheduleTriggerFormValues>(normalizeInitialValues(initialValues));
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const loadedEntityIdRef = useRef<string | null | undefined>(undefined);
    const isDirty = useFormDirty(values, cleanValuesRef.current, normalizeSchedule);
    const isDirtyRef = useRef(isDirty);
    isDirtyRef.current = isDirty;

    useEffect(() => {
        if (!open) {
            loadedEntityIdRef.current = undefined;
            return;
        }
        const nextEntityId = initialValues?.id ?? null;
        if (loadedEntityIdRef.current === nextEntityId && isDirtyRef.current) {
            return;
        }
        const newValues = normalizeInitialValues(initialValues);
        setValues(newValues);
        cleanValuesRef.current = newValues;
        loadedEntityIdRef.current = nextEntityId;
        setErrors({});
        setShowUnsavedDialog(false);
        setShowStaleSaveConfirmation(false);

        const preset = cronPresets.find((p) => p.value === newValues.cronExpression);
        setSelectedPreset(preset ? preset.value : CUSTOM_PRESET);
    }, [open, initialValues]);

    const selectedTemplate = useMemo(
        () => templates.find((t) => t.id === values.templateId),
        [templates, values.templateId],
    );

    useEffect(() => {
        if (!open) {
            return;
        }
        const seeded = seedJobParameters(selectedTemplate?.parameters, values.parameters);
        if (!areJobParameterValuesEqual(seeded, values.parameters)) {
            cleanValuesRef.current = {
                ...cleanValuesRef.current,
                parameters: seedJobParameters(selectedTemplate?.parameters, cleanValuesRef.current.parameters),
            };
            setValues((prev) => ({ ...prev, parameters: seeded }));
        }
    }, [open, selectedTemplate, values.parameters]);

    const conflict = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['scheduleTrigger.updated', 'scheduleTrigger.deleted'],
        deletedEventNames: ['scheduleTrigger.deleted'],
        contextKeys: ['scheduleTrigger.id'],
        enabled: open && isEditing,
    });

    useUnsavedChangesGuard(open && isDirty && !readOnly);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen && isDirty && !readOnly) {
                setShowUnsavedDialog(true);
                return;
            }
            onOpenChange(nextOpen);
        },
        [isDirty, onOpenChange, readOnly],
    );

    const handleDiscard = useCallback(() => {
        setShowUnsavedDialog(false);
        onOpenChange(false);
    }, [onOpenChange]);

    const handleKeepEditing = useCallback(() => {
        setShowUnsavedDialog(false);
    }, []);

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const handleNameChange = useCallback((e: ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, name: e.target.value }));
        setErrors((prev) => ({ ...prev, name: '' }));
    }, []);

    const handleTemplateChange = useCallback(
        (templateId: string) => {
            const template = templates.find((t) => t.id === templateId);
            const defaultSchedule = template?.defaultSchedule || '';
            const seededParameters = seedJobParameters(template?.parameters);
            setValues((prev) => ({
                ...prev,
                templateId,
                cronExpression: prev.cronExpression || defaultSchedule,
                parameters: seededParameters,
            }));
            if (defaultSchedule) {
                const preset = cronPresets.find((p) => p.value === defaultSchedule);
                setSelectedPreset(preset ? preset.value : CUSTOM_PRESET);
            }
            setErrors((prev) => ({ ...prev, templateId: '' }));
        },
        [templates],
    );

    const handlePresetChange = useCallback((preset: string) => {
        setSelectedPreset(preset);
        if (preset !== CUSTOM_PRESET) {
            setValues((prev) => ({ ...prev, cronExpression: preset }));
            setErrors((prev) => ({ ...prev, cronExpression: '' }));
        }
    }, []);

    const handleCronChange = useCallback((e: ChangeEvent<HTMLInputElement>) => {
        const newValue = e.target.value;
        setValues((prev) => ({ ...prev, cronExpression: newValue }));
        setErrors((prev) => ({ ...prev, cronExpression: '' }));

        const preset = cronPresets.find((p) => p.value === newValue);
        setSelectedPreset(preset ? preset.value : CUSTOM_PRESET);
    }, []);

    const handleEnabledChange = useCallback((enabled: boolean) => {
        setValues((prev) => ({ ...prev, enabled }));
    }, []);

    const handleParameterChange = useCallback((name: string, value: string) => {
        setValues((prev) => ({
            ...prev,
            parameters: { ...prev.parameters, [name]: value },
        }));
        setErrors((prev) => {
            const errorKey = jobParameterErrorKey(name);
            if (!prev[errorKey]) {
                return prev;
            }
            const next = { ...prev };
            delete next[errorKey];
            return next;
        });
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        if (!values.templateId) {
            newErrors.templateId = 'Job template is required';
        } else {
            const template = templates.find((t) => t.id === values.templateId);
            const isCurrentTemplate = currentTemplateId === values.templateId;
            if (!isCurrentTemplate && (template?.enabled === false || template?.onDemand === false)) {
                newErrors.templateId = 'Job template is unavailable';
            }
        }

        const cronError = validateCronExpression(values.cronExpression);
        if (cronError) {
            newErrors.cronExpression = cronError;
        }

        Object.assign(newErrors, validateJobParameters(selectedTemplate?.parameters, values.parameters));

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    }, [currentTemplateId, selectedTemplate, templates, values]);

    const save = useCallback(async () => {
        conflict.beginOwnSave();
        try {
            await onSubmit({ ...values, parameters: normalizeJobParameterValues(values.parameters) });
            conflict.endOwnSave(true);
        } catch {
            conflict.endOwnSave(false);
        }
    }, [conflict, onSubmit, values]);

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            if (readOnly || conflict.state === 'deleted' || !validate()) {
                return;
            }
            if (conflict.state === 'stale') {
                setShowStaleSaveConfirmation(true);
                return;
            }
            void save();
        },
        [conflict.state, readOnly, save, validate],
    );

    const selectedPresetLabel = useMemo(() => {
        if (selectedPreset === CUSTOM_PRESET) {
            return 'Custom';
        }
        const preset = cronPresets.find((p) => p.value === selectedPreset);
        return preset?.label ?? 'Custom';
    }, [selectedPreset]);

    const cronPreview = useMemo(() => {
        const expression = values.cronExpression.trim();
        if (!expression) {
            return '';
        }
        const described = describeCron(expression);
        return described === expression ? '' : describeCronUtc(expression);
    }, [values.cronExpression]);

    const parameterSpec = selectedTemplate?.parameters ?? [];
    const inputsDisabled = isSubmitting || readOnly;

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="schedule-trigger-form__sheet" data-component="schedule-trigger-form">
                <SheetHeader>
                    <SheetTitle>
                        {readOnly
                            ? 'View Schedule Trigger'
                            : isEditing
                              ? 'Edit Schedule Trigger'
                              : 'Add Schedule Trigger'}
                    </SheetTitle>
                    <SheetDescription>
                        {readOnly
                            ? `This trigger is managed by ${instanceName} and cannot be edited.`
                            : isEditing
                              ? 'Update the schedule trigger configuration.'
                              : 'Configure a new schedule trigger to run a job automatically.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="schedule-trigger-form">
                    <div className="schedule-trigger-form__body">
                        {conflict.state && (
                            <ExternalChangeConflictWarning
                                state={conflict.state}
                                entityLabel="Schedule trigger"
                                id="schedule-trigger-external-change"
                            />
                        )}
                        {!readOnly && (
                            <Alert
                                variant="warning"
                                icon={<WarningIcon weight="fill" aria-hidden="true" />}
                                className="schedule-trigger-form__caution"
                                data-testid="schedule-trigger-form__caution"
                            >
                                A misconfigured schedule can run jobs more often than intended. Review the schedule
                                before saving.
                            </Alert>
                        )}
                        {readOnly && (
                            <div
                                className="schedule-trigger-form__readonly-banner"
                                role="status"
                                data-field="readonly-banner"
                            >
                                <LockSimpleIcon weight="regular" aria-hidden="true" />
                                <span>
                                    This trigger is managed by {instanceName}. Its schedule, parameters, and enabled
                                    state cannot be changed.
                                </span>
                            </div>
                        )}
                        <div className="schedule-trigger-form__field">
                            <Label htmlFor="trigger-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="trigger-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Schedule"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={inputsDisabled}
                                data-field="name"
                            />
                        </div>

                        <div className="schedule-trigger-form__field">
                            <Label htmlFor="trigger-template" error={!!errors.templateId} required>
                                Job Template
                            </Label>
                            <Select
                                value={values.templateId}
                                onValueChange={handleTemplateChange}
                                disabled={inputsDisabled || isEditing}
                            >
                                <SelectTrigger
                                    id="trigger-template"
                                    placeholder="Select a job template..."
                                    error={!!errors.templateId}
                                    aria-describedby={errors.templateId ? 'trigger-template-error' : undefined}
                                    data-field="templateId"
                                >
                                    {selectedTemplate?.name ?? 'Select a job template...'}
                                </SelectTrigger>
                                <SelectContent>
                                    {templates.map((template) => (
                                        <SelectItem
                                            key={template.id}
                                            value={template.id}
                                            disabled={template.enabled === false || template.onDemand === false}
                                        >
                                            {template.name}
                                            {template.enabled === false || template.onDemand === false
                                                ? ' (unavailable)'
                                                : ''}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                            {errors.templateId && (
                                <p id="trigger-template-error" className="form-error" role="alert">
                                    {errors.templateId}
                                </p>
                            )}
                        </div>

                        <div className="schedule-trigger-form__field">
                            <div className="schedule-trigger-form__label-row">
                                <Label htmlFor="trigger-preset" error={!!errors.cronExpression}>
                                    Schedule
                                </Label>
                                <Tooltip
                                    content={
                                        <p className="schedule-trigger-form__tooltip">
                                            Cron format: minute hour day month weekday
                                            <br />
                                            Cron times are evaluated in UTC.
                                            <br />
                                            Example: 0 */6 * * * (every 6 hours)
                                        </p>
                                    }
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="schedule-trigger-form__info-button"
                                        aria-label="Cron expression help"
                                    >
                                        <InfoIcon weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <Select value={selectedPreset} onValueChange={handlePresetChange} disabled={inputsDisabled}>
                                <SelectTrigger
                                    id="trigger-preset"
                                    placeholder="Select a schedule..."
                                    data-field="preset"
                                >
                                    {selectedPresetLabel}
                                </SelectTrigger>
                                <SelectContent>
                                    {cronPresets.map((preset) => (
                                        <SelectItem key={preset.label} value={preset.value}>
                                            {preset.label}
                                        </SelectItem>
                                    ))}
                                    <SelectItem value={CUSTOM_PRESET}>Custom</SelectItem>
                                </SelectContent>
                            </Select>
                        </div>

                        <div className="schedule-trigger-form__field">
                            <Label htmlFor="trigger-cron" error={!!errors.cronExpression} required>
                                Cron Expression
                            </Label>
                            <Input
                                id="trigger-cron"
                                value={values.cronExpression}
                                onChange={handleCronChange}
                                placeholder="0 * * * *"
                                error={!!errors.cronExpression}
                                errorMessage={errors.cronExpression || undefined}
                                disabled={inputsDisabled}
                                data-field="cronExpression"
                                className="schedule-trigger-form__cron-input"
                                data-testid="schedule-trigger-form__cron-input"
                            />
                            {cronPreview ? (
                                <p
                                    className="schedule-trigger-form__cron-preview"
                                    data-field="cronPreview"
                                    aria-live="polite"
                                >
                                    Runs {lowercaseFirstCharacter(cronPreview)}.
                                </p>
                            ) : (
                                <p className="schedule-trigger-form__help">
                                    Format: minute hour day month weekday. Times are evaluated in UTC.
                                </p>
                            )}
                        </div>

                        <JobParameterFieldset
                            parameters={parameterSpec}
                            values={values.parameters}
                            errors={errors}
                            disabled={inputsDisabled}
                            idPrefix="trigger-parameter"
                            classNames={{
                                fieldset: 'schedule-trigger-form__parameters',
                                legend: 'schedule-trigger-form__legend',
                                field: 'schedule-trigger-form__field',
                                toggleField: 'schedule-trigger-form__field schedule-trigger-form__field--toggle',
                                toggleWrapper: 'schedule-trigger-form__toggle-wrapper',
                                help: 'schedule-trigger-form__help',
                            }}
                            dataField="parameters"
                            onChange={handleParameterChange}
                        />

                        <div className="schedule-trigger-form__field schedule-trigger-form__field--toggle">
                            <div className="schedule-trigger-form__toggle-wrapper">
                                <Switch
                                    id="trigger-enabled"
                                    checked={values.enabled}
                                    onCheckedChange={handleEnabledChange}
                                    disabled={inputsDisabled}
                                    data-field="enabled"
                                />
                                <Label htmlFor="trigger-enabled">Enabled</Label>
                            </div>
                            <p className="schedule-trigger-form__help">Disabled triggers will not run automatically.</p>
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
                            {readOnly ? 'Close' : 'Cancel'}
                        </Button>
                        {!readOnly && (
                            <Button
                                type="submit"
                                variant="primary"
                                loading={isSubmitting}
                                disabled={conflict.state === 'deleted'}
                                data-action="save"
                            >
                                <FloppyDiskIcon weight="bold" aria-hidden="true" />
                                Save
                            </Button>
                        )}
                    </SheetFooter>
                </form>
            </SheetContent>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
            <StaleSaveConfirmationDialog
                open={showStaleSaveConfirmation}
                title="Save changes based on older data?"
                description="This item changed after editing began. Saving will overwrite the newer changes."
                saving={isSubmitting}
                onCancel={() => setShowStaleSaveConfirmation(false)}
                onConfirm={() => {
                    setShowStaleSaveConfirmation(false);
                    void save();
                }}
            />
        </Sheet>
    );
}

ScheduleTriggerForm.displayName = 'ScheduleTriggerForm';

export default ScheduleTriggerForm;
