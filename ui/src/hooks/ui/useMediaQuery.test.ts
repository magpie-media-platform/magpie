import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { useMediaQuery } from './useMediaQuery';

interface MockMql {
    matches: boolean;
    listeners: Set<(e: MediaQueryListEvent) => void>;
}

function installMatchMedia(initialMatches: boolean) {
    const mql: MockMql = { matches: initialMatches, listeners: new Set() };
    const matchMedia = vi.fn((query: string) => ({
        matches: mql.matches,
        media: query,
        onchange: null,
        addListener: () => {},
        removeListener: () => {},
        addEventListener: (_: string, cb: (e: MediaQueryListEvent) => void) => mql.listeners.add(cb),
        removeEventListener: (_: string, cb: (e: MediaQueryListEvent) => void) => mql.listeners.delete(cb),
        dispatchEvent: () => false,
    }));
    Object.defineProperty(window, 'matchMedia', { writable: true, configurable: true, value: matchMedia });
    return mql;
}

afterEach(() => {
    vi.restoreAllMocks();
});

describe('useMediaQuery', () => {
    it('returns the initial match state for the query', () => {
        installMatchMedia(true);
        const { result } = renderHook(() => useMediaQuery('(min-width: 768px) and (max-width: 1023px)'));
        expect(result.current).toBe(true);
    });

    it('returns false when the query does not match', () => {
        installMatchMedia(false);
        const { result } = renderHook(() => useMediaQuery('(min-width: 768px) and (max-width: 1023px)'));
        expect(result.current).toBe(false);
    });

    it('updates when the media query change event fires', () => {
        const mql = installMatchMedia(false);
        const { result } = renderHook(() => useMediaQuery('(min-width: 768px) and (max-width: 1023px)'));
        expect(result.current).toBe(false);

        act(() => {
            mql.matches = true;
            mql.listeners.forEach((cb) => cb({ matches: true } as MediaQueryListEvent));
        });

        expect(result.current).toBe(true);
    });
});
