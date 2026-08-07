import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { useReleaseGrabGuard } from './useReleaseGrabGuard';

describe('useReleaseGrabGuard', () => {
    it('allows only one synchronous submission for the same release', () => {
        const { result } = renderHook(() => useReleaseGrabGuard());
        let firstAttempt = false;
        let secondAttempt = false;

        act(() => {
            firstAttempt = result.current.tryBeginGrab('release-1');
            secondAttempt = result.current.tryBeginGrab('release-1');
        });

        expect(firstAttempt).toBe(true);
        expect(secondAttempt).toBe(false);
        expect(result.current.pendingReleaseIds).toEqual(new Set(['release-1']));
    });

    it('keeps a successful release locked and restores a failed release', () => {
        const { result } = renderHook(() => useReleaseGrabGuard());

        act(() => {
            result.current.tryBeginGrab('release-1');
            result.current.tryBeginGrab('release-2');
            result.current.markGrabSucceeded('release-1');
            result.current.markGrabFailed('release-2');
        });

        expect(result.current.pendingReleaseIds).toEqual(new Set());
        expect(result.current.succeededReleaseIds).toEqual(new Set(['release-1']));

        act(() => {
            expect(result.current.tryBeginGrab('release-1')).toBe(false);
            expect(result.current.tryBeginGrab('release-2')).toBe(true);
        });
    });
});
