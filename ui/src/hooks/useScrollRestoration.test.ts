import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook } from '@testing-library/react';

const mockLocation = { key: 'abc123', pathname: '/list' };
vi.mock('react-router-dom', () => ({
    useLocation: () => mockLocation,
}));

import { useScrollRestoration, __resetScrollRestorationLru } from './useScrollRestoration';

function createMockWrapper() {
    let scrollTop = 0;
    const listeners: Record<string, EventListener[]> = {};

    return {
        get scrollTop() {
            return scrollTop;
        },
        set scrollTop(value: number) {
            scrollTop = value;
        },
        addEventListener(event: string, handler: EventListener, _options?: AddEventListenerOptions) {
            if (!listeners[event]) {
                listeners[event] = [];
            }
            listeners[event].push(handler);
        },
        removeEventListener(event: string, handler: EventListener) {
            if (listeners[event]) {
                listeners[event] = listeners[event].filter((h) => h !== handler);
            }
        },
        dispatchScroll() {
            listeners['scroll']?.forEach((h) => h(new Event('scroll')));
        },
        getListenerCount(event: string) {
            return listeners[event]?.length ?? 0;
        },
    };
}

describe('useScrollRestoration', () => {
    let wrapper: ReturnType<typeof createMockWrapper>;
    let originalQuerySelector: typeof document.querySelector;
    let rafCallbacks: FrameRequestCallback[];

    beforeEach(() => {
        wrapper = createMockWrapper();
        originalQuerySelector = document.querySelector.bind(document);

        vi.spyOn(document, 'querySelector').mockImplementation((selector: string) => {
            if (selector === '.app-shell__main-wrapper, .admin-shell__main-wrapper') {
                return wrapper as unknown as HTMLElement;
            }
            return originalQuerySelector(selector);
        });

        rafCallbacks = [];
        vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb) => {
            rafCallbacks.push(cb);
            return rafCallbacks.length;
        });

        sessionStorage.clear();
        mockLocation.key = 'abc123';
        mockLocation.pathname = '/list';
        __resetScrollRestorationLru();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    function flushRaf() {
        const cbs = [...rafCallbacks];
        rafCallbacks = [];
        cbs.forEach((cb) => cb(performance.now()));
    }

    it('scrolls to top for a new location key', () => {
        wrapper.scrollTop = 500;

        renderHook(() => useScrollRestoration());

        expect(wrapper.scrollTop).toBe(0);
    });

    it('restores saved scroll position for a known location key', () => {
        sessionStorage.setItem('magpie_scroll_abc123', '350');

        renderHook(() => useScrollRestoration());
        flushRaf();

        expect(wrapper.scrollTop).toBe(350);
    });

    it('saves scroll position on scroll events', () => {
        renderHook(() => useScrollRestoration());

        wrapper.scrollTop = 200;
        wrapper.dispatchScroll();
        flushRaf();

        expect(sessionStorage.getItem('magpie_scroll_abc123')).toBe('200');
    });

    it('saves final scroll position on cleanup', () => {
        const { unmount } = renderHook(() => useScrollRestoration());

        wrapper.scrollTop = 750;
        unmount();

        expect(sessionStorage.getItem('magpie_scroll_abc123')).toBe('750');
    });

    it('removes scroll listener on cleanup', () => {
        const { unmount } = renderHook(() => useScrollRestoration());

        expect(wrapper.getListenerCount('scroll')).toBe(1);

        unmount();

        expect(wrapper.getListenerCount('scroll')).toBe(0);
    });

    it('does nothing when wrapper element is not found', () => {
        vi.mocked(document.querySelector).mockReturnValue(null);

        const { unmount } = renderHook(() => useScrollRestoration());
        unmount();
    });

    it('evicts the oldest sessionStorage entry once the cap is exceeded', () => {
        // Simulate visiting many distinct routes by changing the location key
        // and remounting the hook for each one. The hook caps the in-memory
        // LRU at 50, so the 51st visit should evict the first key.
        const totalVisits = 52;
        for (let i = 0; i < totalVisits; i++) {
            mockLocation.key = `key-${i}`;
            const { unmount } = renderHook(() => useScrollRestoration());
            wrapper.scrollTop = i * 10;
            wrapper.dispatchScroll();
            flushRaf();
            unmount();
        }

        // The first two keys should have been evicted.
        expect(sessionStorage.getItem('magpie_scroll_key-0')).toBeNull();
        expect(sessionStorage.getItem('magpie_scroll_key-1')).toBeNull();
        // The most recent key should still be present.
        expect(sessionStorage.getItem(`magpie_scroll_key-${totalVisits - 1}`)).not.toBeNull();
    });
});
