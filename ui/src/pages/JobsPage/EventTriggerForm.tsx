import { useState, useCallback, useMemo, useEffect, useRef, type ReactNode } from 'react';
import { FloppyDisk, X, Info, Plus, Trash } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';

import './EventTriggerForm.css';

export interface JobTemplateOption {
    id: string;
    name: string;
}

export interface EventTriggerFilterItem {
    key: string;
    pattern: string | null;
    negate: boolean;
}

export type ExecutionMode = 'queue' | 'immediate';

export interface EventTriggerFormValues {
    id?: string;
    name: string;
    templateId: string;
    eventNamePattern: string;
    filters: EventTriggerFilterItem[];
    executionMode: ExecutionMode;
    enabled: boolean;
}

export interface EventTriggerFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: EventTriggerFormValues | null;

    templates: JobTemplateOption[];

    onSubmit: (values: EventTriggerFormValues) => void;

    isSubmitting?: boolean;
}

const emptyValues: EventTriggerFormValues = {
    name: '',
    templateId: '',
    eventNamePattern: '',
    filters: [],
    executionMode: 'queue',
    enabled: true,
};

const eventPatternPresets = [
    { label: 'All events', value: '.*' },
    { label: 'Content added', value: 'content\\.added' },
    { label: 'Content updated', value: 'content\\.updated' },
    { label: 'Content removed', value: 'content\\.removed' },
    { label: 'Download completed', value: 'download\\.completed' },
    { label: 'Download failed', value: 'download\\.failed' },
    { label: 'File imported', value: 'file\\.imported' },
    { label: 'Metadata refreshed', value: 'metadata\\.refreshed' },
];

const CUSTOM_PRESET = 'custom';

function validateRegexPattern(pattern: string): string {
    if (!pattern.trim()) {
        return 'Event pattern is required';
    }

    try {
        new RegExp(pattern);
        return '';
    } catch {
        return 'Invalid regex pattern';
    }
}

export function EventTriggerForm({
    open,
    onOpenChange,
    initialValues,
    templates,
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
        if (!initialValues?.eventNamePattern) {
            return CUSTOM_PRESET;
        }
        const preset = eventPatternPresets.find((p) => p.value === initialValues.eventNamePattern);
        return preset ? preset.value : CUSTOM_PRESET;
    });

    const cleanValuesRef = useRef<EventTriggerFormValues>(initialValues ?? emptyValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);

    useEffect(() => {
        if (open) {
            const newValues = initialValues ?? emptyValues;
            setValues(newValues);
            cleanValuesRef.current = newValues;
            setErrors({});
            setShowUnsavedDialog(false);
            setFilterKeys(newValues.filters.map(() => `filter-${filterKeyCounter.current++}`));
            const preset = eventPatternPresets.find((p) => p.value === newValues.eventNamePattern);
            setSelectedPreset(preset ? preset.value : CUSTOM_PRESET);
        }
    }, [open, initialValues]);

    const isDirty = useMemo(
        () =>
            values.name !== cleanValuesRef.current.name ||
            values.templateId !== cleanValuesRef.current.templateId ||
            values.eventNamePattern !== cleanValuesRef.current.eventNamePattern ||
            values.executionMode !== cleanValuesRef.current.executionMode ||
            values.enabled !== cleanValuesRef.current.enabled ||
            JSON.stringify(values.filters) !== JSON.stringify(cleanValuesRef.current.filters),
        [values],
    );

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
            setValues((prev) => ({ ...prev, eventNamePattern: preset }));
            setErrors((prev) => ({ ...prev, eventNamePattern: '' }));
        }
    }, []);

    const handlePatternChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const newValue = e.target.value;
        setValues((prev) => ({ ...prev, eventNamePattern: newValue }));
        setErrors((prev) => ({ ...prev, eventNamePattern: '' }));
        const preset = eventPatternPresets.find((p) => p.value === newValue);
        setSelectedPreset(preset ? preset.value : CUSTOM_PRESET);
    }, []);

    const handleExecutionModeChange = useCallback((mode: string) => {
        setValues((prev) => ({ ...prev, executionMode: mode as ExecutionMode }));
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
        }

        const patternError = validateRegexPattern(values.eventNamePattern);
        if (patternError) {
            newErrors.eventNamePattern = patternError;
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
    }, [values]);

    const handleSubmit = useCallback(
        (e: React.FormEvent) => {
            e.preventDefault();
            if (validate()) {
                onSubmit(values);
            }
        },
        [values, validate, onSubmit],
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

    const executionModeLabel = useMemo(() => {
        return values.executionMode === 'immediate' ? 'Immediate' : 'Queue';
    }, [values.executionMode]);

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
                                disabled={isSubmitting}
                            >
                                <SelectTrigger
                                    id="event-trigger-template"
                                    placeholder="Select a job template..."
                                    error={!!errors.templateId}
                                    aria-describedby={errors.templateId ? 'event-trigger-template-error' : undefined}
                                    data-field="templateId"
                                >
                                    {selectedTemplate?.name ?? 'Select a job template...'}
                                </SelectTrigger>
                                <SelectContent>
                                    {templates.map((template) => (
                                        <SelectItem key={template.id} value={template.id}>
                                            {template.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                            {errors.templateId && (
                                <p id="event-trigger-template-error" className="form-error" role="alert">
                                    {errors.templateId}
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
                                            Example: content\.added (matches content.added)
                                        </p>
                                    }
                                    side="top"
                                >
                                    <button
                                        type="button"
                                        className="event-trigger-form__info-button"
                                        aria-label="Event pattern help"
                                    >
                                        <Info weight="fill" aria-hidden="true" />
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
                            <Input
                                id="event-trigger-pattern"
                                value={values.eventNamePattern}
                                onChange={handlePatternChange}
                                placeholder="content\.added"
                                error={!!errors.eventNamePattern}
                                errorMessage={errors.eventNamePattern || undefined}
                                disabled={isSubmitting}
                                data-field="eventNamePattern"
                                className="event-trigger-form__pattern-input"
                            />
                            <p className="event-trigger-form__help">Regex pattern to match event names</p>
                        </div>

                        <div className="event-trigger-form__field">
                            <Label htmlFor="event-trigger-mode">Execution Mode</Label>
                            <Select
                                value={values.executionMode}
                                onValueChange={handleExecutionModeChange}
                                disabled={isSubmitting}
                            >
                                <SelectTrigger id="event-trigger-mode" data-field="executionMode">
                                    {executionModeLabel}
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value="queue">Queue</SelectItem>
                                    <SelectItem value="immediate">Immediate</SelectItem>
                                </SelectContent>
                            </Select>
                            <p className="event-trigger-form__help">
                                Queue: Creates queued execution. Immediate: Runs right away, fails if duplicate running.
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
                                        <Info weight="fill" aria-hidden="true" />
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
                                                <Trash weight="bold" aria-hidden="true" />
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
                                    <Plus weight="bold" aria-hidden="true" />
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
                            <X weight="bold" aria-hidden="true" />
                            Cancel
                        </Button>
                        <Button type="submit" variant="primary" loading={isSubmitting} data-action="save">
                            <FloppyDisk weight="bold" aria-hidden="true" />
                            Save
                        </Button>
                    </SheetFooter>
                </form>
            </SheetContent>

            <UnsavedChangesDialog open={showUnsavedDialog} onDiscard={handleDiscard} onCancel={handleKeepEditing} />
        </Sheet>
    );
}

EventTriggerForm.displayName = 'EventTriggerForm';

export default EventTriggerForm;
