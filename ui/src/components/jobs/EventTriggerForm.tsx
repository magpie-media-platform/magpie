import { useState, useCallback, useMemo, useEffect, useRef, type ReactNode } from 'react';
import { FloppyDiskIcon, XIcon, InfoIcon, PlusIcon, TrashIcon, WarningIcon } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { stripBlankRows, useFormDirty } from '@/hooks/useFormDirty';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';
import type { EditableEventContextCriterion } from '@/lib/eventContextCriteria';

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

import { eventPatternExample, eventPatternPresets } from './eventPatternPresets';

import './EventTriggerForm.css';
import './TriggerForm.css';

const EVENT_TRIGGER_MODE = 'Event';

export interface JobTemplateOption {
    id: string;
    name: string;
    onDemand?: boolean;
    enabled?: boolean;
    supportedTriggerModes?: string[];
}

export type EventTriggerFilterItem = EditableEventContextCriterion;

export interface EventTriggerFormValues {
    id?: string;
    name: string;
    templateId: string;
    eventNamePatterns: string[];
    filters: EventTriggerFilterItem[];
    enabled: boolean;
    delaySeconds: number;
}

interface EventTriggerFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: EventTriggerFormValues | null;

    templates: JobTemplateOption[];

    currentTemplateId?: string | null;

    onSubmit: (values: EventTriggerFormValues) => void | Promise<void>;

    isSubmitting?: boolean;
}

const emptyValues: EventTriggerFormValues = {
    name: '',
    templateId: '',
    eventNamePatterns: [''],
    filters: [],
    enabled: true,
    delaySeconds: 0,
};

const CUSTOM_PRESET = 'custom';

function isBlankEventFilter(filter: EventTriggerFilterItem): boolean {
    return filter.key.trim() === '' && (filter.pattern === null || filter.pattern.trim() === '');
}

function normalizeTrigger(values: EventTriggerFormValues): unknown {
    return {
        name: values.name,
        templateId: values.templateId,
        eventNamePatterns: values.eventNamePatterns
            .map((pattern) => pattern.trim())
            .filter((pattern) => pattern !== ''),
        enabled: values.enabled,
        delaySeconds: values.delaySeconds,
        filters: stripBlankRows(values.filters, isBlankEventFilter),
    };
}

function isTemplateUnavailableForEventTrigger(template: JobTemplateOption | undefined): boolean {
    return (
        template?.enabled === false ||
        (template?.supportedTriggerModes !== undefined && !template.supportedTriggerModes.includes(EVENT_TRIGGER_MODE))
    );
}

