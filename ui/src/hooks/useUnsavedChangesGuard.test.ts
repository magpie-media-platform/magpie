import { renderHook } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';

import { useUnsavedChangesGuard } from './useUnsavedChangesGuard';

describe('useUnsavedChangesGuard', () => {
    let confirmSpy: ReturnType<typeof vi.spyOn>;
    let originalPushState: typeof window.history.pushState;
    let originalReplaceState: typeof window.history.replaceState;
    let initialPath: string;

    beforeEach(() => {
        // Capture originals so we can restore them between tests -- the hook
        // patches them and a leak between tests would compound.
        originalPushState = window.history.pushState.bind(window.history);
        originalReplaceState = window.history.replaceState.bind(window.history);
        initialPath = window.location.pathname + window.location.search + window.location.hash;
        confirmSpy = vi.spyOn(window, 'confirm');
    });

    afterEach(() => {
        confirmSpy.mockRestore();
        // Reset URL to its starting point so cross-test interference is bounded.
        window.history.replaceState = originalReplaceState;
        window.history.pushState = originalPushState;
        window.history.replaceState(null, '', initialPath);
    });

    describe('beforeunload listener', () => {
        it('registers beforeunload when active', () => {
            const addSpy = vi.spyOn(window, 'addEventListener');

            renderHook(() => useUnsavedChangesGuard(true));

            const call = addSpy.mock.calls.find(([event]) => event === 'beforeunload');
            expect(call).toBeDefined();

            addSpy.mockRestore();
        });

        it('does not register beforeunload when inactive', () => {
            const addSpy = vi.spyOn(window, 'addEventListener');

            renderHook(() => useUnsavedChangesGuard(false));

            const call = addSpy.mock.calls.find(([event]) => event === 'beforeunload');
            expect(call).toBeUndefined();

            addSpy.mockRestore();
        });

        it('removes beforeunload on unmount', () => {
            const removeSpy = vi.spyOn(window, 'removeEventListener');

            const { unmount } = renderHook(() => useUnsavedChangesGuard(true));
            unmount();

            const call = removeSpy.mock.calls.find(([event]) => event === 'beforeunload');
            expect(call).toBeDefined();

            removeSpy.mockRestore();
        });

        it('removes beforeunload when active flips to false', () => {
            const removeSpy = vi.spyOn(window, 'removeEventListener');

            const { rerender } = renderHook(({ active }) => useUnsavedChangesGuard(active), {
                initialProps: { active: true },
            });
            rerender({ active: false });

            const call = removeSpy.mock.calls.find(([event]) => event === 'beforeunload');
            expect(call).toBeDefined();

            removeSpy.mockRestore();
        });

        it('calls preventDefault on beforeunload events', () => {
            const addSpy = vi.spyOn(window, 'addEventListener');

            renderHook(() => useUnsavedChangesGuard(true));

            const call = addSpy.mock.calls.find(([event]) => event === 'beforeunload');
            const handler = call![1] as (e: BeforeUnloadEvent) => void;
            const event = new Event('beforeunload') as BeforeUnloadEvent;
            const preventSpy = vi.spyOn(event, 'preventDefault');

            handler(event);

            expect(preventSpy).toHaveBeenCalled();

            addSpy.mockRestore();
        });

        it('still calls preventDefault on beforeunload while active', () => {
            renderHook(() => useUnsavedChangesGuard(true));

            const event = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
            const preventSpy = vi.spyOn(event, 'preventDefault');

            window.dispatchEvent(event);

            expect(preventSpy).toHaveBeenCalled();
        });
    });

    describe('SPA navigation via pushState', () => {
        it('proceeds without prompting when inactive', () => {
            renderHook(() => useUnsavedChangesGuard(false));

            window.history.pushState(null, '', '/destination');

            expect(confirmSpy).not.toHaveBeenCalled();
            expect(window.location.pathname).toBe('/destination');
        });

        it('blocks navigation when user cancels', () => {
            confirmSpy.mockReturnValue(false);
            window.history.replaceState(null, '', '/start');

            renderHook(() => useUnsavedChangesGuard(true));

            window.history.pushState(null, '', '/destination');

            expect(confirmSpy).toHaveBeenCalledOnce();
            expect(window.location.pathname).toBe('/start');
        });

        it('proceeds with navigation when user confirms', () => {
            confirmSpy.mockReturnValue(true);
            window.history.replaceState(null, '', '/start');

            renderHook(() => useUnsavedChangesGuard(true));

            window.history.pushState(null, '', '/destination');

            expect(confirmSpy).toHaveBeenCalledOnce();
            expect(window.location.pathname).toBe('/destination');
        });

        it('does not prompt on a same-path pushState', () => {
            window.history.replaceState(null, '', '/start');

            renderHook(() => useUnsavedChangesGuard(true));

            const samePath = window.location.pathname + window.location.search + window.location.hash;
            window.history.pushState({ idx: 1 }, '', samePath);

            expect(confirmSpy).not.toHaveBeenCalled();
            expect(window.location.pathname).toBe('/start');
        });

        it('blocks replaceState the same way as pushState when user cancels', () => {
            confirmSpy.mockReturnValue(false);
            window.history.replaceState(null, '', '/start');

            renderHook(() => useUnsavedChangesGuard(true));

            window.history.replaceState(null, '', '/destination');

            expect(confirmSpy).toHaveBeenCalledOnce();
            expect(window.location.pathname).toBe('/start');
        });

        it('restores the original pushState on unmount', () => {
            const { unmount } = renderHook(() => useUnsavedChangesGuard(true));
            const wrappedPushState = window.history.pushState;

            unmount();

            expect(window.history.pushState).not.toBe(wrappedPushState);
        });

        it('does not patch pushState when inactive', () => {
            const before = window.history.pushState;

            renderHook(() => useUnsavedChangesGuard(false));

            expect(window.history.pushState).toBe(before);
        });
    });

    describe('popstate (back/forward)', () => {
        it('does not prompt on popstate when inactive', () => {
            renderHook(() => useUnsavedChangesGuard(false));

            window.dispatchEvent(new PopStateEvent('popstate'));

            expect(confirmSpy).not.toHaveBeenCalled();
        });

        it('registers popstate with capture-phase so it runs before React Router', () => {
            const addSpy = vi.spyOn(window, 'addEventListener');

            renderHook(() => useUnsavedChangesGuard(true));

            const call = addSpy.mock.calls.find(([event]) => event === 'popstate');
            expect(call).toBeDefined();
            // Third arg should be options with capture: true.
            const options = call![2];
            expect(options).toEqual(expect.objectContaining({ capture: true }));

            addSpy.mockRestore();
        });

        it('removes popstate with the same capture option on unmount', () => {
            const removeSpy = vi.spyOn(window, 'removeEventListener');

            const { unmount } = renderHook(() => useUnsavedChangesGuard(true));
            unmount();

            const call = removeSpy.mock.calls.find(([event]) => event === 'popstate');
            expect(call).toBeDefined();
            const options = call![2];
            expect(options).toEqual(expect.objectContaining({ capture: true }));

            removeSpy.mockRestore();
        });

        it('uses history.forward() (not pushState) to roll back when declined', () => {
            confirmSpy.mockReturnValue(false);
            window.history.replaceState(null, '', '/start');

            renderHook(() => useUnsavedChangesGuard(true));

            // Spy on the prototype method since the hook patches the
            // instance pushState. forward() is unwrapped on the prototype.
            const forwardSpy = vi.spyOn(window.history, 'forward').mockImplementation(() => {});
            // pushState on the wrapped instance: ensure it is NOT called as
            // part of the rollback. We need the spy on the wrapped
            // function, which is window.history.pushState right now.
            const wrappedPushState = window.history.pushState;
            const pushStateSpy = vi.fn(wrappedPushState);
            window.history.pushState = pushStateSpy as typeof window.history.pushState;

            const realReplaceState = Object.getPrototypeOf(window.history)
                .replaceState as typeof window.history.replaceState;
            realReplaceState.call(window.history, null, '', '/elsewhere');
            window.dispatchEvent(new PopStateEvent('popstate'));

            expect(confirmSpy).toHaveBeenCalledOnce();
            expect(forwardSpy).toHaveBeenCalledOnce();
            expect(pushStateSpy).not.toHaveBeenCalled();

            forwardSpy.mockRestore();
        });

        it('does not increase history.length across a back+decline cycle', () => {
            confirmSpy.mockReturnValue(false);
            window.history.replaceState(null, '', '/start');

            renderHook(() => useUnsavedChangesGuard(true));

            // forward() in jsdom would normally fire a popstate but jsdom's
            // implementation is a no-op for length. The point of this test
            // is that the hook does not call pushState (which would grow
            // history.length). Snapshot before and after.
            const lengthBefore = window.history.length;

            const realReplaceState = Object.getPrototypeOf(window.history)
                .replaceState as typeof window.history.replaceState;
            realReplaceState.call(window.history, null, '', '/elsewhere');
            window.dispatchEvent(new PopStateEvent('popstate'));

            expect(window.history.length).toBeLessThanOrEqual(lengthBefore);
        });

        it('accepts the new URL on popstate when confirmed', () => {
            confirmSpy.mockReturnValue(true);
            window.history.replaceState(null, '', '/start');

            renderHook(() => useUnsavedChangesGuard(true));

            const realReplaceState = Object.getPrototypeOf(window.history)
                .replaceState as typeof window.history.replaceState;
            realReplaceState.call(window.history, null, '', '/elsewhere');
            window.dispatchEvent(new PopStateEvent('popstate'));

            expect(confirmSpy).toHaveBeenCalledOnce();
            expect(window.location.pathname).toBe('/elsewhere');
        });

        it('suppresses the prompt for the popstate that follows a forward() rollback', () => {
            // Simulate the sequence: real popstate (decline) -> forward() ->
            // jsdom would fire another popstate. Our suppressPrompt flag
            // should swallow that second event without calling confirm again.
            confirmSpy.mockReturnValue(false);
            window.history.replaceState(null, '', '/start');

            renderHook(() => useUnsavedChangesGuard(true));

            // Stub forward() so it does not actually navigate but DOES fire
            // a synthetic popstate event the way the real browser would.
            vi.spyOn(window.history, 'forward').mockImplementation(() => {
                window.dispatchEvent(new PopStateEvent('popstate'));
            });

            const realReplaceState = Object.getPrototypeOf(window.history)
                .replaceState as typeof window.history.replaceState;
            realReplaceState.call(window.history, null, '', '/elsewhere');
            window.dispatchEvent(new PopStateEvent('popstate'));

            // Only the first (real) popstate should have prompted. The
            // synthesized one from forward() must be suppressed.
            expect(confirmSpy).toHaveBeenCalledOnce();
        });
    });
});
