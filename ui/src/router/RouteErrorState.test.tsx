import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { RouteErrorState } from './RouteErrorState';

describe('RouteErrorState', () => {
    it('renders error message', () => {
        render(
            <MemoryRouter>
                <RouteErrorState />
            </MemoryRouter>,
        );
        expect(screen.getByText('Failed to load page')).toBeInTheDocument();
    });

    it('renders default description', () => {
        render(
            <MemoryRouter>
                <RouteErrorState />
            </MemoryRouter>,
        );
        expect(screen.getByText('Try again or go to the dashboard.')).toBeInTheDocument();
    });

    it('renders custom message', () => {
        render(
            <MemoryRouter>
                <RouteErrorState message="Network connection lost" />
            </MemoryRouter>,
        );
        expect(screen.getByText('Network connection lost')).toBeInTheDocument();
    });

    it('has data-component attribute', () => {
        render(
            <MemoryRouter>
                <RouteErrorState />
            </MemoryRouter>,
        );
        expect(document.querySelector('[data-component="route-error"]')).toBeInTheDocument();
    });

    it('renders dashboard link', () => {
        render(
            <MemoryRouter>
                <RouteErrorState />
            </MemoryRouter>,
        );
        const link = screen.getByRole('link', { name: /go to dashboard/i });
        expect(link).toBeInTheDocument();
        expect(link).toHaveAttribute('href', '/');
    });

    it('renders retry button when onRetry provided', () => {
        const handleRetry = vi.fn();
        render(
            <MemoryRouter>
                <RouteErrorState onRetry={handleRetry} />
            </MemoryRouter>,
        );
        expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
    });

    it('does not render retry button when onRetry not provided', () => {
        render(
            <MemoryRouter>
                <RouteErrorState />
            </MemoryRouter>,
        );
        expect(screen.queryByRole('button', { name: /retry/i })).not.toBeInTheDocument();
    });

    it('calls onRetry when retry button clicked', async () => {
        const user = userEvent.setup();
        const handleRetry = vi.fn();
        render(
            <MemoryRouter>
                <RouteErrorState onRetry={handleRetry} />
            </MemoryRouter>,
        );

        await user.click(screen.getByRole('button', { name: /retry/i }));
        expect(handleRetry).toHaveBeenCalledTimes(1);
    });

    it('renders actions container', () => {
        render(
            <MemoryRouter>
                <RouteErrorState onRetry={vi.fn()} />
            </MemoryRouter>,
        );
        expect(document.querySelector('.route-error__actions')).toBeInTheDocument();
    });

    it('uses EmptyState component', () => {
        render(
            <MemoryRouter>
                <RouteErrorState />
            </MemoryRouter>,
        );
        expect(document.querySelector('[data-component="empty-state"]')).toBeInTheDocument();
    });
});
