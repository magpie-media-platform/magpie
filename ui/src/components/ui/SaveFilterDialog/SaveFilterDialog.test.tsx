import { render, screen, waitFor, within } from '@testing-library/react';
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
        render(<SaveFilterDialog {...defaultProps} nameError="Save failed" />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Save failed')).toBeInTheDocument();
    });

    it('shows a query error apart from the name field', async () => {
        render(<SaveFilterDialog {...defaultProps} filterQuery="title =" queryError="Expected value" />);

        const alert = await screen.findByRole('alert');

        expect(alert).toHaveTextContent('The filter query is not valid');
        expect(alert).toHaveTextContent('Expected value');
        expect(screen.getByLabelText(/filter name/i)).not.toHaveAttribute('aria-invalid', 'true');
    });

    it('clears synced error when error prop becomes null', async () => {
        const { rerender } = render(<SaveFilterDialog {...defaultProps} nameError="Save failed" />);

        await waitFor(() => {
            expect(screen.getByText('Save failed')).toBeInTheDocument();
        });

        rerender(<SaveFilterDialog {...defaultProps} nameError={null} />);

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

    it('blocks save and warns when the name duplicates an existing filter', async () => {
        const onSave = vi.fn();
        const user = userEvent.setup();

        render(<SaveFilterDialog {...defaultProps} onSave={onSave} existingNames={['Action Movies']} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.type(screen.getByLabelText('Filter Name'), 'action movies');
        await user.click(screen.getByRole('button', { name: /save filter/i }));

        expect(onSave).not.toHaveBeenCalled();
        expect(screen.getByText(/already exists/i)).toBeInTheDocument();
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

    describe('update vs new (applied saved filter)', () => {
        it('is create-only when no update target is provided', async () => {
            render(<SaveFilterDialog {...defaultProps} />);

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            expect(document.querySelector('[data-action="update-saved-filter"]')).not.toBeInTheDocument();
            expect(screen.getByRole('button', { name: /save filter/i })).toBeInTheDocument();
        });

        it('offers an Update action labelled with the target name, plus Save as New', async () => {
            render(<SaveFilterDialog {...defaultProps} onUpdate={vi.fn()} updateTargetName="Action Movies" />);

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            const updateButton = document.querySelector('[data-action="update-saved-filter"]') as HTMLElement;
            expect(updateButton).toBeInTheDocument();
            expect(updateButton).toHaveTextContent('Update "Action Movies"');

            const saveNewButton = document.querySelector('[data-action="save-new-filter"]') as HTMLElement;
            expect(saveNewButton).toHaveTextContent('Save as New');
        });

        it('keeps the update-mode Cancel action inside the dialog footer and closes from it', async () => {
            const onOpenChange = vi.fn();
            const user = userEvent.setup();

            render(
                <SaveFilterDialog
                    {...defaultProps}
                    onOpenChange={onOpenChange}
                    onUpdate={vi.fn()}
                    updateTargetName="qa-routeforms modified saved filter with a longer display name"
                />,
            );

            const dialog = await screen.findByRole('dialog');
            const footer = dialog.querySelector('.save-filter-dialog__footer');
            expect(footer).toBeInTheDocument();

            const cancelButton = within(footer as HTMLElement).getByRole('button', { name: 'Cancel' });
            await user.click(cancelButton);

            expect(onOpenChange).toHaveBeenCalledWith(false);
        });

        it('calls onUpdate when the Update action is clicked, without requiring a name', async () => {
            const onUpdate = vi.fn();
            const user = userEvent.setup();

            render(<SaveFilterDialog {...defaultProps} onUpdate={onUpdate} updateTargetName="Action Movies" />);

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            await user.click(document.querySelector('[data-action="update-saved-filter"]') as HTMLElement);

            expect(onUpdate).toHaveBeenCalledTimes(1);
        });
    });
});

it('gives simultaneous dialog inputs distinct label targets', () => {
    render(
        <>
            <SaveFilterDialog {...defaultProps} />
            <SaveFilterDialog {...defaultProps} />
        </>,
    );
    const inputs = document.querySelectorAll<HTMLInputElement>('[data-component="save-filter-dialog"] input');
    expect(inputs).toHaveLength(2);
    expect(inputs[0].id).not.toBe(inputs[1].id);
    expect(inputs[0].labels?.[0]).toHaveTextContent('Filter Name');
    expect(inputs[1].labels?.[0]).toHaveTextContent('Filter Name');
});
