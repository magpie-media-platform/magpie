import { describe, expect, it } from 'vitest';

import { toStringSettings, buildSettingsPatch } from './downloadClientUtils';

describe('toStringSettings', () => {
    it('converts number values to strings', () => {
        const result = toStringSettings({ port: 8080 });
        expect(result).toEqual({ port: '8080' });
    });

    it('converts boolean values to strings', () => {
        const result = toStringSettings({ enabled: true, ssl: false });
        expect(result).toEqual({ enabled: 'true', ssl: 'false' });
    });

    it('keeps string values unchanged', () => {
        const result = toStringSettings({ host: 'localhost' });
        expect(result).toEqual({ host: 'localhost' });
    });

    it('omits undefined values', () => {
        const result = toStringSettings({ host: 'localhost', optional: undefined });
        expect(result).toEqual({ host: 'localhost' });
        expect('optional' in result).toBe(false);
    });

    it('returns empty object for empty input', () => {
        const result = toStringSettings({});
        expect(result).toEqual({});
    });

    it('handles mixed value types', () => {
        const result = toStringSettings({ host: 'localhost', port: 6789, ssl: false, apiKey: undefined });
        expect(result).toEqual({ host: 'localhost', port: '6789', ssl: 'false' });
    });
});

describe('buildSettingsPatch', () => {
    it('puts new keys into add bucket', () => {
        const patch = buildSettingsPatch({ apiKey: 'abc' }, {});
        expect(patch).toEqual({ add: { apiKey: 'abc' } });
    });

    it('puts existing keys into update bucket', () => {
        const patch = buildSettingsPatch({ host: 'newhost' }, { host: 'oldhost' });
        expect(patch).toEqual({ update: { host: 'newhost' } });
    });

    it('partitions mixed keys correctly', () => {
        const patch = buildSettingsPatch(
            { host: 'localhost', port: '8080', apiKey: 'abc' },
            { host: 'oldhost', port: '9090' },
        );
        expect(patch).toEqual({
            update: { host: 'localhost', port: '8080' },
            add: { apiKey: 'abc' },
        });
    });

    it('omits add bucket when all keys exist', () => {
        const patch = buildSettingsPatch({ host: 'localhost' }, { host: 'oldhost' });
        expect(patch).toEqual({ update: { host: 'localhost' } });
        expect('add' in patch).toBe(false);
    });

    it('omits update bucket when all keys are new', () => {
        const patch = buildSettingsPatch({ apiKey: 'abc' }, { host: 'oldhost' });
        expect(patch).toEqual({ add: { apiKey: 'abc' } });
        expect('update' in patch).toBe(false);
    });

    it('returns empty patch for empty new settings', () => {
        const patch = buildSettingsPatch({}, { host: 'oldhost' });
        expect(patch).toEqual({});
    });

    it('returns empty patch for both empty', () => {
        const patch = buildSettingsPatch({}, {});
        expect(patch).toEqual({});
    });
});
