/**
 * Leaves Escape to an expanded combobox so that it closes its own popup before the surrounding overlay closes.
 */
export function yieldEscapeToOpenCombobox(event: KeyboardEvent): void {
    if (event.target instanceof Element && event.target.closest('[role="combobox"][aria-expanded="true"]')) {
        event.preventDefault();
    }
}
