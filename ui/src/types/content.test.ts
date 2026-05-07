import { describe, expect, it } from 'vitest';

import {
    mapMonitoringIntent,
    mapMonitoringStatusToApi,
    mapFulfillmentStatus,
    mapFulfillmentStatusToApi,
    mapContentType,
    mapContentKindToApi,
} from './content';

describe('mapMonitoringIntent', () => {
    it('maps None (0) to none', () => {
        expect(mapMonitoringIntent(0)).toBe('none');
    });

    it('maps Active (1) to active', () => {
        expect(mapMonitoringIntent(1)).toBe('active');
    });

    it('maps Excluded (2) to excluded', () => {
        expect(mapMonitoringIntent(2)).toBe('excluded');
    });
});

describe('mapMonitoringStatusToApi', () => {
    it('maps none to 0', () => {
        expect(mapMonitoringStatusToApi('none')).toBe(0);
    });

    it('maps active to 1', () => {
        expect(mapMonitoringStatusToApi('active')).toBe(1);
    });

    it('maps excluded to 2', () => {
        expect(mapMonitoringStatusToApi('excluded')).toBe(2);
    });
});

describe('mapFulfillmentStatus', () => {
    it('maps 0 to missing', () => {
        expect(mapFulfillmentStatus(0)).toBe('missing');
    });

    it('maps 1 to upgradable', () => {
        expect(mapFulfillmentStatus(1)).toBe('upgradable');
    });

    it('maps 2 to fulfilled', () => {
        expect(mapFulfillmentStatus(2)).toBe('fulfilled');
    });

    it('maps 3 to fulfilledExternally', () => {
        expect(mapFulfillmentStatus(3)).toBe('fulfilledExternally');
    });
});

describe('mapFulfillmentStatusToApi', () => {
    it('maps missing to 0', () => {
        expect(mapFulfillmentStatusToApi('missing')).toBe(0);
    });

    it('maps upgradable to 1', () => {
        expect(mapFulfillmentStatusToApi('upgradable')).toBe(1);
    });

    it('maps fulfilled to 2', () => {
        expect(mapFulfillmentStatusToApi('fulfilled')).toBe(2);
    });

    it('maps fulfilledExternally to 3', () => {
        expect(mapFulfillmentStatusToApi('fulfilledExternally')).toBe(3);
    });
});

describe('mapContentType', () => {
    it('maps episodes to episode', () => {
        expect(mapContentType('episodes')).toBe('episode');
    });

    it('maps movies to movie', () => {
        expect(mapContentType('movies')).toBe('movie');
    });

    it('maps music to music', () => {
        expect(mapContentType('music')).toBe('music');
    });

    it('maps books to book', () => {
        expect(mapContentType('books')).toBe('book');
    });

    it('returns unknown for unrecognized types', () => {
        expect(mapContentType('podcasts')).toBe('unknown');
    });

    it('handles case insensitivity', () => {
        expect(mapContentType('Episodes')).toBe('episode');
    });
});

describe('mapContentKindToApi', () => {
    it('maps episode to episodes', () => {
        expect(mapContentKindToApi('episode')).toBe('episodes');
    });

    it('maps movie to movies', () => {
        expect(mapContentKindToApi('movie')).toBe('movies');
    });

    it('maps music to music', () => {
        expect(mapContentKindToApi('music')).toBe('music');
    });

    it('maps book to books', () => {
        expect(mapContentKindToApi('book')).toBe('books');
    });

    it('maps unknown to undefined', () => {
        expect(mapContentKindToApi('unknown')).toBeUndefined();
    });
});
