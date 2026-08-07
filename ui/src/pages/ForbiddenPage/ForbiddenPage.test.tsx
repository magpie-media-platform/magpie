import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { Role } from '@/auth';
import { AuthTestProvider as AuthProvider } from '@/test/AuthTestProvider';
import { ForbiddenPage } from './ForbiddenPage';

describe('ForbiddenPage', () => {
    it('renders the access denied message', () => {
        render(
            <MemoryRouter>
                <AuthProvider>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.getByRole('heading', { name: 'Access denied', level: 1 })).toBeInTheDocument();
        expect(screen.getByText('You do not have permission to access this page.')).toBeInTheDocument();
    });

    it('renders a link to the dashboard', () => {
        render(
            <MemoryRouter>
                <AuthProvider>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        const link = screen.getByRole('link', { name: /go to dashboard/i });
        expect(link).toBeInTheDocument();
        expect(link).toHaveAttribute('href', '/');
    });

    it('renders go back button', () => {
        render(
            <MemoryRouter>
                <AuthProvider>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.getByRole('button', { name: /go back/i })).toBeInTheDocument();
    });

    it('shows user info with role when authenticated', () => {
        const user = { id: '1', username: 'testuser', role: Role.Viewer };

        render(
            <MemoryRouter>
                <AuthProvider initialUser={user}>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.getByText(/Signed in as testuser \(Viewer\)/)).toBeInTheDocument();
    });

    it('shows accessible resources for the user role', () => {
        const user = { id: '1', username: 'testuser', role: Role.Viewer };

        render(
            <MemoryRouter>
                <AuthProvider initialUser={user}>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.getByText('You have read-only access to content and status.')).toBeInTheDocument();
    });

    it('shows correct access description for Manager role', () => {
        const user = { id: '1', username: 'mgr', role: Role.Manager };

        render(
            <MemoryRouter>
                <AuthProvider initialUser={user}>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.getByText(/Signed in as mgr \(Manager\)/)).toBeInTheDocument();
        expect(
            screen.getByText('You can manage content, downloads, wishlists, filters, and tags, and run jobs.'),
        ).toBeInTheDocument();
        expect(screen.queryByText(/indexers, and clients/)).not.toBeInTheDocument();
    });

    it('does not concatenate user info into the permission message', () => {
        const user = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <MemoryRouter>
                <AuthProvider initialUser={user}>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        const description = screen.getByText('You do not have permission to access this page.');
        expect(description).toBeInTheDocument();
        expect(description.textContent).not.toContain('Signed in as');

        expect(screen.getByText(/Signed in as admin \(Admin\)/)).toBeInTheDocument();
    });

    it('does not show user info when not authenticated', () => {
        render(
            <MemoryRouter>
                <AuthProvider>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.queryByText(/signed in as/i)).not.toBeInTheDocument();
    });

    it('shows required role when provided in location state', () => {
        render(
            <MemoryRouter
                initialEntries={[
                    {
                        pathname: '/forbidden',
                        state: { requiredRole: 'Admin' },
                    },
                ]}
            >
                <AuthProvider>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.getByText(/requires Admin role/i)).toBeInTheDocument();
    });

    it('shows required permission when provided in location state', () => {
        render(
            <MemoryRouter
                initialEntries={[
                    {
                        pathname: '/forbidden',
                        state: { requiredPermission: 'users.manage' },
                    },
                ]}
            >
                <AuthProvider>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.getByText(/requires the "users.manage" permission/i)).toBeInTheDocument();
    });

    it('has correct data-page attribute', () => {
        render(
            <MemoryRouter>
                <AuthProvider>
                    <ForbiddenPage />
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(document.querySelector('[data-page="forbidden"]')).toBeInTheDocument();
    });
});
