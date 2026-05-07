import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
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

import { apiGet, apiPost, apiDelete } from '@/lib/api';

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
                <AuthProvider initialUser={mockUser}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
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
        if (url === '/api-keys/my' || url.startsWith('/api-keys/')) {
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
});

describe('UsersPage summary badges', () => {
    it('renders summary badges for each role', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getAllByText('Admin').length).toBeGreaterThanOrEqual(1);
        });
        expect(screen.getAllByText('Manager').length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText('Requester').length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText('Viewer').length).toBeGreaterThanOrEqual(1);
    });

    it('applies data-status attributes to summary badges', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Role summary"]');
            const badges = summary?.querySelectorAll('[data-component="badge"]') ?? [];
            expect(badges.length).toBeGreaterThan(0);
        });

        const summary = document.querySelector('[aria-label="Role summary"]');
        const badges = summary?.querySelectorAll('[data-component="badge"]') ?? [];
        const statuses = Array.from(badges).map((b) => b.getAttribute('data-status'));

        expect(statuses).toContain('primary');
        expect(statuses).toContain('warning');
        expect(statuses).toContain('info');
        expect(statuses).toContain('default');
    });

    it('renders badge dots as decorative', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Role summary"]');
            const dots = summary?.querySelectorAll('[data-component="badge"] [aria-hidden="true"]') ?? [];
            expect(dots.length).toBeGreaterThan(0);
        });

        const summary = document.querySelector('[aria-label="Role summary"]');
        const dots = summary?.querySelectorAll('[data-component="badge"] [aria-hidden="true"]') ?? [];
        dots.forEach((dot) => {
            expect(dot).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('shows correct count for admin users', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Role summary"]');
            const adminBadge = summary?.querySelector('[data-component="badge"][data-status="primary"]');
            const count = adminBadge?.querySelector('[data-component="badge"] .badge__count');

            expect(count).toHaveTextContent('1');
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

describe('UsersPage filters', () => {
    it('renders role and enabled filter dropdowns', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-filter="role"]')).toBeInTheDocument();
        expect(document.querySelector('[data-filter="enabled"]')).toBeInTheDocument();
    });

    it('role filter displays "All Roles" by default', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const roleTrigger = document.querySelector('[data-filter="role"]');
        expect(roleTrigger).toHaveTextContent('All Roles');
    });

    it('enabled filter displays "All Enabled" by default', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const enabledTrigger = document.querySelector('[data-filter="enabled"]');
        expect(enabledTrigger).toHaveTextContent('All Enabled');
    });

    it('filters items by role when selection changes', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('admin')).toBeInTheDocument();
        });

        const roleTrigger = document.querySelector('[data-filter="role"]') as HTMLElement;
        await user.click(roleTrigger);

        const adminOption = await screen.findByRole('option', { name: 'Admin' });
        await user.click(adminOption);

        const list = screen.getByRole('list', { name: /users/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(1);
        expect(screen.getByText('admin')).toBeInTheDocument();
    });

    it('filters items by enabled state when selection changes', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('disabled_user')).toBeInTheDocument();
        });

        const enabledTrigger = document.querySelector('[data-filter="enabled"]') as HTMLElement;
        await user.click(enabledTrigger);

        const disabledOption = await screen.findByRole('option', { name: 'Disabled' });
        await user.click(disabledOption);

        const list = screen.getByRole('list', { name: /users/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(1);
        expect(screen.getByText('disabled_user')).toBeInTheDocument();
    });

    it('shows clear button when filters are active', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        const roleTrigger = document.querySelector('[data-filter="role"]') as HTMLElement;
        await user.click(roleTrigger);

        const adminOption = await screen.findByRole('option', { name: 'Admin' });
        await user.click(adminOption);

        expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
    });

    it('clears filters when clear button is clicked', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('admin')).toBeInTheDocument();
        });

        const roleTrigger = document.querySelector('[data-filter="role"]') as HTMLElement;
        await user.click(roleTrigger);

        const adminOption = await screen.findByRole('option', { name: 'Admin' });
        await user.click(adminOption);

        const clearButton = document.querySelector('[data-action="clear-filters"]') as HTMLElement;
        await user.click(clearButton);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /users/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(6);
        });
    });
});

