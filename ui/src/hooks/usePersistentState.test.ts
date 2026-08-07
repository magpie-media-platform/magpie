import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { usePersistentState } from './usePersistentState';

describe('usePersistentState', () => {
    beforeEach(() => {
        localStorage.clear();
    });

    afterEach(() => {
        localStorage.clear();
    });

    it('returns the default when nothing is stored', () => {
        const { result } = renderHook(() => usePersistentState('k', false));
        expect(result.current[0]).toBe(false);
    });

    it('reads a previously stored value on mount', () => {
        localStorage.setItem('k', JSON.stringify(true));
        const { result } = renderHook(() => usePersistentState('k', false));
        expect(result.current[0]).toBe(true);
    });

    it('writes updates back to localStorage so they survive a remount', () => {
        const { result, unmount } = renderHook(() => usePersistentState('k', false));

        act(() => result.current[1](true));
        expect(result.current[0]).toBe(true);
        expect(JSON.parse(localStorage.getItem('k')!)).toBe(true);

        unmount();
        const { result: remounted } = renderHook(() => usePersistentState('k', false));
        expect(remounted.current[0]).toBe(true);
    });

    it('falls back to the default when the stored value is malformed', () => {
        localStorage.setItem('k', 'not-json');
        const { result } = renderHook(() => usePersistentState('k', 'fallback'));
        expect(result.current[0]).toBe('fallback');
    });
});
