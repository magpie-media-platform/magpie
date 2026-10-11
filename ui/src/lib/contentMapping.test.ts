import { describe, it, expect } from 'vitest';

import { extractContentFields, type ApiContentItemWithMetadata } from './contentMapping';

function makeItem(overrides: Partial<ApiContentItemWithMetadata> = {}): ApiContentItemWithMetadata {
    return {
        id: 'item-1',
        metadataContainerId: 'mc-1',
        contentType: 'episodes',
        level: 'series',
        slug: 'test-item',
        name: 'Test Item',
        monitoring: null,
        effectiveMonitoring: 'Monitored',
        createdAt: '2025-01-01T00:00:00Z',
        ...overrides,
    };
}

describe('extractContentFields', () => {
    it('returns basic fields from item with no metadata', () => {
        const result = extractContentFields(makeItem());

        expect(result.title).toBe('Test Item');
        expect(result.contentType).toBe('episodes');
        expect(result.level).toBe('series');
        expect(result.fulfillment).toBe('missing');
        expect(result.date).toBeUndefined();
        expect(result.posterUrl).toBeUndefined();
        expect(result.subtitle).toBeUndefined();
    });

    it('extracts title from metadata when available', () => {
        const result = extractContentFields(
            makeItem({
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [{ ordinal: 0, title: 'Metadata Title' }],
                },
            }),
        );

        expect(result.title).toBe('Metadata Title');
    });

    it('falls back to item name when metadata has no title', () => {
        const result = extractContentFields(
            makeItem({
                metadataContainer: { id: 'mc-1', contributions: [] },
            }),
        );

        expect(result.title).toBe('Test Item');
    });

    it('extracts publish date from metadata', () => {
        const result = extractContentFields(
            makeItem({
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [{ ordinal: 0, publishDate: '2025-06-15' }],
                },
            }),
        );

        expect(result.date).toBe('2025-06-15');
    });

    it('ExtractContentFields_CarriesThePublishDatePrecision_WhenTheMetadataHasOne', () => {
        // Act
        const result = extractContentFields(
            makeItem({
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [
                        { ordinal: 0, publishDate: '2025-06-15T00:00:00Z', publishDatePrecision: 'Instant' },
                    ],
                },
            }),
        );

        // Assert
        expect(result.datePrecision).toBe('Instant');
    });

    it('extracts poster URL from metadata', () => {
        const result = extractContentFields(
            makeItem({
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [
                        {
                            ordinal: 0,
                            images: [{ type: 'poster', url: 'https://example.com/poster.jpg' }],
                        },
                    ],
                },
            }),
        );

        expect(result.posterUrl).toBe('https://example.com/poster.jpg');
    });

    it('maps fulfillment status from contentItem', () => {
        const result = extractContentFields(
            makeItem({
                contentItem: {
                    id: 'ci-1',
                    contentHierarchyItemId: 'item-1',
                    fulfillment: 'FulfilledExternally',
                    createdAt: '2025-01-01T00:00:00Z',
                },
            }),
        );

        expect(result.fulfillment).toBe('fulfilledExternally');
    });

    it('sets subtitle for episode-level items when name differs from title', () => {
        const result = extractContentFields(
            makeItem({
                contentType: 'episodes',
                level: 'episode',
                name: 'S01E01 - Pilot',
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [{ ordinal: 0, title: 'Breaking Bad' }],
                },
            }),
        );

        expect(result.subtitle).toBe('S01E01 - Pilot');
    });

    it('does not set subtitle when name matches title', () => {
        const result = extractContentFields(
            makeItem({
                contentType: 'episodes',
                level: 'episode',
                name: 'Breaking Bad',
                metadataContainer: {
                    id: 'mc-1',
                    contributions: [{ ordinal: 0, title: 'Breaking Bad' }],
                },
            }),
        );

        expect(result.subtitle).toBeUndefined();
    });

    it('preserves content type slugs opaquely', () => {
        expect(extractContentFields(makeItem({ contentType: 'movies' })).contentType).toBe('movies');
        expect(extractContentFields(makeItem({ contentType: 'episodes' })).contentType).toBe('episodes');
        expect(extractContentFields(makeItem({ contentType: 'music' })).contentType).toBe('music');
        expect(extractContentFields(makeItem({ contentType: 'books' })).contentType).toBe('books');
    });
});
