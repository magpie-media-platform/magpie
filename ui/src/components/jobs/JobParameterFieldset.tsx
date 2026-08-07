import { useCallback, type ChangeEvent, type ReactNode } from 'react';

import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Switch } from '@/components/ui/Switch';
import type { JobParameterDefinition } from '@/hooks/useJobs';

import {
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
    onChange,
}: JobParameterFieldsetProps): ReactNode {
    if (parameters.length === 0) {
        return null;
    }

    return (
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
                    onChange={onChange}
                />
            ))}
        </fieldset>
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
    onChange,
}: JobParameterFieldProps): ReactNode {
    const inputId = `${idPrefix}-${parameter.name}`;
    const helpId = parameter.description ? `${inputId}-help` : undefined;
    const errorId = error ? `${inputId}-error` : undefined;
    const dataField = `parameter-${parameter.name}`;
    const hasError = !!error;

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

    if (parameter.type === JOB_PARAMETER_DATA_TYPE_BOOLEAN) {
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
            <div className={classNames.toggleField} data-parameter={parameter.name}>
                {classNames.toggleWrapper ? <div className={classNames.toggleWrapper}>{control}</div> : control}
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
        <div className={classNames.field} data-parameter={parameter.name}>
            <Label htmlFor={inputId} error={hasError} required={parameter.required}>
                {parameter.name}
            </Label>
            <Input
                id={inputId}
                type={getJobParameterInputType(parameter)}
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