describe('UsersPage enable/disable toggle', () => {
    it('disables an enabled user when toggle is clicked', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPost).mockResolvedValue({ ...mockUsers[1], enabled: false });

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
            expect(apiPost).toHaveBeenCalledWith('/users/user-2/disable');
        });
    });

    it('enables a disabled user when toggle is clicked', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPost).mockResolvedValue({ ...mockUsers[5], enabled: true });

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
            expect(apiPost).toHaveBeenCalledWith('/users/user-6/enable');
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

    it('calls create API when Create Key is clicked', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPost).mockResolvedValue({
            id: 'new-key',
            userId: 'user-1',
            name: 'API Key 1',
            key: 'mgp_test_plaintext',
            keyPrefix: 'mgp_test',
            enabled: true,
            expiresAt: null,
            createdAt: '2024-03-01T00:00:00Z',
            createdByUserId: 'user-1',
        });

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        const createButton = screen.getByRole('button', { name: /create key/i });
        await user.click(createButton);

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/api-keys/my', expect.objectContaining({ name: expect.any(String) }));
        });
    });
});

describe('UsersPage API Keys user selector', () => {
    it('renders user selector in API Keys tab', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const apiKeysTab = document.querySelector('[data-tab="api-keys"]') as HTMLElement;
        await user.click(apiKeysTab);

        expect(document.querySelector('[data-component="user-selector"]')).toBeInTheDocument();
    });

    it('displays current user as default selection', async () => {
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

        const userSelector = document.querySelector('[data-filter="user"]');
        expect(userSelector).toHaveTextContent('admin');
    });

    it('shows empty state when user has no API keys', async () => {
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

    it("updates description when viewing another user's keys", async () => {
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

        expect(screen.getByText(/keys are scoped to your user account/i)).toBeInTheDocument();

        const userSelector = document.querySelector('[data-filter="user"]') as HTMLElement;
        await user.click(userSelector);

        const managerOption = await screen.findByRole('option', { name: /manager/i });
        await user.click(managerOption);

        expect(screen.getByText(/managing keys for manager/i)).toBeInTheDocument();
    });

    it('marks current user option with "(you)" indicator', async () => {
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

        const userSelector = document.querySelector('[data-filter="user"]') as HTMLElement;
        await user.click(userSelector);

        const adminOption = await screen.findByRole('option', { name: /admin.*\(you\)/i });
        expect(adminOption).toBeInTheDocument();
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

    it('summary section has accessible label', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Role summary"]');
        expect(summary).toBeInTheDocument();
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

    it('filter triggers have aria-labels', async () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        const roleFilter = document.querySelector('[data-filter="role"]');
        expect(roleFilter).toHaveAttribute('aria-label', 'Filter by role');

        const enabledFilter = document.querySelector('[data-filter="enabled"]');
        expect(enabledFilter).toHaveAttribute('aria-label', 'Filter by enabled state');
    });
});

describe('UsersPage delete confirmations', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/api-keys/my' || url.startsWith('/api-keys/')) {
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
            expect(apiDelete).toHaveBeenCalledWith('/api-keys/my/key-1');
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
    it('restores api-keys tab from URL search params', async () => {
        render(
            <TestWrapper initialEntries={['/?tab=api-keys']}>
                <UsersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /api keys/i })).toBeInTheDocument();
    });

    it('restores role filter from URL search params', async () => {
        render(
            <TestWrapper initialEntries={[`/?role=${Role.Admin}`]}>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /users/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });

        expect(screen.getByText('admin')).toBeInTheDocument();
    });

    it('restores enabled filter from URL search params', async () => {
        render(
            <TestWrapper initialEntries={['/?enabled=false']}>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /users/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });

        expect(screen.getByText('disabled_user')).toBeInTheDocument();
    });

    it('restores combined role and enabled filters from URL search params', async () => {
        render(
            <TestWrapper initialEntries={[`/?role=${Role.Requester}&enabled=true`]}>
                <UsersPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /users/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });

        expect(screen.getByText('requester')).toBeInTheDocument();
    });

    it('defaults to users tab when no tab param is set', () => {
        render(
            <TestWrapper>
                <UsersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /users/i })).toBeInTheDocument();
    });
});
