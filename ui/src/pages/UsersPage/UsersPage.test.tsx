import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, useSearchParams } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport, ToastList } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
import { testEffectivePermissions } from '@/test/permissions';
import type { UserItem } from '@/types';

import { UsersPage } from './UsersPage';

const mockUsers: UserItem[] = [
    {
        id: 'user-1',
        username: 'admin',
        email: 'admin@example.com',
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: true,
        role: Role.Admin,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: '2024-01-15T10:30:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-2',
        username: 'manager',
        email: 'manager@example.com',
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: true,
        role: Role.Manager,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: '2024-01-15T09:00:00Z',
        createdAt: '2024-01-02T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-3',
        username: 'requester',
        email: 'requester@example.com',
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: true,
        role: Role.Requester,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: '2024-01-14T15:00:00Z',
        createdAt: '2024-01-03T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-4',
        username: 'viewer',
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
        createdAt: '2024-01-04T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-5',
        username: 'locked_user',
        email: 'locked@example.com',
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: true,
        role: Role.Viewer,
        failedLoginAttempts: 5,
        lockedUntil: '2099-01-16T00:00:00Z',
        lastLoginAt: '2024-01-13T00:00:00Z',
        createdAt: '2024-01-05T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-6',
        username: 'disabled_user',
        email: 'disabled@example.com',
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: false,
        role: Role.Requester,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: '2024-01-10T00:00:00Z',
        createdAt: '2024-01-06T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
];

const mockPagedResponse = {
    items: mockUsers,
    pageNumber: 1,
    pageSize: 25,
    totalItems: 6,
};

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
        apiPatch: vi.fn(),
        apiDelete: vi.fn(),
    };
});

import { apiGet, apiPost, apiPatch, apiDelete, ApiError } from '@/lib/api';

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
            mutations: {
                retry: false,
            },
        },
    });
}

function TestWrapper({ children, initialEntries }: { children: ReactNode; initialEntries?: string[] }) {
    const mockUser = {
        id: 'user-1',
        username: 'admin',
        role: Role.Admin,
    };

    const queryClient = createTestQueryClient();

    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={initialEntries}>
                <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

beforeEach(() => {
    vi.clearAllMocks();

    vi.mocked(apiGet).mockImplementation((url: string) => {
        if (url === '/auth-providers') {
            return Promise.resolve(['ldap']);
        }
        if (url === '/api-keys' || url === '/api-keys/my' || url.startsWith('/api-keys/')) {
            return Promise.resolve([]);
        }
        return Promise.resolve(mockPagedResponse);
    });
});

afterEach(() => {
    vi.restoreAllMocks();
});

describe('UsersPage', () => {
    it('renders with correct data-page attribute', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-page="users"]')).toBeInTheDocument();
    });

    it('renders the page title', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('heading', { name: 'Users', level: 1 })).toBeInTheDocument();
    });

    it('renders the user count in the tab', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const usersTab = document.querySelector('[data-tab="users"]');
            const count = usersTab?.querySelector('[data-testid="users-page__tab-count"]');
            expect(count).toHaveTextContent('6');
        });
    });

    it('uses the total user count and reaches users on later API pages', async () => {
        const user = userEvent.setup();
        const laterUser: UserItem = {
            ...mockUsers[1],
            id: 'user-26',
            username: 'later_user',
        };
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys' || url === '/api-keys/my' || url.startsWith('/api-keys/')) {
                return Promise.resolve([]);
            }
            if (url === '/users?pageNumber=2&pageSize=25') {
                return Promise.resolve({
                    items: [laterUser],
                    pageNumber: 2,
                    pageSize: 25,
                    totalItems: 26,
                });
            }
            return Promise.resolve({
                items: mockUsers,
                pageNumber: 1,
                pageSize: 25,
                totalItems: 26,
            });
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const usersTab = document.querySelector('[data-tab="users"]');
        await waitFor(() => {
            expect(usersTab?.querySelector('[data-testid="users-page__tab-count"]')).toHaveTextContent('26');
        });
        await user.click(screen.getByRole('button', { name: 'Go to next page' }));

        expect(await screen.findByText('later_user')).toBeInTheDocument();
        expect(apiGet).toHaveBeenCalledWith('/users?pageNumber=2&pageSize=25');
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
    });

    it('renders the page icon as decorative', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const icon = screen.getByTestId('users-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });

    it('renders an Add User button', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const addButtons = screen.getAllByRole('button', { name: /add user/i });
        expect(addButtons.length).toBeGreaterThanOrEqual(1);
    });

    it('includes the disabled state in the create user request', async () => {
        const user = userEvent.setup();
        vi.mocked(apiPost).mockResolvedValue({
            id: 'user-7',
            username: 'disabled_new',
            email: null,
            extensionId: null,
            providerType: 'local',
            externalId: null,
            settings: null,
            enabled: false,
            role: Role.Viewer,
            failedLoginAttempts: 0,
            lockedUntil: null,
            lastLoginAt: null,
            createdAt: '2024-01-07T00:00:00Z',
            createdByUserId: 'user-1',
            modifiedAt: null,
            modifiedByUserId: null,
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await user.click(document.querySelector('[data-action="add-user"]') as HTMLButtonElement);
        await user.type(screen.getByLabelText(/username/i), 'disabled_new');
        await user.type(screen.getByLabelText(/^password$/i), 'password123');
        await user.click(screen.getByLabelText(/user enabled/i));
        await user.click(screen.getByRole('button', { name: /^create$/i }));

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/users', {
                username: 'disabled_new',
                password: 'password123',
                role: Role.Viewer,
                enabled: false,
            });
        });
    });
});

