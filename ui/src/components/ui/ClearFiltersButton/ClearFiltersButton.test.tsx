import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { ClearFiltersButton } from './ClearFiltersButton';

describe('ClearFiltersButton', () => {
    it('renders a standard button with the default label and clear-filters action', () => {
        render(<ClearFiltersButton onClick={vi.fn()} />);

        const button = screen.getByRole('button', { name: 'Clear filters' });

        expect(button).toHaveAttribute('data-action', 'clear-filters');
        expect(button).toHaveAttribute('data-component', 'button');
    });

    it('invokes onClick when clicked', async () => {
        const user = userEvent.setup();
        const onClick = vi.fn();
        render(<ClearFiltersButton onClick={onClick} />);

        await user.click(screen.getByRole('button', { name: 'Clear filters' }));

        expect(onClick).toHaveBeenCalledOnce();
    });

    it('lets callers override the label and data-action', () => {
        render(<ClearFiltersButton onClick={vi.fn()} label="Clear" data-action="clear-filters-queue" />);

        const button = screen.getByRole('button', { name: 'Clear' });

        expect(button).toHaveAttribute('data-action', 'clear-filters-queue');
    });
});
