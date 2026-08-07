import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { BulkValuePicker } from './BulkValuePicker';

const options = [
    { id: 'opt-1', label: 'Movies (/media/movies)' },
    { id: 'opt-2', label: 'Archive (/media/archive)' },
];

function renderPicker(overrides: Partial<React.ComponentProps<typeof BulkValuePicker>> = {}) {
    const onApply = vi.fn();
    const onOpenChange = vi.fn();
    render(
        <BulkValuePicker
            open
            onOpenChange={onOpenChange}
            title="Root folder"
            description="Set the root folder"
            selectLabel="Root folder"
            inheritLabel="Inherit / default"
            options={options}
            selectedCount={3}
            onApply={onApply}
            data-testid="bulk-picker"
            {...overrides}
        />,
    );
    return { onApply, onOpenChange };
}

describe('BulkValuePicker', () => {
    it('renders the title, description count, and an inherit option', async () => {
        const user = userEvent.setup();
        renderPicker();

        expect(screen.getByRole('heading', { name: /Root folder/i })).toBeInTheDocument();
        expect(screen.getByText(/Set the root folder for 3 selected items\./i)).toBeInTheDocument();

        await user.click(screen.getByTestId('bulk-value-picker__select'));
        expect(screen.getByRole('option', { name: 'Inherit / default' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: /Movies/ })).toBeInTheDocument();
    });

    it('applies null when the inherit option is the selection', async () => {
        const user = userEvent.setup();
        const { onApply } = renderPicker();

        await user.click(screen.getByRole('button', { name: /Apply/i }));

        await waitFor(() => expect(onApply).toHaveBeenCalledWith(null));
    });

    it('applies the chosen option id', async () => {
        const user = userEvent.setup();
        const { onApply } = renderPicker();

        await user.click(screen.getByTestId('bulk-value-picker__select'));
        await user.click(screen.getByRole('option', { name: /Archive/ }));
        await user.click(screen.getByRole('button', { name: /Apply/i }));

        await waitFor(() => expect(onApply).toHaveBeenCalledWith('opt-2'));
    });

    it('shows the empty hint when no options are configured', () => {
        renderPicker({ options: [], emptyHint: 'No root folders are configured.' });

        expect(screen.getByText('No root folders are configured.')).toBeInTheDocument();
    });

    it('closes without applying when Cancel is clicked', async () => {
        const user = userEvent.setup();
        const { onApply, onOpenChange } = renderPicker();

        await user.click(screen.getByRole('button', { name: /Cancel/i }));

        expect(onApply).not.toHaveBeenCalled();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });
});
