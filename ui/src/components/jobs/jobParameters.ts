import type { JobParameterDefinition } from '@/hooks/useJobs';

const JOB_PARAMETER_DATA_TYPE_NUMBER = 1;
export const JOB_PARAMETER_DATA_TYPE_BOOLEAN = 2;
const JOB_PARAMETER_DATA_TYPE_DATE = 3;

export type JobParameterValues = Record<string, string>;

export function jobParameterErrorKey(name: string): string {
    return `parameter-${name}`;
}

function defaultJobParameterValue(parameter: JobParameterDefinition): string {
    if (parameter.defaultValue !== null && parameter.defaultValue !== undefined) {
        return parameter.defaultValue;
    }
    return parameter.type === JOB_PARAMETER_DATA_TYPE_BOOLEAN ? 'false' : '';
}

export function seedJobParameters(
    parameters: JobParameterDefinition[] | undefined,
    existing?: JobParameterValues,
): JobParameterValues {
    const next: JobParameterValues = {};
    for (const [key, value] of Object.entries(existing ?? {})) {
        next[key] = value;
    }
    for (const parameter of parameters ?? []) {
        if (next[parameter.name] === undefined) {
            next[parameter.name] = defaultJobParameterValue(parameter);
        }
    }
    return next;
}

export function normalizeJobParameterValues(parameters: JobParameterValues): JobParameterValues {
    const next: JobParameterValues = {};
    for (const [key, value] of Object.entries(parameters)) {
        if (value !== '') {
            next[key] = value;
        }
    }
    return next;
}

export function areJobParameterValuesEqual(a: JobParameterValues | undefined, b: JobParameterValues | undefined) {
    const left = a ?? {};
    const right = b ?? {};
    const keysA = Object.keys(left);
    const keysB = Object.keys(right);
    if (keysA.length !== keysB.length) {
        return false;
    }
    for (const key of keysA) {
        if (left[key] !== right[key]) {
            return false;
        }
    }
    return true;
}

export function getJobParameterInputType(parameter: JobParameterDefinition): 'text' | 'number' | 'date' {
    if (parameter.type === JOB_PARAMETER_DATA_TYPE_NUMBER) {
        return 'number';
    }
    if (parameter.type === JOB_PARAMETER_DATA_TYPE_DATE) {
        return 'date';
    }
    return 'text';
}

export function validateJobParameters(
    parameters: JobParameterDefinition[] | undefined,
    values: JobParameterValues,
): Record<string, string> {
    const errors: Record<string, string> = {};
    for (const parameter of parameters ?? []) {
        const value = values[parameter.name] ?? '';
        if (parameter.required && parameter.type !== JOB_PARAMETER_DATA_TYPE_BOOLEAN && value.trim() === '') {
            errors[jobParameterErrorKey(parameter.name)] = `${parameter.name} is required`;
            continue;
        }
        if (value === '') {
            continue;
        }
        if (parameter.type === JOB_PARAMETER_DATA_TYPE_NUMBER) {
            const parsed = Number(value);
            if (!Number.isFinite(parsed)) {
                errors[jobParameterErrorKey(parameter.name)] = `${parameter.name} must be a number`;
            }
        } else if (parameter.type === JOB_PARAMETER_DATA_TYPE_BOOLEAN) {
            if (value !== 'true' && value !== 'false') {
                errors[jobParameterErrorKey(parameter.name)] = `${parameter.name} must be true or false`;
            }
        } else if (parameter.type === JOB_PARAMETER_DATA_TYPE_DATE) {
            const date = new Date(value);
            if (Number.isNaN(date.getTime())) {
                errors[jobParameterErrorKey(parameter.name)] = `${parameter.name} must be a valid date`;
            }
        }
    }
    return errors;
}
