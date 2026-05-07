import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';

import { WishlistsPage } from './WishlistsPage';

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
import type { ApiWishlist } from '@/hooks/useWishlists';

const mockApiWishlists: ApiWishlist[] = [
    {
        id: 'wl-1',
        name: 'My Movies',
        contentFilterId: 'filter-1',
        qualityTierListId: 'qtl-1',
        ownerUserId: 'user-1',
        ownerLock: false,
        enabled: true,
        lastSyncedAt: '2024-01-15T10:30:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        contentFilter: { id: 'filter-1', name: 'Action Movies' },
        qualityTierList: { id: 'qtl-1', name: 'HD' },
    },
    {
        id: 'wl-2',
        name: 'Plex Watchlist',
        contentFilterId: 'filter-2',
        qualityTierListId: null,
        ownerUserId: 'user-1',
        ownerLock: true,
        enabled: true,
        lastSyncedAt: null,
        createdAt: '2024-01-02T00:00:00Z',
        contentFilter: { id: 'filter-2', name: 'All Movies' },
        qualityTierList: null,
    },
    {
        id: 'wl-3',
        name: 'Trakt Collection',
        contentFilterId: 'filter-3',
        qualityTierListId: 'qtl-2',
        ownerUserId: 'user-2',
        ownerLock: true,
        enabled: true,
        lastSyncedAt: '2024-01-14T18:00:00Z',
        createdAt: '2024-01-03T00:00:00Z',
        contentFilter: { id: 'filter-3', name: 'Trakt List' },
        qualityTierList: { id: 'qtl-2', name: '4K Ultra HD' },
    },
    {
        id: 'wl-4',
        name: 'IMDb Watchlist',
        contentFilterId: 'filter-4',
        qualityTierListId: null,
        ownerUserId: 'user-2',
        ownerLock: false,
        enabled: false,
        lastSyncedAt: null,
        createdAt: '2024-01-04T00:00:00Z',
        contentFilter: { id: 'filter-4', name: 'IMDb Filter' },
        qualityTierList: null,
    },
];

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

function TestWrapper({
    children,
    role = Role.Admin,
    userId = 'user-1',
    initialEntries = ['/wishlists'],
}: {
    children: ReactNode;
    role?: Role;
    userId?: string;
    initialEntries?: string[];
}) {
    const mockUser = { id: userId, username: 'test', role };
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

describe('WishlistsPage', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }

            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with correct data-page attribute', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-page="wishlists"]')).toBeInTheDocument();
        });
    });

    it('renders the page title', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Wishlists', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders the wishlist count', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const count = screen.getByTestId('wishlists-page__count');
            expect(count).toBeInTheDocument();
            expect(count).toHaveTextContent('4');
        });
    });

    it('renders the page icon as decorative', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const icon = screen.getByTestId('wishlists-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('shows loading state initially', () => {
        mockApiGet.mockReturnValue(new Promise(() => {}));

        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        const skeletons = screen.getAllByRole('status', { name: 'Loading' });
        expect(skeletons.length).toBeGreaterThan(0);
    });
});

describe('WishlistsPage summary badges', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders status badges', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Status summary"]');
            expect(summary?.querySelector('[data-component="badge"][data-status="healthy"]')).toBeInTheDocument();
            expect(summary?.querySelector('[data-component="badge"][data-status="default"]')).toBeInTheDocument();
            expect(summary?.querySelector('[data-component="badge"][data-status="paused"]')).toBeInTheDocument();
        });
    });

    it('summary section has accessible label', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Status summary"]');
            expect(summary).toBeInTheDocument();
        });
    });
});

