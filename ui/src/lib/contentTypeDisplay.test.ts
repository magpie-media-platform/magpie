import { describe, expect, it } from 'vitest';

import type { LibraryTypeDefinition } from '@/hooks/useLibraryTypes';

import { humanizeCustomKey, resolveContentTypeLabel } from './contentTypeDisplay';

const registry: LibraryTypeDefinition[] = [
    { id: 'movie', name: 'Movies', pluralName: 'Movies', slug: 'movie', iconName: 'film-slate', levels: [] },
    { id: 'tv', name: 'TV', pluralName: 'TV', slug: 'tv', iconName: 'television', levels: [] },
];

describe('resolveContentTypeLabel', () => {
    it('returns an empty string for an absent slug', () => {
        expect(resolveContentTypeLabel({}, registry)).toBe('');
        expect(resolveContentTypeLabel({ contentType: '  ' }, registry)).toBe('');
    });

    it('labels a stored undeclared level as needing classification', () => {
        expect(
            resolveContentTypeLabel({ contentType: 'books', level: 'legacy', levelLabel: 'Book' }, [
                {
                    id: 'books',
                    name: 'Books',
                    pluralName: 'Books',
                    slug: 'books',
                    levels: [
                        {
                            slug: 'ebook',
                            displayName: 'E-book',
                            parentSlug: null,
                            standardFactFields: [],
                        },
                    ],
                },
            ]),
        ).toBe('Needs classification');
    });
});

describe('humanizeCustomKey', () => {
    it('splits camelCase/PascalCase into title-cased words', () => {
        expect(humanizeCustomKey('ShoeSize')).toBe('Shoe Size');
        expect(humanizeCustomKey('eyeColour')).toBe('Eye Colour');
        expect(humanizeCustomKey('HairColour')).toBe('Hair Colour');
    });

    it('splits kebab and snake keys', () => {
        expect(humanizeCustomKey('eye-colour')).toBe('Eye Colour');
        expect(humanizeCustomKey('hair_colour')).toBe('Hair Colour');
    });

    it('title-cases each word generically', () => {
        expect(humanizeCustomKey('tv')).toBe('Tv');
        expect(humanizeCustomKey('catalog-code')).toBe('Catalog Code');
    });

    it('returns an empty string for blank input', () => {
        expect(humanizeCustomKey('')).toBe('');
        expect(humanizeCustomKey('   ')).toBe('');
        expect(humanizeCustomKey(undefined)).toBe('');
    });
});
