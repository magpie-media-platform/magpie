import { useEffect } from 'react';

export function OverlayBodyGuard(): null {
    useEffect(() => {
        return () => {
            queueMicrotask(() => {
                const { body } = document;
                if (body.style.pointerEvents !== 'none') {
                    return;
                }
                if (document.querySelector('[data-overlay-content]')) {
                    return;
                }
                body.style.pointerEvents = '';
            });
        };
    }, []);

    return null;
}