describe('WishlistsPage list items', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders wishlist items in the list', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(4);
        });
    });

    it('renders wishlist names', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('My Movies')).toBeInTheDocument();
            expect(screen.getByText('Plex Watchlist')).toBeInTheDocument();
            expect(screen.getByText('Trakt Collection')).toBeInTheDocument();
            expect(screen.getByText('IMDb Watchlist')).toBeInTheDocument();
        });
    });

    it('renders owner usernames instead of raw UUIDs', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const ownerElements = screen.getAllByTestId('wishlists-page__item-owner');
            expect(ownerElements.length).toBeGreaterThan(0);
            ownerElements.forEach((el) => {
                expect(el.textContent).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
            });
        });
    });

    it('resolves owner names from users API', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getAllByText('alice').length).toBeGreaterThan(0);
            expect(screen.getAllByText('bob').length).toBeGreaterThan(0);
        });
    });

    it('renders filter names', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });
    });

    it('applies data-wishlist-id to items', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const items = document.querySelectorAll('[data-wishlist-id]');
            expect(items.length).toBe(4);
            expect(items[0]).toHaveAttribute('data-wishlist-id', 'wl-1');
        });
    });

    it('shows unmonitored badge for unmonitored wishlists', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const unmonitoredBadges = screen.getAllByText('Unmonitored');
            expect(unmonitoredBadges.length).toBeGreaterThan(0);
        });
    });

    it('applies unmonitored class to unmonitored items', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const unmonitoredItem = screen.queryByTestId('wishlists-page__item--unmonitored');
            expect(unmonitoredItem).toBeInTheDocument();
        });
    });
});

describe('WishlistsPage filters', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders filter controls', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-component="wishlist-filters"]')).toBeInTheDocument();
        });
    });

    it('renders owner filter', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('combobox', { name: /filter by owner/i })).toBeInTheDocument();
        });
    });

    it('renders monitored filter', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('combobox', { name: /filter by monitored status/i })).toBeInTheDocument();
        });
    });

    it('filters wishlists by monitored status', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('combobox', { name: /filter by monitored status/i })).toBeInTheDocument();
        });

        const monitoredTrigger = screen.getByRole('combobox', { name: /filter by monitored status/i });
        await user.click(monitoredTrigger);

        const unmonitoredOption = screen.getByRole('option', { name: 'Unmonitored' });
        await user.click(unmonitoredOption);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
    });

    it('shows clear filters button when filters are active', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('combobox', { name: /filter by monitored status/i })).toBeInTheDocument();
        });

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        const monitoredTrigger = screen.getByRole('combobox', { name: /filter by monitored status/i });
        await user.click(monitoredTrigger);

        const unmonitoredOption = screen.getByRole('option', { name: 'Unmonitored' });
        await user.click(unmonitoredOption);

        await waitFor(() => {
            expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
        });
    });
});

describe('WishlistsPage Add Wishlist button', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows Add Wishlist button for Manager role', async () => {
        render(
            <TestWrapper role={Role.Manager}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const addButtons = screen.getAllByRole('button', { name: /add wishlist/i });
            expect(addButtons.length).toBeGreaterThanOrEqual(1);
        });
    });

    it('shows Add Wishlist button for Admin role', async () => {
        render(
            <TestWrapper role={Role.Admin}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const addButtons = screen.getAllByRole('button', { name: /add wishlist/i });
            expect(addButtons.length).toBeGreaterThanOrEqual(1);
        });
    });

    it('hides Add Wishlist button for Viewer role', async () => {
        render(
            <TestWrapper role={Role.Viewer}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Wishlists' })).toBeInTheDocument();
        });

        expect(screen.queryByRole('button', { name: /add wishlist/i })).not.toBeInTheDocument();
    });

    it('shows Add Wishlist button for Requester role', async () => {
        render(
            <TestWrapper role={Role.Requester}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const addButtons = screen.getAllByRole('button', { name: /add wishlist/i });
            expect(addButtons.length).toBeGreaterThanOrEqual(1);
        });
    });
});

