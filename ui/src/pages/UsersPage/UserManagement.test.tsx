import { useState } from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { Role } from '@/auth';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { ApiError } from '@/lib/api';
import { ProviderTypesWrapper } from '@/test/providerTypesTestUtils';
import type { UserItem } from '@/types';

import { UserManagement } from './UserManagement';

const mockUsers: UserItem[] = [
    {
        id: 'user-1',
        username: 'admin',
        email: 'admin@test.com',
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: true,
        role: Role.Admin,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: '2024-01-15T12:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-2',
        username: 'viewer',
        email: null,
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: false,
        role: Role.Viewer,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: '2024-01-10T12:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-3',
        username: 'jdoe',
        email: 'jdoe@test.com',
        extensionId: 'ext-1',
        providerType: 'ldap',
        externalId: 'jdoe',
        settings: null,
        enabled: true,
        role: Role.Requester,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: null,
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: '11111111-1111-1111-1111-111111111111',
        username: 'system',
        email: null,
        extensionId: null,
        providerType: 'system',
        externalId: null,
        settings: null,
        enabled: false,
        role: Role.Admin,
        failedLoginAttempts: 0,
        lockedUntil: '2999-01-01T00:00:00.000Z',
        lastLoginAt: null,
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-5',
        username: 'lockedout',
        email: null,
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: true,
        role: Role.Requester,
        failedLoginAttempts: 5,
        lockedUntil: '2999-01-01T00:00:00.000Z',
        lastLoginAt: '2024-01-12T12:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
];

const mockUsersPage2: UserItem[] = [
    {
        id: 'user-6',
        username: 'page2user',
        email: null,
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: true,
        role: Role.Viewer,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: null,
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
];

const mockCreateUser = vi.fn();
const mockUpdateUser = vi.fn();
const mockToggleUserEnabled = vi.fn();
const mockDeleteUser = vi.fn();
const mockUnlockUser = vi.fn();
let mockIsUnlocking = false;

vi.mock('@/hooks/useUsers', () => ({
    useUsers: (params?: { pageNumber?: number }) => ({
        users: params?.pageNumber === 2 ? mockUsersPage2 : mockUsers,
        isLoading: false,
        isCreating: false,
        isUpdating: false,
        createUser: mockCreateUser,
        updateUser: mockUpdateUser,
        toggleUserEnabled: mockToggleUserEnabled,
        deleteUser: mockDeleteUser,
        unlockUser: mockUnlockUser,
        isUnlocking: mockIsUnlocking,
    }),
}));

vi.mock('@/auth', async () => {
    const actual = await vi.importActual('@/auth');
    return {
        ...actual,
        useAuth: () => ({
            user: { id: 'user-1', username: 'admin', role: 3 },
            isAuthenticated: true,
        }),
    };
});

const mockToast = vi.fn();
vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast: mockToast }),
}));

function renderUserManagement() {
    return render(
        <MemoryRouter>
            <ProviderTypesWrapper>
                <TooltipProvider>
                    <UserManagement />
                </TooltipProvider>
            </ProviderTypesWrapper>
        </MemoryRouter>,
    );
}

