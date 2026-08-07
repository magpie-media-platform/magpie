import { renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { CARD_LAYOUT_QUERY, TABLE_LAYOUT_QUERY, useCardLayout } from './useCardLayout';

function installMatchMedia(width: number) {
    vi.stubGlobal(
        'matchMedia',
        vi.fn((query: string) => ({
            matches: query === CARD_LAYOUT_QUERY ? width <= 1023 : query === TABLE_LAYOUT_QUERY && width >= 1024,
            media: query,
            onchange: null,
            addListener: () => {},
            removeListener: () => {},
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => false,
        })),
    );
}

afterEach(() => {
    vi.unstubAllGlobals();
});

describe('useCardLayout', () => {
    it.each([768, 820, 900, 1023])('uses the compact card layout at %ipx', (width) => {
        installMatchMedia(width);

        const { result } = renderHook(() => useCardLayout());

        expect(result.current).toBe(true);
        expect(window.matchMedia).toHaveBeenCalledWith(CARD_LAYOUT_QUERY);
    });

    it('hands off to the table layout at 1024px', () => {
        installMatchMedia(1024);

        const { result } = renderHook(() => useCardLayout());

        expect(result.current).toBe(false);
        expect(window.matchMedia).toHaveBeenCalledWith(CARD_LAYOUT_QUERY);
    });
});
