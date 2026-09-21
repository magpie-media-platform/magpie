import { describe, it, expect } from 'vitest';

import {
    extractCustomValues,
    extractExternalIds,
    extractImages,
    extractImageUrl,
    extractReferences,
    extractTitle,
    extractOverview,
    extractYear,
    type MetadataContainer,
} from './metadata';

describe('extractImageUrl', () => {
    it('returns undefined for null container', () => {
        expect(extractImageUrl(null)).toBeUndefined();
        expect(extractImageUrl(undefined)).toBeUndefined();
    });

    it('returns undefined for container with no contributions', () => {
        const container: MetadataContainer = { id: 'c1', contributions: [] };
        expect(extractImageUrl(container)).toBeUndefined();
    });

    it('returns undefined for contributions with no images', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [{ ordinal: 0, title: 'Test' }],
        };
        expect(extractImageUrl(container)).toBeUndefined();
    });

    it('returns poster image URL from first contribution', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 0,
                    images: [
                        { type: 'backdrop', url: 'https://example.com/backdrop.jpg' },
                        { type: 'poster', url: 'https://example.com/poster.jpg' },
                    ],
                },
            ],
        };
        expect(extractImageUrl(container)).toBe('https://example.com/poster.jpg');
    });

    it('prefers poster over other types', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 0,
                    images: [
                        { type: 'backdrop', url: 'https://example.com/backdrop.jpg' },
                        { type: 'poster', url: 'https://example.com/poster.jpg' },
                        { type: 'logo', url: 'https://example.com/logo.jpg' },
                    ],
                },
            ],
        };
        expect(extractImageUrl(container)).toBe('https://example.com/poster.jpg');
    });

    it('accepts primary image type', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 0,
                    images: [{ type: 'primary', url: 'https://example.com/primary.jpg' }],
                },
            ],
        };
        expect(extractImageUrl(container)).toBe('https://example.com/primary.jpg');
    });

    it('accepts profile image type', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 0,
                    images: [{ type: 'profile', url: 'https://example.com/profile.jpg' }],
                },
            ],
        };
        expect(extractImageUrl(container)).toBe('https://example.com/profile.jpg');
    });

    it('falls back to first image when no poster/primary/profile exists', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 0,
                    images: [{ type: 'backdrop', url: 'https://example.com/backdrop.jpg' }],
                },
            ],
        };
        expect(extractImageUrl(container)).toBe('https://example.com/backdrop.jpg');
    });

    it('respects contribution ordinal (lower ordinal wins)', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 1,
                    images: [{ type: 'poster', url: 'https://example.com/second.jpg' }],
                },
                {
                    ordinal: 0,
                    images: [{ type: 'poster', url: 'https://example.com/first.jpg' }],
                },
            ],
        };
        expect(extractImageUrl(container)).toBe('https://example.com/first.jpg');
    });

    it('skips contributions with empty images arrays', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                { ordinal: 0, images: [] },
                {
                    ordinal: 1,
                    images: [{ type: 'poster', url: 'https://example.com/poster.jpg' }],
                },
            ],
        };
        expect(extractImageUrl(container)).toBe('https://example.com/poster.jpg');
    });

    it('skips contributions with null images', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                { ordinal: 0, images: null },
                {
                    ordinal: 1,
                    images: [{ type: 'poster', url: 'https://example.com/poster.jpg' }],
                },
            ],
        };
        expect(extractImageUrl(container)).toBe('https://example.com/poster.jpg');
    });

    it('an operator-marked image wins over the type heuristic', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 0,
                    images: [
                        { type: 'poster', url: 'https://example.com/poster.jpg' },
                        { type: 'backdrop', url: 'https://example.com/marked.jpg', isPrimary: true },
                    ],
                },
            ],
        };
        expect(extractImageUrl(container)).toBe('https://example.com/marked.jpg');
    });

    it('is deterministic across repeated calls and respects image ordinal', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 0,
                    images: [
                        { type: 'poster', url: 'https://example.com/p2.jpg', ordinal: 2 },
                        { type: 'poster', url: 'https://example.com/p1.jpg', ordinal: 1 },
                    ],
                },
            ],
        };
        const first = extractImageUrl(container);
        expect(first).toBe('https://example.com/p1.jpg');
        for (let i = 0; i < 5; i++) {
            expect(extractImageUrl(container)).toBe(first);
        }
    });
});

describe('extractTitle', () => {
    it('returns undefined for null container', () => {
        expect(extractTitle(null)).toBeUndefined();
        expect(extractTitle(undefined)).toBeUndefined();
    });

    it('returns undefined for container with no contributions', () => {
        const container: MetadataContainer = { id: 'c1', contributions: [] };
        expect(extractTitle(container)).toBeUndefined();
    });

    it('returns title from first contribution by ordinal', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                { ordinal: 1, title: 'Second' },
                { ordinal: 0, title: 'First' },
            ],
        };
        expect(extractTitle(container)).toBe('First');
    });

    it('skips contributions with null title', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                { ordinal: 0, title: null },
                { ordinal: 1, title: 'Fallback Title' },
            ],
        };
        expect(extractTitle(container)).toBe('Fallback Title');
    });
});

