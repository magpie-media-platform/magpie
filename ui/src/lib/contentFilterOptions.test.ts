import { describe, expect, it } from 'vitest';

import type { LibraryTypeDefinition } from '@/types/library';

import {
    buildContentFilterOptions,
    contentFilterSelectionValue,
    parseContentFilterSelectionValue,
} from './contentFilterOptions';

describe('contentFilterOptions', () => {
    it('builds flat content type and root-level options in registry order', () => {
        const contentTypes: LibraryTypeDefinition[] = [
            {
                id: 'kind-x',
                name: 'Kind-X',
                pluralName: 'Kind-X',
                slug: 'kind-x',
                levels: [
                    { slug: 'kind-x-scene', displayName: 'Scene', parentSlug: null, standardFactFields: [] },
                    {
                        slug: 'kind-x-scene-part',
                        displayName: 'Scene part',
                        parentSlug: 'kind-x-scene',
                        standardFactFields: [],
                    },
                    { slug: 'kind-x-movie', displayName: 'Movie', parentSlug: null, standardFactFields: [] },
                ],
            },
            {
                id: 'books',
                name: 'Books',
                pluralName: 'Books',
                slug: 'books',
                levels: [
                    { slug: 'E-book_V1', displayName: 'E-book', parentSlug: null, standardFactFields: [] },
                    { slug: 'audio.book', displayName: 'Audiobook', parentSlug: null, standardFactFields: [] },
                ],
            },
        ];

        const options = buildContentFilterOptions(contentTypes);

        expect(options.map(({ label, contentType, level }) => ({ label, contentType, level }))).toEqual([
            { label: 'Kind-X', contentType: 'kind-x', level: undefined },
            { label: 'Kind-X > Scene', contentType: 'kind-x', level: 'kind-x-scene' },
            { label: 'Kind-X > Movie', contentType: 'kind-x', level: 'kind-x-movie' },
            { label: 'Books', contentType: 'books', level: undefined },
            { label: 'Books > E-book', contentType: 'books', level: 'E-book_V1' },
            { label: 'Books > Audiobook', contentType: 'books', level: 'audio.book' },
        ]);
        expect(options.some((option) => option.label.includes('Scene part'))).toBe(false);
    });

    it('omits the redundant root option when a content type has a single root', () => {
        const contentTypes: LibraryTypeDefinition[] = [
            {
                id: 'movie',
                name: 'Movie',
                pluralName: 'Movies',
                slug: 'movie',
                levels: [
                    { slug: 'movie', displayName: 'Movie', parentSlug: null, standardFactFields: [] },
                    { slug: 'movie-part', displayName: 'Movie part', parentSlug: 'movie', standardFactFields: [] },
                ],
            },
        ];

        const options = buildContentFilterOptions(contentTypes);

        expect(options.map(({ label, contentType, level }) => ({ label, contentType, level }))).toEqual([
            { label: 'Movie', contentType: 'movie', level: undefined },
        ]);
    });

    it('restricts options to a single content type and its roots when a restrict slug is given', () => {
        const contentTypes: LibraryTypeDefinition[] = [
            {
                id: 'kind-x',
                name: 'Kind-X',
                pluralName: 'Kind-X',
                slug: 'kind-x',
                levels: [
                    { slug: 'kind-x-scene', displayName: 'Scene', parentSlug: null, standardFactFields: [] },
                    { slug: 'kind-x-movie', displayName: 'Movie', parentSlug: null, standardFactFields: [] },
                ],
            },
            {
                id: 'movie',
                name: 'Movie',
                pluralName: 'Movies',
                slug: 'movie',
                levels: [{ slug: 'movie', displayName: 'Movie', parentSlug: null, standardFactFields: [] }],
            },
        ];

        const options = buildContentFilterOptions(contentTypes, 'kind-x');

        expect(options.map(({ label, contentType, level }) => ({ label, contentType, level }))).toEqual([
            { label: 'Kind-X', contentType: 'kind-x', level: undefined },
            { label: 'Kind-X > Scene', contentType: 'kind-x', level: 'kind-x-scene' },
            { label: 'Kind-X > Movie', contentType: 'kind-x', level: 'kind-x-movie' },
        ]);
    });

    it('restricts a single-root content type to just the bare type option', () => {
        const contentTypes: LibraryTypeDefinition[] = [
            {
                id: 'movie',
                name: 'Movie',
                pluralName: 'Movies',
                slug: 'movie',
                levels: [{ slug: 'movie', displayName: 'Movie', parentSlug: null, standardFactFields: [] }],
            },
            {
                id: 'books',
                name: 'Books',
                pluralName: 'Books',
                slug: 'books',
                levels: [
                    { slug: 'E-book_V1', displayName: 'E-book', parentSlug: null, standardFactFields: [] },
                    { slug: 'audio.book', displayName: 'Audiobook', parentSlug: null, standardFactFields: [] },
                ],
            },
        ];

        const options = buildContentFilterOptions(contentTypes, 'movie');

        expect(options.map(({ label, contentType, level }) => ({ label, contentType, level }))).toEqual([
            { label: 'Movie', contentType: 'movie', level: undefined },
        ]);
    });

    it('round-trips exact registered slugs through an option value', () => {
        const selection = { contentType: 'vendor.books', level: 'E-book_V1' };

        expect(parseContentFilterSelectionValue(contentFilterSelectionValue(selection))).toEqual(selection);
    });

    it('includes registered child levels when a local list can show descendants', () => {
        const options = buildContentFilterOptions(
            [
                {
                    id: 'tv',
                    name: 'TV',
                    pluralName: 'TV',
                    slug: 'tv',
                    levels: [
                        { slug: 'show', displayName: 'Show', parentSlug: null, standardFactFields: [] },
                        { slug: 'season', displayName: 'Season', parentSlug: 'show', standardFactFields: [] },
                    ],
                },
            ],
            undefined,
            true,
        );
        expect(options.map(({ label, level }) => ({ label, level }))).toEqual([
            { label: 'TV', level: undefined },
            { label: 'TV > Show', level: 'show' },
            { label: 'TV > Season', level: 'season' },
        ]);
    });
});
