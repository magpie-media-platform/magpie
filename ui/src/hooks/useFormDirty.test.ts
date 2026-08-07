import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

import { deepEqual, foldDefaultsIntoBaseline, stripBlankRows, useFormDirty } from './useFormDirty';

describe('deepEqual', () => {
    it('treats identical primitives as equal', () => {
        expect(deepEqual('a', 'a')).toBe(true);
        expect(deepEqual(1, 1)).toBe(true);
        expect(deepEqual('a', 'b')).toBe(false);
    });

    it('compares object keys order-insensitively', () => {
        expect(deepEqual({ host: 'x', port: '1' }, { port: '1', host: 'x' })).toBe(true);
    });

    it('flags an object with a differing value', () => {
        expect(deepEqual({ host: 'x' }, { host: 'y' })).toBe(false);
    });

    it('flags objects with different key sets', () => {
        expect(deepEqual({ host: 'x' }, { host: 'x', port: '1' })).toBe(false);
    });

    it('compares arrays by order', () => {
        expect(deepEqual([1, 2, 3], [1, 2, 3])).toBe(true);
        expect(deepEqual([1, 2, 3], [3, 2, 1])).toBe(false);
    });

    it('recurses through nested structures', () => {
        expect(deepEqual({ a: [{ k: 1 }] }, { a: [{ k: 1 }] })).toBe(true);
        expect(deepEqual({ a: [{ k: 1 }] }, { a: [{ k: 2 }] })).toBe(false);
    });

    it('distinguishes an array from a plain object', () => {
        expect(deepEqual([], {})).toBe(false);
    });
});

describe('stripBlankRows', () => {
    it('drops rows the predicate marks blank', () => {
        const rows = [{ v: '' }, { v: 'x' }, { v: '' }];
        expect(stripBlankRows(rows, (row) => row.v === '')).toEqual([{ v: 'x' }]);
    });

    it('returns all rows when none are blank', () => {
        const rows = [{ v: 'a' }, { v: 'b' }];
        expect(stripBlankRows(rows, (row) => row.v === '')).toEqual(rows);
    });
});

describe('foldDefaultsIntoBaseline', () => {
    const schema: IntegrationSettingDefinition[] = [
        { key: 'url', type: SchemaDataType.String, displayName: 'URL', required: true },
        { key: 'method', type: SchemaDataType.String, displayName: 'Method', defaultValue: 'POST' },
    ];

    it('seeds a default into both the rendered settings and the baseline', () => {
        const { next, baseline, changed } = foldDefaultsIntoBaseline({}, {}, schema);
        expect(changed).toBe(true);
        expect(next.method).toBe('POST');
        expect(baseline.method).toBe('POST');
    });

    it('does not overwrite a value the operator already supplied', () => {
        const { next, baseline } = foldDefaultsIntoBaseline({ method: 'PUT' }, { method: 'PUT' }, schema);
        expect(next.method).toBe('PUT');
        expect(baseline.method).toBe('PUT');
    });

    it('keeps an unedited seeded-default form reading clean', () => {
        const { next, baseline } = foldDefaultsIntoBaseline({}, {}, schema);
        expect(deepEqual(next, baseline)).toBe(true);
    });
});

describe('useFormDirty', () => {
    it('is not dirty when current and baseline share a reference', () => {
        const value = { name: '' };
        const { result } = renderHook(() => useFormDirty(value, value));
        expect(result.current).toBe(false);
    });

    it('is not dirty when a normalized snapshot drops a blank sub-row', () => {
        const baseline = { rows: [] as { v: string }[] };
        const current = { rows: [{ v: '' }] };
        const normalize = (value: { rows: { v: string }[] }) => ({
            rows: stripBlankRows(value.rows, (row) => row.v === ''),
        });
        const { result } = renderHook(() => useFormDirty(current, baseline, normalize));
        expect(result.current).toBe(false);
    });

    it('is dirty once a sub-row carries real content', () => {
        const baseline = { rows: [] as { v: string }[] };
        const current = { rows: [{ v: 'real' }] };
        const normalize = (value: { rows: { v: string }[] }) => ({
            rows: stripBlankRows(value.rows, (row) => row.v === ''),
        });
        const { result } = renderHook(() => useFormDirty(current, baseline, normalize));
        expect(result.current).toBe(true);
    });

    it('is dirty when a tracked scalar changes', () => {
        const { result } = renderHook(() => useFormDirty({ name: 'b' }, { name: 'a' }));
        expect(result.current).toBe(true);
    });

    it('ignores object key order in the snapshot', () => {
        const { result } = renderHook(() => useFormDirty({ a: '1', b: '2' }, { b: '2', a: '1' }));
        expect(result.current).toBe(false);
    });
});
