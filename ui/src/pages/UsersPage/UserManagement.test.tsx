import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { Role } from '@/auth';
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
];

const mockCreateUser = vi.fn();
const mockUpdateUser = vi.fn();
const mockToggleUserEnabled = vi.fn();
const mockDeleteUser = vi.fn();

vi.mock('@/hooks/useUsers', () => ({
    useUsers: () => ({
        users: mockUsers,
        isLoading: false,
        isCreating: false,
        isUpdating: false,
        createUser: mockCreateUser,
        updateUser: mockUpdateUser,
        toggleUserEnabled: mockToggleUserEnabled,
        deleteUser: mockDeleteUser,
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
            <UserManagement />
        </MemoryRouter>,
    );
}

describe('UserManagement', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders the user list', () => {
        renderUserManagement();
        expect(screen.getByText('admin')).toBeInTheDocument();
        expect(screen.getByText('viewer')).toBeInTheDocument();
    });

    it('shows role badges in summary', () => {
        renderUserManagement();
        expect(screen.getByText('Admin')).toBeInTheDocument();
        expect(screen.getByText('Viewer')).toBeInTheDocument();
    });

    it('renders role badge counts in the summary', () => {
        renderUserManagement();
        const summary = screen.getByLabelText('Role summary');
        expect(summary).toBeInTheDocument();
    });

    it('shows current user badge for the authenticated user', () => {
        renderUserManagement();
        expect(screen.getByTestId('users-page__current-user-badge')).toBeInTheDocument();
    });

    it('disables edit and delete buttons for current user', () => {
        renderUserManagement();
        const editButtons = screen.getAllByTestId('users-page__action-btn');
        // The first user (admin) is the current user -- buttons should be disabled
        const adminEditBtn = editButtons.find(
            (btn) => btn.getAttribute('data-target') === 'user-1' && btn.getAttribute('data-action') === 'edit',
        );
        expect(adminEditBtn).toBeDisabled();
    });

    it('renders the user list with correct role', () => {
        renderUserManagement();
        const items = screen.getAllByTestId('users-page__item');
        expect(items).toHaveLength(2);
    });

    it('shows disabled state for disabled users', () => {
        renderUserManagement();
        const viewerItem = screen.getAllByTestId('users-page__item').find(
            (el) => el.getAttribute('data-user-id') === 'user-2',
        );
        expect(viewerItem).toHaveAttribute('data-enabled', 'false');
    });
});
