import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { AuthProvider } from './AuthContext';
import { IfPermission, usePermission } from './IfPermission';
import { Role } from './types';

function PermissionHookConsumer({ action }: { action: Parameters<typeof usePermission>[0] }) {
    const { allowed, requiredRoleName } = usePermission(action);
    return (
        <div>
            <span data-testid="allowed">{allowed.toString()}</span>
            <span data-testid="required-role">{requiredRoleName}</span>
        </div>
    );
}

describe('IfPermission', () => {
    it('renders children when user has permission', () => {
        const user = { id: '1', username: 'manager', role: Role.Manager };

        render(
            <AuthProvider initialUser={user}>
                <IfPermission action="content.edit">
                    <span>Edit button</span>
                </IfPermission>
            </AuthProvider>,
        );

        expect(screen.getByText('Edit button')).toBeInTheDocument();
    });

    it('renders nothing when user lacks permission', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        render(
            <AuthProvider initialUser={user}>
                <IfPermission action="content.edit">
                    <span>Edit button</span>
                </IfPermission>
            </AuthProvider>,
        );

        expect(screen.queryByText('Edit button')).not.toBeInTheDocument();
    });

    it('renders fallback when user lacks permission', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        render(
            <AuthProvider initialUser={user}>
                <IfPermission action="users.manage" fallback={<span>Admin only</span>}>
                    <span>User editor</span>
                </IfPermission>
            </AuthProvider>,
        );

        expect(screen.queryByText('User editor')).not.toBeInTheDocument();
        expect(screen.getByText('Admin only')).toBeInTheDocument();
    });

    it('renders nothing when user is not authenticated', () => {
        render(
            <AuthProvider>
                <IfPermission action="content.view">
                    <span>Content</span>
                </IfPermission>
            </AuthProvider>,
        );

        expect(screen.queryByText('Content')).not.toBeInTheDocument();
    });

    it('shows content for Requester role with request permission', () => {
        const requester = { id: '1', username: 'req', role: Role.Requester };

        render(
            <AuthProvider initialUser={requester}>
                <IfPermission action="content.request">
                    <span>Request button</span>
                </IfPermission>
            </AuthProvider>,
        );

        expect(screen.getByText('Request button')).toBeInTheDocument();
    });

    it('hides content for Viewer role without request permission', () => {
        const viewer = { id: '1', username: 'view', role: Role.Viewer };

        render(
            <AuthProvider initialUser={viewer}>
                <IfPermission action="content.request">
                    <span>Request button</span>
                </IfPermission>
            </AuthProvider>,
        );

        expect(screen.queryByText('Request button')).not.toBeInTheDocument();
    });

    it('shows content for Admin role with backups permission', () => {
        const admin = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={admin}>
                <IfPermission action="backups.manage">
                    <span>Backup panel</span>
                </IfPermission>
            </AuthProvider>,
        );

        expect(screen.getByText('Backup panel')).toBeInTheDocument();
    });

    it('hides content for Manager role without backups permission', () => {
        const manager = { id: '1', username: 'mgr', role: Role.Manager };

        render(
            <AuthProvider initialUser={manager}>
                <IfPermission action="backups.manage">
                    <span>Backup panel</span>
                </IfPermission>
            </AuthProvider>,
        );

        expect(screen.queryByText('Backup panel')).not.toBeInTheDocument();
    });
});

describe('usePermission', () => {
    it('returns allowed=true when user has permission', () => {
        const user = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={user}>
                <PermissionHookConsumer action="users.manage" />
            </AuthProvider>,
        );

        expect(screen.getByTestId('allowed')).toHaveTextContent('true');
    });

    it('returns allowed=false when user lacks permission', () => {
        const user = { id: '1', username: 'viewer', role: Role.Viewer };

        render(
            <AuthProvider initialUser={user}>
                <PermissionHookConsumer action="settings.edit" />
            </AuthProvider>,
        );

        expect(screen.getByTestId('allowed')).toHaveTextContent('false');
    });

    it('returns the required role name for the permission', () => {
        render(
            <AuthProvider>
                <PermissionHookConsumer action="users.manage" />
            </AuthProvider>,
        );

        expect(screen.getByTestId('required-role')).toHaveTextContent('Admin');
    });

    it('returns Requester for edit permissions', () => {
        render(
            <AuthProvider>
                <PermissionHookConsumer action="content.edit" />
            </AuthProvider>,
        );

        expect(screen.getByTestId('required-role')).toHaveTextContent('Requester');
    });
});