describe('UsersPage list items', () => {
    it('renders user items in the list', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /users/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(6);
        });
    });

    it('renders user names', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('admin')).toBeInTheDocument();
        });
        expect(screen.getByText('manager')).toBeInTheDocument();
        expect(screen.getByText('requester')).toBeInTheDocument();
        expect(screen.getByText('viewer')).toBeInTheDocument();
    });

    it('renders email addresses', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('admin@example.com')).toBeInTheDocument();
        });
        expect(screen.getByText('manager@example.com')).toBeInTheDocument();
    });

    it('renders last login time', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText(/Login Never/)).toBeInTheDocument();
        });
    });

    it('applies data-user-id to items', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const items = document.querySelectorAll('[data-user-id]');
            expect(items.length).toBe(6);
        });

        const items = document.querySelectorAll('[data-user-id]');
        expect(items[0]).toHaveAttribute('data-user-id', 'user-1');
    });

    it('applies data-enabled attribute to items', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const enabledItems = document.querySelectorAll('[data-testid="users-page__item"][data-enabled="true"]');
            expect(enabledItems.length).toBe(5);
        });

        const disabledItems = document.querySelectorAll('[data-testid="users-page__item"][data-enabled="false"]');
        expect(disabledItems.length).toBe(1);
    });

    it('applies disabled styling to disabled users', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const disabledItems = document.querySelectorAll('[data-testid="users-page__item"][data-enabled="false"]');
            expect(disabledItems.length).toBe(1);
        });
    });

    it('shows locked badge for locked users', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Locked')).toBeInTheDocument();
        });
    });

    it('shows current user badge for the logged-in user', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const adminItem = document.querySelector('[data-user-id="user-1"]');
            expect(adminItem?.querySelector('[data-testid="users-page__current-user-badge"]')).toBeInTheDocument();
        });
    });
});

describe('UsersPage role badges', () => {
    it('renders role badges on user items', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const items = screen.getAllByTestId('users-page__item');
            expect(items.length).toBeGreaterThan(0);
        });

        const items = screen.getAllByTestId('users-page__item');
        items.forEach((item) => {
            expect(item.querySelector('[data-component="badge"]')).toBeInTheDocument();
        });
    });

    it('renders Admin badge with primary variant', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const adminItem = document.querySelector('[data-user-id="user-1"]');
            expect(adminItem).toBeInTheDocument();
        });

        const adminItem = document.querySelector('[data-user-id="user-1"]');
        const badge = adminItem?.querySelector('[data-component="badge"]');
        expect(badge).toHaveAttribute('data-variant', 'primary');
        expect(badge).toHaveTextContent('Admin');
    });

    it('renders Manager badge with warning variant', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const managerItem = document.querySelector('[data-user-id="user-2"]');
            expect(managerItem).toBeInTheDocument();
        });

        const managerItem = document.querySelector('[data-user-id="user-2"]');
        const badge = managerItem?.querySelector('[data-component="badge"]');
        expect(badge).toHaveAttribute('data-variant', 'warning');
        expect(badge).toHaveTextContent('Manager');
    });

    it('renders Requester badge with info variant', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const requesterItem = document.querySelector('[data-user-id="user-3"]');
            expect(requesterItem).toBeInTheDocument();
        });

        const requesterItem = document.querySelector('[data-user-id="user-3"]');
        const badge = requesterItem?.querySelector('[data-component="badge"]');
        expect(badge).toHaveAttribute('data-variant', 'info');
        expect(badge).toHaveTextContent('Requester');
    });

    it('renders Viewer badge with default variant', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const viewerItem = document.querySelector('[data-user-id="user-4"]');
            expect(viewerItem).toBeInTheDocument();
        });

        const viewerItem = document.querySelector('[data-user-id="user-4"]');
        const badge = viewerItem?.querySelector('[data-component="badge"]');
        expect(badge).toHaveAttribute('data-variant', 'default');
        expect(badge).toHaveTextContent('Viewer');
    });
});

