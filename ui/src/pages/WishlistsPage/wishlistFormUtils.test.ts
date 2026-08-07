import { describe, expect, it } from 'vitest';

import { getQueryContentType, setQueryContentType } from './wishlistFormUtils';

describe('getQueryContentType', () => {
    it('returns an empty string when the query has no content type', () => {
        expect(getQueryContentType('')).toBe('');
        expect(getQueryContentType('genre has "Action"')).toBe('');
    });

    it('reads an equals clause anywhere in the query', () => {
        expect(getQueryContentType('contentType = "movie"')).toBe('movie');
        expect(getQueryContentType('genre has "Action" and contentType = "book"')).toBe('book');
    });

    it('reads the first value of an in clause', () => {
        expect(getQueryContentType('contentType in ("movie", "show")')).toBe('movie');
    });
});

describe('setQueryContentType', () => {
    it('inserts a content-type clause into a query that has none', () => {
        expect(setQueryContentType('genre has "Action"', 'book')).toBe('contentType = "book" and genre has "Action"');
    });

    it('replaces an existing equals clause in place', () => {
        expect(setQueryContentType('contentType = "movie" and genre has "Action"', 'book')).toBe(
            'contentType = "book" and genre has "Action"',
        );
    });

    it('replaces an existing in clause', () => {
        expect(setQueryContentType('contentType in ("movie", "show") and genre has "Action"', 'book')).toBe(
            'contentType = "book" and genre has "Action"',
        );
    });

    it('clears the clause when the slug is empty', () => {
        expect(setQueryContentType('contentType = "movie" and genre has "Action"', '')).toBe('genre has "Action"');
        expect(setQueryContentType('contentType = "movie"', '')).toBe('');
    });

    it('sets the only clause when the query was empty', () => {
        expect(setQueryContentType('', 'movie')).toBe('contentType = "movie"');
    });

    it('parenthesises a top-level or so the content type applies to both branches', () => {
        expect(setQueryContentType('genre = "Action" or year >= 2020', 'book')).toBe(
            'contentType = "book" and (genre = "Action" or year >= 2020)',
        );
    });

    it('does not parenthesise a remainder with no top-level or', () => {
        expect(setQueryContentType('genre has "Action" and year >= 2020', 'book')).toBe(
            'contentType = "book" and genre has "Action" and year >= 2020',
        );
    });

    it('replaces an existing clause and parenthesises an or remainder', () => {
        expect(setQueryContentType('contentType = "movie" and (genre = "Action" or year >= 2020)', 'book')).toBe(
            'contentType = "book" and (genre = "Action" or year >= 2020)',
        );
    });
});
