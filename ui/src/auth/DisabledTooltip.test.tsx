import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { AuthProvider } from './AuthContext';
import { DisabledTooltip, useDisabledState } from './DisabledTooltip';
import { Role } from './types';

function HookConsumer({ options }: { options: { action: string } | { minRole: number } }) {
    const { disabled, tooltipMessage } = useDisabledState(options as Parameters<typeof useDisabledState>[0]);
    return (
        <div>
            <span data-testid="disabled">{disabled.toString()}</span>
            <span data-testid="tooltip-message">{tooltipMessage}</span>
        </div>
    );
}

describe('DisabledTooltip', () => {
    describe('with permission prop', () => {
        it('renders tooltip when user lacks permission', async () => {
            const user = userEvent.setup();
            const viewer = { id: '1', username: 'viewer', role: Role.Viewer };

            render(
                <AuthProvider initialUser={viewer}>
                    <DisabledTooltip action="content.edit">
                        <button disabled>Edit</button>
                    </DisabledTooltip>
                </AuthProvider>,
            );

            const button = screen.getByRole('button', { name: 'Edit' });
            await user.hover(button);

            expect(await screen.findByRole('tooltip')).toHaveTextContent('Requires Requester role');
        });

        it('does not wrap with tooltip when user has permission', () => {
            const manager = { id: '1', username: 'manager', role: Role.Manager };

            render(
                <AuthProvider initialUser={manager}>
                    <DisabledTooltip action="content.edit">
                        <button>Edit</button>
                    </DisabledTooltip>
                </AuthProvider>,
            );

            expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
            expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
        });

        it('shows Admin for admin-only permissions', async () => {
            const user = userEvent.setup();
            const manager = { id: '1', username: 'manager', role: Role.Manager };

            render(
                <AuthProvider initialUser={manager}>
                    <DisabledTooltip action="users.manage">
                        <button disabled>Manage Users</button>
                    </DisabledTooltip>
                </AuthProvider>,
            );

            const button = screen.getByRole('button', { name: 'Manage Users' });
            await user.hover(button);

            expect(await screen.findByRole('tooltip')).toHaveTextContent('Requires Admin role');
        });

        it('uses custom message when provided', async () => {
            const user = userEvent.setup();
            const viewer = { id: '1', username: 'viewer', role: Role.Viewer };

            render(
                <AuthProvider initialUser={viewer}>
                    <DisabledTooltip action="content.delete" message="Only managers can delete content">
                        <button disabled>Delete</button>
                    </DisabledTooltip>
                </AuthProvider>,
            );

            const button = screen.getByRole('button', { name: 'Delete' });
            await user.hover(button);

            expect(await screen.findByRole('tooltip')).toHaveTextContent('Only managers can delete content');
        });
    });

    describe('with minRole prop', () => {
        it('renders tooltip when user role is below minimum', async () => {
            const user = userEvent.setup();
            const viewer = { id: '1', username: 'viewer', role: Role.Viewer };

            render(
                <AuthProvider initialUser={viewer}>
                    <DisabledTooltip minRole={Role.Manager}>
                        <button disabled>Manage</button>
                    </DisabledTooltip>
                </AuthProvider>,
            );

            const button = screen.getByRole('button', { name: 'Manage' });
            await user.hover(button);

            expect(await screen.findByRole('tooltip')).toHaveTextContent('Requires Manager role');
        });

        it('does not wrap with tooltip when user meets role requirement', () => {
            const admin = { id: '1', username: 'admin', role: Role.Admin };

            render(
                <AuthProvider initialUser={admin}>
                    <DisabledTooltip minRole={Role.Manager}>
                        <button>Manage</button>
                    </DisabledTooltip>
                </AuthProvider>,
            );

            expect(screen.getByRole('button', { name: 'Manage' })).toBeInTheDocument();
            expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
        });

        it('shows Requester for requester-level actions', async () => {
            const user = userEvent.setup();
            const viewer = { id: '1', username: 'viewer', role: Role.Viewer };

            render(
                <AuthProvider initialUser={viewer}>
                    <DisabledTooltip minRole={Role.Requester}>
                        <button disabled>Request</button>
                    </DisabledTooltip>
                </AuthProvider>,
            );

            const button = screen.getByRole('button', { name: 'Request' });
            await user.hover(button);

            expect(await screen.findByRole('tooltip')).toHaveTextContent('Requires Requester role');
        });
    });

    describe('unauthenticated user', () => {
        it('renders tooltip when not authenticated', async () => {
            const user = userEvent.setup();

            render(
                <AuthProvider>
                    <DisabledTooltip action="content.view">
                        <button disabled>View</button>
                    </DisabledTooltip>
                </AuthProvider>,
            );

            const button = screen.getByRole('button', { name: 'View' });
            await user.hover(button);

            expect(await screen.findByRole('tooltip')).toHaveTextContent('Requires Viewer role');
        });
    });
});

describe('useDisabledState', () => {
    describe('with permission', () => {
        it('returns disabled=false when user has permission', () => {
            const admin = { id: '1', username: 'admin', role: Role.Admin };

            render(
                <AuthProvider initialUser={admin}>
                    <HookConsumer options={{ action: 'content.edit' }} />
                </AuthProvider>,
            );

            expect(screen.getByTestId('disabled')).toHaveTextContent('false');
        });

        it('returns disabled=true when user lacks permission', () => {
            const viewer = { id: '1', username: 'viewer', role: Role.Viewer };

            render(
                <AuthProvider initialUser={viewer}>
                    <HookConsumer options={{ action: 'content.edit' }} />
                </AuthProvider>,
            );

            expect(screen.getByTestId('disabled')).toHaveTextContent('true');
        });

        it('returns tooltip message with required role', () => {
            render(
                <AuthProvider>
                    <HookConsumer options={{ action: 'users.manage' }} />
                </AuthProvider>,
            );

            expect(screen.getByTestId('tooltip-message')).toHaveTextContent('Requires Admin role');
        });
    });

    describe('with minRole', () => {
        it('returns disabled=false when user meets role', () => {
            const manager = { id: '1', username: 'manager', role: Role.Manager };

            render(
                <AuthProvider initialUser={manager}>
                    <HookConsumer options={{ minRole: Role.Manager }} />
                </AuthProvider>,
            );

            expect(screen.getByTestId('disabled')).toHaveTextContent('false');
        });

        it('returns disabled=true when user is below role', () => {
            const viewer = { id: '1', username: 'viewer', role: Role.Viewer };

            render(
                <AuthProvider initialUser={viewer}>
                    <HookConsumer options={{ minRole: Role.Admin }} />
                </AuthProvider>,
            );

            expect(screen.getByTestId('disabled')).toHaveTextContent('true');
        });

        it('returns tooltip message with required role', () => {
            render(
                <AuthProvider>
                    <HookConsumer options={{ minRole: Role.Admin }} />
                </AuthProvider>,
            );

            expect(screen.getByTestId('tooltip-message')).toHaveTextContent('Requires Admin role');
        });
    });
});
