import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import {
    Dialog,
    DialogTrigger,
    DialogContent,
    DialogHeader,
    DialogBody,
    DialogFooter,
    DialogTitle,
    DialogDescription,
    DialogClose,
} from './Dialog';
import { ToastContext } from '@/hooks/ui/useToast';

describe('Dialog', () => {
    it('renders trigger element', () => {
        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument();
    });

    it('opens dialog when trigger is clicked', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Dialog Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
    });

    it('renders title and description', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogHeader>
                        <DialogTitle>Dialog Title</DialogTitle>
                        <DialogDescription>This is a description</DialogDescription>
                    </DialogHeader>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByText('Dialog Title')).toBeInTheDocument();
            expect(screen.getByText('This is a description')).toBeInTheDocument();
        });
    });

    it('closes dialog when close button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Close' }));

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });

    it('closes dialog when overlay is clicked', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const overlay = document.querySelector('.dialog-overlay');
        expect(overlay).toBeInTheDocument();
        await user.click(overlay!);

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });

    it('DialogContent_StaysOpen_WhenAControlInAnotherOverlayIsPressed', async () => {
        // Arrange
        const user = userEvent.setup();
        const handleOpenChange = vi.fn();
        render(
            <>
                <Dialog open onOpenChange={handleOpenChange}>
                    <DialogContent>
                        <DialogTitle>Title</DialogTitle>
                    </DialogContent>
                </Dialog>
                <div data-overlay-content style={{ pointerEvents: 'auto' }}>
                    <button type="button">Accept</button>
                </div>
            </>,
        );
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        // Act
        await user.click(screen.getByRole('button', { name: 'Accept', hidden: true }));

        // Assert
        expect(handleOpenChange).not.toHaveBeenCalled();
        expect(screen.getByRole('dialog')).toBeInTheDocument();
    });

    it('DialogContent_DismissesTheToastsOnScreen_WhenItOpens', () => {
        // Arrange
        const dismissAll = vi.fn();
        const { rerender } = render(
            <ToastContext
                value={{ toasts: [{ id: 'toast-1', title: 'Saved' }], toast: vi.fn(), dismiss: vi.fn(), dismissAll }}
            >
                <Dialog open={false}>
                    <DialogContent>
                        <DialogTitle>Title</DialogTitle>
                    </DialogContent>
                </Dialog>
            </ToastContext>,
        );

        // Act
        rerender(
            <ToastContext
                value={{ toasts: [{ id: 'toast-1', title: 'Saved' }], toast: vi.fn(), dismiss: vi.fn(), dismissAll }}
            >
                <Dialog open>
                    <DialogContent>
                        <DialogTitle>Title</DialogTitle>
                    </DialogContent>
                </Dialog>
            </ToastContext>,
        );

        // Assert
        expect(dismissAll).toHaveBeenCalledTimes(1);
    });

    it('DialogContent_KeepsAToast_WhenTheToastShowsWhileItIsOpen', () => {
        // Arrange
        const dismissAll = vi.fn();
        const { rerender } = render(
            <ToastContext value={{ toasts: [], toast: vi.fn(), dismiss: vi.fn(), dismissAll }}>
                <Dialog open>
                    <DialogContent>
                        <DialogTitle>Title</DialogTitle>
                    </DialogContent>
                </Dialog>
            </ToastContext>,
        );

        // Act
        rerender(
            <ToastContext
                value={{ toasts: [{ id: 'toast-1', title: 'Saved' }], toast: vi.fn(), dismiss: vi.fn(), dismissAll }}
            >
                <Dialog open>
                    <DialogContent>
                        <DialogTitle>Title</DialogTitle>
                    </DialogContent>
                </Dialog>
            </ToastContext>,
        );

        // Assert
        expect(dismissAll).toHaveBeenCalledTimes(1);
    });

    it.each([
        ['toast-viewport', 'View'],
        ['toast', 'Undo'],
        ['toast-tray', 'Clear all'],
    ])('DialogContent_StaysOpen_WhenAToastControlIsPressed (%s)', async (className, label) => {
        // Arrange
        const user = userEvent.setup();
        const handleOpenChange = vi.fn();
        render(
            <>
                <Dialog open onOpenChange={handleOpenChange}>
                    <DialogContent>
                        <DialogTitle>Title</DialogTitle>
                    </DialogContent>
                </Dialog>
                <div className={className} style={{ pointerEvents: 'auto' }}>
                    <button type="button">{label}</button>
                </div>
            </>,
        );
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        // Act
        await user.click(screen.getByRole('button', { name: label, hidden: true }));

        // Assert
        expect(handleOpenChange).not.toHaveBeenCalled();
        expect(screen.getByRole('dialog')).toBeInTheDocument();
    });

    it('closes dialog when Escape key is pressed', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.keyboard('{Escape}');

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });

    it('hides close button when hideCloseButton is true', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent hideCloseButton>
                    <DialogTitle>Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    });

    it('renders footer with action buttons', async () => {
        const user = userEvent.setup();
        const handleConfirm = vi.fn();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogHeader>
                        <DialogTitle>Confirm Action</DialogTitle>
                    </DialogHeader>
                    <DialogFooter>
                        <DialogClose asChild>
                            <button>Cancel</button>
                        </DialogClose>
                        <button onClick={handleConfirm}>Confirm</button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Confirm' })).toBeInTheDocument();
        });
    });

    it('calls onOpenChange callback', async () => {
        const user = userEvent.setup();
        const handleOpenChange = vi.fn();

        render(
            <Dialog onOpenChange={handleOpenChange}>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        expect(handleOpenChange).toHaveBeenCalledWith(true);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.keyboard('{Escape}');

        expect(handleOpenChange).toHaveBeenCalledWith(false);
    });

    it('supports controlled open state', async () => {
        const { rerender } = render(
            <Dialog open={false}>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

        rerender(
            <Dialog open={true}>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
    });

    it('applies custom className to content', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent className="custom-dialog">
                    <DialogTitle>Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const content = document.querySelector('.dialog-content');
            expect(content).toHaveClass('dialog-content');
            expect(content).toHaveClass('custom-dialog');
        });
    });

    it('applies custom className to header', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogHeader className="custom-header">
                        <DialogTitle>Title</DialogTitle>
                    </DialogHeader>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const header = document.querySelector('.dialog-header');
            expect(header).toHaveClass('dialog-header');
            expect(header).toHaveClass('custom-header');
        });
    });

    it('applies custom className to footer', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                    <DialogFooter className="custom-footer">
                        <button>OK</button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const footer = document.querySelector('.dialog-footer');
            expect(footer).toHaveClass('dialog-footer');
            expect(footer).toHaveClass('custom-footer');
        });
    });

    it('applies custom className to title', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle className="custom-title">Title</DialogTitle>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const title = document.querySelector('.dialog-title');
            expect(title).toHaveClass('dialog-title');
            expect(title).toHaveClass('custom-title');
        });
    });

    it('applies custom className to description', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                    <DialogDescription className="custom-description">Description text</DialogDescription>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const description = document.querySelector('.dialog-description');
            expect(description).toHaveClass('dialog-description');
            expect(description).toHaveClass('custom-description');
        });
    });

    it('traps focus inside the dialog', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogHeader>
                        <DialogTitle>Title</DialogTitle>
                    </DialogHeader>
                    <DialogFooter>
                        <button>First</button>
                        <button>Second</button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.tab();
        await user.tab();
        await user.tab();

        const dialog = screen.getByRole('dialog');
        expect(dialog.contains(document.activeElement)).toBe(true);
    });

    it('traps focus with Shift+Tab (reverse tab)', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogHeader>
                        <DialogTitle>Title</DialogTitle>
                    </DialogHeader>
                    <DialogFooter>
                        <button>First</button>
                        <button>Second</button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.tab({ shift: true });
        await user.tab({ shift: true });
        await user.tab({ shift: true });
        await user.tab({ shift: true });

        const dialog = screen.getByRole('dialog');
        expect(dialog.contains(document.activeElement)).toBe(true);
    });

    it('returns focus to trigger after closing with Escape', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogHeader>
                        <DialogTitle>Title</DialogTitle>
                    </DialogHeader>
                </DialogContent>
            </Dialog>,
        );

        const trigger = screen.getByRole('button', { name: 'Open' });
        await user.click(trigger);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.keyboard('{Escape}');

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });

        expect(document.activeElement).toBe(trigger);
    });

    it('returns focus to trigger after closing with close button', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogHeader>
                        <DialogTitle>Title</DialogTitle>
                    </DialogHeader>
                </DialogContent>
            </Dialog>,
        );

        const trigger = screen.getByRole('button', { name: 'Open' });
        await user.click(trigger);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Close' }));

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });

        expect(document.activeElement).toBe(trigger);
    });

    it('renders body children with the padded dialog-body class', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogHeader>
                        <DialogTitle>Title</DialogTitle>
                    </DialogHeader>
                    <DialogBody>
                        <p>Body content</p>
                    </DialogBody>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const body = document.querySelector('.dialog-body');
            expect(body).toBeInTheDocument();
            expect(body).toHaveClass('dialog-body');
            expect(body).toContainElement(screen.getByText('Body content'));
        });
    });

    it('applies custom className to body', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent>
                    <DialogTitle>Title</DialogTitle>
                    <DialogBody className="custom-body">Content</DialogBody>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const body = document.querySelector('.dialog-body');
            expect(body).toHaveClass('dialog-body');
            expect(body).toHaveClass('custom-body');
        });
    });
});

