import { useState, useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { FloppyDisk, X, Info } from '@phosphor-icons/react';

import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { Tooltip } from '@/components/ui/Tooltip';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';

import './ScheduleTriggerForm.css';

export interface JobTemplateOption {
    id: string;
    name: string;
}

export interface ScheduleTriggerFormValues {
    id?: string;
    name: string;
    templateId: string;
    cronExpression: string;
    enabled: boolean;
}

export interface ScheduleTriggerFormProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    initialValues?: ScheduleTriggerFormValues | null;

    templates: JobTemplateOption[];

    onSubmit: (values: ScheduleTriggerFormValues) => void;

    isSubmitting?: boolean;
}

const emptyValues: ScheduleTriggerFormValues = {
    name: '',
    templateId: '',
    cronExpression: '',
    enabled: true,
};

const cronPresets = [
    { label: 'Every hour', value: '0 * * * *' },
    { label: 'Every 6 hours', value: '0 */6 * * *' },
    { label: 'Every 12 hours', value: '0 */12 * * *' },
    { label: 'Daily at midnight', value: '0 0 * * *' },
    { label: 'Daily at 6 AM', value: '0 6 * * *' },
    { label: 'Weekly on Sunday', value: '0 0 * * 0' },
];

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

export function ScheduleTriggerForm({
    open,
    onOpenChange,
    initialValues,
    templates,
    onSubmit,
    isSubmitting = false,
}: ScheduleTriggerFormProps): ReactNode {
    const isEditing = !!initialValues?.id;

    const [values, setValues] = useState<ScheduleTriggerFormValues>(initialValues ?? emptyValues);
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [selectedPreset, setSelectedPreset] = useState<string>(() => {
        if (!initialValues?.cronExpression) {
            return CUSTOM_PRESET;
        }
        const preset = cronPresets.find((p) => p.value === initialValues.cronExpression);
        return preset ? preset.value : CUSTOM_PRESET;
    });

    const cleanValuesRef = useRef<ScheduleTriggerFormValues>(initialValues ?? emptyValues);
    const [showUnsavedDialog, setShowUnsavedDialog] = useState(false);

    useEffect(() => {
        if (open) {
            const newValues = initialValues ?? emptyValues;
            setValues(newValues);
            cleanValuesRef.current = newValues;
            setErrors({});
            setShowUnsavedDialog(false);

            const preset = cronPresets.find((p) => p.value === newValues.cronExpression);
            setSelectedPreset(preset ? preset.value : CUSTOM_PRESET);
        }
    }, [open, initialValues]);

    const isDirty = useMemo(
        () =>
            values.name !== cleanValuesRef.current.name ||
            values.templateId !== cleanValuesRef.current.templateId ||
            values.cronExpression !== cleanValuesRef.current.cronExpression ||
            values.enabled !== cleanValuesRef.current.enabled,
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
            setValues((prev) => ({ ...prev, cronExpression: preset }));
            setErrors((prev) => ({ ...prev, cronExpression: '' }));
        }
    }, []);

    const handleCronChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const newValue = e.target.value;
        setValues((prev) => ({ ...prev, cronExpression: newValue }));
        setErrors((prev) => ({ ...prev, cronExpression: '' }));

        const preset = cronPresets.find((p) => p.value === newValue);
        setSelectedPreset(preset ? preset.value : CUSTOM_PRESET);
    }, []);

    const handleEnabledChange = useCallback((enabled: boolean) => {
        setValues((prev) => ({ ...prev, enabled }));
    }, []);

    const validate = useCallback((): boolean => {
        const newErrors: Record<string, string> = {};

        if (!values.name.trim()) {
            newErrors.name = 'Name is required';
        }

        if (!values.templateId) {
            newErrors.templateId = 'Job template is required';
        }

        const cronError = validateCronExpression(values.cronExpression);
        if (cronError) {
            newErrors.cronExpression = cronError;
        }

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
        const preset = cronPresets.find((p) => p.value === selectedPreset);
        return preset?.label ?? 'Custom';
    }, [selectedPreset]);

    return (
        <Sheet open={open} onOpenChange={handleOpenChange}>
            <SheetContent side="right" className="schedule-trigger-form__sheet" data-component="schedule-trigger-form">
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit Schedule Trigger' : 'Add Schedule Trigger'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? 'Update the schedule trigger configuration.'
                            : 'Configure a new schedule trigger to run a job automatically.'}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="schedule-trigger-form">
                    <div className="schedule-trigger-form__body">
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
                                disabled={isSubmitting}
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
                                disabled={isSubmitting}
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
                                        <SelectItem key={template.id} value={template.id}>
                                            {template.name}
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
                                        <Info weight="fill" aria-hidden="true" />
                                    </button>
                                </Tooltip>
                            </div>
                            <Select value={selectedPreset} onValueChange={handlePresetChange} disabled={isSubmitting}>
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
                                disabled={isSubmitting}
                                data-field="cronExpression"
                                className="schedule-trigger-form__cron-input"
                                data-testid="schedule-trigger-form__cron-input"
                            />
                            <p className="schedule-trigger-form__help">Format: minute hour day month weekday</p>
                        </div>

                        <div className="schedule-trigger-form__field schedule-trigger-form__field--toggle">
                            <div className="schedule-trigger-form__toggle-wrapper">
                                <Switch
                                    id="trigger-enabled"
                                    checked={values.enabled}
                                    onCheckedChange={handleEnabledChange}
                                    disabled={isSubmitting}
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

ScheduleTriggerForm.displayName = 'ScheduleTriggerForm';

export default ScheduleTriggerForm;
