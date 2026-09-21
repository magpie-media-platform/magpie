import { useCallback, useEffect, useRef } from 'react';
import type { PointerEvent as ReactPointerEvent } from 'react';

interface UseLongPressOptions {
    onLongPress: () => void;

    delayMs?: number;

    moveTolerancePx?: number;
}

interface LongPressHandlers {
    onPointerDown: (event: ReactPointerEvent) => void;
    onPointerMove: (event: ReactPointerEvent) => void;
    onPointerUp: () => void;
    onPointerLeave: () => void;
    onPointerCancel: () => void;
}

interface UseLongPressResult {
    handlers: LongPressHandlers;

    /** Suppresses the synthetic click and returns true when a long press just fired; call it from the click handler. */
    consumeClick: (event: { preventDefault: () => void; stopPropagation: () => void }) => boolean;
}

export function useLongPress({
    onLongPress,
    delayMs = 450,
    moveTolerancePx = 10,
}: UseLongPressOptions): UseLongPressResult {
    const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const firedRef = useRef(false);
    const startRef = useRef<{ x: number; y: number } | null>(null);

    const clear = useCallback(() => {
        if (timerRef.current !== null) {
            clearTimeout(timerRef.current);
            timerRef.current = null;
        }
        startRef.current = null;
    }, []);

    useEffect(() => clear, [clear]);

    const onPointerDown = useCallback(
        (event: ReactPointerEvent) => {
            if (event.pointerType === 'mouse' && event.button !== 0) {
                return;
            }
            firedRef.current = false;
            startRef.current = { x: event.clientX, y: event.clientY };
            timerRef.current = setTimeout(() => {
                firedRef.current = true;
                timerRef.current = null;
                onLongPress();
            }, delayMs);
        },
        [delayMs, onLongPress],
    );

    const onPointerMove = useCallback(
        (event: ReactPointerEvent) => {
            const start = startRef.current;
            if (!start) {
                return;
            }
            if (
                Math.abs(event.clientX - start.x) > moveTolerancePx ||
                Math.abs(event.clientY - start.y) > moveTolerancePx
            ) {
                clear();
            }
        },
        [clear, moveTolerancePx],
    );

    const consumeClick = useCallback((event: { preventDefault: () => void; stopPropagation: () => void }) => {
        if (firedRef.current) {
            firedRef.current = false;
            event.preventDefault();
            event.stopPropagation();
            return true;
        }
        return false;
    }, []);

    return {
        handlers: { onPointerDown, onPointerMove, onPointerUp: clear, onPointerLeave: clear, onPointerCancel: clear },
        consumeClick,
    };
}
