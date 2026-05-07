import { describe, expect, it } from 'vitest';

import { queryClient, POLLING_INTERVALS } from './queryClient';

describe('queryClient', () => {
    it('exports a QueryClient instance', () => {
        expect(queryClient).toBeDefined();
        expect(queryClient.getDefaultOptions).toBeDefined();
    });

    it('configures default query options', () => {
        const defaults = queryClient.getDefaultOptions();
        const queries = defaults.queries;

        expect(queries).toBeDefined();
        expect(queries?.staleTime).toBeGreaterThan(0);
        expect(queries?.gcTime).toBeGreaterThan(0);
        expect(queries?.gcTime).toBeGreaterThan(queries?.staleTime ?? 0);
        expect(queries?.retry).toBeGreaterThanOrEqual(0);
        expect(queries?.refetchOnWindowFocus).toBe(true);
    });
});

describe('POLLING_INTERVALS', () => {
    it('defines all expected speed tiers', () => {
        expect(POLLING_INTERVALS).toHaveProperty('fast');
        expect(POLLING_INTERVALS).toHaveProperty('normal');
        expect(POLLING_INTERVALS).toHaveProperty('slow');
    });

    it('has intervals in ascending order', () => {
        expect(POLLING_INTERVALS.fast).toBeLessThan(POLLING_INTERVALS.normal);
        expect(POLLING_INTERVALS.normal).toBeLessThan(POLLING_INTERVALS.slow);
    });

    it('uses positive intervals', () => {
        expect(POLLING_INTERVALS.fast).toBeGreaterThan(0);
        expect(POLLING_INTERVALS.normal).toBeGreaterThan(0);
        expect(POLLING_INTERVALS.slow).toBeGreaterThan(0);
    });
});
