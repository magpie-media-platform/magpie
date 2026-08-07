import { describe, expect, it } from 'vitest';

import {
    formatLevelLabel,
    getFulfillmentBadge,
    mapFulfillmentStatus,
    mapToContentDetailItem,
    type ApiContentHierarchyItem,
} from './content';

describe('mapFulfillmentStatus', () => {
    it('maps Missing to missing', () => {
        expect(mapFulfillmentStatus('Missing')).toBe('missing');
    });

    it('maps Upgradable to upgradable', () => {
        expect(mapFulfillmentStatus('Upgradable')).toBe('upgradable');
    });

    it('maps Fulfilled to fulfilled', () => {
        expect(mapFulfillmentStatus('Fulfilled')).toBe('fulfilled');
    });

    it('maps FulfilledExternally to fulfilledExternally', () => {
        expect(mapFulfillmentStatus('FulfilledExternally')).toBe('fulfilledExternally');
    });

    it('maps Partial to partial', () => {
        expect(mapFulfillmentStatus('Partial')).toBe('partial');
    });
});

describe('getFulfillmentBadge', () => {
    it('maps each status to its variant and label', () => {
        expect(getFulfillmentBadge('missing')).toEqual({ variant: 'missing', label: 'Missing' });
        expect(getFulfillmentBadge('upgradable')).toEqual({ variant: 'upgradable', label: 'Upgradable' });
        expect(getFulfillmentBadge('fulfilled')).toEqual({ variant: 'fulfilled', label: 'Fulfilled' });
        expect(getFulfillmentBadge('fulfilledExternally')).toEqual({
            variant: 'fulfilledExternally',
            label: 'External',
        });
        expect(getFulfillmentBadge('partial')).toEqual({ variant: 'partial', label: 'Partial' });
    });
});

describe('formatLevelLabel', () => {
    it('labels a TV show by its level, not the collapsed content type', () => {
        expect(formatLevelLabel('show')).toBe('Show');
    });

    it('labels a season and an episode distinctly', () => {
        expect(formatLevelLabel('season')).toBe('Season');
        expect(formatLevelLabel('episode')).toBe('Episode');
    });

    it('labels a movie level', () => {
        expect(formatLevelLabel('movie')).toBe('Movie');
    });

    it('capitalises arbitrary level slugs (music, books)', () => {
        expect(formatLevelLabel('artist')).toBe('Artist');
        expect(formatLevelLabel('album')).toBe('Album');
        expect(formatLevelLabel('book')).toBe('Book');
    });

    it('returns empty when the level is blank', () => {
        expect(formatLevelLabel('')).toBe('');
        expect(formatLevelLabel(undefined)).toBe('');
    });
});

function makeItem(overrides: Partial<ApiContentHierarchyItem> = {}): ApiContentHierarchyItem {
    return {
        id: 'content-1',
        metadataContainerId: 'metadata-1',
        contentType: 'movie',
        level: 'movie',
        slug: 'test-movie',
        name: 'Test Movie',
        monitoring: null,
        effectiveMonitoring: true,
        createdAt: '2026-01-01T00:00:00Z',
        ...overrides,
    };
}

describe('mapToContentDetailItem', () => {
    it('maps the materialized publish date', () => {
        const result = mapToContentDetailItem(
            makeItem({
                metadataContainer: {
                    id: 'metadata-1',
                    contributions: [{ ordinal: 0, publishDate: '2025-08-04T00:00:00Z' }],
                },
            }),
        );

        expect(result.publishDate).toBe('2025-08-04T00:00:00Z');
    });

    it('omits the publish date when metadata does not provide one', () => {
        const result = mapToContentDetailItem(makeItem());

        expect(result.publishDate).toBeUndefined();
    });

    it('maps previews from metadata contributions', () => {
        const result = mapToContentDetailItem(
            makeItem({
                metadataContainer: {
                    id: 'metadata-1',
                    contributions: [
                        {
                            ordinal: 0,
                            previews: [{ ordinal: 0, kind: 'trailer', url: 'https://example.com/trailer' }],
                        },
                    ],
                },
            }),
        );

        expect(result.previews).toEqual([{ kind: 'trailer', url: 'https://example.com/trailer' }]);
    });
});