describe('UsersPage actions', () => {
    it('renders enable/disable toggle for each user', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const toggles = document.querySelectorAll('[data-action="toggle-enabled"]');
            expect(toggles.length).toBe(6);
        });
    });

    it('toggle shows checked state for enabled users', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const managerItem = document.querySelector('[data-user-id="user-2"]');
            expect(managerItem).toBeInTheDocument();
        });

        const managerItem = document.querySelector('[data-user-id="user-2"]');
        const toggle = managerItem?.querySelector('[data-action="toggle-enabled"]');
        expect(toggle).toHaveAttribute('aria-checked', 'true');
    });

    it('toggle shows unchecked state for disabled users', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const disabledItem = document.querySelector('[data-user-id="user-6"]');
            expect(disabledItem).toBeInTheDocument();
        });

        const disabledItem = document.querySelector('[data-user-id="user-6"]');
        const toggle = disabledItem?.querySelector('[data-action="toggle-enabled"]');
        expect(toggle).toHaveAttribute('aria-checked', 'false');
    });

    it('toggle is disabled for current user', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const adminItem = document.querySelector('[data-user-id="user-1"]');
            expect(adminItem).toBeInTheDocument();
        });

        const adminItem = document.querySelector('[data-user-id="user-1"]');
        const toggle = adminItem?.querySelector('[data-action="toggle-enabled"]');
        expect(toggle).toBeDisabled();
    });

    it('renders edit button for each user', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const editButtons = document.querySelectorAll('[data-action="edit"]');
            expect(editButtons.length).toBe(6);
        });
    });

    it('renders delete button for each user', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const deleteButtons = document.querySelectorAll('[data-action="delete"]');
            expect(deleteButtons.length).toBe(6);
        });
    });

    it('delete button is disabled for current user', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const adminItem = document.querySelector('[data-user-id="user-1"]');
            expect(adminItem).toBeInTheDocument();
        });

        const adminItem = document.querySelector('[data-user-id="user-1"]');
        const deleteButton = adminItem?.querySelector('[data-action="delete"]');
        expect(deleteButton).toBeDisabled();
    });

    it('edit button is disabled for current user', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const adminItem = document.querySelector('[data-user-id="user-1"]');
            expect(adminItem).toBeInTheDocument();
        });

        const adminItem = document.querySelector('[data-user-id="user-1"]');
        const editButton = adminItem?.querySelector('[data-action="edit"]');
        expect(editButton).toBeDisabled();
    });

    it('edit button has correct aria-label', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^edit admin$/i })).toBeInTheDocument();
        });

        const editButton = screen.getByRole('button', { name: /^edit admin$/i });
        expect(editButton).toHaveAttribute('data-action', 'edit');
    });

    it('delete button has correct aria-label', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete manager$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete manager$/i });
        expect(deleteButton).toHaveAttribute('data-action', 'delete');
    });

    it('opens edit form when edit button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^edit manager$/i })).toBeInTheDocument();
        });

        const editButton = screen.getByRole('button', { name: /^edit manager$/i });
        await user.click(editButton);

        expect(screen.getByText('Edit User')).toBeInTheDocument();
        expect(screen.getByDisplayValue('manager')).toBeInTheDocument();
    });
});

