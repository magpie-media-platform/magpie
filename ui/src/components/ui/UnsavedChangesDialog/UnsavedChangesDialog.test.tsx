import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { UnsavedChangesDialog } from './UnsavedChangesDialog';

const defaultProps = {
    open: true,
    onDiscard: vi.fn(),
    onCancel: vi.fn(),
};

describe('UnsavedChangesDialog', () => {
    it('renders title and description when open', async () => {
        render(<UnsavedChangesDialog {...defaultProps} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(screen.getByText('Close without saving your changes?')).toBeInTheDocument();
    });

    it('does not render when open is false', () => {
        render(<UnsavedChangesDialog {...defaultProps} open={false} />);

        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('calls onDiscard when discard button is clicked', async () => {
        const onDiscard = vi.fn();
        const user = userEvent.setup();

        render(<UnsavedChangesDialog {...defaultProps} onDiscard={onDiscard} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /discard changes/i }));

        expect(onDiscard).toHaveBeenCalledOnce();
    });

    it('calls onCancel when keep editing button is clicked', async () => {
        const onCancel = vi.fn();
        const user = userEvent.setup();

        render(<UnsavedChangesDialog {...defaultProps} onCancel={onCancel} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /keep editing/i }));

        expect(onCancel).toHaveBeenCalledOnce();
    });

    it('calls onCancel when Escape key is pressed', async () => {
        const onCancel = vi.fn();
        const user = userEvent.setup();

        render(<UnsavedChangesDialog {...defaultProps} onCancel={onCancel} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.keyboard('{Escape}');

        expect(onCancel).toHaveBeenCalled();
    });

    it('calls onCancel when overlay is clicked', async () => {
        const onCancel = vi.fn();
        const user = userEvent.setup();

        render(<UnsavedChangesDialog {...defaultProps} onCancel={onCancel} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const overlay = document.querySelector('.dialog-overlay');
        expect(overlay).toBeInTheDocument();
        await user.click(overlay!);

        expect(onCancel).toHaveBeenCalled();
    });
});