describe('WishlistsPage Edit and Delete actions', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('Manager can delete all wishlists', async () => {
        render(
            <TestWrapper role={Role.Manager}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const deleteButtons = document.querySelectorAll('[data-action="delete"]');
            expect(deleteButtons.length).toBe(4);
        });
    });

    it('Requester cannot delete wishlists', async () => {
        render(
            <TestWrapper role={Role.Requester} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Wishlists' })).toBeInTheDocument();
        });

        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        expect(deleteButtons.length).toBe(0);
    });

    it('Viewer cannot edit or delete wishlists', async () => {
        render(
            <TestWrapper role={Role.Viewer}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Wishlists' })).toBeInTheDocument();
        });

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        const deleteButtons = document.querySelectorAll('[data-action="delete"]');

        expect(editButtons.length).toBe(0);
        expect(deleteButtons.length).toBe(0);
    });

    it('opens confirmation dialog when delete is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-action="delete"]')).toBeInTheDocument();
        });

        const deleteButton = document.querySelector(
            '[data-wishlist-id="wl-1"] [data-action="delete"]',
        ) as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Wishlist')).toBeInTheDocument();
        });

        expect(screen.getByText(/Are you sure you want to delete "My Movies"\?/)).toBeInTheDocument();
    });

    it('does not delete immediately when delete is clicked', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);

        render(
            <TestWrapper role={Role.Manager}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-action="delete"]')).toBeInTheDocument();
        });

        const deleteButton = document.querySelector(
            '[data-wishlist-id="wl-1"] [data-action="delete"]',
        ) as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Wishlist')).toBeInTheDocument();
        });

        expect(mockApiDelete).not.toHaveBeenCalled();
    });

    it('deletes wishlist after confirmation', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        render(
            <TestWrapper role={Role.Manager} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-action="delete"]')).toBeInTheDocument();
        });

        const deleteButton = document.querySelector(
            '[data-wishlist-id="wl-1"] [data-action="delete"]',
        ) as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Wishlist')).toBeInTheDocument();
        });

        const confirmDeleteButton = document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement;
        await user.click(confirmDeleteButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalled();
        });
    });

    it('closes confirmation dialog on cancel', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-action="delete"]')).toBeInTheDocument();
        });

        const deleteButton = document.querySelector(
            '[data-wishlist-id="wl-1"] [data-action="delete"]',
        ) as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Wishlist')).toBeInTheDocument();
        });

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLButtonElement;
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByText('Delete Wishlist')).not.toBeInTheDocument();
        });
    });
});

describe('WishlistsPage owner lock indicator', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows lock indicator for wishlists with ownerLock=true', async () => {
        render(
            <TestWrapper userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const lockIndicators = document.querySelectorAll('[data-owner-lock="true"]');
            expect(lockIndicators.length).toBe(2);
        });
    });

    it('shows data-locked attribute on locked wishlist items', async () => {
        render(
            <TestWrapper userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const lockedItems = document.querySelectorAll('[data-locked="true"]');
            expect(lockedItems.length).toBe(2);
        });
    });
});

describe('WishlistsPage lock/unlock actions', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows lock button for wishlists owned by current user', async () => {
        render(
            <TestWrapper userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const lockButtons = document.querySelectorAll('[data-action="lock"], [data-action="unlock"]');
            expect(lockButtons.length).toBe(2);
        });
    });

    it('does not show lock button for wishlists owned by others', async () => {
        render(
            <TestWrapper userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const wl3 = document.querySelector('[data-wishlist-id="wl-3"]');
            const lockButton = wl3?.querySelector('[data-action="lock"], [data-action="unlock"]');
            expect(lockButton).not.toBeInTheDocument();
        });
    });

    it('shows unlock action for locked wishlists', async () => {
        render(
            <TestWrapper userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const wl2 = document.querySelector('[data-wishlist-id="wl-2"]');
            const unlockButton = wl2?.querySelector('[data-action="unlock"]');
            expect(unlockButton).toBeInTheDocument();
        });
    });

    it('shows lock action for unlocked wishlists', async () => {
        render(
            <TestWrapper userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const wl1 = document.querySelector('[data-wishlist-id="wl-1"]');
            const lockButton = wl1?.querySelector('[data-action="lock"]');
            expect(lockButton).toBeInTheDocument();
        });
    });

    it('calls API when lock button is clicked', async () => {
        const user = userEvent.setup();
        mockApiPost.mockResolvedValue({ ...mockApiWishlists[0], ownerLock: true });

        render(
            <TestWrapper userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const wl1 = document.querySelector('[data-wishlist-id="wl-1"]');
            expect(wl1).toBeInTheDocument();
        });

        const wl1 = document.querySelector('[data-wishlist-id="wl-1"]');
        const lockButton = wl1?.querySelector('[data-action="lock"]') as HTMLButtonElement;

        await user.click(lockButton);

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/wishlists/my/wl-1/lock');
        });
    });
});