describe('UsersPage enable/disable toggle', () => {
    it('disables an enabled user when toggle is clicked', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPatch).mockResolvedValue(undefined);

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-user-id="user-2"]')).toBeInTheDocument();
        });

        const managerItem = document.querySelector('[data-user-id="user-2"]');
        const toggle = managerItem?.querySelector('[data-action="toggle-enabled"]') as HTMLElement;
        expect(toggle).toHaveAttribute('aria-checked', 'true');

        await user.click(toggle);

        await waitFor(() => {
            expect(apiPatch).toHaveBeenCalledWith('/users/user-2', {
                enabled: { value: false },
            });
        });
    });

    it('enables a disabled user when toggle is clicked', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPatch).mockResolvedValue(undefined);

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-user-id="user-6"]')).toBeInTheDocument();
        });

        const disabledItem = document.querySelector('[data-user-id="user-6"]');
        const toggle = disabledItem?.querySelector('[data-action="toggle-enabled"]') as HTMLElement;
        expect(toggle).toHaveAttribute('aria-checked', 'false');

        await user.click(toggle);

        await waitFor(() => {
            expect(apiPatch).toHaveBeenCalledWith('/users/user-6', {
                enabled: { value: true },
            });
        });
    });
});

describe('UsersPage API Keys tab', () => {
    it('switches to API Keys tab when clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        expect(screen.getByRole('list', { name: /api keys/i })).toBeInTheDocument();
    });

    it('renders API key count (0 when API returns empty)', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]');
        const count = apiKeysTab?.querySelector('[data-testid="users-page__tab-count"]');
        expect(count).toHaveTextContent('0');
    });

    it('shows empty state when no API keys', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        expect(screen.getByText('No API keys')).toBeInTheDocument();
    });

    it('renders Create Key button', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        expect(screen.getByRole('button', { name: /create key/i })).toBeInTheDocument();
    });

    it('opens the create dialog and requires a name before submitting', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        await user.click(screen.getByRole('button', { name: /create key/i }));

        await waitFor(() => {
            expect(document.querySelector('[data-component="create-api-key-dialog"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-action="submit-create-key"]') as HTMLButtonElement);

        expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
        expect(apiPost).not.toHaveBeenCalled();
    });

    it('creates a named key and reveals the plaintext once', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPost).mockResolvedValue({
            key: {
                id: 'new-key',
                userId: 'user-1',
                name: 'Home automation',
                keyPrefix: 'mgp_test',
                enabled: true,
                expiresAt: null,
                lastUsedAt: null,
                createdAt: '2024-03-01T00:00:00Z',
                createdByUserId: 'user-1',
                modifiedAt: null,
                modifiedByUserId: null,
            },
            plaintextKey: 'mgp_test_plaintext_secret',
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        await user.click(screen.getByRole('button', { name: /create key/i }));

        const nameInput = document.querySelector('[data-field="api-key-name"]') as HTMLInputElement;
        await user.type(nameInput, 'Home automation');

        await user.click(document.querySelector('[data-action="submit-create-key"]') as HTMLButtonElement);

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/api-keys', {
                name: 'Home automation',
                expiresAt: null,
            });
        });

        await waitFor(() => {
            expect(document.querySelector('[data-component="reveal-api-key-dialog"]')).toBeInTheDocument();
        });
        expect(screen.getByTestId('reveal-api-key-value')).toHaveTextContent('mgp_test_plaintext_secret');
    });

    it('copies the revealed key to the clipboard and dismisses the modal', async () => {
        const user = userEvent.setup();
        const writeText = vi.fn().mockResolvedValue(undefined);
        Object.defineProperty(navigator, 'clipboard', {
            value: { writeText },
            configurable: true,
        });

        vi.mocked(apiPost).mockResolvedValue({
            key: {
                id: 'new-key',
                userId: 'user-1',
                name: 'CI token',
                keyPrefix: 'mgp_ci',
                enabled: true,
                expiresAt: null,
                lastUsedAt: null,
                createdAt: '2024-03-01T00:00:00Z',
                createdByUserId: 'user-1',
                modifiedAt: null,
                modifiedByUserId: null,
            },
            plaintextKey: 'mgp_ci_plaintext_secret',
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        await user.click(screen.getByRole('button', { name: /create key/i }));
        await user.type(document.querySelector('[data-field="api-key-name"]') as HTMLInputElement, 'CI token');
        await user.click(document.querySelector('[data-action="submit-create-key"]') as HTMLButtonElement);

        await waitFor(() => {
            expect(document.querySelector('[data-component="reveal-api-key-dialog"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-action="copy-api-key"]') as HTMLButtonElement);
        expect(writeText).toHaveBeenCalledWith('mgp_ci_plaintext_secret');

        await user.click(document.querySelector('[data-action="dismiss-reveal-key"]') as HTMLButtonElement);

        await waitFor(() => {
            expect(document.querySelector('[data-component="reveal-api-key-dialog"]')).not.toBeInTheDocument();
        });
    });
});

describe('UsersPage API Keys Mine/All toggle', () => {
    it('renders a Mine/All ownership toggle for admins, no dropdown', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        expect(document.querySelector('[data-component="user-selector"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-filter="user"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-filter="scope"][data-value="mine"]')).toBeInTheDocument();
        expect(document.querySelector('[data-filter="scope"][data-value="all"]')).toBeInTheDocument();
    });

    it('shows empty state for the current user with no keys', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('admin')).toBeInTheDocument();
        });

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        expect(screen.getByText('No API keys')).toBeInTheDocument();
    });

    it('fetches every users keys and labels owners when switching to All', async () => {
        const user = userEvent.setup();

        const allKeys = [
            {
                id: 'key-all-1',
                userId: 'user-2',
                name: 'Manager Key',
                keyPrefix: 'mgp_mgr',
                enabled: true,
                expiresAt: null,
                lastUsedAt: null,
                createdAt: '2024-01-01T00:00:00Z',
                createdByUserId: 'user-2',
                modifiedAt: null,
                modifiedByUserId: null,
            },
        ];

        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys') {
                return Promise.resolve(allKeys);
            }
            if (url === '/api-keys/my' || url.startsWith('/api-keys/')) {
                return Promise.resolve([]);
            }
            return Promise.resolve(mockPagedResponse);
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('admin')).toBeInTheDocument();
        });

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        const allToggle = document.querySelector('[data-filter="scope"][data-value="all"]') as HTMLElement;
        await user.click(allToggle);

        await waitFor(() => {
            expect(apiGet).toHaveBeenCalledWith('/api-keys');
        });

        expect(await screen.findByText('Manager Key')).toBeInTheDocument();
        expect(screen.getByTestId('api-key-owner-badge')).toHaveTextContent('manager');
    });

    it('updates the API Keys tab count when switching from Mine to All', async () => {
        const user = userEvent.setup();

        const allKeys = [
            {
                id: 'key-all-1',
                userId: 'user-2',
                name: 'Manager Key',
                keyPrefix: 'mgp_mgr',
                enabled: true,
                expiresAt: null,
                lastUsedAt: null,
                createdAt: '2024-01-01T00:00:00Z',
                createdByUserId: 'user-2',
                modifiedAt: null,
                modifiedByUserId: null,
            },
            {
                id: 'key-all-2',
                userId: 'user-3',
                name: 'Requester Key',
                keyPrefix: 'mgp_req',
                enabled: true,
                expiresAt: null,
                lastUsedAt: null,
                createdAt: '2024-01-02T00:00:00Z',
                createdByUserId: 'user-3',
                modifiedAt: null,
                modifiedByUserId: null,
            },
        ];

        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys') {
                return Promise.resolve(allKeys);
            }
            if (url === '/api-keys' || url === '/api-keys/my' || url.startsWith('/api-keys/')) {
                return Promise.resolve([]);
            }
            return Promise.resolve(mockPagedResponse);
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        const count = apiKeysTab.querySelector('[data-testid="users-page__tab-count"]');
        expect(count).toHaveTextContent('0');

        await user.click(apiKeysTab);
        await user.click(document.querySelector('[data-filter="scope"][data-value="all"]') as HTMLElement);

        expect(await screen.findByText('Manager Key')).toBeInTheDocument();
        expect(screen.getByText('Requester Key')).toBeInTheDocument();
        await waitFor(() => {
            expect(count).toHaveTextContent('2');
        });
    });
});

