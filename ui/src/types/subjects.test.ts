import { describe, expect, it } from 'vitest';

import { formatSubjectKindLabel, formatSubjectKindFilterLabel } from './subjects';

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

describe('formatSubjectKindFilterLabel', () => {
    it('returns "All Kinds" for empty string', () => {
        expect(formatSubjectKindFilterLabel('')).toBe('All Kinds');
    });

    it('returns plural label for known kinds', () => {
        expect(formatSubjectKindFilterLabel('show')).toBe('Shows');
        expect(formatSubjectKindFilterLabel('movie')).toBe('Movies');
        expect(formatSubjectKindFilterLabel('person')).toBe('People');
        expect(formatSubjectKindFilterLabel('album')).toBe('Albums');
        expect(formatSubjectKindFilterLabel('book')).toBe('Books');
    });

    it('returns capitalized fallback for extension-defined kinds', () => {
        expect(formatSubjectKindFilterLabel('studio')).toBe('Studio');
        expect(formatSubjectKindFilterLabel('publisher')).toBe('Publisher');
    });
});
