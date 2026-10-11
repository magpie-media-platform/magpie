import type { RefObject } from 'react';

/**
 * Builds a dialog's onCloseAutoFocus handler that returns focus to the control that opened it, or to a fallback when
 * that control cannot take focus. A dialog opened without a trigger (from a menu or a callback) otherwise sends focus
 * to the page body. With no refs, the handler is undefined and the dialog keeps its default focus behaviour.
 */
export function returnFocusOnClose(
    returnFocusRef?: RefObject<HTMLElement | null>,
    fallbackFocusRef?: RefObject<HTMLElement | null>,
): ((event: Event) => void) | undefined {
    if (!returnFocusRef && !fallbackFocusRef) {
        return undefined;
    }
    return (event) => {
        const target = [returnFocusRef?.current, fallbackFocusRef?.current].find(
            (element) => element?.isConnected && !element.matches(':disabled'),
        );
        if (target) {
            event.preventDefault();
            target.focus({ preventScroll: true });
            if (target.matches(':focus-visible')) {
                target.scrollIntoView({ block: 'nearest' });
            }
        }
    };
}