describe('UsersPage API key enable toggle', () => {
    const disabledKey = [
        {
            id: 'key-1',
            userId: 'user-1',
            name: 'Test Key',
            keyPrefix: 'mgp_test',
            enabled: false,
            expiresAt: null,
            lastUsedAt: null,
            createdAt: '2024-01-01T00:00:00Z',
            createdByUserId: 'user-1',
            modifiedAt: null,
            modifiedByUserId: null,
        },
    ];

    const enabledKey = [
        {
            id: 'key-1',
            userId: 'user-1',
            name: 'Test Key',
            keyPrefix: 'mgp_test',
            enabled: true,
            expiresAt: null,
            lastUsedAt: null,
            createdAt: '2024-01-01T00:00:00Z',
            createdByUserId: 'user-1',
            modifiedAt: null,
            modifiedByUserId: null,
        },
    ];

    it('patches the key enabled to true when a disabled key is toggled on', async () => {
        const user = userEvent.setup();
        vi.mocked(apiPatch).mockResolvedValue(undefined);
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys/my') {
                return Promise.resolve(disabledKey);
            }
            if (url.startsWith('/api-keys')) {
                return Promise.resolve([]);
            }
            return Promise.resolve(mockPagedResponse);
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await user.click(document.querySelector('[data-tab="api-keys"]') as HTMLElement);

        await waitFor(() => {
            expect(document.querySelector('[data-action="toggle-enabled"][data-target="key-1"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-action="toggle-enabled"][data-target="key-1"]') as HTMLElement);

        await waitFor(() => {
            expect(apiPatch).toHaveBeenCalledWith('/api-keys/key-1', { enabled: { value: true } });
        });
    });

    it('patches the key enabled to false when an enabled key is toggled off', async () => {
        const user = userEvent.setup();
        vi.mocked(apiPatch).mockResolvedValue(undefined);
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys/my') {
                return Promise.resolve(enabledKey);
            }
            if (url.startsWith('/api-keys')) {
                return Promise.resolve([]);
            }
            return Promise.resolve(mockPagedResponse);
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await user.click(document.querySelector('[data-tab="api-keys"]') as HTMLElement);

        await waitFor(() => {
            expect(document.querySelector('[data-action="toggle-enabled"][data-target="key-1"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-action="toggle-enabled"][data-target="key-1"]') as HTMLElement);

        await waitFor(() => {
            expect(apiPatch).toHaveBeenCalledWith('/api-keys/key-1', { enabled: { value: false } });
        });
    });

    it('shows the server error detail when the toggle patch fails', async () => {
        const user = userEvent.setup();
        vi.mocked(apiPatch).mockRejectedValue(new ApiError(403, 'That API key belongs to another user.'));
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys/my') {
                return Promise.resolve(enabledKey);
            }
            if (url.startsWith('/api-keys')) {
                return Promise.resolve([]);
            }
            return Promise.resolve(mockPagedResponse);
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await user.click(document.querySelector('[data-tab="api-keys"]') as HTMLElement);

        await waitFor(() => {
            expect(document.querySelector('[data-action="toggle-enabled"][data-target="key-1"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-action="toggle-enabled"][data-target="key-1"]') as HTMLElement);

        expect(await screen.findByText('That API key belongs to another user.')).toBeInTheDocument();
    });
});

describe('UsersPage accessibility', () => {
    it('list has accessible label', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /users/i })).toBeInTheDocument();
    });

    it('toggle switches have aria-labels', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const toggles = document.querySelectorAll('[data-action="toggle-enabled"]');
            expect(toggles.length).toBeGreaterThan(0);
        });

        const toggles = document.querySelectorAll('[data-action="toggle-enabled"]');
        toggles.forEach((toggle) => {
            expect(toggle).toHaveAttribute('aria-label');
        });
    });

    it('action buttons have aria-labels', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const actionButtons = screen.getAllByTestId('users-page__action-btn');
            expect(actionButtons.length).toBeGreaterThan(0);
        });

        const actionButtons = screen.getAllByTestId('users-page__action-btn');
        actionButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
    });
});

