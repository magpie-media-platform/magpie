import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';

import { useDebouncedValue } from './useDebouncedValue';

describe('useDebouncedValue', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });

    afterEach(() => {
        vi.useRealTimers();
        vi.restoreAllMocks();
    });

    it('returns the initial value immediately', () => {
        const { result } = renderHook(() => useDebouncedValue('initial', 300));

        expect(result.current).toBe('initial');
    });

    it('does not update the value before the delay', () => {
        const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 300), {
            initialProps: { value: 'initial' },
        });

        rerender({ value: 'updated' });

        act(() => {
            vi.advanceTimersByTime(200);
        });

        expect(result.current).toBe('initial');
    });

    it('updates the value after the delay', () => {
        const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 300), {
            initialProps: { value: 'initial' },
        });

        rerender({ value: 'updated' });

        act(() => {
            vi.advanceTimersByTime(300);
        });

        expect(result.current).toBe('updated');
    });

    it('resets the timer on rapid value changes', () => {
        const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 300), {
            initialProps: { value: 'initial' },
        });

        rerender({ value: 'first' });
        act(() => {
            vi.advanceTimersByTime(200);
        });

        rerender({ value: 'second' });
        act(() => {
            vi.advanceTimersByTime(200);
        });

        rerender({ value: 'third' });
        act(() => {
            vi.advanceTimersByTime(200);
        });

        expect(result.current).toBe('initial');

        act(() => {
            vi.advanceTimersByTime(100);
        });

        expect(result.current).toBe('third');
    });

    it('uses default delay of 300ms', () => {
        const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value), {
            initialProps: { value: 'initial' },
        });

        rerender({ value: 'updated' });

        act(() => {
            vi.advanceTimersByTime(299);
        });
        expect(result.current).toBe('initial');

        act(() => {
            vi.advanceTimersByTime(1);
        });
        expect(result.current).toBe('updated');
    });

    it('works with different value types', () => {
        const { result: numberResult, rerender: rerenderNumber } = renderHook(
            ({ value }) => useDebouncedValue(value, 100),
            { initialProps: { value: 42 } },
        );

        rerenderNumber({ value: 100 });
        act(() => {
            vi.advanceTimersByTime(100);
        });
        expect(numberResult.current).toBe(100);

        const { result: objectResult, rerender: rerenderObject } = renderHook(
            ({ value }) => useDebouncedValue(value, 100),
            { initialProps: { value: { a: 1 } as Record<string, number> } },
        );

        const newObject: Record<string, number> = { b: 2 };
        rerenderObject({ value: newObject });
        act(() => {
            vi.advanceTimersByTime(100);
        });
        expect(objectResult.current).toBe(newObject);
    });

    it('handles empty string values', () => {
        const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 100), {
            initialProps: { value: 'some text' },
        });

        rerender({ value: '' });
        act(() => {
            vi.advanceTimersByTime(100);
        });

        expect(result.current).toBe('');
    });

    it('fires onFlush on unmount with the latest pending value', () => {
        const onFlush = vi.fn();
        const { rerender, unmount } = renderHook(({ value }) => useDebouncedValue(value, 300, onFlush), {
            initialProps: { value: 'initial' },
        });

        rerender({ value: 'pending' });
        unmount();

        expect(onFlush).toHaveBeenCalledTimes(1);
        expect(onFlush).toHaveBeenCalledWith('pending');
    });

    it('does not fire onFlush on unmount when the timer already settled', () => {
        const onFlush = vi.fn();
        const { rerender, unmount } = renderHook(({ value }) => useDebouncedValue(value, 300, onFlush), {
            initialProps: { value: 'initial' },
        });

        rerender({ value: 'updated' });
        act(() => {
            vi.advanceTimersByTime(300);
        });
        unmount();

        expect(onFlush).not.toHaveBeenCalled();
    });
});
