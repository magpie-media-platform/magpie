import { describe, expect, it } from 'vitest';

import { buildQuery, escapeQueryString, EMPTY_FIELDS, isSimpleQuery, parseQuery } from './simpleFilterQuery';

describe('simpleFilterQuery', () => {
    describe('escapeQueryString', () => {
        it('escapes backslashes and double quotes', () => {
            expect(escapeQueryString('a\\b"c')).toBe('a\\\\b\\"c');
        });
    });

    describe('buildQuery', () => {
        it('joins content clauses with and', () => {
            const query = buildQuery(
                {
                    ...EMPTY_FIELDS,
                    contentType: 'movie',
                    title: 'matrix',
                    genre: 'Action',
                    tag: 'favorite',
                },
                'content',
            );

            expect(query).toBe(
                'contentType = "movie" and title contains "matrix" and genre has "Action" and tag has "favorite"',
            );
        });

        it('emits the exact level slug with its content type', () => {
            const query = buildQuery({ ...EMPTY_FIELDS, contentType: 'books', level: 'E-book_V1' }, 'content');

            expect(query).toBe('contentType = "books" and level = "E-book_V1"');
        });

        it.each([
            [['in'], 'publishDate = "1999"'],
            [['before'], 'publishDate < "1999"'],
            [['after'], 'publishDate > "1999"'],
            [['before', 'in'], 'publishDate <= "1999"'],
            [['in', 'after'], 'publishDate >= "1999"'],
            [['before', 'after'], 'publishDate != "1999"'],
        ] as const)('builds canonical temporal year criteria for %j', (yearOps, expected) => {
            const query = buildQuery({ ...EMPTY_FIELDS, releaseYear: '1999', yearOps: [...yearOps] }, 'content');

            expect(query).toBe(expected);
        });

        it('omits the neutral all-selected year criterion while retaining other clauses', () => {
            const query = buildQuery(
                {
                    ...EMPTY_FIELDS,
                    contentType: 'movie',
                    releaseYear: '1999',
                    yearOps: ['before', 'in', 'after'],
                    tag: 'favorite',
                },
                'content',
            );

            expect(query).toBe('contentType = "movie" and tag has "favorite"');
        });

        it('builds a relationship clause from the selected subject-kind slug', () => {
            const query = buildQuery(
                { ...EMPTY_FIELDS, relationshipKind: 'studio', relationshipName: 'A24' },
                'content',
            );

            expect(query).toBe('Relationships has (Name contains "A24" and Type = "studio")');
        });

        it('builds relationship clauses when only kind or name is set', () => {
            expect(buildQuery({ ...EMPTY_FIELDS, relationshipName: 'A24' }, 'content')).toBe(
                'Relationships has (Name contains "A24")',
            );
            expect(buildQuery({ ...EMPTY_FIELDS, relationshipKind: 'studio' }, 'content')).toBe(
                'Relationships has (Type = "studio")',
            );
        });

        it('omits content-only fields in subject context', () => {
            const query = buildQuery({ ...EMPTY_FIELDS, kind: 'person', genre: 'Action', title: 'x' }, 'subject');

            expect(query).toBe('kind = "person" and title contains "x"');
        });
    });

    describe('parseQuery', () => {
        it('round-trips a built content query', () => {
            const fields = {
                ...EMPTY_FIELDS,
                contentType: 'movie',
                title: 'matrix',
                releaseYear: '1999',
                yearOps: ['in' as const],
                relationshipKind: 'crew',
                relationshipName: 'Johansson',
            };
            const query = buildQuery(fields, 'content');

            const parsed = parseQuery(query, 'content');

            expect(parsed).not.toBeNull();
            expect(parsed?.contentType).toBe('movie');
            expect(parsed?.level).toBe('');
            expect(parsed?.title).toBe('matrix');
            expect(parsed?.releaseYear).toBe('1999');
            expect(parsed?.yearOps).toEqual(['in']);
            expect(parsed?.relationshipKind).toBe('crew');
            expect(parsed?.relationshipName).toBe('Johansson');
        });

        it('parses a root level alongside its content type', () => {
            const parsed = parseQuery('contentType = "kind-x" and level = "kind-x-scene"', 'content');

            expect(parsed).toMatchObject({ contentType: 'kind-x', level: 'kind-x-scene' });
        });

        it.each([
            ['publishDate = "1999"', ['in']],
            ['publishDate < "1999"', ['before']],
            ['publishDate > "1999"', ['after']],
            ['publishDate <= "1999"', ['before', 'in']],
            ['publishDate >= "1999"', ['in', 'after']],
            ['publishDate != "1999"', ['before', 'after']],
        ] as const)('parses canonical temporal year criterion %s', (query, yearOps) => {
            const parsed = parseQuery(query, 'content');

            expect(parsed?.releaseYear).toBe('1999');
            expect(parsed?.yearOps).toEqual(yearOps);
            expect(buildQuery(parsed!, 'content')).toBe(query);
        });

        it.each([
            ['(publishDate >= "1999-01-01" and publishDate <= "1999-12-31")', 'publishDate = "1999"'],
            ['publishDate < "1999-01-01"', 'publishDate < "1999"'],
            ['publishDate > "1999-12-31"', 'publishDate > "1999"'],
            ['publishDate <= "1999-12-31"', 'publishDate <= "1999"'],
            ['publishDate >= "1999-01-01"', 'publishDate >= "1999"'],
            ['(publishDate < "1999-01-01" or publishDate > "1999-12-31")', 'publishDate != "1999"'],
        ] as const)('parses legacy year criterion %s and normalizes it on build', (query, canonical) => {
            const parsed = parseQuery(query, 'content');

            expect(parsed?.releaseYear).toBe('1999');
            expect(buildQuery(parsed!, 'content')).toBe(canonical);
        });

        it('parses relationship clauses with only kind or name', () => {
            const nameOnly = parseQuery('Relationships has (Name contains "A24")', 'content');
            expect(nameOnly?.relationshipName).toBe('A24');
            expect(nameOnly?.relationshipKind).toBe('');

            const kindOnly = parseQuery('Relationships has (Type = "studio")', 'content');
            expect(kindOnly?.relationshipName).toBe('');
            expect(kindOnly?.relationshipKind).toBe('studio');
        });

        it('returns null for an unsupported clause', () => {
            expect(parseQuery('rating >= 8', 'content')).toBeNull();
        });
    });

    describe('isSimpleQuery', () => {
        it('treats empty and supported queries as simple', () => {
            expect(isSimpleQuery('', 'content')).toBe(true);
            expect(isSimpleQuery('contentType = "movie" and monitoring = true', 'content')).toBe(true);
        });

        it('rejects unsupported queries', () => {
            expect(isSimpleQuery('contentType = "movie" or contentType = "show"', 'content')).toBe(false);
        });
    });
});
