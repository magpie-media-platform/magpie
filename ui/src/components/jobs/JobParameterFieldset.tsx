import { useCallback, useState, type ChangeEvent, type ReactNode } from 'react';

import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import { Badge } from '@/components/ui/Badge';
import type { JobParameterDefinition } from '@/hooks/useJobs';

import {
    defaultJobParameterValue,
    getEventBindingKind,
    getJobParameterInputType,
    JOB_PARAMETER_DATA_TYPE_BOOLEAN,
    jobParameterErrorKey,
    type JobParameterValues,
} from './jobParameters';

interface JobParameterFieldsetClassNames {
    fieldset: string;
    legend: string;
    field: string;
    toggleField: string;
    help: string;
    toggleWrapper?: string;
}

interface JobParameterFieldsetProps {
    parameters: JobParameterDefinition[];
    values: JobParameterValues;
    errors?: Record<string, string>;
    disabled: boolean;
    idPrefix: string;
    classNames: JobParameterFieldsetClassNames;
    dataField?: string;
    showBooleanDescription?: boolean;
    eventBindingKeys?: readonly string[];
    eventBoundParameters?: ReadonlySet<string>;
    onEventBindingChange?: (name: string, enabled: boolean) => void;
    onChange: (name: string, value: string) => void;
}

const EMPTY_ERRORS: Record<string, string> = {};

export function JobParameterFieldset({
    parameters,
    values,
    errors = EMPTY_ERRORS,
    disabled,
    idPrefix,
    classNames,
    dataField,
    showBooleanDescription = true,
    eventBindingKeys,
    eventBoundParameters,
    onEventBindingChange,
    onChange,
}: JobParameterFieldsetProps): ReactNode {
    const [pendingDiscardParameter, setPendingDiscardParameter] = useState<string | null>(null);

    if (parameters.length === 0) {
        return null;
    }

    const handleEventBindingChange = (name: string, enabled: boolean) => {
        if (!enabled && getEventBindingKind(values[name] ?? '') !== 'static') {
            setPendingDiscardParameter(name);
            return;
        }
        onEventBindingChange?.(name, enabled);
    };

    const discardEventBinding = () => {
        const parameter = parameters.find((candidate) => candidate.name === pendingDiscardParameter);
        if (parameter) {
            onChange(parameter.name, defaultJobParameterValue(parameter));
            onEventBindingChange?.(parameter.name, false);
        }
        setPendingDiscardParameter(null);
    };

    return (
        <>
            <fieldset className={classNames.fieldset} data-field={dataField} disabled={disabled}>
                <legend className={classNames.legend}>Parameters</legend>
                {parameters.map((parameter) => (
                    <JobParameterField
                        key={parameter.name}
                        parameter={parameter}
                        value={values[parameter.name] ?? ''}
                        error={errors[jobParameterErrorKey(parameter.name)]}
                        disabled={disabled}
                        idPrefix={idPrefix}
                        classNames={classNames}
                        showBooleanDescription={showBooleanDescription}
                        eventBindingListId={eventBindingKeys ? `${idPrefix}-event-bindings` : undefined}
                        eventBindingEnabled={eventBoundParameters?.has(parameter.name) ?? false}
                        onEventBindingChange={handleEventBindingChange}
                        onChange={onChange}
                    />
                ))}
                {eventBindingKeys && (
                    <datalist id={`${idPrefix}-event-bindings`} data-testid={`${idPrefix}-event-binding-suggestions`}>
                        {eventBindingKeys.map((key) => (
                            <option key={key} value={`\${event.${key}}`} />
                        ))}
                    </datalist>
                )}
            </fieldset>
            <Dialog
                open={pendingDiscardParameter !== null}
                onOpenChange={(open) => (!open ? setPendingDiscardParameter(null) : undefined)}
            >
                <DialogContent size="alert">
                    <DialogHeader>
                        <DialogTitle>Turn off event binding?</DialogTitle>
                        <DialogDescription>This discards the event binding value.</DialogDescription>
                    </DialogHeader>
                    <DialogFooter>
                        <Button variant="ghost" onClick={() => setPendingDiscardParameter(null)} autoFocus>
                            Keep binding
                        </Button>
                        <Button variant="destructive" onClick={discardEventBinding}>
                            Discard value
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </>
    );
}

interface JobParameterFieldProps {
    parameter: JobParameterDefinition;
    value: string;
    error?: string;
    disabled: boolean;
    idPrefix: string;
    classNames: JobParameterFieldsetClassNames;
    showBooleanDescription: boolean;
    eventBindingListId?: string;
    eventBindingEnabled: boolean;
    onEventBindingChange?: (name: string, enabled: boolean) => void;
    onChange: (name: string, value: string) => void;
}

