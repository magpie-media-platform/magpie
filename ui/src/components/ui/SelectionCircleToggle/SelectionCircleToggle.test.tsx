import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { SelectionCircleToggle } from './SelectionCircleToggle';

describe('SelectionCircleToggle', () => {
    it('renders unselected and calls onToggle on click', async () => {
        const user = userEvent.setup();
        const onToggle = vi.fn();
        render(<SelectionCircleToggle selected={false} onToggle={onToggle} aria-label="Select row" />);

        const toggle = screen.getByRole('button', { name: 'Select row' });
        expect(toggle).toHaveAttribute('aria-pressed', 'false');
        expect(toggle).not.toHaveClass('selection-circle-toggle--selected');

        await user.click(toggle);
        expect(onToggle).toHaveBeenCalledOnce();
    });

    it('renders selected state with the filled variant', () => {
        render(<SelectionCircleToggle selected onToggle={vi.fn()} aria-label="Deselect row" />);

        const toggle = screen.getByRole('button', { name: 'Deselect row' });
        expect(toggle).toHaveAttribute('aria-pressed', 'true');
        expect(toggle).toHaveClass('selection-circle-toggle--selected');
    });

    it('stops the click from propagating to an ancestor handler', async () => {
        const user = userEvent.setup();
        const onToggle = vi.fn();
        const onAncestorClick = vi.fn();
        render(
            <div onClick={onAncestorClick}>
                <SelectionCircleToggle selected={false} onToggle={onToggle} aria-label="Select row" />
            </div>,
        );

        await user.click(screen.getByRole('button', { name: 'Select row' }));
        expect(onToggle).toHaveBeenCalledOnce();
        expect(onAncestorClick).not.toHaveBeenCalled();
    });
});
