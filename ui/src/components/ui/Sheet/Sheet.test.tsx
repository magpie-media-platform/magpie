import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import {
    Sheet,
    SheetTrigger,
    SheetContent,
    SheetHeader,
    SheetFooter,
    SheetTitle,
    SheetDescription,
    SheetClose,
    SheetOverlay,
    SheetPortal,
} from './Sheet';
import { Dialog, DialogContent, DialogTitle } from '../Dialog';
import { ToastContext } from '@/hooks/ui/useToast';

describe('Sheet', () => {
    it('renders trigger element', () => {
        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument();
    });

    it('opens sheet when trigger is clicked', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Sheet Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
    });

    it('renders title and description', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetHeader>
                        <SheetTitle>Sheet Title</SheetTitle>
                        <SheetDescription>This is a description</SheetDescription>
                    </SheetHeader>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByText('Sheet Title')).toBeInTheDocument();
            expect(screen.getByText('This is a description')).toBeInTheDocument();
        });
    });

    it('closes sheet when close button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
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

    it('closes sheet when overlay is clicked', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const overlay = document.querySelector('.sheet-overlay');
        expect(overlay).toBeInTheDocument();
        await user.click(overlay!);

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });

    it('closes sheet when Escape key is pressed', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
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
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent hideCloseButton>
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
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
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetHeader>
                        <SheetTitle>Confirm Action</SheetTitle>
                    </SheetHeader>
                    <SheetFooter>
                        <SheetClose asChild>
                            <button>Cancel</button>
                        </SheetClose>
                        <button onClick={handleConfirm}>Confirm</button>
                    </SheetFooter>
                </SheetContent>
            </Sheet>,
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
            <Sheet onOpenChange={handleOpenChange}>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        expect(handleOpenChange).toHaveBeenCalledWith(true);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.keyboard('{Escape}');

        expect(handleOpenChange).toHaveBeenCalledWith(false);
    });

    it('does not close the sheet when interacting with a nested dialog', async () => {
        const user = userEvent.setup();
        const handleOpenChange = vi.fn();

        render(
            <Sheet open onOpenChange={handleOpenChange}>
                <SheetContent>
                    <SheetTitle>Sheet Title</SheetTitle>
                </SheetContent>
                <Dialog open>
                    <DialogContent>
                        <DialogTitle>Nested Dialog</DialogTitle>
                        <button type="button">Keep Editing</button>
                    </DialogContent>
                </Dialog>
            </Sheet>,
        );

        await waitFor(() => {
            expect(screen.getByText('Nested Dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByText('Keep Editing'));

        expect(handleOpenChange).not.toHaveBeenCalledWith(false);
        expect(screen.getByText('Sheet Title')).toBeInTheDocument();
    });

    it('SheetContent_DismissesTheToastsOnScreen_WhenItOpens', () => {
        // Arrange
        const dismissAll = vi.fn();
        const { rerender } = render(
            <ToastContext
                value={{ toasts: [{ id: 'toast-1', title: 'Saved' }], toast: vi.fn(), dismiss: vi.fn(), dismissAll }}
            >
                <Sheet open={false}>
                    <SheetContent>
                        <SheetTitle>Title</SheetTitle>
                    </SheetContent>
                </Sheet>
            </ToastContext>,
        );

        // Act
        rerender(
            <ToastContext
                value={{ toasts: [{ id: 'toast-1', title: 'Saved' }], toast: vi.fn(), dismiss: vi.fn(), dismissAll }}
            >
                <Sheet open>
                    <SheetContent>
                        <SheetTitle>Title</SheetTitle>
                    </SheetContent>
                </Sheet>
            </ToastContext>,
        );

        // Assert
        expect(dismissAll).toHaveBeenCalledTimes(1);
    });

    it('SheetContent_KeepsAToast_WhenTheToastShowsWhileItIsOpen', () => {
        // Arrange
        const dismissAll = vi.fn();
        const { rerender } = render(
            <ToastContext value={{ toasts: [], toast: vi.fn(), dismiss: vi.fn(), dismissAll }}>
                <Sheet open>
                    <SheetContent>
                        <SheetTitle>Title</SheetTitle>
                    </SheetContent>
                </Sheet>
            </ToastContext>,
        );

        // Act
        rerender(
            <ToastContext
                value={{ toasts: [{ id: 'toast-1', title: 'Saved' }], toast: vi.fn(), dismiss: vi.fn(), dismissAll }}
            >
                <Sheet open>
                    <SheetContent>
                        <SheetTitle>Title</SheetTitle>
                    </SheetContent>
                </Sheet>
            </ToastContext>,
        );

        // Assert
        expect(dismissAll).toHaveBeenCalledTimes(1);
    });

    it.each([
        ['toast-viewport', 'View'],
        ['toast', 'Undo'],
        ['toast-tray', 'Clear all'],
    ])('SheetContent_StaysOpen_WhenAToastControlIsPressed (%s)', async (className, label) => {
        // Arrange
        const user = userEvent.setup();
        const handleOpenChange = vi.fn();
        render(
            <>
                <Sheet open onOpenChange={handleOpenChange}>
                    <SheetContent>
                        <SheetTitle>Sheet Title</SheetTitle>
                    </SheetContent>
                </Sheet>
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

    it('supports controlled open state', async () => {
        const { rerender } = render(
            <Sheet open={false}>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

        rerender(
            <Sheet open={true}>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
    });

    it('applies custom className to content', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent className="custom-sheet">
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const content = document.querySelector('.sheet-content');
            expect(content).toHaveClass('sheet-content');
            expect(content).toHaveClass('custom-sheet');
        });
    });

    it('applies custom className to header', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetHeader className="custom-header">
                        <SheetTitle>Title</SheetTitle>
                    </SheetHeader>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const header = document.querySelector('.sheet-header');
            expect(header).toHaveClass('sheet-header');
            expect(header).toHaveClass('custom-header');
        });
    });

    it('applies custom className to footer', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                    <SheetFooter className="custom-footer">
                        <button>OK</button>
                    </SheetFooter>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const footer = document.querySelector('.sheet-footer');
            expect(footer).toHaveClass('sheet-footer');
            expect(footer).toHaveClass('custom-footer');
        });
    });

    it('applies custom className to title', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle className="custom-title">Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const title = document.querySelector('.sheet-title');
            expect(title).toHaveClass('sheet-title');
            expect(title).toHaveClass('custom-title');
        });
    });

    it('applies custom className to description', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                    <SheetDescription className="custom-description">Description text</SheetDescription>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const description = document.querySelector('.sheet-description');
            expect(description).toHaveClass('sheet-description');
            expect(description).toHaveClass('custom-description');
        });
    });

    it('traps focus inside the sheet', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetHeader>
                        <SheetTitle>Title</SheetTitle>
                    </SheetHeader>
                    <SheetFooter>
                        <button>First</button>
                        <button>Second</button>
                    </SheetFooter>
                </SheetContent>
            </Sheet>,
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
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetHeader>
                        <SheetTitle>Title</SheetTitle>
                    </SheetHeader>
                    <SheetFooter>
                        <button>First</button>
                        <button>Second</button>
                    </SheetFooter>
                </SheetContent>
            </Sheet>,
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
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetHeader>
                        <SheetTitle>Title</SheetTitle>
                    </SheetHeader>
                </SheetContent>
            </Sheet>,
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
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetHeader>
                        <SheetTitle>Title</SheetTitle>
                    </SheetHeader>
                </SheetContent>
            </Sheet>,
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

    it('exports SheetOverlay for compound usage', () => {
        expect(SheetOverlay).toBeDefined();
    });

    it('exports SheetPortal for compound usage', () => {
        expect(SheetPortal).toBeDefined();
    });
});

