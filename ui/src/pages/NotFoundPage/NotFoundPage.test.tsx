import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { NotFoundPage } from './NotFoundPage';

describe('NotFoundPage', () => {
    it('renders the not found message', () => {
        render(
            <MemoryRouter>
                <NotFoundPage />
            </MemoryRouter>,
        );

        expect(screen.getByText('Page not found')).toBeInTheDocument();
        expect(screen.getByText("The page you're looking for doesn't exist or may have moved.")).toBeInTheDocument();
    });

    it('renders a link to the dashboard', () => {
        render(
            <MemoryRouter>
                <NotFoundPage />
            </MemoryRouter>,
        );

        const link = screen.getByRole('link', { name: /go to dashboard/i });
        expect(link).toBeInTheDocument();
        expect(link).toHaveAttribute('href', '/');
    });

    it('has correct data-page attribute', () => {
        render(
            <MemoryRouter>
                <NotFoundPage />
            </MemoryRouter>,
        );

        expect(document.querySelector('[data-page="not-found"]')).toBeInTheDocument();
    });
});