export function EventTriggerForm({
    open,
    onOpenChange,
    initialValues,
    templates,
    currentTemplateId = null,
    onSubmit,
    isSubmitting = false,
}: EventTriggerFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<EventTriggerFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});
    const filterKeyCounter = useRef(0);
    const [filterKeys, setFilterKeys] = useState<string[]>(() =>
        (initialValues?.filters ?? []).map(() => `filter-${filterKeyCounter.current++}`),
    );
    const [selectedPreset, setSelectedPreset] = useState<string>(() => {
        const primaryPattern = initialValues?.eventNamePatterns?.[0];
        if (!primaryPattern) {
            return CUSTOM_PRESET;
        }
        const preset = eventPatternPresets.find((p) => p.value === primaryPattern);
        return preset ? preset.value : CUSTOM_PRESET;
    });

    const cleanValuesRef = useRef<EventTriggerFormValues>(initialValues ?? emptyValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const loadedEntityIdRef = useRef<string | null | undefined>(undefined);
    const isDirty = useFormDirty(values, cleanValuesRef.current, normalizeTrigger);
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
        const newValues = initialValues ?? emptyValues;
        setValues(newValues);
        cleanValuesRef.current = newValues;
        loadedEntityIdRef.current = nextEntityId;
        setErrors({});
        setShowUnsavedDialog(false);
        setShowStaleSaveConfirmation(false);
        setFilterKeys(newValues.filters.map(() => `filter-${filterKeyCounter.current++}`));
        const primaryPattern = newValues.eventNamePatterns[0];
        const preset = eventPatternPresets.find((p) => p.value === primaryPattern);
        setSelectedPreset(preset ? preset.value : CUSTOM_PRESET);
    }, [open, initialValues]);

    const conflict = useExternalChangeConflict({
        dirty: isDirty,
        entityId: initialValues?.id,
        eventNames: ['eventTrigger.updated', 'eventTrigger.deleted'],
        deletedEventNames: ['eventTrigger.deleted'],
        contextKeys: ['eventTrigger.id'],
        enabled: open && isEditing,
    });

    useUnsavedChangesGuard(open && isDirty);

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

    const handleCancel = useCallback(() => {
        handleOpenChange(false);
    }, [handleOpenChange]);

    const handleNameChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setValues((prev) => ({ ...prev, name: e.target.value }));
        setErrors((prev) => ({ ...prev, name: '' }));
    }, []);

    const handleTemplateChange = useCallback((templateId: string) => {
        setValues((prev) => ({ ...prev, templateId }));
        setErrors((prev) => ({ ...prev, templateId: '' }));
    }, []);

    const handlePresetChange = useCallback((preset: string) => {
        setSelectedPreset(preset);
        if (preset !== CUSTOM_PRESET) {
            setValues((prev) => ({
                ...prev,
                eventNamePatterns: prev.eventNamePatterns.map((pattern, i) => (i === 0 ? preset : pattern)),
            }));
            setErrors((prev) => ({ ...prev, eventNamePattern: '' }));
        }
    }, []);

    const handlePatternChange = useCallback((index: number, newValue: string) => {
        setValues((prev) => ({
            ...prev,
            eventNamePatterns: prev.eventNamePatterns.map((pattern, i) => (i === index ? newValue : pattern)),
        }));
        setErrors((prev) => ({ ...prev, [index === 0 ? 'eventNamePattern' : `eventNamePattern-${index}`]: '' }));
        if (index === 0) {
            const preset = eventPatternPresets.find((p) => p.value === newValue);
            setSelectedPreset(preset ? preset.value : CUSTOM_PRESET);
        }
    }, []);

    const handleAddPattern = useCallback(() => {
        setValues((prev) => ({ ...prev, eventNamePatterns: [...prev.eventNamePatterns, ''] }));
    }, []);

    const handleRemovePattern = useCallback((index: number) => {
        setValues((prev) => {
            if (prev.eventNamePatterns.length <= 1) {
                return prev;
            }
            return { ...prev, eventNamePatterns: prev.eventNamePatterns.filter((_, i) => i !== index) };
        });
    }, []);

    const handleDelaySecondsChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const parsed = Number(e.target.value);
        setValues((prev) => ({ ...prev, delaySeconds: Number.isFinite(parsed) ? Math.max(0, parsed) : 0 }));
        setErrors((prev) => ({ ...prev, delaySeconds: '' }));
    }, []);

    const handleEnabledChange = useCallback((enabled: boolean) => {
        setValues((prev) => ({ ...prev, enabled }));
    }, []);

    const handleAddFilter = useCallback(() => {
        setValues((prev) => ({
            ...prev,
            filters: [...prev.filters, { key: '', pattern: null, negate: false }],
        }));
        setFilterKeys((prev) => [...prev, `filter-${filterKeyCounter.current++}`]);
    }, []);

    const handleRemoveFilter = useCallback((index: number) => {
        setValues((prev) => ({
            ...prev,
            filters: prev.filters.filter((_, i) => i !== index),
        }));
        setFilterKeys((prev) => prev.filter((_, i) => i !== index));
    }, []);

    const handleFilterKeyChange = useCallback((index: number, key: string) => {
        setValues((prev) => ({
            ...prev,
            filters: prev.filters.map((f, i) => (i === index ? { ...f, key } : f)),
        }));
    }, []);

    const handleFilterPatternChange = useCallback((index: number, pattern: string) => {
        setValues((prev) => ({
            ...prev,
            filters: prev.filters.map((f, i) => (i === index ? { ...f, pattern: pattern || null } : f)),
        }));
    }, []);

    const handleFilterNegateChange = useCallback((index: number, negate: boolean) => {
        setValues((prev) => ({
            ...prev,
            filters: prev.filters.map((f, i) => (i === index ? { ...f, negate } : f)),
        }));
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
            if (!isCurrentTemplate && isTemplateUnavailableForEventTrigger(template)) {
                newErrors.templateId = 'Job template is unavailable';
            }
        }

        const nonBlankPatterns = values.eventNamePatterns.filter((pattern) => pattern.trim() !== '');
        if (nonBlankPatterns.length === 0) {
            newErrors.eventNamePattern = 'Event pattern is required';
        } else {
            values.eventNamePatterns.forEach((pattern, index) => {
                if (!pattern.trim()) {
                    return;
                }
                try {
                    new RegExp(pattern);
                } catch {
                    newErrors[index === 0 ? 'eventNamePattern' : `eventNamePattern-${index}`] = 'Invalid regex pattern';
                }
            });
        }

        if (!Number.isFinite(values.delaySeconds) || values.delaySeconds < 0) {
            newErrors.delaySeconds = 'Delay must be 0 or greater';
        }

        values.filters.forEach((filter, index) => {
            if (!filter.key.trim()) {
                newErrors[`filter-${index}-key`] = 'Key is required';
            }
            if (filter.pattern) {
                try {
                    new RegExp(filter.pattern);
                } catch {
                    newErrors[`filter-${index}-pattern`] = 'Invalid regex';
                }
            }
        });

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    }, [currentTemplateId, templates, values]);

    const save = useCallback(async () => {
        conflict.beginOwnSave();
        try {
            await onSubmit(values);
            conflict.endOwnSave(true);
        } catch {
            conflict.endOwnSave(false);
        }
    }, [conflict, onSubmit, values]);

    const handleSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            if (conflict.state === 'deleted' || !validate()) {
                return;
            }
            if (conflict.state === 'stale') {
                setShowStaleSaveConfirmation(true);
                return;
            }
            void save();
        },
        [conflict.state, save, validate],
    );

    const selectedTemplate = useMemo(
        () => templates.find((t) => t.id === values.templateId),
        [templates, values.templateId],
    );

    const selectedPresetLabel = useMemo(() => {
        if (selectedPreset === CUSTOM_PRESET) {
            return 'Custom';
        }
        const preset = eventPatternPresets.find((p) => p.value === selectedPreset);
        return preset?.label ?? 'Custom';
    }, [selectedPreset]);

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="event-trigger-form__sheet" data-component="event-trigger-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Event Trigger' : 'Add Event Trigger'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the event trigger configuration.'
                            : 'Configure a new event trigger to run a job when events occur.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="event-trigger-form">
                    <div className="event-trigger-form__body">
                        {conflict.state && (
                            <ExternalChangeConflictWarning
                                state={conflict.state}
                                entityLabel="Event trigger"
                                id="event-trigger-external-change"
                            />
                        )}
                        <Alert
                            variant="warning"
                            icon={<WarningIcon weight="fill" aria-hidden="true" />}
                            className="event-trigger-form__caution"
                            data-testid="event-trigger-form__caution"
                        >
                            Event triggers are an advanced feature. Use with caution: a broad event filter can fire jobs
                            far more often than expected.
                        </Alert>

                        <div className="event-trigger-form__field">
                            <Label htmlFor="event-trigger-name" error={!!errors.name} required>
                                Name
                            </Label>
                            <Input
                                id="event-trigger-name"
                                value={values.name}
                                onChange={handleNameChange}
                                placeholder="My Event Trigger"
                                error={!!errors.name}
                                errorMessage={errors.name || undefined}
                                disabled={isSubmitting}
                                data-field="name"
                            />
                        </div>

                        <div className="event-trigger-form__field">
                            <Label htmlFor="event-trigger-template" error={!!errors.templateId} required>
                                Job Template
                            </Label>
                            <Select
                                value={values.templateId}
                                onValueChange={handleTemplateChange}
                                disabled={isSubmitting || isEditing}
                            >
                                <SelectTrigger
                                    id="event-trigger-template"
                                    placeholder="Select a job template..."
                                    error={!!errors.templateId}
                                    aria-describedby={
                                        errors.templateId
                                            ? 'event-trigger-template-error'
                                            : isEditing
                                              ? 'event-trigger-template-help'
                                              : undefined
                                    }
                                    data-field="templateId"
                                >
                                    {selectedTemplate?.name ?? 'Select a job template...'}
                                </SelectTrigger>
                                <SelectContent>
                                    {templates.map((template) => (
                                        <SelectItem
                                            key={template.id}
                                            value={template.id}
                                            disabled={isTemplateUnavailableForEventTrigger(template)}
                                        >
                                            {template.name}
                                            {isTemplateUnavailableForEventTrigger(template) ? ' (unavailable)' : ''}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                            {errors.templateId && (
                                <p id="event-trigger-template-error" className="form-error" role="alert">
                                    {errors.templateId}
                                </p>
                            )}
                            {isEditing && !errors.templateId && (
                                <p id="event-trigger-template-help" className="event-trigger-form__help">
                                    Job template cannot be changed after the event trigger is created.
                                </p>
                            )}
                        </div>

                        <div className="event-trigger-form__field">
                            <div className="event-trigger-form__label-row">
                                <Label htmlFor="event-trigger-preset" error={!!errors.eventNamePattern}>
                                    Event Pattern
                                </Label>
                                <Tooltip
                                    content={
                                        <p className="event-trigger-form__tooltip">
                                            Regex pattern to match against event names.
                                            <br />
                                            Example: {eventPatternExample.pattern} (matches{' '}
                                            {eventPatternExample.eventName})
                                        </p>
                                    }
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="event-trigger-form__info-button"
                                        aria-label="Event pattern help"
                                    >
                                        <InfoIcon weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <Select value={selectedPreset} onValueChange={handlePresetChange} disabled={isSubmitting}>
                                <SelectTrigger
                                    id="event-trigger-preset"
                                    placeholder="Select an event pattern..."
                                    data-field="preset"
                                >
                                    {selectedPresetLabel}
                                </SelectTrigger>
                                <SelectContent>
                                    {eventPatternPresets.map((preset) => (
                                        <SelectItem key={preset.label} value={preset.value}>
                                            {preset.label}
                                        </SelectItem>
                                    ))}
                                    <SelectItem value={CUSTOM_PRESET}>Custom</SelectItem>
                                </SelectContent>
                            </Select>
                        </div>

                        <div className="event-trigger-form__field">
                            <Label htmlFor="event-trigger-pattern" error={!!errors.eventNamePattern} required>
                                Pattern (Regex)
                            </Label>
                            <div className="event-trigger-form__patterns">
                                {values.eventNamePatterns.map((pattern, index) => {
                                    const fieldName = index === 0 ? 'eventNamePattern' : `eventNamePattern-${index}`;
                                    return (
                                        <div
                                            key={index}
                                            className="event-trigger-form__pattern-row"
                                            data-pattern-index={index}
                                        >
                                            <Input
                                                id={
                                                    index === 0
                                                        ? 'event-trigger-pattern'
                                                        : `event-trigger-pattern-${index}`
                                                }
                                                value={pattern}
                                                onChange={(e) => handlePatternChange(index, e.target.value)}
                                                placeholder={eventPatternExample.pattern}
                                                error={!!errors[fieldName]}
                                                errorMessage={errors[fieldName] || undefined}
                                                disabled={isSubmitting}
                                                data-field={fieldName}
                                                className="event-trigger-form__pattern-input"
                                                aria-label={index === 0 ? 'Pattern (Regex)' : `Event name ${index + 1}`}
                                            />
                                            {values.eventNamePatterns.length > 1 && (
                                                <Button
                                                    type="button"
                                                    variant="ghost"
                                                    size="sm"
                                                    onClick={() => handleRemovePattern(index)}
                                                    disabled={isSubmitting}
                                                    aria-label={`Remove event name ${index + 1}`}
                                                    data-action={`remove-pattern-${index}`}
                                                    className="event-trigger-form__pattern-remove"
                                                >
                                                    <TrashIcon weight="bold" aria-hidden="true" />
                                                </Button>
                                            )}
                                        </div>
                                    );
                                })}
                                <Button
                                    type="button"
                                    variant="secondary"
                                    size="sm"
                                    onClick={handleAddPattern}
                                    disabled={isSubmitting}
                                    data-action="add-pattern"
                                    className="event-trigger-form__add-pattern"
                                >
                                    <PlusIcon weight="bold" aria-hidden="true" />
                                    Add Event Name
                                </Button>
                            </div>
                            <p className="event-trigger-form__help">
                                Regex pattern to match event names. Add more to match any of several event names.
                            </p>
                        </div>

                        <div className="event-trigger-form__field">
                            <Label htmlFor="event-trigger-delay-seconds">Delay (seconds)</Label>
                            <Input
                                id="event-trigger-delay-seconds"
                                type="number"
                                inputMode="numeric"
                                min={0}
                                step={1}
                                value={values.delaySeconds}
                                onChange={handleDelaySecondsChange}
                                error={!!errors.delaySeconds}
                                errorMessage={errors.delaySeconds || undefined}
                                disabled={isSubmitting}
                                data-field="delaySeconds"
                            />
                            <p className="event-trigger-form__help">
                                Wait this many seconds after the event fires before running the job.
                            </p>
                        </div>

                        <div className="event-trigger-form__field">
                            <div className="event-trigger-form__label-row">
                                <Label>Filters</Label>
                                <Tooltip
                                    content={
                                        <p className="event-trigger-form__tooltip">
                                            Filter conditions to match against event context.
                                            <br />
                                            All conditions must match for the trigger to fire.
                                        </p>
                                    }
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="event-trigger-form__info-button"
                                        aria-label="Filters help"
                                    >
                                        <InfoIcon weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <div className="event-trigger-form__filters">
                                {values.filters.length === 0 && (
                                    <p className="event-trigger-form__filters-empty">
                                        No filters configured. Trigger will fire for all matching events.
                                    </p>
                                )}
                                {values.filters.map((filter, index) => (
                                    <div
                                        key={filterKeys[index]}
                                        className="event-trigger-form__filter"
                                        data-filter-index={index}
                                    >
                                        <div className="event-trigger-form__filter-row">
                                            <div className="event-trigger-form__filter-key">
                                                <Input
                                                    id={`filter-${index}-key`}
                                                    value={filter.key}
                                                    onChange={(e) => handleFilterKeyChange(index, e.target.value)}
                                                    placeholder="Key"
                                                    error={!!errors[`filter-${index}-key`]}
                                                    errorMessage={errors[`filter-${index}-key`] || undefined}
                                                    disabled={isSubmitting}
                                                    aria-label={`Filter ${index + 1} key`}
                                                    data-field={`filter-${index}-key`}
                                                />
                                            </div>
                                            <div className="event-trigger-form__filter-pattern">
                                                <Input
                                                    id={`filter-${index}-pattern`}
                                                    value={filter.pattern ?? ''}
                                                    onChange={(e) => handleFilterPatternChange(index, e.target.value)}
                                                    placeholder="Pattern (regex)"
                                                    error={!!errors[`filter-${index}-pattern`]}
                                                    errorMessage={errors[`filter-${index}-pattern`] || undefined}
                                                    disabled={isSubmitting}
                                                    aria-label={`Filter ${index + 1} pattern`}
                                                    data-field={`filter-${index}-pattern`}
                                                    className="event-trigger-form__pattern-input"
                                                />
                                            </div>
                                            <div className="event-trigger-form__filter-negate">
                                                <Switch
                                                    checked={filter.negate}
                                                    onCheckedChange={(negate) =>
                                                        handleFilterNegateChange(index, negate)
                                                    }
                                                    disabled={isSubmitting}
                                                    aria-label={`Filter ${index + 1} negate`}
                                                    data-field={`filter-${index}-negate`}
                                                />
                                                <span className="event-trigger-form__filter-negate-label">Negate</span>
                                            </div>
                                            <Button
                                                type="button"
                                                variant="ghost"
                                                size="sm"
                                                onClick={() => handleRemoveFilter(index)}
                                                disabled={isSubmitting}
                                                aria-label={`Remove filter ${index + 1}`}
                                                data-action={`remove-filter-${index}`}
                                                className="event-trigger-form__filter-remove"
                                            >
                                                <TrashIcon weight="bold" aria-hidden="true" />
                                            </Button>
                                        </div>
                                    </div>
                                ))}
                                <Button
                                    type="button"
                                    variant="secondary"
                                    size="sm"
                                    onClick={handleAddFilter}
                                    disabled={isSubmitting}
                                    data-action="add-filter"
                                    className="event-trigger-form__add-filter"
                                >
                                    <PlusIcon weight="bold" aria-hidden="true" />
                                    Add Filter
                                </Button>
                            </div>
                        </div>

                        <div className="event-trigger-form__field event-trigger-form__field--toggle">
                            <div className="event-trigger-form__toggle-wrapper">
                                <Switch
                                    id="event-trigger-enabled"
                                    checked={values.enabled}
                                    onCheckedChange={handleEnabledChange}
                                    disabled={isSubmitting}
                                    data-field="enabled"
                                />
                                <Label htmlFor="event-trigger-enabled">Enabled</Label>
                            </div>
                            <p className="event-trigger-form__help">
                                Disabled triggers will not fire when events occur.
                            </p>
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
                            disabled={conflict.state === 'deleted'}
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

EventTriggerForm.displayName = 'EventTriggerForm';

export default EventTriggerForm;
