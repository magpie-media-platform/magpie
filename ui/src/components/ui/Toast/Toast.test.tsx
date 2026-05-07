import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ToastProvider, ToastViewport, Toast, ToastAction, ToastClose, ToastTitle, ToastDescription } from './Toast';
import { ToastStateProvider } from './ToastContext';
import { useToast } from './useToast';
import { ToastList } from './ToastList';
import { LiveAnnouncerProvider } from '../LiveAnnouncer';

function TestToastComponent() {
    return (
        <ToastProvider>
            <Toast open>
                <ToastTitle>Test Title</ToastTitle>
                <ToastDescription>Test description</ToastDescription>
                <ToastClose />
            </Toast>
            <ToastViewport />
        </ToastProvider>
    );
}

describe('Toast', () => {
    it('renders title and description', () => {
        render(<TestToastComponent />);

        expect(screen.getByText('Test Title')).toBeInTheDocument();
        expect(screen.getByText('Test description')).toBeInTheDocument();
    });

    it('renders close button with aria-label', () => {
        render(<TestToastComponent />);

        expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument();
    });

    it('applies default variant class', () => {
        render(
            <ToastProvider>
                <Toast open data-testid="toast">
                    <ToastTitle>Test</ToastTitle>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        const toast = screen.getByTestId('toast');
        expect(toast).toHaveClass('toast');
        expect(toast).toHaveAttribute('data-variant', 'default');
    });

    it('applies success variant class', () => {
        render(
            <ToastProvider>
                <Toast open variant="success" data-testid="toast">
                    <ToastTitle>Success</ToastTitle>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        expect(screen.getByTestId('toast')).toHaveAttribute('data-variant', 'success');
    });

    it('applies error variant class', () => {
        render(
            <ToastProvider>
                <Toast open variant="error" data-testid="toast">
                    <ToastTitle>Error</ToastTitle>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        expect(screen.getByTestId('toast')).toHaveAttribute('data-variant', 'error');
    });

    it('applies warning variant class', () => {
        render(
            <ToastProvider>
                <Toast open variant="warning" data-testid="toast">
                    <ToastTitle>Warning</ToastTitle>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        expect(screen.getByTestId('toast')).toHaveAttribute('data-variant', 'warning');
    });

    it('applies info variant class', () => {
        render(
            <ToastProvider>
                <Toast open variant="info" data-testid="toast">
                    <ToastTitle>Info</ToastTitle>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        expect(screen.getByTestId('toast')).toHaveAttribute('data-variant', 'info');
    });

    it('applies custom className', () => {
        render(
            <ToastProvider>
                <Toast open className="custom-toast" data-testid="toast">
                    <ToastTitle>Test</ToastTitle>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        const toast = screen.getByTestId('toast');
        expect(toast).toHaveClass('toast');
        expect(toast).toHaveClass('custom-toast');
    });

    it('closes toast when close button is clicked', async () => {
        const user = userEvent.setup();
        const handleOpenChange = vi.fn();

        render(
            <ToastProvider>
                <Toast open onOpenChange={handleOpenChange}>
                    <ToastTitle>Test</ToastTitle>
                    <ToastClose />
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'Close' }));

        expect(handleOpenChange).toHaveBeenCalledWith(false);
    });

    it('renders action button', async () => {
        const user = userEvent.setup();
        const handleAction = vi.fn();

        render(
            <ToastProvider>
                <Toast open>
                    <ToastTitle>Test</ToastTitle>
                    <ToastAction altText="Undo action" onClick={handleAction}>
                        Undo
                    </ToastAction>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        const actionButton = screen.getByRole('button', { name: 'Undo' });
        expect(actionButton).toBeInTheDocument();

        await user.click(actionButton);
        expect(handleAction).toHaveBeenCalled();
    });

    it('auto-dismisses after duration', async () => {
        vi.useFakeTimers();
        const handleOpenChange = vi.fn();

        render(
            <ToastProvider duration={100}>
                <Toast open onOpenChange={handleOpenChange}>
                    <ToastTitle>Test</ToastTitle>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        await act(async () => {
            vi.advanceTimersByTime(150);
        });

        expect(handleOpenChange).toHaveBeenCalledWith(false);

        vi.useRealTimers();
    });
});

describe('ToastViewport', () => {
    it('applies default bottom-right position', () => {
        render(
            <ToastProvider>
                <ToastViewport data-testid="viewport" />
            </ToastProvider>,
        );

        expect(screen.getByTestId('viewport')).toHaveAttribute('data-position', 'bottom-right');
    });

    it('applies top-right position', () => {
        render(
            <ToastProvider>
                <ToastViewport position="top-right" data-testid="viewport" />
            </ToastProvider>,
        );

        expect(screen.getByTestId('viewport')).toHaveAttribute('data-position', 'top-right');
    });

    it('applies top-left position', () => {
        render(
            <ToastProvider>
                <ToastViewport position="top-left" data-testid="viewport" />
            </ToastProvider>,
        );

        expect(screen.getByTestId('viewport')).toHaveAttribute('data-position', 'top-left');
    });

    it('applies bottom-left position', () => {
        render(
            <ToastProvider>
                <ToastViewport position="bottom-left" data-testid="viewport" />
            </ToastProvider>,
        );

        expect(screen.getByTestId('viewport')).toHaveAttribute('data-position', 'bottom-left');
    });

    it('applies custom className', () => {
        render(
            <ToastProvider>
                <ToastViewport className="custom-viewport" data-testid="viewport" />
            </ToastProvider>,
        );

        const viewport = screen.getByTestId('viewport');
        expect(viewport).toHaveClass('toast-viewport');
        expect(viewport).toHaveClass('custom-viewport');
    });
});

describe('ToastTitle', () => {
    it('applies custom className', () => {
        render(
            <ToastProvider>
                <Toast open>
                    <ToastTitle className="custom-title" data-testid="title">
                        Test Title
                    </ToastTitle>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        const title = screen.getByTestId('title');
        expect(title).toHaveClass('toast-title');
        expect(title).toHaveClass('custom-title');
    });
});

describe('ToastDescription', () => {
    it('applies custom className', () => {
        render(
            <ToastProvider>
                <Toast open>
                    <ToastTitle>Test</ToastTitle>
                    <ToastDescription className="custom-desc" data-testid="desc">
                        Description
                    </ToastDescription>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        const desc = screen.getByTestId('desc');
        expect(desc).toHaveClass('toast-description');
        expect(desc).toHaveClass('custom-desc');
    });
});

describe('ToastAction', () => {
    it('applies custom className', () => {
        render(
            <ToastProvider>
                <Toast open>
                    <ToastTitle>Test</ToastTitle>
                    <ToastAction className="custom-action" altText="Test action">
                        Action
                    </ToastAction>
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        const action = screen.getByRole('button', { name: 'Action' });
        expect(action).toHaveClass('toast-action');
        expect(action).toHaveClass('custom-action');
    });
});

describe('ToastClose', () => {
    it('applies custom className', () => {
        render(
            <ToastProvider>
                <Toast open>
                    <ToastTitle>Test</ToastTitle>
                    <ToastClose className="custom-close" />
                </Toast>
                <ToastViewport />
            </ToastProvider>,
        );

        const close = screen.getByRole('button', { name: 'Close' });
        expect(close).toHaveClass('toast-close');
        expect(close).toHaveClass('custom-close');
    });
});

describe('useToast', () => {
    function TestUseToastComponent() {
        const { toasts, toast, dismiss, dismissAll } = useToast();

        return (
            <div>
                <button
                    data-testid="add-toast"
                    onClick={() => toast({ title: 'Test toast', description: 'Test description' })}
                >
                    Add Toast
                </button>
                <button
                    data-testid="add-success"
                    onClick={() => toast({ title: 'Success', variant: 'success', description: 'It worked!' })}
                >
                    Add Success
                </button>
                <button data-testid="dismiss-all" onClick={() => dismissAll()}>
                    Dismiss All
                </button>
                <div data-testid="toast-count">{toasts.length}</div>
                {toasts.map((t) => (
                    <div key={t.id} data-testid={`toast-${t.id}`}>
                        <span data-testid={`title-${t.id}`}>{t.title}</span>
                        <span data-testid={`desc-${t.id}`}>{t.description}</span>
                        <span data-testid={`variant-${t.id}`}>{t.variant}</span>
                        <button data-testid={`dismiss-${t.id}`} onClick={() => dismiss(t.id)}>
                            Dismiss
                        </button>
                    </div>
                ))}
            </div>
        );
    }

    function renderWithProvider() {
        return render(
            <ToastStateProvider>
                <TestUseToastComponent />
            </ToastStateProvider>,
        );
    }

    it('adds toast to list', async () => {
        renderWithProvider();

        expect(screen.getByTestId('toast-count')).toHaveTextContent('0');

        await act(async () => {
            screen.getByTestId('add-toast').click();
        });

        await waitFor(() => {
            expect(screen.getByTestId('toast-count')).toHaveTextContent('1');
        });
        expect(screen.getByText('Test toast')).toBeInTheDocument();
        expect(screen.getByText('Test description')).toBeInTheDocument();
    });

    it('adds multiple toasts', async () => {
        renderWithProvider();

        await act(async () => {
            screen.getByTestId('add-toast').click();
        });
        await act(async () => {
            screen.getByTestId('add-success').click();
        });

        await waitFor(() => {
            expect(screen.getByTestId('toast-count')).toHaveTextContent('2');
        });
        expect(screen.getByText('Test toast')).toBeInTheDocument();
        expect(screen.getByText('Success')).toBeInTheDocument();
    });

    it('dismisses single toast', async () => {
        renderWithProvider();

        await act(async () => {
            screen.getByTestId('add-toast').click();
        });

        await waitFor(() => {
            expect(screen.getByTestId('toast-count')).toHaveTextContent('1');
        });

        const dismissButton = screen.getByText('Dismiss');
        await act(async () => {
            dismissButton.click();
        });

        await waitFor(() => {
            expect(screen.getByTestId('toast-count')).toHaveTextContent('0');
        });
    });

    it('dismisses all toasts', async () => {
        renderWithProvider();

        await act(async () => {
            screen.getByTestId('add-toast').click();
        });
        await act(async () => {
            screen.getByTestId('add-success').click();
        });

        await waitFor(() => {
            expect(screen.getByTestId('toast-count')).toHaveTextContent('2');
        });

        await act(async () => {
            screen.getByTestId('dismiss-all').click();
        });

        await waitFor(() => {
            expect(screen.getByTestId('toast-count')).toHaveTextContent('0');
        });
    });

    it('generates unique IDs for each toast', async () => {
        renderWithProvider();

        await act(async () => {
            screen.getByTestId('add-toast').click();
        });
        await act(async () => {
            screen.getByTestId('add-toast').click();
        });

        await waitFor(() => {
            expect(screen.getByTestId('toast-count')).toHaveTextContent('2');
        });

        const toastElements = screen.getAllByTestId(/^toast-toast-/);
        const ids = toastElements.map((el) => el.getAttribute('data-testid'));
        const uniqueIds = new Set(ids);

        expect(uniqueIds.size).toBe(ids.length);
    });

    it('preserves toast variant', async () => {
        renderWithProvider();

        await act(async () => {
            screen.getByTestId('add-success').click();
        });

        await waitFor(() => {
            expect(screen.getByTestId('toast-count')).toHaveTextContent('1');
        });
        expect(screen.getByText('success')).toBeInTheDocument();
    });
});

describe('useToast with LiveAnnouncer', () => {
    function TestToastWithAnnouncer() {
        const { toast } = useToast();

        return (
            <div>
                <button
                    data-testid="add-default"
                    onClick={() => toast({ title: 'Default toast', description: 'Description' })}
                >
                    Default
                </button>
                <button
                    data-testid="add-error"
                    onClick={() => toast({ title: 'Error toast', variant: 'error', description: 'Failed' })}
                >
                    Error
                </button>
                <button data-testid="add-success" onClick={() => toast({ title: 'Success', variant: 'success' })}>
                    Success
                </button>
                <button data-testid="add-title-only" onClick={() => toast({ title: 'Just title' })}>
                    Title Only
                </button>
                <button data-testid="add-desc-only" onClick={() => toast({ description: 'Just description' })}>
                    Desc Only
                </button>
            </div>
        );
    }

    it('announces toast to polite region by default', async () => {
        render(
            <LiveAnnouncerProvider>
                <ToastStateProvider>
                    <TestToastWithAnnouncer />
                </ToastStateProvider>
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-default').click();
        });

        const politeRegion = document.querySelector('[data-politeness="polite"]');
        expect(politeRegion).toHaveTextContent('Default toast. Description');
    });

    it('announces error toasts to assertive region', async () => {
        render(
            <LiveAnnouncerProvider>
                <ToastStateProvider>
                    <TestToastWithAnnouncer />
                </ToastStateProvider>
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-error').click();
        });

        const assertiveRegion = document.querySelector('[data-politeness="assertive"]');
        expect(assertiveRegion).toHaveTextContent('Error toast. Failed');
    });

    it('announces success toasts to polite region', async () => {
        render(
            <LiveAnnouncerProvider>
                <ToastStateProvider>
                    <TestToastWithAnnouncer />
                </ToastStateProvider>
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-success').click();
        });

        const politeRegion = document.querySelector('[data-politeness="polite"]');
        expect(politeRegion).toHaveTextContent('Success');
    });

    it('announces title only when no description', async () => {
        render(
            <LiveAnnouncerProvider>
                <ToastStateProvider>
                    <TestToastWithAnnouncer />
                </ToastStateProvider>
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-title-only').click();
        });

        const politeRegion = document.querySelector('[data-politeness="polite"]');
        expect(politeRegion).toHaveTextContent('Just title');
    });

    it('announces description only when no title', async () => {
        render(
            <LiveAnnouncerProvider>
                <ToastStateProvider>
                    <TestToastWithAnnouncer />
                </ToastStateProvider>
            </LiveAnnouncerProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-desc-only').click();
        });

        const politeRegion = document.querySelector('[data-politeness="polite"]');
        expect(politeRegion).toHaveTextContent('Just description');
    });

    it('throws without ToastStateProvider', () => {
        expect(() => {
            render(<TestToastWithAnnouncer />);
        }).toThrow('useToast must be used within a ToastStateProvider');
    });
});

describe('ToastList', () => {
    function TestToastListTrigger() {
        const { toast } = useToast();

        return (
            <div>
                <button
                    data-testid="add-success"
                    onClick={() => toast({ title: 'Saved', description: 'Settings saved.', variant: 'success' })}
                >
                    Success
                </button>
                <button
                    data-testid="add-error"
                    onClick={() => toast({ title: 'Failed', description: 'Something went wrong.', variant: 'error' })}
                >
                    Error
                </button>
                <button
                    data-testid="add-default"
                    onClick={() => toast({ title: 'Notice', description: 'A default toast.' })}
                >
                    Default
                </button>
            </div>
        );
    }

    it('renders success toast with icon', async () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestToastListTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-success').click();
        });

        expect(screen.getByText('Saved')).toBeInTheDocument();
        expect(screen.getByText('Settings saved.')).toBeInTheDocument();
        const toast = screen.getByText('Saved').closest('[data-component="toast"]');
        expect(toast).toHaveAttribute('data-variant', 'success');
        expect(toast).toHaveAttribute('role', 'status');
        expect(toast?.querySelector('.toast-list__icon--success')).toBeInTheDocument();
    });

    it('renders error toast with icon', async () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestToastListTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-error').click();
        });

        expect(screen.getByText('Failed')).toBeInTheDocument();
        expect(screen.getByText('Something went wrong.')).toBeInTheDocument();
        const toast = screen.getByText('Failed').closest('[data-component="toast"]');
        expect(toast).toHaveAttribute('data-variant', 'error');
        expect(toast).toHaveAttribute('role', 'alert');
        expect(toast?.querySelector('.toast-list__icon--error')).toBeInTheDocument();
    });

    it('renders warning toast with icon and alert role', async () => {
        function WarningTrigger() {
            const { toast } = useToast();
            return (
                <button
                    data-testid="add-warning"
                    onClick={() => toast({ title: 'Heads up', description: 'Partial failure.', variant: 'warning' })}
                >
                    Warning
                </button>
            );
        }

        render(
            <ToastProvider>
                <ToastStateProvider>
                    <WarningTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-warning').click();
        });

        expect(screen.getByText('Heads up')).toBeInTheDocument();
        expect(screen.getByText('Partial failure.')).toBeInTheDocument();
        const toast = screen.getByText('Heads up').closest('[data-component="toast"]');
        expect(toast).toHaveAttribute('data-variant', 'warning');
        expect(toast).toHaveAttribute('role', 'alert');
        expect(toast?.querySelector('.toast-list__icon--warning')).toBeInTheDocument();
    });

    it('renders info toast with icon and status role', async () => {
        function InfoTrigger() {
            const { toast } = useToast();
            return (
                <button
                    data-testid="add-info"
                    onClick={() => toast({ title: 'FYI', description: 'Just so you know.', variant: 'info' })}
                >
                    Info
                </button>
            );
        }

        render(
            <ToastProvider>
                <ToastStateProvider>
                    <InfoTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-info').click();
        });

        expect(screen.getByText('FYI')).toBeInTheDocument();
        expect(screen.getByText('Just so you know.')).toBeInTheDocument();
        const toast = screen.getByText('FYI').closest('[data-component="toast"]');
        expect(toast).toHaveAttribute('data-variant', 'info');
        expect(toast).toHaveAttribute('role', 'status');
        expect(toast?.querySelector('.toast-list__icon--info')).toBeInTheDocument();
    });

    it('renders default toast without icon', async () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestToastListTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-default').click();
        });

        expect(screen.getByText('Notice')).toBeInTheDocument();
        expect(screen.getByText('A default toast.')).toBeInTheDocument();
    });

    it('renders multiple toasts', async () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestToastListTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-success').click();
        });
        await act(async () => {
            screen.getByTestId('add-error').click();
        });

        expect(screen.getByText('Saved')).toBeInTheDocument();
        expect(screen.getByText('Failed')).toBeInTheDocument();
    });

    it('renders close button for each toast', async () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestToastListTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-success').click();
        });

        expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument();
    });

    it('renders nothing when no toasts', () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        expect(screen.queryByRole('status')).not.toBeInTheDocument();
    });
});
