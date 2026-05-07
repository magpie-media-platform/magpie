import { renderHook, act } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { useToggle } from './useToggle';

describe('useToggle', () => {
    it('defaults to false', () => {
        const { result } = renderHook(() => useToggle());
        expect(result.current[0]).toBe(false);
    });

    it('accepts a custom initial value', () => {
        const { result } = renderHook(() => useToggle(true));
        expect(result.current[0]).toBe(true);
    });

    it('toggle flips the value', () => {
        const { result } = renderHook(() => useToggle());
        act(() => result.current[1]());
        expect(result.current[0]).toBe(true);
        act(() => result.current[1]());
        expect(result.current[0]).toBe(false);
    });

    it('on sets value to true', () => {
        const { result } = renderHook(() => useToggle(false));
        act(() => result.current[2]());
        expect(result.current[0]).toBe(true);

        act(() => result.current[2]());
        expect(result.current[0]).toBe(true);
    });

    it('off sets value to false', () => {
        const { result } = renderHook(() => useToggle(true));
        act(() => result.current[3]());
        expect(result.current[0]).toBe(false);

        act(() => result.current[3]());
        expect(result.current[0]).toBe(false);
    });

    it('callbacks are referentially stable', () => {
        const { result, rerender } = renderHook(() => useToggle());
        const [, toggle1, on1, off1] = result.current;

        act(() => result.current[1]());
        rerender();

        const [, toggle2, on2, off2] = result.current;
        expect(toggle2).toBe(toggle1);
        expect(on2).toBe(on1);
        expect(off2).toBe(off1);
    });
});
