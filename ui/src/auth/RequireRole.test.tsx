import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { AuthTestProvider as AuthProvider } from '@/test/AuthTestProvider';
import { RequireRole, useRequireRole } from './RequireRole';
import { Role } from './types';

function RoleHookConsumer({ min }: { min: Role }) {
    const { allowed, requiredRoleName } = useRequireRole(min);
    return (
        <div>
            <span data-testid="allowed">{allowed.toString()}</span>
            <span data-testid="required-role">{requiredRoleName}</span>
        </div>
    );
}

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

describe('useRequireRole', () => {
    it('returns allowed=true when user meets requirement', () => {
        const user = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={user}>
                <RoleHookConsumer min={Role.Manager} />
            </AuthProvider>,
        );

        expect(screen.getByTestId('allowed')).toHaveTextContent('true');
    });

    it('returns allowed=false when user does not meet requirement', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        render(
            <AuthProvider initialUser={user}>
                <RoleHookConsumer min={Role.Admin} />
            </AuthProvider>,
        );

        expect(screen.getByTestId('allowed')).toHaveTextContent('false');
    });

    it('returns the required role name', () => {
        render(
            <AuthProvider>
                <RoleHookConsumer min={Role.Admin} />
            </AuthProvider>,
        );

        expect(screen.getByTestId('required-role')).toHaveTextContent('Admin');
    });
});
