import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { DeleteConfirmDialog } from './DeleteConfirmDialog';

const defaultProps = {
    open: true,
    onConfirm: vi.fn(),
    onCancel: vi.fn(),
    itemName: 'My Trigger',
    itemType: 'Schedule Trigger',
};

describe('DeleteConfirmDialog', () => {
    it('renders title with item type', async () => {
        render(<DeleteConfirmDialog {...defaultProps} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Delete Schedule Trigger')).toBeInTheDocument();
    });

    it('renders description with item name', async () => {
        render(<DeleteConfirmDialog {...defaultProps} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(
            screen.getByText(/Are you sure you want to delete "My Trigger"\? This action cannot be undone\./),
        ).toBeInTheDocument();
    });

    it('does not render when open is false', () => {
        render(<DeleteConfirmDialog {...defaultProps} open={false} />);

        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('calls onConfirm when delete button is clicked', async () => {
        const onConfirm = vi.fn();
        const user = userEvent.setup();

        render(<DeleteConfirmDialog {...defaultProps} onConfirm={onConfirm} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /delete/i }));

        expect(onConfirm).toHaveBeenCalledOnce();
    });

    it('calls onCancel when cancel button is clicked', async () => {
        const onCancel = vi.fn();
        const user = userEvent.setup();

        render(<DeleteConfirmDialog {...defaultProps} onCancel={onCancel} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(onCancel).toHaveBeenCalledOnce();
    });

    it('calls onCancel when Escape key is pressed', async () => {
        const onCancel = vi.fn();
        const user = userEvent.setup();

        render(<DeleteConfirmDialog {...defaultProps} onCancel={onCancel} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.keyboard('{Escape}');

        expect(onCancel).toHaveBeenCalled();
    });

    it('disables buttons when isDeleting is true', async () => {
        render(<DeleteConfirmDialog {...defaultProps} isDeleting />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
        expect(screen.getByRole('button', { name: /deleting/i })).toBeDisabled();
    });

    it('shows "Deleting..." text when isDeleting is true', async () => {
        render(<DeleteConfirmDialog {...defaultProps} isDeleting />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Deleting...')).toBeInTheDocument();
    });

    it('shows "Delete" text when isDeleting is false', async () => {
        render(<DeleteConfirmDialog {...defaultProps} isDeleting={false} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: /delete/i })).toBeInTheDocument();
        expect(screen.queryByText('Deleting...')).not.toBeInTheDocument();
    });

    it('renders with different item type and name', async () => {
        render(<DeleteConfirmDialog {...defaultProps} itemType="Wishlist" itemName="Action Movies" />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Delete Wishlist')).toBeInTheDocument();
        expect(
            screen.getByText(/Are you sure you want to delete "Action Movies"\? This action cannot be undone\./),
        ).toBeInTheDocument();
    });

    it('calls onCancel when overlay is clicked', async () => {
        const onCancel = vi.fn();
        const user = userEvent.setup();

        render(<DeleteConfirmDialog {...defaultProps} onCancel={onCancel} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const overlay = document.querySelector('.dialog-overlay');
        expect(overlay).toBeInTheDocument();
        await user.click(overlay!);

        expect(onCancel).toHaveBeenCalled();
    });
});
