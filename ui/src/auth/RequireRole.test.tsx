import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { AuthTestProvider as AuthProvider } from '@/test/AuthTestProvider';
import { AuthProvider as CatalogAuthProvider } from './AuthContext';
import { RequireRole } from './RequireRole';
import { Role } from './types';

describe('RequireRole', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('renders children when user meets role requirement', () => {
        const user = { id: '1', username: 'manager', role: Role.Manager };

        render(
            <AuthProvider initialUser={user}>
                <RequireRole min={Role.Manager}>
                    <span>Protected content</span>
                </RequireRole>
            </AuthProvider>,
        );

        expect(screen.getByText('Protected content')).toBeInTheDocument();
    });

    it('renders children when user exceeds role requirement', () => {
        const user = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={user}>
                <RequireRole min={Role.Manager}>
                    <span>Protected content</span>
                </RequireRole>
            </AuthProvider>,
        );

        expect(screen.getByText('Protected content')).toBeInTheDocument();
    });

    it('renders nothing when user does not meet role requirement', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        render(
            <AuthProvider initialUser={user}>
                <RequireRole min={Role.Manager}>
                    <span>Protected content</span>
                </RequireRole>
            </AuthProvider>,
        );

        expect(screen.queryByText('Protected content')).not.toBeInTheDocument();
    });

    it('renders fallback when user does not meet role requirement', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        render(
            <AuthProvider initialUser={user}>
                <RequireRole min={Role.Admin} fallback={<span>Not authorized</span>}>
                    <span>Protected content</span>
                </RequireRole>
            </AuthProvider>,
        );

        expect(screen.queryByText('Protected content')).not.toBeInTheDocument();
        expect(screen.getByText('Not authorized')).toBeInTheDocument();
    });

    it.each([
        [Role.Viewer, false],
        [Role.Requester, true],
    ])('gates children on a permission for role %s', (role, shown) => {
        render(
            <AuthProvider initialUser={{ id: '1', username: 'user', role }}>
                <RequireRole permission="content.add">
                    <span>Protected content</span>
                </RequireRole>
            </AuthProvider>,
        );

        expect(screen.queryByText('Protected content') !== null).toBe(shown);
    });

    it.each([
        {
            name: 'hides a permission-gated control while the permission request loads',
            fetch: () => new Promise<Response>(() => {}),
            shown: false,
        },
        {
            name: 'shows a permission-gated control when the permission request fails',
            fetch: () => Promise.resolve(new Response(null, { status: 500 })),
            shown: true,
        },
    ])('$name', async ({ fetch, shown }) => {
        // Arrange
        vi.stubGlobal('fetch', fetch);

        // Act
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <CatalogAuthProvider initialUser={{ id: '1', username: 'viewer', role: Role.Viewer }}>
                    <RequireRole permission="content.add">
                        <span>Protected content</span>
                    </RequireRole>
                </CatalogAuthProvider>
            </QueryClientProvider>,
        );

        // Assert
        await waitFor(() => expect(screen.queryByText('Protected content') !== null).toBe(shown));
    });

    it('renders nothing when user is not authenticated', () => {
        render(
            <AuthProvider>
                <RequireRole min={Role.Viewer}>
                    <span>Protected content</span>
                </RequireRole>
            </AuthProvider>,
        );

        expect(screen.queryByText('Protected content')).not.toBeInTheDocument();
    });

    it('supports Viewer role requirement', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        render(
            <AuthProvider initialUser={user}>
                <RequireRole min={Role.Viewer}>
                    <span>Viewer content</span>
                </RequireRole>
            </AuthProvider>,
        );

        expect(screen.getByText('Viewer content')).toBeInTheDocument();
    });
});
