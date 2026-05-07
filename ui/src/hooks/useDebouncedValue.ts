import { useState, useEffect, useRef } from 'react';

/**
 * Returns a debounced copy of `value`, updating `delay` ms after `value` stops changing.
 *
 * `onFlush`, if provided, fires once on unmount with the latest value when a debounce
 * timer was still pending. Use it for "debounce-then-submit" patterns where dropping
 * the trailing value would silently lose user input.
 */
export function useDebouncedValue<T>(value: T, delay = 300, onFlush?: (value: T) => void): T {
    const [debouncedValue, setDebouncedValue] = useState<T>(value);

    const latestValueRef = useRef(value);
    latestValueRef.current = value;
    const onFlushRef = useRef(onFlush);
    onFlushRef.current = onFlush;

    const pendingRef = useRef(false);

    useEffect(() => {
        pendingRef.current = true;
        const timer = setTimeout(() => {
            setDebouncedValue(value);
            pendingRef.current = false;
        }, delay);

        return () => {
            clearTimeout(timer);
        };
    }, [value, delay]);

    useEffect(() => {
        return () => {
            if (pendingRef.current && onFlushRef.current) {
                onFlushRef.current(latestValueRef.current);
            }
        };
    }, []);

    return debouncedValue;
}
