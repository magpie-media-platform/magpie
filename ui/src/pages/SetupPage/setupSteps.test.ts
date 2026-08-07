import { describe, expect, it } from 'vitest';

import type { SetupStatus } from '@/lib/setupApi';

import { SETUP_STEP_ORDER, nextStepAfter, pickInitialStep } from './setupSteps';

const SETUP_STATUS: SetupStatus = {
    isRequired: true,
    isComplete: false,
    requiresAdminClaim: true,
};

describe('setupSteps order', () => {
    it('places the root-folder step before the media-server step', () => {
        const rootIndex = SETUP_STEP_ORDER.indexOf('root');
        const serverIndex = SETUP_STEP_ORDER.indexOf('server');

        expect(rootIndex).toBeGreaterThanOrEqual(0);
        expect(serverIndex).toBeGreaterThanOrEqual(0);
        expect(rootIndex).toBeLessThan(serverIndex);
    });

    it('walks download -> root -> server -> done', () => {
        const availability = { includeMetadata: true };

        expect(nextStepAfter('download', availability)).toBe('root');
        expect(nextStepAfter('root', availability)).toBe('server');
        expect(nextStepAfter('server', availability)).toBe('done');
        expect(nextStepAfter('done', availability)).toBeNull();
    });
});

describe('setupSteps metadata availability', () => {
    it('includes the metadata step when an extension settings group exists', () => {
        expect(nextStepAfter('admin', { includeMetadata: true })).toBe('metadata');
    });

    it('drops the metadata step when no extension settings group exists', () => {
        expect(nextStepAfter('admin', { includeMetadata: false })).toBe('indexer');
    });

    it('defaults to including the metadata step when availability is omitted', () => {
        expect(nextStepAfter('admin')).toBe('metadata');
    });
});

describe('pickInitialStep', () => {
    it('always starts the walk at the database step', () => {
        expect(pickInitialStep(SETUP_STATUS)).toBe('db');
    });
});
