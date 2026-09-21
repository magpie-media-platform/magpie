import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

const auth = vi.hoisted(() => ({ hasRole: vi.fn(() => true) }));

vi.mock('@/auth/useAuth', () => ({
    useAuth: () => ({ hasRole: auth.hasRole }),
}));

import { AdminLink } from './AdminLink';

describe('AdminLink', () => {
    it('links an admin to the admin area from outside it', () => {
        auth.hasRole.mockReturnValue(true);

        render(
            <MemoryRouter initialEntries={['/library']}>
                <AdminLink />
            </MemoryRouter>,
        );

        expect(screen.getByRole('link', { name: 'Admin' })).toHaveAttribute('href', '/admin');
    });

    it.each(['/admin', '/admin/users'])('links an admin home from %s', (pathname) => {
        auth.hasRole.mockReturnValue(true);

        render(
            <MemoryRouter initialEntries={[pathname]}>
                <AdminLink />
            </MemoryRouter>,
        );

        expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/');
        expect(screen.queryByRole('link', { name: 'Admin' })).not.toBeInTheDocument();
    });

    it('renders nothing for a user below the Admin role', () => {
        auth.hasRole.mockReturnValue(false);

        render(
            <MemoryRouter initialEntries={['/library']}>
                <AdminLink />
            </MemoryRouter>,
        );

        expect(screen.queryByRole('link')).not.toBeInTheDocument();
    });

    it.each([
        ['/library', 'Admin'],
        ['/admin', 'Home'],
    ])('names the link on %s even when the text label is hidden', (pathname, name) => {
        auth.hasRole.mockReturnValue(true);

        render(
            <MemoryRouter initialEntries={[pathname]}>
                <AdminLink />
            </MemoryRouter>,
        );

        expect(screen.getByRole('link', { name })).toHaveAttribute('aria-label', name);
        expect(document.querySelector('.admin-link__label')).toHaveTextContent(name);
    });
});
