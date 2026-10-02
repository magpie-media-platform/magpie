import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ToastProvider, ToastViewport, Toast, ToastAction, ToastClose, ToastTitle, ToastDescription } from './Toast';
import { ToastList } from './ToastList';
import { ToastTray } from './ToastTray';
import { ToastStateProvider, useToast } from './index';
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

    it('adds toast to list', async () => {
        render(
            <ToastStateProvider>
                <TestUseToastComponent />
            </ToastStateProvider>,
        );

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
        render(
            <ToastStateProvider>
                <TestUseToastComponent />
            </ToastStateProvider>,
        );

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
        render(
            <ToastStateProvider>
                <TestUseToastComponent />
            </ToastStateProvider>,
        );

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
        render(
            <ToastStateProvider>
                <TestUseToastComponent />
            </ToastStateProvider>,
        );

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
        render(
            <ToastStateProvider>
                <TestUseToastComponent />
            </ToastStateProvider>,
        );

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
        render(
            <ToastStateProvider>
                <TestUseToastComponent />
            </ToastStateProvider>,
        );

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
    afterEach(() => {
        vi.useRealTimers();
        vi.unstubAllGlobals();
    });

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
                <button data-testid="add-timed" onClick={() => toast({ title: 'Timed', duration: 5000 })}>
                    Add timed
                </button>
                <button data-testid="add-blocker" onClick={() => toast({ title: 'Blocker', duration: Infinity })}>
                    Add blocker
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

    it('renders the action of a toast and announces its alternative text', async () => {
        function ActionTrigger() {
            const { toast } = useToast();
            return (
                <button
                    data-testid="add-action"
                    onClick={() =>
                        toast({
                            title: 'Scan complete',
                            description: '"Movies": 4 files ready to import.',
                            variant: 'info',
                            action: { label: 'Import now', altText: 'Open the library import page', onClick: () => {} },
                        })
                    }
                >
                    Action
                </button>
            );
        }

        render(
            <ToastProvider>
                <ToastStateProvider>
                    <ActionTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-action').click();
        });

        const toast = screen.getByText('Scan complete').closest('[data-component="toast"]') as HTMLElement;
        expect(within(toast).getByRole('button', { name: 'Import now' })).toBeInTheDocument();
        expect(await screen.findByText(/Open the library import page/)).toBeInTheDocument();
    });

    it('runs the action of a toast and dismisses the toast when the action is clicked', async () => {
        const user = userEvent.setup();
        const handleAction = vi.fn();
        function ActionTrigger() {
            const { toast } = useToast();
            return (
                <button
                    data-testid="add-action"
                    onClick={() =>
                        toast({
                            title: 'Scan complete',
                            description: '"Movies": 4 files ready to import.',
                            variant: 'info',
                            action: {
                                label: 'Import now',
                                altText: 'Open the library import page',
                                onClick: handleAction,
                            },
                        })
                    }
                >
                    Action
                </button>
            );
        }

        render(
            <ToastProvider>
                <ToastStateProvider>
                    <ActionTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-action').click();
        });
        await user.click(screen.getByRole('button', { name: 'Import now' }));

        expect(handleAction).toHaveBeenCalledTimes(1);
        await waitFor(() => {
            expect(screen.queryByText('Scan complete')).not.toBeInTheDocument();
        });
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

    it('marks each toast with its depth behind the newest toast', async () => {
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
        await act(async () => {
            screen.getByTestId('add-default').click();
        });

        const oldest = screen.getByText('Saved').closest('[data-component="toast"]');
        const middle = screen.getByText('Failed').closest('[data-component="toast"]');
        const newest = screen.getByText('Notice').closest('[data-component="toast"]');

        expect(newest).toHaveAttribute('data-stack-depth', '0');
        expect(middle).toHaveAttribute('data-stack-depth', '1');
        expect(oldest).toHaveAttribute('data-stack-depth', '2');
    });

    it('promotes the next-newest toast to the front when the front toast is dismissed', async () => {
        const user = userEvent.setup();
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
        await act(async () => {
            screen.getByTestId('add-default').click();
        });

        const front = screen.getByText('Notice').closest('[data-component="toast"]') as HTMLElement;
        await user.click(within(front).getByRole('button', { name: 'Close' }));

        await waitFor(() => {
            expect(screen.getByText('Failed').closest('[data-component="toast"]')).toHaveAttribute(
                'data-stack-depth',
                '0',
            );
        });
    });

    it('conceals the toasts behind the front toast on the mobile stack and reveals the next on dismiss', async () => {
        vi.stubGlobal('innerWidth', 390);
        const user = userEvent.setup();
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
        await act(async () => {
            screen.getByTestId('add-default').click();
        });

        const front = screen.getByText('Notice').closest('[data-component="toast"]') as HTMLElement;
        expect(front).not.toHaveAttribute('inert');
        expect(screen.getByText('Failed').closest('[data-component="toast"]')).toHaveAttribute('inert');
        expect(screen.getByText('Saved').closest('[data-component="toast"]')).toHaveAttribute('inert');

        await user.click(within(front).getByRole('button', { name: 'Close' }));

        await waitFor(() => {
            expect(screen.getByText('Failed').closest('[data-component="toast"]')).not.toHaveAttribute('inert');
        });
        expect(screen.getByText('Saved').closest('[data-component="toast"]')).toHaveAttribute('inert');
    });

    it('conceals the toast behind the front toast at a width where the bottom navigation shows', async () => {
        vi.stubGlobal('innerWidth', 700);
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

        expect(screen.getByText('Saved').closest('[data-component="toast"]')).toHaveAttribute('inert');
        expect(screen.getByText('Failed').closest('[data-component="toast"]')).not.toHaveAttribute('inert');
    });

    it('keeps the dismiss timer of a toast concealed behind the front toast paused on the mobile stack', () => {
        vi.useFakeTimers();
        vi.stubGlobal('innerWidth', 390);
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestToastListTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        act(() => {
            screen.getByTestId('add-timed').click();
        });
        act(() => {
            screen.getByTestId('add-blocker').click();
        });
        act(() => {
            vi.advanceTimersByTime(6000);
        });

        expect(screen.getByText('Timed')).toBeInTheDocument();

        act(() => {
            within(screen.getByText('Blocker').closest('[data-component="toast"]') as HTMLElement)
                .getByRole('button', { name: 'Close' })
                .click();
        });
        act(() => {
            vi.advanceTimersByTime(4999);
        });
        expect(screen.getByText('Timed')).toBeInTheDocument();
        act(() => {
            vi.advanceTimersByTime(1);
        });
        expect(screen.queryByText('Timed')).not.toBeInTheDocument();
    });

    it('leaves every toast reachable and timed off the mobile stack', () => {
        vi.useFakeTimers();
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestToastListTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        act(() => {
            screen.getByTestId('add-timed').click();
        });
        act(() => {
            screen.getByTestId('add-blocker').click();
        });

        expect(screen.getByText('Timed').closest('[data-component="toast"]')).not.toHaveAttribute('inert');

        act(() => {
            vi.advanceTimersByTime(5000);
        });
        expect(screen.queryByText('Timed')).not.toBeInTheDocument();
    });

    it('keeps the newest three toasts reachable on the mobile stack and hides only what is deeper', async () => {
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
        await act(async () => {
            screen.getByTestId('add-default').click();
        });
        await act(async () => {
            screen.getByTestId('add-success').click();
        });

        const saved = screen.getAllByText('Saved').map((el) => el.closest('[data-component="toast"]'));
        const failed = screen.getByText('Failed').closest('[data-component="toast"]');
        const notice = screen.getByText('Notice').closest('[data-component="toast"]');

        expect(saved[1]).toHaveAttribute('data-stack-hidden', 'false');
        expect(notice).toHaveAttribute('data-stack-hidden', 'false');
        expect(failed).toHaveAttribute('data-stack-hidden', 'false');
        expect(saved[0]).toHaveAttribute('data-stack-hidden', 'true');
    });

    it('pauses a hidden toast dismiss timer and resumes it once promoted back into view', async () => {
        vi.useFakeTimers();
        vi.stubGlobal(
            'matchMedia',
            vi.fn((query: string) => ({
                matches: true,
                media: query,
                onchange: null,
                addListener: () => {},
                removeListener: () => {},
                addEventListener: () => {},
                removeEventListener: () => {},
                dispatchEvent: () => false,
            })),
        );

        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestToastListTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        act(() => {
            screen.getByTestId('add-success').click();
        });
        act(() => {
            screen.getByTestId('add-error').click();
        });
        act(() => {
            screen.getByTestId('add-default').click();
        });
        act(() => {
            screen.getByTestId('add-success').click();
        });

        const hidden = screen.getAllByText('Saved')[0].closest('[data-component="toast"]');
        expect(hidden).toHaveAttribute('data-stack-hidden', 'true');

        await act(async () => {
            vi.advanceTimersByTime(6000);
        });

        expect(screen.getByText('Saved')).toBeInTheDocument();

        vi.useRealTimers();
    });

    it('ignores resume events while hidden and preserves elapsed time when promoted', () => {
        vi.useFakeTimers();
        vi.stubGlobal(
            'matchMedia',
            vi.fn((query: string) => ({
                matches: true,
                media: query,
                onchange: null,
                addListener: () => {},
                removeListener: () => {},
                addEventListener: () => {},
                removeEventListener: () => {},
                dispatchEvent: () => false,
            })),
        );

        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestToastListTrigger />
                    <ToastList />
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>,
        );

        act(() => {
            screen.getByTestId('add-timed').click();
        });
        act(() => {
            vi.advanceTimersByTime(2000);
        });
        act(() => {
            screen.getByTestId('add-blocker').click();
            screen.getByTestId('add-blocker').click();
            screen.getByTestId('add-blocker').click();
        });

        expect(screen.getByText('Timed').closest('[data-component="toast"]')).toHaveAttribute(
            'data-stack-hidden',
            'true',
        );

        act(() => {
            document.querySelector('.toast-viewport')?.dispatchEvent(new CustomEvent('toast.viewportResume'));
            vi.advanceTimersByTime(6000);
        });
        expect(screen.getByText('Timed')).toBeInTheDocument();

        const blocker1 = screen.getAllByText('Blocker')[0].closest('[data-component="toast"]') as HTMLElement;
        act(() => {
            within(blocker1).getByRole('button', { name: 'Close' }).click();
        });
        const blocker2 = screen.getAllByText('Blocker')[0].closest('[data-component="toast"]') as HTMLElement;
        act(() => {
            within(blocker2).getByRole('button', { name: 'Close' }).click();
        });
        const blocker3 = screen.getAllByText('Blocker')[0].closest('[data-component="toast"]') as HTMLElement;
        act(() => {
            within(blocker3).getByRole('button', { name: 'Close' }).click();
        });

        expect(screen.getByText('Timed').closest('[data-component="toast"]')).toHaveAttribute(
            'data-stack-hidden',
            'false',
        );
        act(() => {
            vi.advanceTimersByTime(2999);
        });
        expect(screen.getByText('Timed')).toBeInTheDocument();
        act(() => {
            vi.advanceTimersByTime(1);
        });
        expect(screen.queryByText('Timed')).not.toBeInTheDocument();
    });
});

