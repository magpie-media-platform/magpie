import { describe, it, expect, beforeEach, vi } from 'vitest';

import { reloadForPreloadError } from './preloadErrorReload';

const GUARD_KEY = 'magpie:reloaded-for-preload-error';

describe('reloadForPreloadError', () => {
    let reload: ReturnType<typeof vi.fn>;

    beforeEach(() => {
        sessionStorage.clear();
        reload = vi.fn();
        Object.defineProperty(window, 'location', {
            value: { reload },
            writable: true,
            configurable: true,
        });
    });

    it('reloads once and sets the guard on first call', () => {
        const result = reloadForPreloadError(1_000);

        expect(result).toBe(true);
        expect(reload).toHaveBeenCalledTimes(1);
        expect(sessionStorage.getItem(GUARD_KEY)).toBe('1000');
    });

    it('does not reload again while the guard is fresh', () => {
        reloadForPreloadError(1_000);
        reload.mockClear();

        const result = reloadForPreloadError(5_000);

        expect(result).toBe(false);
        expect(reload).not.toHaveBeenCalled();
        expect(sessionStorage.getItem(GUARD_KEY)).toBe('1000');
    });

    it('reloads again once the guard has expired (later genuine redeploy)', () => {
        reloadForPreloadError(1_000);
        reload.mockClear();

        const result = reloadForPreloadError(1_000 + 10_000);

        expect(result).toBe(true);
        expect(reload).toHaveBeenCalledTimes(1);
        expect(sessionStorage.getItem(GUARD_KEY)).toBe(String(1_000 + 10_000));
    });

    it('reloads when the guard value is non-numeric garbage', () => {
        sessionStorage.setItem(GUARD_KEY, 'not-a-number');

        const result = reloadForPreloadError(2_000);

        expect(result).toBe(true);
        expect(reload).toHaveBeenCalledTimes(1);
    });
});
