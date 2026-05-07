import { describe, expect, it } from 'vitest';

import { stateNameToValue, stateValueToName, readFiltersFromParams } from './downloadFilters';

describe('stateNameToValue', () => {
    it('maps all state names to numeric string values', () => {
        expect(stateNameToValue['grabbed']).toBe('0');
        expect(stateNameToValue['queued']).toBe('1');
        expect(stateNameToValue['downloading']).toBe('2');
        expect(stateNameToValue['paused']).toBe('3');
        expect(stateNameToValue['stalled']).toBe('4');
        expect(stateNameToValue['downloaded']).toBe('5');
        expect(stateNameToValue['imported']).toBe('6');
        expect(stateNameToValue['failed']).toBe('7');
    });
});

describe('stateValueToName', () => {
    it('maps all numeric string values to state names', () => {
        expect(stateValueToName['0']).toBe('grabbed');
        expect(stateValueToName['1']).toBe('queued');
        expect(stateValueToName['2']).toBe('downloading');
        expect(stateValueToName['3']).toBe('paused');
        expect(stateValueToName['4']).toBe('stalled');
        expect(stateValueToName['5']).toBe('downloaded');
        expect(stateValueToName['6']).toBe('imported');
        expect(stateValueToName['7']).toBe('failed');
    });
});

describe('readFiltersFromParams', () => {
    it('returns empty strings when params are absent', () => {
        const params = new URLSearchParams();
        expect(readFiltersFromParams(params, 'client', 'state')).toEqual({ client: '', state: '' });
    });

    it('reads client param', () => {
        const params = new URLSearchParams('client=abc');
        expect(readFiltersFromParams(params, 'client', 'state')).toEqual({ client: 'abc', state: '' });
    });

    it('converts state name to value', () => {
        const params = new URLSearchParams('state=downloading');
        expect(readFiltersFromParams(params, 'client', 'state')).toEqual({ client: '', state: '2' });
    });

    it('handles state name case-insensitively', () => {
        const params = new URLSearchParams('state=FAILED');
        expect(readFiltersFromParams(params, 'client', 'state')).toEqual({ client: '', state: '7' });
    });

    it('returns empty state for unknown state name', () => {
        const params = new URLSearchParams('state=unknown');
        expect(readFiltersFromParams(params, 'client', 'state')).toEqual({ client: '', state: '' });
    });

    it('reads both client and state together', () => {
        const params = new URLSearchParams('client=xyz&state=paused');
        expect(readFiltersFromParams(params, 'client', 'state')).toEqual({ client: 'xyz', state: '3' });
    });

    it('uses the specified key names', () => {
        const params = new URLSearchParams('c=abc&s=imported');
        expect(readFiltersFromParams(params, 'c', 's')).toEqual({ client: 'abc', state: '6' });
    });
});
