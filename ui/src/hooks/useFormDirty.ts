import { useMemo } from 'react';

import type { IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

interface SeededDefaults {
    next: Record<string, string>;
    baseline: Record<string, string>;
    changed: boolean;
}

export function foldDefaultsIntoBaseline(
    current: Record<string, string>,
    baseline: Record<string, string>,
    schema: IntegrationSettingDefinition[],
): SeededDefaults {
    const next = { ...current };
    const nextBaseline = { ...baseline };
    let changed = false;

    for (const field of schema) {
        if (field.defaultValue !== undefined && !(field.key in next)) {
            next[field.key] = field.defaultValue;
            changed = true;
        }
        if (
            field.defaultValue !== undefined &&
            !(field.key in nextBaseline) &&
            next[field.key] === field.defaultValue
        ) {
            nextBaseline[field.key] = field.defaultValue;
        }
    }

    return {
        next: changed ? next : current,
        baseline: nextBaseline,
        changed,
    };
}

export function deepEqual(a: unknown, b: unknown): boolean {
    if (Object.is(a, b)) {
        return true;
    }
    if (a === null || b === null || typeof a !== 'object' || typeof b !== 'object') {
        return false;
    }

    const aArray = Array.isArray(a);
    const bArray = Array.isArray(b);
    if (aArray !== bArray) {
        return false;
    }

    if (aArray && bArray) {
        if (a.length !== b.length) {
            return false;
        }
        for (let i = 0; i < a.length; i++) {
            if (!deepEqual(a[i], b[i])) {
                return false;
            }
        }
        return true;
    }

    const aObj = a as Record<string, unknown>;
    const bObj = b as Record<string, unknown>;
    const aKeys = Object.keys(aObj);
    const bKeys = Object.keys(bObj);
    if (aKeys.length !== bKeys.length) {
        return false;
    }
    for (const key of aKeys) {
        if (!Object.prototype.hasOwnProperty.call(bObj, key) || !deepEqual(aObj[key], bObj[key])) {
            return false;
        }
    }
    return true;
}

export function stripBlankRows<T>(rows: readonly T[], isBlank: (row: T) => boolean): T[] {
    return rows.filter((row) => !isBlank(row));
}

function identity<T>(value: T): T {
    return value;
}

export function useFormDirty<T>(current: T, baseline: T, normalize: (value: T) => unknown = identity): boolean {
    return useMemo(() => !deepEqual(normalize(current), normalize(baseline)), [current, baseline, normalize]);
}
