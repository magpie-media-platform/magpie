import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import {
    Tooltip,
    TooltipProvider,
    TooltipRoot,
    TooltipTrigger,
    TooltipContent,
    TooltipPortal,
    TooltipArrow,
} from './Tooltip';

function renderWithProvider(ui: React.ReactNode) {
    return render(<TooltipProvider>{ui}</TooltipProvider>);
}

describe('Tooltip (simple API)', () => {
    it('renders the trigger element', () => {
        renderWithProvider(
            <Tooltip content="Tooltip text">
                <button>Hover me</button>
            </Tooltip>,
        );
        expect(screen.getByRole('button', { name: 'Hover me' })).toBeInTheDocument();
    });

    it('shows tooltip content on hover', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <Tooltip content="Tooltip text" delayDuration={0}>
                <button>Hover me</button>
            </Tooltip>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            expect(screen.getByRole('tooltip')).toBeInTheDocument();
        });
    });

    it('updates trigger state on mouse leave', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <Tooltip content="Tooltip text" delayDuration={0}>
                <button>Hover me</button>
            </Tooltip>,
        );

        const button = screen.getByRole('button');
        await user.hover(button);
        await waitFor(() => {
            expect(screen.getByRole('tooltip')).toBeInTheDocument();
        });

        await user.click(document.body);
        await waitFor(() => {
            const triggerState = button.getAttribute('data-state');
            expect(triggerState === 'closed' || triggerState === null).toBe(true);
        });
    });

    it('shows tooltip on focus', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <Tooltip content="Tooltip text" delayDuration={0}>
                <button>Focus me</button>
            </Tooltip>,
        );

        await user.tab();
        expect(screen.getByRole('button')).toHaveFocus();

        await waitFor(() => {
            expect(screen.getByRole('tooltip')).toBeInTheDocument();
        });
    });

    it('applies side prop', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <Tooltip content="Tooltip text" side="bottom" delayDuration={0}>
                <button>Hover me</button>
            </Tooltip>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            const content = document.querySelector('.tooltip-content');
            expect(content).toHaveAttribute('data-side', 'bottom');
        });
    });

    it('applies align prop', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <Tooltip content="Tooltip text" align="start" delayDuration={0}>
                <button>Hover me</button>
            </Tooltip>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            const content = document.querySelector('.tooltip-content');
            expect(content).toHaveAttribute('data-align', 'start');
        });
    });

    it('renders ReactNode content', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <Tooltip content={<span data-testid="custom-content">Custom Content</span>} delayDuration={0}>
                <button>Hover me</button>
            </Tooltip>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            const elements = screen.getAllByTestId('custom-content');
            expect(elements.length).toBeGreaterThanOrEqual(1);
        });
    });

    it('uses default side of top', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <Tooltip content="Tooltip text" delayDuration={0}>
                <button>Hover me</button>
            </Tooltip>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            const content = document.querySelector('.tooltip-content');
            expect(content).toHaveAttribute('data-side', 'top');
        });
    });

    it('uses default align of center', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <Tooltip content="Tooltip text" delayDuration={0}>
                <button>Hover me</button>
            </Tooltip>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            const content = document.querySelector('.tooltip-content');
            expect(content).toHaveAttribute('data-align', 'center');
        });
    });
});

describe('Tooltip (compound API)', () => {
    it('renders with compound components', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <TooltipRoot delayDuration={0}>
                <TooltipTrigger asChild>
                    <button>Trigger</button>
                </TooltipTrigger>
                <TooltipPortal>
                    <TooltipContent>
                        Compound tooltip
                        <TooltipArrow />
                    </TooltipContent>
                </TooltipPortal>
            </TooltipRoot>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            expect(screen.getByRole('tooltip')).toBeInTheDocument();
        });
    });

    it('applies custom className to content', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <TooltipRoot delayDuration={0}>
                <TooltipTrigger asChild>
                    <button>Trigger</button>
                </TooltipTrigger>
                <TooltipPortal>
                    <TooltipContent className="custom-class">Content</TooltipContent>
                </TooltipPortal>
            </TooltipRoot>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            const content = document.querySelector('.tooltip-content');
            expect(content).toHaveClass('tooltip-content');
            expect(content).toHaveClass('custom-class');
        });
    });

    it('passes through additional props to content', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <TooltipRoot delayDuration={0}>
                <TooltipTrigger asChild>
                    <button>Trigger</button>
                </TooltipTrigger>
                <TooltipPortal>
                    <TooltipContent data-testid="tooltip-content">Content</TooltipContent>
                </TooltipPortal>
            </TooltipRoot>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            expect(screen.getByTestId('tooltip-content')).toBeInTheDocument();
        });
    });

    it('applies sideOffset to content', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <TooltipRoot delayDuration={0}>
                <TooltipTrigger asChild>
                    <button>Trigger</button>
                </TooltipTrigger>
                <TooltipPortal>
                    <TooltipContent sideOffset={10}>Content</TooltipContent>
                </TooltipPortal>
            </TooltipRoot>,
        );

        await user.hover(screen.getByRole('button'));

        await waitFor(() => {
            expect(screen.getByRole('tooltip')).toBeInTheDocument();
        });
    });
});

describe('TooltipProvider', () => {
    it('allows multiple tooltips to share state', async () => {
        const user = userEvent.setup();
        renderWithProvider(
            <>
                <Tooltip content="First tooltip" delayDuration={0}>
                    <button>First</button>
                </Tooltip>
                <Tooltip content="Second tooltip" delayDuration={0}>
                    <button>Second</button>
                </Tooltip>
            </>,
        );

        await user.hover(screen.getByRole('button', { name: 'First' }));
        await waitFor(() => {
            expect(screen.getByRole('tooltip')).toBeInTheDocument();
        });

        await user.unhover(screen.getByRole('button', { name: 'First' }));
        await user.hover(screen.getByRole('button', { name: 'Second' }));

        await waitFor(() => {
            expect(screen.getByRole('tooltip')).toBeInTheDocument();
        });
    });
});
