import { describe, expect, it } from 'vitest';

import {
    readContentFilter,
    readContentFilterSelection,
    setContentFilter,
    setContentFilterSelection,
    splitClauses,
} from './contentColumnFilters';

describe('contentColumnFilters', () => {
    describe('setContentFilter', () => {
        it('adds a predicate to an empty query', () => {
            expect(setContentFilter('', 'monitoring', 'true')).toBe('monitoring = true');
        });

        it('quotes string predicates', () => {
            expect(setContentFilter('', 'title', 'dune')).toBe('title contains "dune"');
        });

        it('replaces an existing predicate for the same field in place', () => {
            const result = setContentFilter('monitoring = true', 'monitoring', 'false');
            expect(result).toBe('monitoring = false');
        });

        it('removes a predicate when the value is empty', () => {
            expect(setContentFilter('monitoring = true', 'monitoring', '')).toBe('');
        });

        it('preserves unrelated predicates when editing one field', () => {
            const query = 'genre has "Action" and monitoring = true';
            const result = setContentFilter(query, 'monitoring', 'false');
            expect(result).toBe('genre has "Action" and monitoring = false');
        });

        it('preserves unrecognised predicates when removing a field', () => {
            const query = 'genre has "Action" and monitoring = true';
            expect(setContentFilter(query, 'monitoring', '')).toBe('genre has "Action"');
        });

        it('supports a publish-date range across two fields', () => {
            let query = setContentFilter('', 'publishDateFrom', '2020-01-01');
            query = setContentFilter(query, 'publishDateTo', '2020-12-31');
            expect(query).toBe('publishDate >= "2020-01-01" and publishDate <= "2020-12-31"');
        });

        it('does not mangle a query that uses or / groups (preserved verbatim)', () => {
            const query = 'monitoring = true or monitoring = false';
            const result = setContentFilter(query, 'title', 'dune');
            expect(result).toBe('monitoring = true or monitoring = false and title contains "dune"');
        });
    });

    describe('readContentFilter (round-trip)', () => {
        it('reads back a value that was written', () => {
            const query = setContentFilter('', 'monitoring', 'true');
            expect(readContentFilter(query, 'monitoring')).toBe('true');
        });

        it('reads a field from a mixed query', () => {
            const query = 'genre has "Action" and contentType = "movie" and title contains "dune"';
            expect(readContentFilter(query, 'title')).toBe('dune');
        });

        it('returns empty string when the field is absent', () => {
            expect(readContentFilter('genre has "Action"', 'monitoring')).toBe('');
        });

        it('reads both ends of a publish-date range', () => {
            const query = 'publishDate >= "2020-01-01" and publishDate <= "2020-12-31"';
            expect(readContentFilter(query, 'publishDateFrom')).toBe('2020-01-01');
            expect(readContentFilter(query, 'publishDateTo')).toBe('2020-12-31');
        });
    });

    describe('content filter selection', () => {
        it('writes a broad content type as one predicate', () => {
            expect(setContentFilterSelection('', { contentType: 'kind-x' })).toBe('contentType = "kind-x"');
        });

        it('writes a root selection with the exact registered level slug', () => {
            expect(setContentFilterSelection('', { contentType: 'books', level: 'E-book_V1' })).toBe(
                'contentType = "books" and level = "E-book_V1"',
            );
        });

        it('reads an existing root selection as one composite value', () => {
            expect(
                readContentFilterSelection(
                    'title contains "dune" and contentType = "kind-x" and level = "kind-x-scene"',
                ),
            ).toEqual({ contentType: 'kind-x', level: 'kind-x-scene' });
        });

        it('changes a root selection to a broad type and preserves unrelated predicates', () => {
            const query = 'title contains "dune" and contentType = "kind-x" and level = "kind-x-scene"';

            expect(setContentFilterSelection(query, { contentType: 'books' })).toBe(
                'title contains "dune" and contentType = "books"',
            );
        });

        it('clears both owned predicates and preserves unrelated predicates', () => {
            const query = 'contentType = "kind-x" and level = "kind-x-scene" and monitoring = true';

            expect(setContentFilterSelection(query, null)).toBe('monitoring = true');
        });
    });

    describe('splitClauses', () => {
        it('decomposes a flat multi-field query into recognised predicates', () => {
            const predicates = splitClauses('contentType = "movie" and level = "movie-root" and title contains "dune"');
            expect(predicates.map((p) => p.field)).toEqual(['contentType', 'level', 'title']);
        });

        it('treats a fulfillment clause as opaque now that file status owns a dedicated control', () => {
            const predicates = splitClauses('contentType = "movie" and Fulfillment = Missing');
            expect(predicates.map((p) => p.field)).toEqual(['contentType', null]);
        });

        it('collapses an or / group query into a single opaque clause', () => {
            const predicates = splitClauses('monitoring = true or monitoring = false');
            expect(predicates).toHaveLength(1);
            expect(predicates[0].field).toBeNull();
        });

        it('flags genre/tag clauses as opaque (field null)', () => {
            const predicates = splitClauses('genre has "Action" and monitoring = true');
            expect(predicates.map((p) => p.field)).toEqual([null, 'monitoring']);
        });
    });

    describe('paren-aware editing (person / release-year clauses present)', () => {
        const personClause = 'Relationships has (Name contains "Johansson" and Type = "person")';
        const yearClause = '(publishDate >= "1999-01-01" and publishDate <= "1999-12-31")';

        it('splits top-level clauses while keeping a person clause opaque and intact', () => {
            const predicates = splitClauses(`contentType = "movie" and ${personClause}`);
            expect(predicates.map((p) => p.field)).toEqual(['contentType', null]);
            expect(predicates[1].raw).toBe(personClause);
        });

        it('splits top-level clauses while keeping a two-bound year clause opaque and intact', () => {
            const predicates = splitClauses(`${yearClause} and monitoring = true`);
            expect(predicates.map((p) => p.field)).toEqual([null, 'monitoring']);
            expect(predicates[0].raw).toBe(yearClause);
        });

        it('applies a date filter while preserving a person clause', () => {
            const result = setContentFilter(personClause, 'publishDateFrom', '2020-01-01');
            expect(result).toBe(`${personClause} and publishDate >= "2020-01-01"`);
        });

        it('applies a date filter while preserving a two-bound year clause', () => {
            const result = setContentFilter(yearClause, 'publishDateTo', '2020-12-31');
            expect(result).toBe(`${yearClause} and publishDate <= "2020-12-31"`);
        });

        it('applies a date filter while preserving both a person and a year clause', () => {
            const query = `${yearClause} and ${personClause}`;
            const result = setContentFilter(query, 'publishDateFrom', '2020-01-01');
            expect(result).toBe(`${yearClause} and ${personClause} and publishDate >= "2020-01-01"`);
        });

        it('replaces an existing date filter in place alongside a person clause', () => {
            const query = `${personClause} and publishDate >= "2020-01-01"`;
            const result = setContentFilter(query, 'publishDateFrom', '2021-06-01');
            expect(result).toBe(`${personClause} and publishDate >= "2021-06-01"`);
        });

        it('removes a date filter while preserving a person clause', () => {
            const query = `${personClause} and publishDate >= "2020-01-01"`;
            const result = setContentFilter(query, 'publishDateFrom', '');
            expect(result).toBe(personClause);
        });

        it('reads a date filter that sits alongside parenthesised clauses', () => {
            const query = `${yearClause} and publishDate <= "2020-12-31" and ${personClause}`;
            expect(readContentFilter(query, 'publishDateTo')).toBe('2020-12-31');
        });

        it('does not regress the no-parenthesis date filter case', () => {
            let query = setContentFilter('contentType = "movie"', 'publishDateFrom', '2020-01-01');
            query = setContentFilter(query, 'publishDateTo', '2020-12-31');
            expect(query).toBe('contentType = "movie" and publishDate >= "2020-01-01" and publishDate <= "2020-12-31"');
        });

        it('keeps a quoted value containing " and " as a single clause', () => {
            const predicates = splitClauses('title contains "spy and the city" and monitoring = true');
            expect(predicates.map((p) => p.field)).toEqual(['title', 'monitoring']);
            expect(predicates[0].value).toBe('spy and the city');
        });
    });
});
