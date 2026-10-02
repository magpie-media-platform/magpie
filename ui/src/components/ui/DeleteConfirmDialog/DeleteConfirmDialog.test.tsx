import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { TooltipProvider } from '../Tooltip';
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

    it('renders a custom description in place of the default body', async () => {
        render(<DeleteConfirmDialog {...defaultProps} description="Delete 3 items? This cannot be undone." />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Delete 3 items? This cannot be undone.')).toBeInTheDocument();
        expect(screen.queryByText(/Are you sure you want to delete/)).not.toBeInTheDocument();
    });

    it('singularizes the count-bearing description for a single item', async () => {
        const count = 1;
        render(
            <DeleteConfirmDialog
                {...defaultProps}
                description={`Delete ${count} item${count !== 1 ? 's' : ''}? This cannot be undone.`}
            />,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Delete 1 item? This cannot be undone.')).toBeInTheDocument();
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

    it('renders no extra toggle when extraToggle is not provided', async () => {
        render(<DeleteConfirmDialog {...defaultProps} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.queryByRole('switch')).not.toBeInTheDocument();
        expect(document.querySelector('[data-component="delete-confirm-toggle"]')).not.toBeInTheDocument();
    });

    it('renders the extra toggle off by default when extraToggle is provided', async () => {
        render(
            <DeleteConfirmDialog
                {...defaultProps}
                extraToggle={{ label: 'Also delete its filter "Action"', checked: false, onChange: vi.fn() }}
            />,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch');
        expect(toggle).toBeInTheDocument();
        expect(toggle).toHaveAttribute('data-state', 'unchecked');
        expect(screen.getByText('Also delete its filter "Action"')).toBeInTheDocument();
    });

    it('invokes extraToggle.onChange when the extra toggle is flipped', async () => {
        const onChange = vi.fn();
        const user = userEvent.setup();

        render(
            <DeleteConfirmDialog
                {...defaultProps}
                extraToggle={{ label: 'Also delete its filter "Action"', checked: false, onChange }}
            />,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('switch'));

        expect(onChange).toHaveBeenCalledWith(true);
    });

    it('disables the extra toggle and describes the reason when disabledReason is set', async () => {
        render(
            <DeleteConfirmDialog
                {...defaultProps}
                extraToggle={{
                    label: 'Also delete its filter "Action"',
                    checked: false,
                    onChange: vi.fn(),
                    disabledReason: 'You do not have permission to delete this filter.',
                }}
            />,
            { wrapper: TooltipProvider },
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch');
        expect(toggle).toBeDisabled();
        expect(toggle.parentElement).toHaveAccessibleDescription('You do not have permission to delete this filter.');
    });

    it('focuses Cancel and opens no tooltip when the dialog opens with a disabled extra toggle', async () => {
        render(
            <DeleteConfirmDialog
                {...defaultProps}
                extraToggle={{
                    label: 'Also delete its filter "Action"',
                    checked: false,
                    onChange: vi.fn(),
                    disabledReason: 'You do not have permission to delete this filter.',
                }}
            />,
            { wrapper: TooltipProvider },
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus();
        expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
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

it('associates each extra-toggle label with its own dialog control', () => {
    render(
        <>
            <DeleteConfirmDialog
                {...defaultProps}
                extraToggle={{ label: 'Delete files', checked: false, onChange: vi.fn() }}
            />
            <DeleteConfirmDialog
                {...defaultProps}
                extraToggle={{ label: 'Delete records', checked: true, onChange: vi.fn() }}
            />
        </>,
    );
    const files = screen.getByLabelText('Delete files');
    const records = screen.getByLabelText('Delete records');
    expect(files).toHaveAttribute('aria-checked', 'false');
    expect(records).toHaveAttribute('aria-checked', 'true');
    expect(files.id).not.toBe(records.id);
});
