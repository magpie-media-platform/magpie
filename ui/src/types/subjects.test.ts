import { describe, expect, it } from 'vitest';

import { formatSubjectKindLabel } from './subjects';

describe('formatSubjectKindLabel', () => {
    it('returns label for known kinds', () => {
        expect(formatSubjectKindLabel('show')).toBe('Show');
        expect(formatSubjectKindLabel('movie')).toBe('Movie');
        expect(formatSubjectKindLabel('person')).toBe('Person');
        expect(formatSubjectKindLabel('album')).toBe('Album');
        expect(formatSubjectKindLabel('book')).toBe('Book');
        expect(formatSubjectKindLabel('unknown')).toBe('Unknown');
    });

    it('returns capitalized fallback for extension-defined kinds', () => {
        expect(formatSubjectKindLabel('studio')).toBe('Studio');
        expect(formatSubjectKindLabel('publisher')).toBe('Publisher');
        expect(formatSubjectKindLabel('network')).toBe('Network');
    });
});
