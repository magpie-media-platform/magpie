/**
 * Keeps the surrounding overlay open when the outside interaction is on a toast.
 */
export function ignoreToastInteraction(event: Event): void {
    if (event.target instanceof Element && event.target.closest('.toast-viewport, .toast, .toast-tray')) {
        event.preventDefault();
    }
}