describe('WishlistsPage edit permissions with ownerLock', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('Manager cannot edit locked wishlists owned by others', async () => {
        render(
            <TestWrapper role={Role.Manager} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const wl3 = document.querySelector('[data-wishlist-id="wl-3"]');
            const editButton = wl3?.querySelector('[data-action="edit"]');
            expect(editButton).not.toBeInTheDocument();
        });
    });

    it('Manager can edit unlocked wishlists owned by others', async () => {
        render(
            <TestWrapper role={Role.Manager} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const wl4 = document.querySelector('[data-wishlist-id="wl-4"]');
            const editButton = wl4?.querySelector('[data-action="edit"]');
            expect(editButton).toBeInTheDocument();
        });
    });

    it('Owner can edit their own locked wishlists', async () => {
        render(
            <TestWrapper role={Role.Requester} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const wl2 = document.querySelector('[data-wishlist-id="wl-2"]');
            const editButton = wl2?.querySelector('[data-action="edit"]');
            expect(editButton).toBeInTheDocument();
        });
    });
});

describe('WishlistsPage navigation', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('wishlist rows are focusable', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const firstItem = screen.getAllByTestId('wishlists-page__item')[0];
            expect(firstItem).toHaveAttribute('tabindex', '0');
        });
    });
});

describe('WishlistsPage accessibility', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('wishlists list has accessible label', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('list', { name: /wishlists/i })).toBeInTheDocument();
        });
    });

    it('filter comboboxes have accessible labels', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('combobox', { name: /filter by owner/i })).toBeInTheDocument();
            expect(screen.getByRole('combobox', { name: /filter by monitored status/i })).toBeInTheDocument();
        });
    });

    it('action buttons have aria-labels', async () => {
        render(
            <TestWrapper role={Role.Manager}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const editButtons = document.querySelectorAll('[data-action="edit"]');
            const deleteButtons = document.querySelectorAll('[data-action="delete"]');

            editButtons.forEach((btn) => {
                expect(btn).toHaveAttribute('aria-label');
            });
            deleteButtons.forEach((btn) => {
                expect(btn).toHaveAttribute('aria-label');
            });
        });
    });

    it('add wishlist button has data-action attribute', async () => {
        render(
            <TestWrapper role={Role.Manager}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const addButtons = screen.getAllByRole('button', { name: /add wishlist/i });
            expect(addButtons[0]).toHaveAttribute('data-action', 'add-wishlist');
        });
    });
});

describe('WishlistsPage URL search param persistence', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'user-1',
                            username: 'alice',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                        {
                            id: 'user-2',
                            username: 'bob',
                            role: 0,
                            enabled: true,
                            providerType: 'local',
                            email: null,
                            extensionId: null,
                            externalId: null,
                            settings: null,
                            lockedUntil: null,
                            lastLoginAt: null,
                            createdAt: '2024-01-01T00:00:00Z',
                            createdByUserId: null,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 50,
                    totalItems: 2,
                });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('pre-filters by monitored status from URL param', async () => {
        render(
            <TestWrapper initialEntries={['/wishlists?monitored=false']}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            const items = within(list).getAllByRole('listitem');

            expect(items.length).toBe(1);
        });
    });

    it('pre-filters by owner from URL param', async () => {
        render(
            <TestWrapper initialEntries={['/wishlists?owner=user-2']}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            const items = within(list).getAllByRole('listitem');

            expect(items.length).toBe(2);
        });
    });

    it('pre-filters by both owner and monitored from URL params', async () => {
        render(
            <TestWrapper initialEntries={['/wishlists?owner=user-2&monitored=true']}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            const items = within(list).getAllByRole('listitem');

            expect(items.length).toBe(1);
        });
    });

    it('shows clear filters button when URL params are set', async () => {
        render(
            <TestWrapper initialEntries={['/wishlists?monitored=true']}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
        });
    });

    it('clears URL params when clear filters is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper initialEntries={['/wishlists?owner=user-1&monitored=true']}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
        });

        const clearButton = document.querySelector('[data-action="clear-filters"]') as HTMLButtonElement;
        await user.click(clearButton);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            const items = within(list).getAllByRole('listitem');

            expect(items.length).toBe(4);
        });
    });
});
