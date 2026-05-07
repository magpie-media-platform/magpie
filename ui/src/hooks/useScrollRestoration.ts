import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

const SCROLL_STORAGE_PREFIX = 'magpie_scroll_';
const MAX_SCROLL_ENTRIES = 50;

// LRU bound on sessionStorage entries.
const recentKeys: string[] = [];

function touchKey(key: string) {
    const idx = recentKeys.indexOf(key);
    if (idx !== -1) {
        recentKeys.splice(idx, 1);
    }
    recentKeys.push(key);
    while (recentKeys.length > MAX_SCROLL_ENTRIES) {
        const evicted = recentKeys.shift();
        if (evicted !== undefined) {
            try {
                sessionStorage.removeItem(SCROLL_STORAGE_PREFIX + evicted);
            } catch {
                /* swallow quota / unavailable */
            }
        }
    }
}

export function useScrollRestoration() {
    const { key } = useLocation();

    useEffect(() => {
        const wrapper = document.querySelector<HTMLElement>('.app-shell__main-wrapper, .admin-shell__main-wrapper');
        if (!wrapper) {
            return;
        }

        const saved = sessionStorage.getItem(SCROLL_STORAGE_PREFIX + key);
        if (saved != null) {
            requestAnimationFrame(() => {
                wrapper.scrollTop = parseInt(saved, 10);
            });
        } else {
            wrapper.scrollTop = 0;
        }

        let ticking = false;
        const onScroll = () => {
            if (!ticking) {
                ticking = true;
                requestAnimationFrame(() => {
                    try {
                        sessionStorage.setItem(SCROLL_STORAGE_PREFIX + key, String(wrapper.scrollTop));
                        touchKey(key);
                    } catch {
                        /* swallow quota / unavailable */
                    }
                    ticking = false;
                });
            }
        };

        wrapper.addEventListener('scroll', onScroll, { passive: true });

        return () => {
            try {
                sessionStorage.setItem(SCROLL_STORAGE_PREFIX + key, String(wrapper.scrollTop));
                touchKey(key);
            } catch {
                /* swallow quota / unavailable */
            }
            wrapper.removeEventListener('scroll', onScroll);
        };
    }, [key]);
}

/**
 * Test-only: reset the in-memory LRU. Not exported from index.
 */
export function __resetScrollRestorationLru() {
    recentKeys.length = 0;
}