function JobParameterField({
    parameter,
    value,
    error,
    disabled,
    idPrefix,
    classNames,
    showBooleanDescription,
    eventBindingListId,
    eventBindingEnabled,
    onEventBindingChange,
    onChange,
}: JobParameterFieldProps): ReactNode {
    const inputId = `${idPrefix}-${parameter.name}`;
    const helpId = parameter.description ? `${inputId}-help` : undefined;
    const errorId = error ? `${inputId}-error` : undefined;
    const dataField = `parameter-${parameter.name}`;
    const hasError = !!error;
    const bindingKind = eventBindingListId === undefined ? undefined : getEventBindingKind(value);
    const bindingInputId = `${inputId}-event-binding`;

    const handleInputChange = useCallback(
        (e: ChangeEvent<HTMLInputElement>) => {
            onChange(parameter.name, e.target.value);
        },
        [parameter.name, onChange],
    );

    const handleBoolChange = useCallback(
        (checked: boolean) => {
            onChange(parameter.name, checked ? 'true' : 'false');
        },
        [parameter.name, onChange],
    );

    const handleEventBindingChange = useCallback(
        (enabled: boolean) => {
            onEventBindingChange?.(parameter.name, enabled);
        },
        [onEventBindingChange, parameter.name],
    );

    const bindingControl = eventBindingListId && onEventBindingChange && (
        <>
            <Switch
                id={bindingInputId}
                size="sm"
                checked={eventBindingEnabled}
                onCheckedChange={handleEventBindingChange}
                disabled={disabled}
            />
            <Label htmlFor={bindingInputId}>Bind to event</Label>
        </>
    );

    if (parameter.type === JOB_PARAMETER_DATA_TYPE_BOOLEAN && !eventBindingEnabled) {
        const describedBy =
            [showBooleanDescription ? helpId : undefined, errorId].filter(Boolean).join(' ') || undefined;
        const control = (
            <>
                <Switch
                    id={inputId}
                    checked={value === 'true'}
                    onCheckedChange={handleBoolChange}
                    disabled={disabled}
                    data-field={dataField}
                    error={hasError}
                    aria-describedby={describedBy}
                />
                <Label htmlFor={inputId} error={hasError} required={parameter.required}>
                    {parameter.name}
                </Label>
            </>
        );

        return (
            <div className={classNames.toggleField} data-parameter={parameter.name} data-binding={bindingKind}>
                {bindingControl ? (
                    <div className="job-parameter-field__label-row">
                        {classNames.toggleWrapper ? <div className={classNames.toggleWrapper}>{control}</div> : control}
                        <div className={classNames.toggleWrapper}>{bindingControl}</div>
                    </div>
                ) : classNames.toggleWrapper ? (
                    <div className={classNames.toggleWrapper}>{control}</div>
                ) : (
                    control
                )}
                {showBooleanDescription && parameter.description && (
                    <p id={helpId} className={classNames.help}>
                        {parameter.description}
                    </p>
                )}
                {error && (
                    <p id={errorId} className="form-error" role="alert">
                        {error}
                    </p>
                )}
            </div>
        );
    }

    return (
        <div className={classNames.field} data-parameter={parameter.name} data-binding={bindingKind}>
            <div className="job-parameter-field__label-row">
                <Label htmlFor={inputId} error={hasError} required={parameter.required}>
                    {parameter.name}
                </Label>
                {eventBindingListId && (
                    <div className={classNames.toggleWrapper}>
                        <Badge
                            variant={
                                bindingKind === 'invalid' ? 'failed' : bindingKind === 'event' ? 'primary' : 'default'
                            }
                            size="sm"
                            badgeStyle="outline"
                        >
                            {bindingKind === 'invalid'
                                ? 'Invalid binding'
                                : bindingKind === 'event'
                                  ? 'Event binding'
                                  : 'Static'}
                        </Badge>
                        {bindingControl}
                    </div>
                )}
            </div>
            <Input
                id={inputId}
                type={eventBindingEnabled ? 'text' : getJobParameterInputType(parameter)}
                list={eventBindingEnabled ? eventBindingListId : undefined}
                value={value}
                onChange={handleInputChange}
                placeholder={parameter.defaultValue ?? undefined}
                error={hasError}
                errorMessage={error || undefined}
                disabled={disabled}
                data-field={dataField}
                aria-describedby={helpId}
            />
            {parameter.description && (
                <p id={helpId} className={classNames.help}>
                    {parameter.description}
                </p>
            )}
        </div>
    );
}

JobParameterFieldset.displayName = 'JobParameterFieldset';

export default JobParameterFieldset;
