import { useRef } from 'react';

/** Runs a menu action after the menu closes. */
export function useMenuAction() {
    const triggerRef = useRef<HTMLButtonElement>(null);
    const pendingRef = useRef<(() => void) | null>(null);
    return {
        triggerRef,
        schedule: (action: () => void) => () => {
            pendingRef.current = action;
        },
        onCloseAutoFocus: (event: Event) => {
            const action = pendingRef.current;
            pendingRef.current = null;
            if (action) {
                event.preventDefault();
                triggerRef.current?.focus();
                action();
            }
        },
    };
}
