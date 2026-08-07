import { useState, useEffect, useRef } from 'react';

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
