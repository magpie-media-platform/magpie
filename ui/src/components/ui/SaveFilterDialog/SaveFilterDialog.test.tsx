import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { SaveFilterDialog } from './SaveFilterDialog';

const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    onSave: vi.fn(),
};

describe('SaveFilterDialog', () => {
    it('renders when open', async () => {
        render(<SaveFilterDialog {...defaultProps} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByRole('heading', { name: /save filter/i })).toBeInTheDocument();
    });

    it('does not render when open is false', () => {
        render(<SaveFilterDialog {...defaultProps} open={false} />);

        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('displays external error', async () => {
        render(<SaveFilterDialog {...defaultProps} error="Save failed" />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Save failed')).toBeInTheDocument();
    });

    it('clears synced error when error prop becomes null', async () => {
        const { rerender } = render(<SaveFilterDialog {...defaultProps} error="Save failed" />);

        await waitFor(() => {
            expect(screen.getByText('Save failed')).toBeInTheDocument();
        });

        rerender(<SaveFilterDialog {...defaultProps} error={null} />);

        await waitFor(() => {
            expect(screen.queryByText('Save failed')).not.toBeInTheDocument();
        });
    });

    it('calls onSave with trimmed name on submit', async () => {
        const onSave = vi.fn();
        const user = userEvent.setup();

        render(<SaveFilterDialog {...defaultProps} onSave={onSave} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.type(screen.getByLabelText('Filter Name'), '  My Filter  ');
        await user.click(screen.getByRole('button', { name: /save filter/i }));

        expect(onSave).toHaveBeenCalledWith('My Filter');
    });

    it('shows validation error when name is empty', async () => {
        const onSave = vi.fn();
        const user = userEvent.setup();

        render(<SaveFilterDialog {...defaultProps} onSave={onSave} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.type(screen.getByLabelText('Filter Name'), '   ');

        expect(screen.getByRole('button', { name: /save filter/i })).toBeDisabled();

        expect(onSave).not.toHaveBeenCalled();
    });
});
