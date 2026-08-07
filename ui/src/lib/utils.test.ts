import { describe, expect, it } from 'vitest';

import { queryErrorOrNull } from './utils';

describe('queryErrorOrNull', () => {
    it('returns the error when it is an Error instance', () => {
        const error = new Error('something failed');
        expect(queryErrorOrNull({ error })).toBe(error);
    });

    it('returns null when error is null', () => {
        expect(queryErrorOrNull({ error: null })).toBeNull();
    });

    it('returns null when error is undefined', () => {
        expect(queryErrorOrNull({ error: undefined })).toBeNull();
    });

    it('returns null when error is a string', () => {
        expect(queryErrorOrNull({ error: 'not an Error' })).toBeNull();
    });

    it('returns null when error is a plain object', () => {
        expect(queryErrorOrNull({ error: { message: 'oops' } })).toBeNull();
    });
});
