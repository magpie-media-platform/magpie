import { describe, expect, it } from 'vitest';
import type { ApiIndexer } from '@/hooks/useIndexers';

import { availableQueryModes, fieldLabel } from './queryModes';

const indexer: ApiIndexer = {
    id: 'indexer-1',
    name: 'Archive',
    providerType: 'provider-a',
    availability: 'Public',
    connectionCount: 1,
    createdAt: '2024-01-01T00:00:00Z',
    supportsKeywordSearch: true,
    searchFacets: [['catalog-id'], ['edition']],
};

describe('queryModes', () => {
    it('returns the union of available modes when capabilities differ', () => {
        // Act
        const modes = availableQueryModes([
            indexer,
            { ...indexer, supportsKeywordSearch: false, searchFacets: [['catalogid'], ['volume', 'part']] },
        ]);

        // Assert
        expect(modes).toEqual([
            { key: 'catalog', fields: ['catalog'], label: 'Catalog' },
            { key: 'edition', fields: ['edition'], label: 'Edition' },
            { key: 'q', fields: ['q'], label: 'Keyword' },
            { key: 'part,volume', fields: ['part', 'volume'], label: 'Part + Volume' },
        ]);
    });

    it.each([{ indexers: [] }, { indexers: [{ ...indexer, supportsKeywordSearch: false, searchFacets: [] }] }])(
        'AvailableModes_ReturnsEmpty_WhenNoModesExist $indexers',
        ({ indexers }) => {
            // Act
            const modes = availableQueryModes(indexers);

            // Assert
            expect(modes).toEqual([]);
        },
    );

    it.each([
        ['q', 'Keyword'],
        ['edition', 'Edition'],
        ['volume', 'Volume'],
    ])('FieldLabel_UsesDisplayCase_WhenFieldProvided %s', (field, expected) => {
        // Act
        const label = fieldLabel(field);

        // Assert
        expect(label).toBe(expected);
    });
});
