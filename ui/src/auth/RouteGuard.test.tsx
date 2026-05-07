import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

import { AuthProvider } from './AuthContext';
import { RequireAuth, RoleGuard, PermissionGuard } from './RouteGuard';
import { Role } from './types';

function renderWithRouter(ui: React.ReactNode, { route = '/' }: { route?: string } = {}) {
    return render(<MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>);
}

describe('RequireAuth', () => {
    it('renders children when authenticated', () => {
        const user = { id: '1', username: 'test', role: Role.Viewer };

        renderWithRouter(
            <AuthProvider initialUser={user}>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <RequireAuth>
                                <span>Protected content</span>
                            </RequireAuth>
                        }
                    />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.getByText('Protected content')).toBeInTheDocument();
    });

    it('redirects to login when not authenticated', () => {
        renderWithRouter(
            <AuthProvider>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <RequireAuth>
                                <span>Protected content</span>
                            </RequireAuth>
                        }
                    />
                    <Route path="/login" element={<span>Login page</span>} />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.queryByText('Protected content')).not.toBeInTheDocument();
        expect(screen.getByText('Login page')).toBeInTheDocument();
    });

    it('uses custom login path when specified', () => {
        renderWithRouter(
            <AuthProvider>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <RequireAuth loginPath="/auth">
                                <span>Protected content</span>
                            </RequireAuth>
                        }
                    />
                    <Route path="/auth" element={<span>Custom login</span>} />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.getByText('Custom login')).toBeInTheDocument();
    });

    it('renders loading state while loading', () => {
        renderWithRouter(
            <AuthProvider initialLoading={true} skipSessionInit>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <RequireAuth>
                                <span>Protected content</span>
                            </RequireAuth>
                        }
                    />
                    <Route path="/login" element={<span>Login page</span>} />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.queryByText('Protected content')).not.toBeInTheDocument();
        expect(screen.queryByText('Login page')).not.toBeInTheDocument();
        expect(screen.getByRole('status', { name: 'Loading page' })).toBeInTheDocument();
    });
});

describe('RoleGuard', () => {
    it('renders children when user meets role requirement', () => {
        const user = { id: '1', username: 'admin', role: Role.Admin };

        renderWithRouter(
            <AuthProvider initialUser={user}>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <RoleGuard minRole={Role.Admin}>
                                <span>Admin content</span>
                            </RoleGuard>
                        }
                    />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.getByText('Admin content')).toBeInTheDocument();
    });

    it('redirects to forbidden when user does not meet role requirement', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        renderWithRouter(
            <AuthProvider initialUser={user}>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <RoleGuard minRole={Role.Admin}>
                                <span>Admin content</span>
                            </RoleGuard>
                        }
                    />
                    <Route path="/forbidden" element={<span>Forbidden page</span>} />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.queryByText('Admin content')).not.toBeInTheDocument();
        expect(screen.getByText('Forbidden page')).toBeInTheDocument();
    });

    it('uses custom forbidden path when specified', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        renderWithRouter(
            <AuthProvider initialUser={user}>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <RoleGuard minRole={Role.Admin} forbiddenPath="/no-access">
                                <span>Admin content</span>
                            </RoleGuard>
                        }
                    />
                    <Route path="/no-access" element={<span>No access page</span>} />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.getByText('No access page')).toBeInTheDocument();
    });

    it('allows Manager when Manager role is required', () => {
        const user = { id: '1', username: 'manager', role: Role.Manager };

        renderWithRouter(
            <AuthProvider initialUser={user}>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <RoleGuard minRole={Role.Manager}>
                                <span>Manager content</span>
                            </RoleGuard>
                        }
                    />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.getByText('Manager content')).toBeInTheDocument();
    });

    it('renders loading state while auth is resolving rather than redirecting to forbidden', () => {
        // Race condition guarded against: a fresh page load on an admin-only route
        // where the AuthProvider hasn't resolved the stored user yet. Without the
        // isLoading short-circuit, hasRole(Admin) returns false for one render cycle
        // and a real admin briefly lands on /forbidden.
        renderWithRouter(
            <AuthProvider initialLoading={true} skipSessionInit>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <RoleGuard minRole={Role.Admin}>
                                <span>Admin content</span>
                            </RoleGuard>
                        }
                    />
                    <Route path="/forbidden" element={<span>Forbidden page</span>} />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.queryByText('Admin content')).not.toBeInTheDocument();
        expect(screen.queryByText('Forbidden page')).not.toBeInTheDocument();
        expect(screen.getByRole('status', { name: 'Loading page' })).toBeInTheDocument();
    });
});

describe('PermissionGuard', () => {
    it('renders children when user has permission', () => {
        const user = { id: '1', username: 'manager', role: Role.Manager };

        renderWithRouter(
            <AuthProvider initialUser={user}>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <PermissionGuard permission="content.edit">
                                <span>Edit page</span>
                            </PermissionGuard>
                        }
                    />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.getByText('Edit page')).toBeInTheDocument();
    });

    it('redirects to forbidden when user lacks permission', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        renderWithRouter(
            <AuthProvider initialUser={user}>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <PermissionGuard permission="users.manage">
                                <span>User management</span>
                            </PermissionGuard>
                        }
                    />
                    <Route path="/forbidden" element={<span>Forbidden</span>} />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.queryByText('User management')).not.toBeInTheDocument();
        expect(screen.getByText('Forbidden')).toBeInTheDocument();
    });

    it('handles not authenticated state', () => {
        renderWithRouter(
            <AuthProvider>
                <Routes>
                    <Route
                        path="/"
                        element={
                            <PermissionGuard permission="content.view">
                                <span>Content</span>
                            </PermissionGuard>
                        }
                    />
                    <Route path="/forbidden" element={<span>Forbidden</span>} />
                </Routes>
            </AuthProvider>,
        );

        expect(screen.getByText('Forbidden')).toBeInTheDocument();
    });
});
