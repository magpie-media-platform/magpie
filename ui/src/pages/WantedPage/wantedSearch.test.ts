import { describe, expect, it, vi } from 'vitest';

import type { ContentListItem } from '@/types';

import { countMissingDescendants, resolveWantedSearchLeaves } from './wantedSearch';

function item(id: string, overrides: Partial<ContentListItem> = {}): ContentListItem {
    return {
        id,
        title: id,
        level: 'episode',
        levelLabel: 'Episode',
        contentType: 'shows',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'missing',
        ...overrides,
    };
}

describe('resolveWantedSearchLeaves', () => {
    it('expands a parent to its leaves and skips active acquisitions', async () => {
        const show = item('show', { level: 'show', levelLabel: 'Show', fulfillment: 'partial' });
        const season = item('season', { level: 'season', levelLabel: 'Season', fulfillment: 'partial' });
        const episode = item('episode');
        const activeEpisode = item('active-episode', { acquisitionStatus: 'downloading' });
        const loadChildren = vi.fn(async (id: string) => {
            if (id === 'show') {
                return [season];
            }
            if (id === 'season') {
                return [episode, activeEpisode];
            }
            return [];
        });

        const result = await resolveWantedSearchLeaves(
            [show],
            (candidate) => candidate.level === 'show' || candidate.level === 'season',
            loadChildren,
        );

        expect(result).toEqual({ leafIds: ['episode'], skippedActiveCount: 1 });
        expect(loadChildren).toHaveBeenCalledTimes(2);
    });

    it('deduplicates a parent and separately selected child before dispatching leaves', async () => {
        const parent = item('parent', { level: 'show', fulfillment: 'partial' });
        const child = item('child');
        const loadChildren = vi.fn(async () => [child]);

        const result = await resolveWantedSearchLeaves(
            [parent, child],
            (candidate) => candidate.level === 'show',
            loadChildren,
        );

        expect(result).toEqual({ leafIds: ['child'], skippedActiveCount: 0 });
    });

    it('keeps leaf upgrade searches while excluding non-actionable fulfillment states', async () => {
        const upgradable = item('upgrade', { fulfillment: 'upgradable' });
        const fulfilled = item('fulfilled', { fulfillment: 'fulfilled' });
        const partial = item('partial', { fulfillment: 'partial' });

        const result = await resolveWantedSearchLeaves(
            [upgradable, fulfilled, partial],
            () => false,
            async () => [],
        );

        expect(result).toEqual({ leafIds: ['upgrade'], skippedActiveCount: 0 });
    });
});

describe('countMissingDescendants', () => {
    it('counts monitored missing leaves under nested containers', async () => {
        const show = item('show', { level: 'show', fulfillment: 'partial' });
        const season = item('season', { level: 'season', fulfillment: 'partial' });
        const missing = item('missing');
        const unmonitored = item('unmonitored', { effectiveMonitoring: false });
        const upgradable = item('upgradable', { fulfillment: 'upgradable' });
        const loadChildren = vi.fn(async (id: string) => {
            if (id === 'show') {
                return [season];
            }
            if (id === 'season') {
                return [missing, unmonitored, upgradable];
            }
            return [];
        });

        const result = await countMissingDescendants(
            show,
            (candidate) => candidate.level === 'show' || candidate.level === 'season',
            loadChildren,
        );

        expect(result).toBe(1);
    });
});