describe('extractOverview', () => {
    it('returns undefined for null container', () => {
        expect(extractOverview(null)).toBeUndefined();
        expect(extractOverview(undefined)).toBeUndefined();
    });

    it('returns overview from first contribution by ordinal', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                { ordinal: 1, overview: 'Second overview' },
                { ordinal: 0, overview: 'First overview' },
            ],
        };
        expect(extractOverview(container)).toBe('First overview');
    });

    it('skips contributions with null overview', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                { ordinal: 0, overview: null },
                { ordinal: 1, overview: 'Fallback overview' },
            ],
        };
        expect(extractOverview(container)).toBe('Fallback overview');
    });

    it('collapses a trailing four-dot run into a single ellipsis', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [{ ordinal: 0, overview: 'A celebrated actor and director....' }],
        };
        expect(extractOverview(container)).toBe('A celebrated actor and director…');
    });

    it('normalizes a trailing three-dot ellipsis to a single ellipsis char', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [{ ordinal: 0, overview: 'The story continues...' }],
        };
        expect(extractOverview(container)).toBe('The story continues…');
    });

    it('leaves a normal sentence-ending period untouched', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [{ ordinal: 0, overview: 'A complete sentence.' }],
        };
        expect(extractOverview(container)).toBe('A complete sentence.');
    });
});

describe('extractYear', () => {
    it('returns undefined for null container', () => {
        expect(extractYear(null)).toBeUndefined();
        expect(extractYear(undefined)).toBeUndefined();
    });

    it('returns undefined for container with no contributions', () => {
        const container: MetadataContainer = { id: 'c1', contributions: [] };
        expect(extractYear(container)).toBeUndefined();
    });

    it('returns undefined for contributions with no publishDate', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [{ ordinal: 0, title: 'Test' }],
        };
        expect(extractYear(container)).toBeUndefined();
    });

    it('returns year from first contribution by ordinal', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                { ordinal: 1, publishDate: '2020-06-15T00:00:00Z' },
                { ordinal: 0, publishDate: '1999-03-31T00:00:00Z' },
            ],
        };
        expect(extractYear(container)).toBe(1999);
    });

    it('skips contributions with null publishDate', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                { ordinal: 0, publishDate: null },
                { ordinal: 1, publishDate: '2024-01-01T00:00:00Z' },
            ],
        };
        expect(extractYear(container)).toBe(2024);
    });
});

describe('extractExternalIds', () => {
    it('returns bare source and value identifiers with no url', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 0,
                    externalIds: [
                        { ordinal: 0, source: 'provider-h', value: 'tt0133093' },
                        { ordinal: 1, source: 'provider-a', value: '1399' },
                    ],
                },
            ],
        };
        const ids = extractExternalIds(container);
        expect(ids).toEqual([
            { source: 'provider-h', value: 'tt0133093' },
            { source: 'provider-a', value: '1399' },
        ]);
        expect(ids.every((id) => !('url' in id))).toBe(true);
    });

    it('dedupes ids by source and value across contributions', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 1,
                    externalIds: [{ ordinal: 0, source: 'provider-h', value: 'tt0133093' }],
                },
                {
                    ordinal: 0,
                    externalIds: [{ ordinal: 0, source: 'PROVIDER-H', value: 'tt0133093' }],
                },
            ],
        };
        const ids = extractExternalIds(container);
        expect(ids).toEqual([{ source: 'PROVIDER-H', value: 'tt0133093' }]);
    });

    it('excludes internal routing-helper sources while keeping real ids', () => {
        const container: MetadataContainer = {
            id: 'c1',
            contributions: [
                {
                    ordinal: 0,
                    externalIds: [
                        { ordinal: 0, source: 'provider-a.season', value: '144593' },
                        { ordinal: 1, source: 'internal.provider-a.series', value: '100088' },
                        { ordinal: 2, source: 'internal.provider-a.season-number', value: '1' },
                        { ordinal: 3, source: 'internal.provider-a.episode-number', value: '1' },
                    ],
                },
            ],
        };
        const ids = extractExternalIds(container);
        expect(ids.map((id) => id.source)).toEqual(['provider-a.season']);
        expect(ids.some((id) => id.source.startsWith('internal.'))).toBe(false);
    });
});