describe('UsersPage delete confirmations', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys' || url === '/api-keys/my' || url.startsWith('/api-keys/')) {
                return Promise.resolve([]);
            }
            return Promise.resolve(mockPagedResponse);
        });
    });

    it('opens confirmation dialog when user delete button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete manager$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete manager$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete User')).toBeInTheDocument();
            expect(screen.getByText(/Are you sure you want to delete "manager"\?/)).toBeInTheDocument();
        });

        expect(apiDelete).not.toHaveBeenCalled();
    });

    it('calls deleteUser after confirming in the dialog', async () => {
        const user = userEvent.setup();
        vi.mocked(apiDelete).mockResolvedValue(undefined);

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete manager$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete manager$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete User')).toBeInTheDocument();
        });

        const confirmButton = screen.getByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(apiDelete).toHaveBeenCalledWith('/users/user-2');
        });
    });

    it('does not call deleteUser when cancel is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete manager$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete manager$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete User')).toBeInTheDocument();
        });

        const cancelButton = screen.getByRole('button', { name: /^cancel$/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByText('Delete User')).not.toBeInTheDocument();
        });

        expect(apiDelete).not.toHaveBeenCalled();
    });

    it('opens confirmation dialog when API key delete button is clicked', async () => {
        const user = userEvent.setup();

        const mockApiKeys = [
            {
                id: 'key-1',
                userId: 'user-1',
                name: 'Test Key',
                keyPrefix: 'mgp_test',
                enabled: true,
                expiresAt: null,
                lastUsedAt: null,
                createdAt: '2024-01-01T00:00:00Z',
                createdByUserId: 'user-1',
                modifiedAt: null,
                modifiedByUserId: null,
            },
        ];

        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys/my') {
                return Promise.resolve(mockApiKeys);
            }
            if (url.startsWith('/api-keys/')) {
                return Promise.resolve([]);
            }
            return Promise.resolve(mockPagedResponse);
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete test key$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete test key$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete API Key')).toBeInTheDocument();
            expect(screen.getByText(/Are you sure you want to delete "Test Key"\?/)).toBeInTheDocument();
        });

        expect(apiDelete).not.toHaveBeenCalled();
    });

    it('calls deleteApiKey after confirming in the dialog', async () => {
        const user = userEvent.setup();
        vi.mocked(apiDelete).mockResolvedValue(undefined);

        const mockApiKeys = [
            {
                id: 'key-1',
                userId: 'user-1',
                name: 'Test Key',
                keyPrefix: 'mgp_test',
                enabled: true,
                expiresAt: null,
                lastUsedAt: null,
                createdAt: '2024-01-01T00:00:00Z',
                createdByUserId: 'user-1',
                modifiedAt: null,
                modifiedByUserId: null,
            },
        ];

        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys/my') {
                return Promise.resolve(mockApiKeys);
            }
            if (url.startsWith('/api-keys/')) {
                return Promise.resolve([]);
            }
            return Promise.resolve(mockPagedResponse);
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete test key$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete test key$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete API Key')).toBeInTheDocument();
        });

        const confirmButton = screen.getByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(apiDelete).toHaveBeenCalledWith('/api-keys/key-1');
        });
    });

    it('does not call deleteApiKey when cancel is clicked', async () => {
        const user = userEvent.setup();

        const mockApiKeys = [
            {
                id: 'key-1',
                userId: 'user-1',
                name: 'Test Key',
                keyPrefix: 'mgp_test',
                enabled: true,
                expiresAt: null,
                lastUsedAt: null,
                createdAt: '2024-01-01T00:00:00Z',
                createdByUserId: 'user-1',
                modifiedAt: null,
                modifiedByUserId: null,
            },
        ];

        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/auth-providers') {
                return Promise.resolve(['ldap']);
            }
            if (url === '/api-keys/my') {
                return Promise.resolve(mockApiKeys);
            }
            if (url.startsWith('/api-keys/')) {
                return Promise.resolve([]);
            }
            return Promise.resolve(mockPagedResponse);
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete test key$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete test key$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete API Key')).toBeInTheDocument();
        });

        const cancelButton = screen.getByRole('button', { name: /^cancel$/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByText('Delete API Key')).not.toBeInTheDocument();
        });

        expect(apiDelete).not.toHaveBeenCalled();
    });
});

describe('UsersPage search params persistence', () => {
    function UrlObserver() {
        const [params] = useSearchParams();
        return <span data-testid="url-params">{params.toString()}</span>;
    }

    it('restores api-keys tab from URL search params', async () => {
        render(
            <TestWrapper initialEntries={['/?tab=api-keys']}>
                <UsersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /api keys/i })).toBeInTheDocument();
    });

    it('defaults to users tab when no tab param is set', () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /users/i })).toBeInTheDocument();
    });

    it('falls back to Users and normalizes the URL when ?tab= is invalid', async () => {
        render(
            <TestWrapper initialEntries={['/?tab=bogus']}>
                <UsersPage />
                <UrlObserver />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /users/i })).toBeInTheDocument();
        await waitFor(() => {
            expect(screen.getByTestId('url-params').textContent).toBe('');
        });
    });
});
