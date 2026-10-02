import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

import { useMediaQuery } from './ui/useMediaQuery';

const SCROLL_STORAGE_PREFIX = 'magpie_scroll_';

export function useScrollRestoration() {
    const { pathname } = useLocation();
    const mainOwnsScroll = useMediaQuery('(max-width: 1023px)');

    useEffect(() => {
        const scroller = document.querySelector<HTMLElement>(
            mainOwnsScroll ? '[data-component="app-shell-main"]' : '[data-component="app-shell-main-wrapper"]',
        );
        if (!scroller) {
            return;
        }

        let restoreFrame: number | null = null;
        const saved = sessionStorage.getItem(SCROLL_STORAGE_PREFIX + pathname);
        if (saved != null) {
            restoreFrame = requestAnimationFrame(() => {
                scroller.scrollTop = parseInt(saved, 10);
                restoreFrame = null;
            });
        } else {
            scroller.scrollTop = 0;
        }

        let saveFrame: number | null = null;
        const onScroll = () => {
            if (saveFrame === null) {
                saveFrame = requestAnimationFrame(() => {
                    try {
                        sessionStorage.setItem(SCROLL_STORAGE_PREFIX + pathname, String(scroller.scrollTop));
                    } catch {
                        void 0;
                    }
                    saveFrame = null;
                });
            }
        };

        scroller.addEventListener('scroll', onScroll, { passive: true });

        return () => {
            if (restoreFrame !== null) {
                cancelAnimationFrame(restoreFrame);
            }
            if (saveFrame !== null) {
                cancelAnimationFrame(saveFrame);
            }
            try {
                sessionStorage.setItem(SCROLL_STORAGE_PREFIX + pathname, String(scroller.scrollTop));
            } catch {
                void 0;
            }
            scroller.removeEventListener('scroll', onScroll);
        };
    }, [mainOwnsScroll, pathname]);
}
