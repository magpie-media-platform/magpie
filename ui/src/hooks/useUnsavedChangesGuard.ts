import { useEffect } from 'react';

/**
 * Prompts the user before navigating away while `active` is true. Covers tab close,
 * browser back/forward, and SPA navigation via <Link> / useNavigate. Falls back to
 * the native window.confirm because <BrowserRouter> is not a data-router, so
 * react-router's useBlocker is not available.
 *
 * Implementation note: the popstate listener is registered with { capture: true }
 * so it runs before React Router's bubble-phase handler. If react-router ever
 * switches to capture-phase, the prompt will fire after the route has changed
 * and won't be able to block.
 */
export function useUnsavedChangesGuard(active: boolean): void {
    useEffect(() => {
        if (!active) {
            return;
        }

        const message = 'You have unsaved changes. Leave this page and discard them?';

        const getCurrentPath = (): string => window.location.pathname + window.location.search + window.location.hash;

        const handleBeforeUnload = (e: BeforeUnloadEvent) => {
            e.preventDefault();
        };

        window.addEventListener('beforeunload', handleBeforeUnload);

        // Re-entrancy guard: when we walk forward after a decline, the
        // browser fires another popstate. Skip prompting on that one.
        // Same idea for the wrapped pushState recursion path.
        let suppressPrompt = false;

        const handlePopState = () => {
            if (suppressPrompt) {
                suppressPrompt = false;
                return;
            }
            const confirmed = window.confirm(message);
            if (confirmed) {
                return;
            }
            // User declined. Walk forward through the existing history stack
            // back to where we were. This avoids pushState's side effect of
            // erasing forward entries and bloating history.length.
            suppressPrompt = true;
            window.history.forward();
        };

        window.addEventListener('popstate', handlePopState, { capture: true });

        // Decline = don't call original pushState; window.location stays put, so the router stays in sync.
        const originalPushState = window.history.pushState.bind(window.history);
        const originalReplaceState = window.history.replaceState.bind(window.history);

        const wrap = <T extends typeof originalPushState | typeof originalReplaceState>(original: T): T => {
            return ((data: unknown, unused: string, url?: string | URL | null) => {
                const targetPath = url == null ? null : typeof url === 'string' ? url : url.toString();
                const currentPath = getCurrentPath();

                // Same-URL state push (e.g. router updating internal state) or
                // a re-entrant rollback push: pass through silently.
                if (suppressPrompt || targetPath === null || targetPath === currentPath) {
                    return original(data, unused, url ?? undefined);
                }

                const confirmed = window.confirm(message);
                if (confirmed) {
                    return original(data, unused, url ?? undefined);
                }

                return undefined;
            }) as T;
        };

        window.history.pushState = wrap(originalPushState);
        window.history.replaceState = wrap(originalReplaceState);

        return () => {
            window.removeEventListener('beforeunload', handleBeforeUnload);
            window.removeEventListener('popstate', handlePopState, { capture: true });
            window.history.pushState = originalPushState;
            window.history.replaceState = originalReplaceState;
        };
    }, [active]);
}
