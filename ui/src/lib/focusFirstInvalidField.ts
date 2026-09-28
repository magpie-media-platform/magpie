/**
 * Moves focus to the first invalid control in a form after a blocked submit, which scrolls it into view.
 * It waits a frame so that the controls marked invalid by the failed validation have rendered.
 */
export function focusFirstInvalidField(form: EventTarget | null): void {
    if (!(form instanceof HTMLElement)) {
        return;
    }
    requestAnimationFrame(() => {
        form.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
    });
}
