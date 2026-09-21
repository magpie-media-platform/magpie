import type { JobParameterDefinition } from '@/hooks/useJobs';
import type { DictionaryPatch } from '@/types/patch';

const JOB_PARAMETER_DATA_TYPE_NUMBER = 1;
export const JOB_PARAMETER_DATA_TYPE_BOOLEAN = 2;
const JOB_PARAMETER_DATA_TYPE_DATE = 3;

export type JobParameterValues = Record<string, string>;

export function jobParameterErrorKey(name: string): string {
    return `parameter-${name}`;
}

export function defaultJobParameterValue(parameter: JobParameterDefinition): string {
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

export function buildJobParameterPatch(
    original: JobParameterValues,
    next: JobParameterValues,
): DictionaryPatch<string, string> | null {
    const add: JobParameterValues = {};
    const update: JobParameterValues = {};
    const remove: string[] = [];

    for (const [key, value] of Object.entries(next)) {
        if (value === '') {
            if (original[key] !== undefined) {
                remove.push(key);
            }
            continue;
        }
        if (original[key] === undefined) {
            add[key] = value;
        } else if (original[key] !== value) {
            update[key] = value;
        }
    }

    for (const key of Object.keys(original)) {
        if (next[key] === undefined) {
            remove.push(key);
        }
    }

    const patch: DictionaryPatch<string, string> = {};
    if (Object.keys(add).length > 0) {
        patch.add = add;
    }
    if (Object.keys(update).length > 0) {
        patch.update = update;
    }
    if (remove.length > 0) {
        patch.remove = remove;
    }
    return patch.add || patch.update || patch.remove ? patch : null;
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
    allowEventBindings = false,
): Record<string, string> {
    const errors: Record<string, string> = {};
    for (const parameter of parameters ?? []) {
        const value = values[parameter.name] ?? '';
        if (
            !allowEventBindings &&
            parameter.required &&
            parameter.type !== JOB_PARAMETER_DATA_TYPE_BOOLEAN &&
            value.trim() === ''
        ) {
            errors[jobParameterErrorKey(parameter.name)] = `${parameter.name} is required`;
            continue;
        }
        if (value === '') {
            continue;
        }

        let valueToValidate = value;
        if (allowEventBindings) {
            const analysis = analyzeEventBindings(value, eventBindingValidationSample(parameter.type));
            if (analysis.malformed) {
                errors[jobParameterErrorKey(parameter.name)] = `${parameter.name} has a malformed event binding`;
                continue;
            }
            if (analysis.hasBinding) {
                valueToValidate = analysis.value;
            }
        }
        if (parameter.type === JOB_PARAMETER_DATA_TYPE_NUMBER) {
            const number = valueToValidate.trim();
            if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i.test(number) || !Number.isFinite(Number(number))) {
                errors[jobParameterErrorKey(parameter.name)] = `${parameter.name} must be a number`;
            }
        } else if (parameter.type === JOB_PARAMETER_DATA_TYPE_BOOLEAN) {
            const boolean = valueToValidate.trim().toLowerCase();
            if (boolean !== 'true' && boolean !== 'false') {
                errors[jobParameterErrorKey(parameter.name)] = `${parameter.name} must be true or false`;
            }
        } else if (parameter.type === JOB_PARAMETER_DATA_TYPE_DATE) {
            const date = /^\d{4}-\d{2}-\d{2}$/.test(valueToValidate) ? new Date(`${valueToValidate}T00:00:00Z`) : null;
            if (!date || Number.isNaN(date.getTime()) || date.toISOString().slice(0, 10) !== valueToValidate) {
                errors[jobParameterErrorKey(parameter.name)] = `${parameter.name} must be a valid date`;
            }
        }
    }
    return errors;
}

type EventBindingKind = 'static' | 'event' | 'invalid';

interface EventBindingAnalysis {
    hasBinding: boolean;
    malformed: boolean;
    value: string;
}

function eventBindingValidationSample(type: number): string {
    if (type === JOB_PARAMETER_DATA_TYPE_NUMBER) {
        return '0';
    }
    if (type === JOB_PARAMETER_DATA_TYPE_BOOLEAN) {
        return 'true';
    }
    if (type === JOB_PARAMETER_DATA_TYPE_DATE) {
        return '2000-01-01';
    }
    return '';
}

function analyzeEventBindings(value: string, replacement: string): EventBindingAnalysis {
    let result = '';
    let hasBinding = false;

    for (let index = 0; index < value.length; ) {
        if (value.startsWith('$${', index)) {
            result += '${';
            index += 3;
            continue;
        }
        if (value.startsWith('${', index)) {
            if (!value.startsWith('${event.', index)) {
                return { hasBinding, malformed: true, value: result };
            }
            const keyStart = index + 8;
            let end = keyStart;
            while (end < value.length && value[end] !== '}') {
                if (value[end] === '{') {
                    return { hasBinding, malformed: true, value: result };
                }
                end++;
            }
            if (end === value.length || end === keyStart) {
                return { hasBinding, malformed: true, value: result };
            }
            hasBinding = true;
            result += replacement;
            index = end + 1;
            continue;
        }
        result += value[index];
        index++;
    }

    return { hasBinding, malformed: false, value: result };
}

export function getEventBindingKind(value: string): EventBindingKind {
    const analysis = analyzeEventBindings(value, '');
    if (analysis.malformed) {
        return 'invalid';
    }
    return analysis.hasBinding ? 'event' : 'static';
}
