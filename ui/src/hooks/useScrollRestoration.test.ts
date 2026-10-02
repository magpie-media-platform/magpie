import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook } from '@testing-library/react';

const mockLocation = { key: 'abc123', pathname: '/list' };
vi.mock('react-router-dom', () => ({
    useLocation: () => mockLocation,
}));

vi.mock('./ui/useMediaQuery', () => ({
    useMediaQuery: vi.fn(() => false),
}));

import { useMediaQuery } from './ui/useMediaQuery';
import { useScrollRestoration } from './useScrollRestoration';

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
    let rafCallbacks: Map<number, FrameRequestCallback>;
    let nextRafId: number;

    beforeEach(() => {
        vi.mocked(useMediaQuery).mockReturnValue(false);
        wrapper = createMockWrapper();

        vi.spyOn(document, 'querySelector').mockImplementation((selector: string) => {
            if (selector === '[data-component="app-shell-main-wrapper"]') {
                return wrapper as unknown as HTMLElement;
            }
            return null;
        });

        rafCallbacks = new Map();
        nextRafId = 1;
        vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb) => {
            const id = nextRafId++;
            rafCallbacks.set(id, cb);
            return id;
        });
        vi.spyOn(window, 'cancelAnimationFrame').mockImplementation((id) => {
            rafCallbacks.delete(id);
        });

        sessionStorage.clear();
        mockLocation.key = 'abc123';
        mockLocation.pathname = '/list';
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    function flushRaf() {
        const cbs = [...rafCallbacks.values()];
        rafCallbacks.clear();
        cbs.forEach((cb) => cb(performance.now()));
    }

    it('scrolls to top for a new pathname', () => {
        wrapper.scrollTop = 500;

        renderHook(() => useScrollRestoration());

        expect(wrapper.scrollTop).toBe(0);
    });

    it('restores tablet scroll on the main area', () => {
        // Arrange
        vi.mocked(useMediaQuery).mockReturnValue(true);
        const main = createMockWrapper();
        vi.spyOn(document, 'querySelector').mockImplementation((selector: string) => {
            if (selector === '[data-component="app-shell-main"]') {
                return main as unknown as HTMLElement;
            }
            return null;
        });
        sessionStorage.setItem('magpie_scroll_/list', '350');

        // Act
        renderHook(() => useScrollRestoration());
        flushRaf();

        // Assert
        expect(useMediaQuery).toHaveBeenCalledWith('(max-width: 1023px)');
        expect(main.scrollTop).toBe(350);
        expect(wrapper.scrollTop).toBe(0);
    });

    it('restores saved scroll position for a known pathname', () => {
        sessionStorage.setItem('magpie_scroll_/list', '350');

        renderHook(() => useScrollRestoration());
        flushRaf();

        expect(wrapper.scrollTop).toBe(350);
    });

    it('saves scroll position on scroll events', () => {
        renderHook(() => useScrollRestoration());

        wrapper.scrollTop = 200;
        wrapper.dispatchScroll();
        flushRaf();

        expect(sessionStorage.getItem('magpie_scroll_/list')).toBe('200');
    });

    it('saves final scroll position on cleanup', () => {
        const { unmount } = renderHook(() => useScrollRestoration());

        wrapper.scrollTop = 750;
        unmount();

        expect(sessionStorage.getItem('magpie_scroll_/list')).toBe('750');
    });

    it('removes scroll listener on cleanup', () => {
        const { unmount } = renderHook(() => useScrollRestoration());

        expect(wrapper.getListenerCount('scroll')).toBe(1);

        unmount();

        expect(wrapper.getListenerCount('scroll')).toBe(0);
    });

    it('cancels a pending save before another route can change the shared scroller', () => {
        const { unmount } = renderHook(() => useScrollRestoration());
        wrapper.scrollTop = 200;
        wrapper.dispatchScroll();

        unmount();
        wrapper.scrollTop = 900;
        flushRaf();

        expect(sessionStorage.getItem('magpie_scroll_/list')).toBe('200');
    });

    it('does nothing when wrapper element is not found', () => {
        vi.spyOn(document, 'querySelector').mockReturnValue(null);

        const { unmount } = renderHook(() => useScrollRestoration());
        unmount();
    });
});
