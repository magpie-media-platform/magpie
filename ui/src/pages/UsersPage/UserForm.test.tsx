import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';

import { Role } from '@/auth';
import { ProviderTypesWrapper } from '@/test/providerTypesTestUtils';

import { UserForm } from './UserForm';

function TestWrapper({ children }: { children: ReactNode }) {
    return (
        <MemoryRouter>
            <ProviderTypesWrapper>{children}</ProviderTypesWrapper>
        </MemoryRouter>
    );
}

describe('UserForm', () => {
    it('renders nothing when closed', () => {
        render(
            <TestWrapper>
                <UserForm open={false} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByText('Add User')).not.toBeInTheDocument();
    });

    it('renders the form when open', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Add User')).toBeInTheDocument();
    });

    it('shows description for new user', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Create a new user account.')).toBeInTheDocument();
    });

    it('shows "Edit User" when editing', () => {
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'user-1',
                        username: 'testuser',
                        role: Role.Viewer,
                        enabled: true,
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit User')).toBeInTheDocument();
        expect(screen.getByText('Update user account settings.')).toBeInTheDocument();
    });

    it('pre-fills form fields when editing', () => {
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'user-1',
                        username: 'testuser',
                        role: Role.Manager,
                        enabled: true,
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('testuser')).toBeInTheDocument();
    });
});

describe('UserForm fields', () => {
    it('renders username input field', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/username/i)).toBeInTheDocument();
    });

    it('renders role selector', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/role/i)).toBeInTheDocument();
    });

    it('renders enabled toggle', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/user enabled/i)).toBeInTheDocument();
    });

    it('renders password input field', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
    });
});

describe('UserForm buttons', () => {
    it('renders Create and Cancel buttons for new user', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: /create/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
    });

    it('renders Save and Cancel buttons when editing', () => {
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'user-1',
                        username: 'testuser',
                        role: Role.Viewer,
                        enabled: true,
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
    });
});

describe('UserForm role selector', () => {
    it('shows Viewer as default role for new user', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const roleTrigger = document.querySelector('[data-field="role"]');
        expect(roleTrigger).toHaveTextContent('Viewer');
    });

    it('shows all role options when clicked', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const roleTrigger = document.querySelector('[data-field="role"]') as HTMLElement;
        await user.click(roleTrigger);

        expect(await screen.findByRole('option', { name: 'Viewer' })).toBeInTheDocument();
        expect(await screen.findByRole('option', { name: 'Requester' })).toBeInTheDocument();
        expect(await screen.findByRole('option', { name: 'Manager' })).toBeInTheDocument();
        expect(await screen.findByRole('option', { name: 'Admin' })).toBeInTheDocument();
    });

    it('updates role when selection changes', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const usernameInput = screen.getByLabelText(/username/i);
        await user.type(usernameInput, 'newuser');

        const passwordInput = screen.getByLabelText(/password/i);
        await user.type(passwordInput, 'securepassword123');

        const roleTrigger = document.querySelector('[data-field="role"]') as HTMLElement;
        await user.click(roleTrigger);

        const managerOption = await screen.findByRole('option', { name: 'Manager' });
        await user.click(managerOption);

        const submitButton = screen.getByRole('button', { name: /create/i });
        await user.click(submitButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                role: Role.Manager,
            }),
        );
    });
});

describe('UserForm inline validation', () => {
    it('shows username error inline when username is cleared after typing', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const usernameInput = screen.getByLabelText(/username/i);
        await user.type(usernameInput, 'a');
        await user.clear(usernameInput);

        expect(screen.getByRole('alert')).toHaveTextContent('Username is required');
    });

    it('shows password error inline when password is too short', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const passwordInput = screen.getByLabelText(/password/i);
        await user.type(passwordInput, 'short');

        const alerts = screen.getAllByRole('alert');
        const passwordAlert = alerts.find((el) => el.textContent?.includes('Password must be'));
        expect(passwordAlert).toBeTruthy();
    });

    it('clears password error when password becomes long enough', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const passwordInput = screen.getByLabelText(/password/i);
        await user.type(passwordInput, 'short');

        const alertsBefore = screen.getAllByRole('alert');
        expect(alertsBefore.some((el) => el.textContent?.includes('Password must be'))).toBe(true);

        await user.type(passwordInput, 'enough123');

        const alertsAfter = screen.queryAllByRole('alert');
        expect(alertsAfter.some((el) => el.textContent?.includes('Password must be'))).toBe(false);
    });

    it('does not show username error before the field is interacted with', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
});

describe('UserForm validation', () => {
    it('shows error when submitting with empty username', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const passwordInput = screen.getByLabelText(/password/i);
        await user.type(passwordInput, 'securepassword123');

        const submitButton = screen.getByRole('button', { name: /create/i });
        await user.click(submitButton);

        expect(screen.getByRole('alert')).toHaveTextContent('Username is required');
    });

    it('shows error when password is too short for new user', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const usernameInput = screen.getByLabelText(/username/i);
        await user.type(usernameInput, 'newuser');

        const passwordInput = screen.getByLabelText(/password/i);
        await user.type(passwordInput, 'short');

        const submitButton = screen.getByRole('button', { name: /create/i });
        await user.click(submitButton);

        const alerts = screen.getAllByRole('alert');
        const passwordAlert = alerts.find((el) => el.textContent?.includes('Password must be'));
        expect(passwordAlert).toBeTruthy();
    });

    it('allows submit when username is filled for new user with password', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const usernameInput = screen.getByLabelText(/username/i);
        await user.type(usernameInput, 'newuser');

        const passwordInput = screen.getByLabelText(/password/i);
        await user.type(passwordInput, 'securepassword123');

        const submitButton = screen.getByRole('button', { name: /create/i });
        await user.click(submitButton);

        expect(onSubmit).toHaveBeenCalledTimes(1);
    });

    it('shows password hint about minimum length', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/minimum 8 characters/i)).toBeInTheDocument();
    });
});