describe('UserManagement', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockIsUnlocking = false;
    });

    it('renders the user list', () => {
        renderUserManagement();
        expect(screen.getByText('admin')).toBeInTheDocument();
        expect(screen.getByText('viewer')).toBeInTheDocument();
    });

    it('shows a provider badge for non-local users only', () => {
        renderUserManagement();

        const ldapRow = screen.getByText('jdoe').closest('[data-testid="users-page__item"]') as HTMLElement;
        expect(within(ldapRow).getByTestId('users-page__provider-badge')).toHaveTextContent('ldap');

        const localRow = screen.getByText('viewer').closest('[data-testid="users-page__item"]') as HTMLElement;
        expect(within(localRow).queryByTestId('users-page__provider-badge')).not.toBeInTheDocument();
    });

    it('shows current user badge for the authenticated user', () => {
        renderUserManagement();
        expect(screen.getByTestId('users-page__current-user-badge')).toBeInTheDocument();
    });

    it('disables edit and delete buttons for current user', () => {
        renderUserManagement();
        const editButtons = screen.getAllByTestId('users-page__action-btn');
        const adminEditBtn = editButtons.find(
            (btn) => btn.getAttribute('data-target') === 'user-1' && btn.getAttribute('data-action') === 'edit',
        );
        expect(adminEditBtn).toBeDisabled();
    });

    it('renders the user list with correct role', () => {
        renderUserManagement();
        const items = screen.getAllByTestId('users-page__item');
        expect(items).toHaveLength(5);
    });

    it('shows disabled state for disabled users', () => {
        renderUserManagement();
        const viewerItem = screen
            .getAllByTestId('users-page__item')
            .find((el) => el.getAttribute('data-user-id') === 'user-2');
        expect(viewerItem).toHaveAttribute('data-enabled', 'false');
    });

    it('disables edit, delete, enable-toggle, and unlock controls for the system account', () => {
        renderUserManagement();
        const systemId = '11111111-1111-1111-1111-111111111111';

        const actionButtons = screen.getAllByTestId('users-page__action-btn');
        const systemEditBtn = actionButtons.find(
            (btn) => btn.getAttribute('data-target') === systemId && btn.getAttribute('data-action') === 'edit',
        );
        const systemDeleteBtn = actionButtons.find(
            (btn) => btn.getAttribute('data-target') === systemId && btn.getAttribute('data-action') === 'delete',
        );
        const systemUnlockBtn = actionButtons.find(
            (btn) => btn.getAttribute('data-target') === systemId && btn.getAttribute('data-action') === 'unlock',
        );
        expect(systemEditBtn).toBeDisabled();
        expect(systemDeleteBtn).toBeDisabled();
        expect(systemUnlockBtn).toBeDisabled();

        const systemToggle = document.querySelector(`[data-action="toggle-enabled"][data-target="${systemId}"]`);
        expect(systemToggle).toBeDisabled();
    });

    it('shows the lockout state and an unlock action for a locked account', () => {
        renderUserManagement();
        const lockedRow = screen
            .getAllByTestId('users-page__item')
            .find((el) => el.getAttribute('data-user-id') === 'user-5') as HTMLElement;

        expect(lockedRow).toHaveAttribute('data-enabled', 'true');
        expect(lockedRow).toHaveAttribute('data-locked', 'true');
        expect(within(lockedRow).getByText('Locked')).toBeInTheDocument();
        expect(within(lockedRow).getByTestId('users-page__lock-expiry')).toHaveTextContent('5 failed sign-ins');
        expect(within(lockedRow).getByLabelText('Unlock lockedout')).toBeInTheDocument();
    });

    it('shows no unlock action for an account that is disabled rather than locked', () => {
        renderUserManagement();
        const disabledRow = screen
            .getAllByTestId('users-page__item')
            .find((el) => el.getAttribute('data-user-id') === 'user-2') as HTMLElement;

        expect(disabledRow).toHaveAttribute('data-locked', 'false');
        expect(within(disabledRow).queryByTestId('users-page__lock-expiry')).not.toBeInTheDocument();
        expect(within(disabledRow).queryByLabelText('Unlock viewer')).not.toBeInTheDocument();
    });

    it('clears the lockout when the unlock action is used', async () => {
        renderUserManagement();

        fireEvent.click(screen.getByLabelText('Unlock lockedout'));

        await waitFor(() => expect(mockUnlockUser).toHaveBeenCalledWith('user-5'));
    });

    it('disables the unlock action while a previous unlock is still in flight', () => {
        mockIsUnlocking = true;
        renderUserManagement();

        expect(screen.getByLabelText('Unlock lockedout')).toBeDisabled();
    });

    it('shows the API error message when user creation fails', async () => {
        mockCreateUser.mockRejectedValue(new ApiError(409, "Username 'taken' is already taken."));
        renderUserManagement();

        fireEvent.click(document.querySelector('[data-action="add-user"]') as HTMLElement);
        fireEvent.change(await screen.findByLabelText(/username/i), { target: { value: 'taken' } });
        fireEvent.change(screen.getByLabelText(/password/i), { target: { value: 'password123' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create' }));

        await waitFor(() =>
            expect(mockToast).toHaveBeenCalledWith({
                title: 'Failed to create user',
                description: "Username 'taken' is already taken.",
                variant: 'error',
            }),
        );
    });
});

describe('UserManagement selection', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockIsUnlocking = false;
    });

    it('renders no selection overlay outside select mode', () => {
        renderUserManagement();

        expect(screen.queryByLabelText('Select viewer')).not.toBeInTheDocument();
        expect(document.querySelectorAll('[data-action="select-row"]').length).toBe(0);
        expect(screen.getByRole('button', { name: 'Select' })).toBeInTheDocument();
    });

    it('shows an overlay circle per row in select mode, disabled for the current user and the system account', async () => {
        const user = userEvent.setup();
        renderUserManagement();

        await user.click(screen.getByRole('button', { name: 'Select' }));

        expect(screen.getByLabelText('Select admin')).toBeDisabled();
        expect(screen.getByLabelText('Select system')).toBeDisabled();
        expect(screen.getByLabelText('Select viewer')).not.toBeDisabled();
        expect(screen.getByLabelText('Select jdoe')).not.toBeDisabled();
    });

    it('clears selection when leaving select mode', async () => {
        const user = userEvent.setup();
        renderUserManagement();

        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(screen.getByLabelText('Select viewer'));
        expect(screen.getByLabelText('Select viewer')).toHaveAttribute('aria-pressed', 'true');

        await user.click(screen.getByRole('button', { name: 'Stop selecting' }));
        await user.click(screen.getByRole('button', { name: 'Select' }));
        expect(screen.getByLabelText('Select viewer')).toHaveAttribute('aria-pressed', 'false');
    });

    it('bulk-deletes every selected user', async () => {
        mockDeleteUser.mockResolvedValue(undefined);
        const user = userEvent.setup();
        renderUserManagement();

        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(screen.getByLabelText('Select viewer'));
        await user.click(screen.getByLabelText('Select jdoe'));
        await user.click(
            within(screen.getByRole('region', { name: 'User selection actions' })).getByRole('button', {
                name: 'Delete selected users',
            }),
        );
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLElement);

        await waitFor(() => {
            expect(mockDeleteUser).toHaveBeenCalledWith('user-2');
            expect(mockDeleteUser).toHaveBeenCalledWith('user-3');
        });
    });

    it('bulk-enables every selected user', async () => {
        mockToggleUserEnabled.mockResolvedValue(undefined);
        const user = userEvent.setup();
        renderUserManagement();

        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(screen.getByLabelText('Select viewer'));
        await user.click(
            within(screen.getByRole('region', { name: 'User selection actions' })).getByRole('button', {
                name: 'Enable selected users',
            }),
        );

        await waitFor(() => {
            expect(mockToggleUserEnabled).toHaveBeenCalledWith('user-2', true);
        });
    });

    it('acts on the current page selection only when the selection spans a page change', async () => {
        mockDeleteUser.mockResolvedValue(undefined);
        const user = userEvent.setup();
        function PagedUserManagement() {
            const [page, setPage] = useState(1);
            return (
                <UserManagement
                    queryParams={{ pageNumber: page, pageSize: 25 }}
                    page={page}
                    pageCount={2}
                    onPageChange={setPage}
                />
            );
        }
        render(
            <MemoryRouter>
                <ProviderTypesWrapper>
                    <TooltipProvider>
                        <PagedUserManagement />
                    </TooltipProvider>
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(screen.getByLabelText('Select viewer'));
        expect(
            within(screen.getByRole('region', { name: 'User selection actions' })).getByText('1 selected'),
        ).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Go to next page' }));
        await screen.findByText('page2user');

        expect(screen.queryByText('1 selected')).not.toBeInTheDocument();

        await user.click(screen.getByLabelText('Select page2user'));
        expect(
            within(screen.getByRole('region', { name: 'User selection actions' })).getByText('1 selected'),
        ).toBeInTheDocument();

        await user.click(
            within(screen.getByRole('region', { name: 'User selection actions' })).getByRole('button', {
                name: 'Delete selected users',
            }),
        );
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLElement);

        await waitFor(() => {
            expect(mockDeleteUser).toHaveBeenCalledWith('user-6');
        });
        expect(mockDeleteUser).not.toHaveBeenCalledWith('user-2');
        expect(mockDeleteUser).toHaveBeenCalledTimes(1);
    });
});
