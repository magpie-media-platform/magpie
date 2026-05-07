import { describe, it, expect } from 'vitest';

import { extractImageUrl, extractTitle, extractOverview, extractYear, type MetadataContainer } from './metadata';

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