describe('UserForm submission', () => {
    it('calls onSubmit with form values when submitted', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const usernameInput = screen.getByLabelText(/username/i);
        await user.type(usernameInput, 'newuser');

        const passwordInput = screen.getByLabelText(/password/i);
        await user.type(passwordInput, 'securepassword123');

        const submitButton = screen.getByRole('button', { name: /create/i });
        await user.click(submitButton);

        expect(onSubmit).toHaveBeenCalledTimes(1);
        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                username: 'newuser',
                password: 'securepassword123',
                role: Role.Viewer,
                enabled: true,
            }),
        );
    });

    it('calls onSubmit with updated values when editing', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'user-1',
                        username: 'olduser',
                        role: Role.Viewer,
                        enabled: true,
                    }}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const usernameInput = screen.getByLabelText(/username/i);
        expect(usernameInput).toBeDisabled();

        const enabledToggle = screen.getByLabelText(/user enabled/i);
        await user.click(enabledToggle);

        const submitButton = screen.getByRole('button', { name: /save/i });
        await user.click(submitButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                id: 'user-1',
                username: 'olduser',
                enabled: false,
            }),
        );
    });

    it('shows loading state when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    onSubmit={vi.fn()}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        const createButton = screen.getByRole('button', { name: /create/i });
        expect(createButton).toHaveAttribute('aria-busy', 'true');
        expect(createButton).toBeDisabled();
    });

    it('disables form fields when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    onSubmit={vi.fn()}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        expect(cancelButton).toBeDisabled();

        const saveButton = screen.getByRole('button', { name: /create/i });
        expect(saveButton).toBeDisabled();

        expect(screen.getByLabelText(/username/i)).toBeDisabled();
    });
});

describe('UserForm enabled toggle', () => {
    it('enabled toggle defaults to true for new user', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const enabledToggle = screen.getByLabelText(/user enabled/i);
        expect(enabledToggle).toHaveAttribute('aria-checked', 'true');
    });

    it('shows disabled state when editing disabled user', () => {
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'user-1',
                        username: 'disableduser',
                        role: Role.Viewer,
                        enabled: false,
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const enabledToggle = screen.getByLabelText(/user enabled/i);
        expect(enabledToggle).toHaveAttribute('aria-checked', 'false');
    });

    it('toggles enabled state when clicked', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'user-1',
                        username: 'testuser',
                        role: Role.Viewer,
                        enabled: true,
                    }}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const enabledToggle = screen.getByLabelText(/user enabled/i);
        await user.click(enabledToggle);

        expect(enabledToggle).toHaveAttribute('aria-checked', 'false');

        const submitButton = screen.getByRole('button', { name: /save/i });
        await user.click(submitButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                enabled: false,
            }),
        );
    });
});

describe('UserForm cancel', () => {
    it('calls onOpenChange with false when cancel is clicked', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        await user.click(cancelButton);

        expect(onOpenChange).toHaveBeenCalledWith(false);
    });
});

describe('UserForm password field', () => {
    it('validates password is required for new user on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const usernameInput = screen.getByLabelText(/username/i);
        await user.type(usernameInput, 'newuser');

        const submitButton = screen.getByRole('button', { name: /create/i });
        await user.click(submitButton);

        const alerts = screen.getAllByRole('alert');
        const passwordAlert = alerts.find((el) => el.textContent?.includes('Password must be'));
        expect(passwordAlert).toBeTruthy();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('hides password field when editing', () => {
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'user-1',
                        username: 'testuser',
                        role: Role.Viewer,
                        enabled: true,
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.queryByLabelText(/password/i)).not.toBeInTheDocument();
    });
});

describe('UserForm external (non-local) users', () => {
    it('hides the password field when editing a non-local user', () => {
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'user-1',
                        username: 'jdoe',
                        role: Role.Viewer,
                        enabled: true,
                        providerType: 'ldap',
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.queryByLabelText(/password/i)).not.toBeInTheDocument();
        expect(screen.getByDisplayValue('ldap')).toBeInTheDocument();
    });

    it('offers no external provider when creating a user', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/^password$/i)).toBeInTheDocument();
        expect(screen.queryByLabelText(/external id/i)).not.toBeInTheDocument();
        expect(document.querySelector('[data-field="provider"]')).toBeDisabled();
        expect(screen.getByText(/created by their authentication provider at first sign-in/i)).toBeInTheDocument();
    });
});

describe('UserForm disabled field explanation', () => {
    it('shows helper text explaining username cannot be changed when editing', () => {
        render(
            <TestWrapper>
                <UserForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'user-1',
                        username: 'testuser',
                        role: Role.Viewer,
                        enabled: true,
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Usernames cannot be changed after creation.')).toBeInTheDocument();
    });

    it('does not show username helper text when creating a new user', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByText('Usernames cannot be changed after creation.')).not.toBeInTheDocument();
    });
});

describe('UserForm accessibility', () => {
    it('form fields have proper labels', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/username/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/role/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
    });

    it('has a sheet description', () => {
        render(
            <TestWrapper>
                <UserForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Create a new user account.')).toBeInTheDocument();
    });
});
