import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { LibraryAlphabetRail } from './LibraryAlphabetRail';

const LETTER_HEIGHT = 20;

class TestPointerEvent extends MouseEvent {
    pointerId: number;

    constructor(type: string, init: PointerEventInit = {}) {
        super(type, init);
        this.pointerId = init.pointerId ?? 0;
    }
}

describe('LibraryAlphabetRail', () => {
    beforeEach(() => {
        vi.stubGlobal('PointerEvent', TestPointerEvent);
        vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
            const index = this.dataset.letter ? Array.from(this.parentElement!.children).indexOf(this) : 0;
            return {
                top: index * LETTER_HEIGHT,
                bottom: (index + 1) * LETTER_HEIGHT,
                height: LETTER_HEIGHT,
            } as DOMRect;
        });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it('Click_SelectsTheLetter_WhenAKeyboardActivatesIt', async () => {
        // Arrange
        const onChange = vi.fn();
        render(<LibraryAlphabetRail titlePrefix="" onTitlePrefixChange={onChange} />);
        screen.getByRole('button', { name: 'C' }).focus();

        // Act
        await userEvent.keyboard('{Enter}');

        // Assert
        expect(onChange).toHaveBeenCalledExactlyOnceWith('C');
    });

    it('Click_SelectsTheLetterOnce_WhenTheLetterIsPressed', async () => {
        // Arrange
        const onChange = vi.fn();
        render(<LibraryAlphabetRail titlePrefix="" onTitlePrefixChange={onChange} />);

        // Act
        await userEvent.click(screen.getByRole('button', { name: 'C' }));

        // Assert
        expect(onChange).toHaveBeenCalledExactlyOnceWith('C');
    });

    it('Click_ClearsTheLetter_WhenTheActiveLetterIsPressed', async () => {
        // Arrange
        const onChange = vi.fn();
        render(<LibraryAlphabetRail titlePrefix="c" onTitlePrefixChange={onChange} />);

        // Act
        await userEvent.click(screen.getByRole('button', { name: 'C' }));

        // Assert
        expect(onChange).toHaveBeenCalledExactlyOnceWith('');
    });

    it('Slide_SelectsTheLastLetterOnRelease_WhenThePointerMovesAlongTheRail', () => {
        // Arrange
        const onChange = vi.fn();
        render(<LibraryAlphabetRail titlePrefix="" onTitlePrefixChange={onChange} />);
        const rail = screen.getByRole('group', { name: 'Filter by first letter' });

        // Act
        fireEvent.pointerDown(screen.getByRole('button', { name: 'B' }), { button: 0, pointerId: 1 });
        fireEvent.pointerMove(rail, { pointerId: 1, clientY: 4 * LETTER_HEIGHT + 5 });
        const previewWhileSliding = rail.querySelector('[data-preview="true"]')?.textContent;
        const callsWhileSliding = onChange.mock.calls.length;
        fireEvent.pointerMove(rail, { pointerId: 1, clientY: 6 * LETTER_HEIGHT + 5 });
        fireEvent.pointerUp(rail, { pointerId: 1 });

        // Assert
        expect(previewWhileSliding).toBe('E');
        expect(callsWhileSliding).toBe(0);
        expect(onChange).toHaveBeenCalledExactlyOnceWith('G');
    });

    it('Slide_SelectsTheNearestEndLetter_WhenThePointerLeavesTheRail', () => {
        // Arrange
        const onChange = vi.fn();
        render(<LibraryAlphabetRail titlePrefix="" onTitlePrefixChange={onChange} />);
        const rail = screen.getByRole('group', { name: 'Filter by first letter' });

        // Act
        fireEvent.pointerDown(screen.getByRole('button', { name: 'B' }), { button: 0, pointerId: 1 });
        fireEvent.pointerMove(rail, { pointerId: 1, clientY: 40 * LETTER_HEIGHT });
        fireEvent.pointerUp(rail, { pointerId: 1 });

        // Assert
        expect(onChange).toHaveBeenCalledExactlyOnceWith('Z');
    });

    it('Slide_SelectsNothing_WhenThePressIsCanceled', () => {
        // Arrange
        const onChange = vi.fn();
        render(<LibraryAlphabetRail titlePrefix="" onTitlePrefixChange={onChange} />);
        const rail = screen.getByRole('group', { name: 'Filter by first letter' });

        // Act
        fireEvent.pointerDown(screen.getByRole('button', { name: 'B' }), { button: 0, pointerId: 1 });
        fireEvent.pointerCancel(rail, { pointerId: 1 });
        fireEvent.pointerUp(rail, { pointerId: 1 });

        // Assert
        expect(onChange).not.toHaveBeenCalled();
    });
});
