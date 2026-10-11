import { useContext, useEffect } from 'react';

import { ToastContext } from '@/hooks/ui/useToast';

/**
 * Dismisses the toasts that are on screen when the surrounding overlay opens.
 */
export function DismissToastsOnOpen(): null {
    const dismissAll = useContext(ToastContext)?.dismissAll;

    useEffect(() => {
        dismissAll?.();
    }, [dismissAll]);

    return null;
}