describe('DialogClose', () => {
    it('closes dialog when clicked', async () => {
        const user = userEvent.setup();

        render(
            <Dialog>
                <DialogTrigger asChild>
                    <button>Open</button>
                </DialogTrigger>
                <DialogContent hideCloseButton>
                    <DialogTitle>Title</DialogTitle>
                    <DialogClose asChild>
                        <button>Close Dialog</button>
                    </DialogClose>
                </DialogContent>
            </Dialog>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Close Dialog' }));

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });
});

describe('Dialog body pointer-events safety net', () => {
    afterEach(() => {
        document.body.style.pointerEvents = '';
    });

    const ControlledDialog = ({ open }: { open: boolean }) => (
        <Dialog open={open}>
            <DialogContent>
                <DialogTitle>Title</DialogTitle>
            </DialogContent>
        </Dialog>
    );

    it('restores body pointer-events even when radix leaves a stale lock', async () => {
        document.body.style.pointerEvents = 'none';

        const { rerender } = render(<ControlledDialog open={true} />);
        await waitFor(() => expect(screen.getByRole('dialog')).toBeInTheDocument());
        expect(document.body.style.pointerEvents).toBe('none');

        rerender(<ControlledDialog open={false} />);
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        await waitFor(() => expect(document.body.style.pointerEvents).toBe(''));
    });
});