describe('ToastTray', () => {
    function TestTrayTrigger() {
        const { toast } = useToast();

        return (
            <button data-testid="add-toast" onClick={() => toast({ title: 'Toast', description: 'A toast.' })}>
                Add
            </button>
        );
    }

    it('renders nothing with no toasts', () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestTrayTrigger />
                    <ToastList />
                    <ToastViewport>
                        <ToastTray />
                    </ToastViewport>
                </ToastStateProvider>
            </ToastProvider>,
        );

        expect(screen.queryByRole('button', { name: 'Clear all notifications' })).not.toBeInTheDocument();
        expect(document.querySelector('.toast-tray')).not.toBeInTheDocument();
    });

    it('renders nothing with a single toast', async () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestTrayTrigger />
                    <ToastList />
                    <ToastViewport>
                        <ToastTray />
                    </ToastViewport>
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-toast').click();
        });

        expect(screen.getAllByTestId('toast-title')).toHaveLength(1);
        expect(screen.queryByRole('button', { name: 'Clear all notifications' })).not.toBeInTheDocument();
        expect(document.querySelector('.toast-tray')).not.toBeInTheDocument();
    });

    it('shows the clear-all button with two toasts', async () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestTrayTrigger />
                    <ToastList />
                    <ToastViewport>
                        <ToastTray />
                    </ToastViewport>
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-toast').click();
        });
        await act(async () => {
            screen.getByTestId('add-toast').click();
        });

        expect(screen.getAllByTestId('toast-title')).toHaveLength(2);
        expect(screen.getByRole('button', { name: 'Clear all notifications' })).toBeInTheDocument();
    });

    it('dismisses all toasts when clicked', async () => {
        const user = userEvent.setup();
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestTrayTrigger />
                    <ToastList />
                    <ToastViewport>
                        <ToastTray />
                    </ToastViewport>
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-toast').click();
        });
        await act(async () => {
            screen.getByTestId('add-toast').click();
        });

        await user.click(screen.getByRole('button', { name: 'Clear all notifications' }));

        await waitFor(() => {
            expect(screen.queryAllByTestId('toast-title')).toHaveLength(0);
        });
        expect(screen.queryByRole('button', { name: 'Clear all notifications' })).not.toBeInTheDocument();
    });

    it('never renders an expand/collapse toggle', async () => {
        render(
            <ToastProvider>
                <ToastStateProvider>
                    <TestTrayTrigger />
                    <ToastList />
                    <ToastViewport>
                        <ToastTray />
                    </ToastViewport>
                </ToastStateProvider>
            </ToastProvider>,
        );

        await act(async () => {
            screen.getByTestId('add-toast').click();
        });
        await act(async () => {
            screen.getByTestId('add-toast').click();
        });

        expect(screen.queryByRole('button', { name: /^(Show all|Collapse)/ })).not.toBeInTheDocument();
        expect(document.querySelector('.toast-tray__stack-toggle')).not.toBeInTheDocument();
    });
});
