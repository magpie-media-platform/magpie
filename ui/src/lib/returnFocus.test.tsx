import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { returnFocusOnClose } from './returnFocus';

describe('returnFocusOnClose', () => {
    it('ReturnFocusOnClose_FocusesTheFallback_WhenTheOpenerIsDisabled', () => {
        // Arrange
        render(
            <>
                <button disabled>Retry</button>
                <div tabIndex={-1} data-testid="fallback" />
            </>,
        );
        const fallback = screen.getByTestId('fallback');
        const event = new Event('focus.autoFocusOnUnmount', { cancelable: true });

        // Act
        returnFocusOnClose({ current: screen.getByRole('button', { name: 'Retry' }) }, { current: fallback })?.(event);

        // Assert
        expect(fallback).toHaveFocus();
        expect(event.defaultPrevented).toBe(true);
    });

    it('ReturnFocusOnClose_KeepsTheScrollPosition_WhenItFocusesTheFallback', () => {
        // Arrange
        render(<h2 tabIndex={-1}>Bluey</h2>);
        const fallback = screen.getByRole('heading', { name: 'Bluey' });
        const focus = vi.spyOn(fallback, 'focus');

        // Act
        returnFocusOnClose(undefined, { current: fallback })?.(new Event('focus.autoFocusOnUnmount'));

        // Assert
        expect(focus).toHaveBeenCalledWith({ preventScroll: true });
    });

    it('ReturnFocusOnClose_ScrollsTheFallbackIntoView_WhenFocusIsVisible', () => {
        // Arrange
        render(<h2 tabIndex={-1}>Bluey</h2>);
        const fallback = screen.getByRole('heading', { name: 'Bluey' });
        const scrollIntoView = vi.spyOn(fallback, 'scrollIntoView');

        // Act
        returnFocusOnClose(undefined, { current: fallback })?.(new Event('focus.autoFocusOnUnmount'));

        // Assert
        expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest' });
    });

    it('ReturnFocusOnClose_KeepsTheScrollPosition_WhenFocusIsNotVisible', () => {
        // Arrange
        render(<h2 tabIndex={-1}>Bluey</h2>);
        const fallback = screen.getByRole('heading', { name: 'Bluey' });
        vi.spyOn(fallback, 'matches').mockReturnValue(false);
        const scrollIntoView = vi.spyOn(fallback, 'scrollIntoView');

        // Act
        returnFocusOnClose(undefined, { current: fallback })?.(new Event('focus.autoFocusOnUnmount'));

        // Assert
        expect(fallback).toHaveFocus();
        expect(scrollIntoView).not.toHaveBeenCalled();
    });
});
