import { describe, expect, it } from 'vitest';

import { cn, queryErrorOrNull } from './utils';

describe('cn', () => {
    it('joins multiple class names', () => {
        expect(cn('a', 'b', 'c')).toBe('a b c');
    });

    it('filters out falsy values', () => {
        expect(cn('a', undefined, null, false, 'b')).toBe('a b');
    });

    it('returns empty string when all values are falsy', () => {
        expect(cn(undefined, null, false)).toBe('');
    });

    it('returns empty string when called with no arguments', () => {
        expect(cn()).toBe('');
    });

    it('returns single class when only one is provided', () => {
        expect(cn('only')).toBe('only');
    });
});

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
