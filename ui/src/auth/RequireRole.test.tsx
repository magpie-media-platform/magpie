import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { AuthTestProvider as AuthProvider } from '@/test/AuthTestProvider';
import { RequireRole } from './RequireRole';
import { Role } from './types';

describe('RequireRole', () => {
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