describe('Sheet sides', () => {
    it('renders with right side class by default', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent>
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const content = document.querySelector('.sheet-content');
            expect(content).toHaveAttribute('data-side', 'right');
        });
    });

    it('renders with left side class when side="left"', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent side="left">
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const content = document.querySelector('.sheet-content');
            expect(content).toHaveAttribute('data-side', 'left');
        });
    });

    it('renders with top side class when side="top"', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent side="top">
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const content = document.querySelector('.sheet-content');
            expect(content).toHaveAttribute('data-side', 'top');
        });
    });

    it('renders with bottom side class when side="bottom"', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent side="bottom">
                    <SheetTitle>Title</SheetTitle>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            const content = document.querySelector('.sheet-content');
            expect(content).toHaveAttribute('data-side', 'bottom');
        });
    });
});

describe('Sheet body pointer-events safety net', () => {
    afterEach(() => {
        document.body.style.pointerEvents = '';
    });

    const ControlledSheet = ({ open }: { open: boolean }) => (
        <Sheet open={open}>
            <SheetContent>
                <SheetTitle>Title</SheetTitle>
            </SheetContent>
        </Sheet>
    );

    it('locks body pointer-events while open and restores them on close', async () => {
        const { rerender } = render(<ControlledSheet open={false} />);

        rerender(<ControlledSheet open={true} />);
        await waitFor(() => expect(screen.getByRole('dialog')).toBeInTheDocument());
        expect(document.body.style.pointerEvents).toBe('none');

        rerender(<ControlledSheet open={false} />);
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        await waitFor(() => expect(document.body.style.pointerEvents).toBe(''));
    });

    it('restores body pointer-events even when radix leaves a stale lock', async () => {
        document.body.style.pointerEvents = 'none';

        const { rerender } = render(<ControlledSheet open={true} />);
        await waitFor(() => expect(screen.getByRole('dialog')).toBeInTheDocument());
        expect(document.body.style.pointerEvents).toBe('none');

        rerender(<ControlledSheet open={false} />);
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        await waitFor(() => expect(document.body.style.pointerEvents).toBe(''));
    });
});

describe('SheetClose', () => {
    it('closes sheet when clicked', async () => {
        const user = userEvent.setup();

        render(
            <Sheet>
                <SheetTrigger asChild>
                    <button>Open</button>
                </SheetTrigger>
                <SheetContent hideCloseButton>
                    <SheetTitle>Title</SheetTitle>
                    <SheetClose asChild>
                        <button>Close Sheet</button>
                    </SheetClose>
                </SheetContent>
            </Sheet>,
        );

        await user.click(screen.getByRole('button', { name: 'Open' }));

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Close Sheet' }));

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });
});