describe('extractReferences', () => {
    it('returns deduped references with a valid url across contributions, displayName falling back to url', () => {
        const container: MetadataContainer = {
            id: 'mc',
            contributions: [
                {
                    ordinal: 1,
                    references: [
                        {
                            ordinal: 1,
                            kind: 'database',
                            source: 'IMDb',
                            url: 'https://imdb.com/x',
                            displayName: 'IMDb',
                        },
                        { ordinal: 0, kind: 'homepage', source: 'Site', url: 'https://site.example' },
                    ],
                },
                {
                    ordinal: 0,
                    references: [
                        { ordinal: 0, kind: 'database', source: 'IMDb', url: 'https://IMDb.com/x' },
                        {
                            ordinal: 1,
                            kind: 'studio',
                            source: 'Studio',
                            url: 'https://studio.example',
                            displayName: 'Studio home',
                        },
                    ],
                },
            ],
        };

        const result = extractReferences(container);

        expect(result.map((r) => r.url)).toEqual([
            'https://IMDb.com/x',
            'https://studio.example',
            'https://site.example',
        ]);
        expect(result.find((r) => r.url === 'https://studio.example')?.displayName).toBe('Studio home');
        expect(result.find((r) => r.url === 'https://site.example')?.displayName).toBe('https://site.example');
        expect(result.find((r) => r.url === 'https://IMDb.com/x')?.displayName).toBe('https://IMDb.com/x');
    });

    it('drops references without a non-empty url and returns empty for no container', () => {
        const container: MetadataContainer = {
            id: 'mc',
            contributions: [
                {
                    ordinal: 0,
                    references: [
                        { ordinal: 0, kind: 'homepage', source: 'Site', url: '   ' },
                        { ordinal: 1, kind: 'homepage', source: 'Site', url: '' },
                    ],
                },
            ],
        };

        expect(extractReferences(container)).toEqual([]);
        expect(extractReferences(null)).toEqual([]);
        expect(extractReferences(undefined)).toEqual([]);
    });
});

describe('extractImages', () => {
    it('returns an empty array for an absent or empty container', () => {
        expect(extractImages(null)).toEqual([]);
        expect(extractImages(undefined)).toEqual([]);
        expect(extractImages({ id: 'mc', contributions: [] })).toEqual([]);
    });

    it('flattens images across ordinal-sorted contributions into the gallery shape', () => {
        const container: MetadataContainer = {
            id: 'mc',
            contributions: [
                {
                    ordinal: 1,
                    images: [{ type: 'backdrop', url: 'https://img/backdrop.jpg', width: 1920, height: 1080 }],
                },
                {
                    ordinal: 0,
                    images: [{ type: 'poster', url: 'https://img/poster.jpg', isPrimary: true }],
                },
            ],
        };

        const result = extractImages(container);

        expect(result).toEqual([
            { id: '0-https://img/poster.jpg', url: 'https://img/poster.jpg', type: 'poster', isPrimary: true },
            {
                id: '1-https://img/backdrop.jpg',
                url: 'https://img/backdrop.jpg',
                type: 'backdrop',
                isPrimary: undefined,
                width: 1920,
                height: 1080,
            },
        ]);
    });

    it('passes an unknown provider type (e.g. Provider A still) through verbatim', () => {
        const container: MetadataContainer = {
            id: 'mc',
            contributions: [{ ordinal: 0, images: [{ type: 'still', url: 'https://img/still.jpg' }] }],
        };

        expect(extractImages(container)[0]?.type).toBe('still');
    });
});

describe('extractCustomValues', () => {
    it('returns deduped key/value pairs in priority then source order', () => {
        const container: MetadataContainer = {
            id: 'mc',
            contributions: [
                {
                    ordinal: 0,
                    customMetadata: [
                        { key: 'Ethnicity', value: 'Caucasian' },
                        { key: 'Height', value: '170 cm' },
                    ],
                },
                {
                    ordinal: 1,
                    customMetadata: [
                        { key: 'Height', value: '168 cm' },
                        { key: 'Eye colour', value: 'Brown' },
                    ],
                },
            ],
        };

        expect(extractCustomValues(container)).toEqual([
            { key: 'Ethnicity', value: 'Caucasian' },
            { key: 'Height', value: '170 cm' },
            { key: 'Eye colour', value: 'Brown' },
        ]);
    });

    it('dedupes case-insensitively and keeps the highest-priority value', () => {
        const container: MetadataContainer = {
            id: 'mc',
            contributions: [
                { ordinal: 0, customMetadata: [{ key: 'height', value: 'first' }] },
                { ordinal: 1, customMetadata: [{ key: 'Height', value: 'second' }] },
            ],
        };

        expect(extractCustomValues(container)).toEqual([{ key: 'height', value: 'first' }]);
    });

    it('skips entries without a non-empty key or value', () => {
        const container: MetadataContainer = {
            id: 'mc',
            contributions: [
                {
                    ordinal: 0,
                    customMetadata: [
                        { key: 'Nationality', value: '' },
                        { key: '  ', value: 'orphan' },
                        { key: 'Weight', value: null },
                        { key: 'Hair colour', value: 'Black' },
                    ],
                },
            ],
        };

        expect(extractCustomValues(container)).toEqual([{ key: 'Hair colour', value: 'Black' }]);
    });

    it('returns an empty array when no container or custom values are present', () => {
        expect(extractCustomValues(undefined)).toEqual([]);
        expect(extractCustomValues({ contributions: [] })).toEqual([]);
    });
});
