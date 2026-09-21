import { describe, expect, it } from 'vitest';

import { resolveSubjectKindLabel } from './subjects';

describe('resolveSubjectKindLabel', () => {
    it('returns label for known kinds', () => {
        expect(resolveSubjectKindLabel('show')).toBe('Show');
        expect(resolveSubjectKindLabel('movie')).toBe('Movie');
        expect(resolveSubjectKindLabel('person')).toBe('Person');
        expect(resolveSubjectKindLabel('album')).toBe('Album');
        expect(resolveSubjectKindLabel('book')).toBe('Book');
        expect(resolveSubjectKindLabel('unknown')).toBe('Unknown');
    });

    it('returns capitalized fallback for extension-defined kinds', () => {
        expect(resolveSubjectKindLabel('studio')).toBe('Studio');
        expect(resolveSubjectKindLabel('publisher')).toBe('Publisher');
        expect(resolveSubjectKindLabel('network')).toBe('Network');
    });
});
