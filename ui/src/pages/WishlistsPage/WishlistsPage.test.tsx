import { act, render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useEffect, type ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
import { testEffectivePermissions } from '@/test/permissions';

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

const mockFormatMagpieQlCriteria = vi.fn();
const mockParseMagpieQl = vi.fn();

vi.mock('@/lib/magpieQl', () => ({
    formatMagpieQlCriteria: (...args: unknown[]) => mockFormatMagpieQlCriteria(...args),
    parseMagpieQl: (...args: unknown[]) => mockParseMagpieQl(...args),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
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
        contentFilter: { id: 'filter-1', name: 'Action Movies', ownerUserId: 'user-1', ownerLock: false },
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
        contentFilter: { id: 'filter-2', name: 'All Movies', ownerUserId: null, ownerLock: false },
        qualityTierList: null,
    },
    {
        id: 'wl-3',
        name: 'Provider I Collection',
        contentFilterId: 'filter-3',
        qualityTierListId: 'qtl-2',
        ownerUserId: 'user-2',
        ownerLock: true,
        enabled: true,
        monitoring: false,
        lastSyncedAt: '2024-01-14T18:00:00Z',
        createdAt: '2024-01-03T00:00:00Z',
        contentFilter: { id: 'filter-3', name: 'Provider I List' },
        qualityTierList: { id: 'qtl-2', name: '4K Ultra HD' },
    },
    {
        id: 'wl-4',
        name: 'Provider H Watchlist',
        contentFilterId: 'filter-4',
        qualityTierListId: null,
        ownerUserId: 'user-2',
        ownerLock: false,
        enabled: false,
        lastSyncedAt: null,
        createdAt: '2024-01-04T00:00:00Z',
        contentFilter: { id: 'filter-4', name: 'Provider H Filter' },
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
                <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
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

function LocationStateProbe({ onChange }: { onChange: (state: unknown) => void }) {
    const location = useLocation();

    useEffect(() => {
        onChange(location.state);
    }, [location.state, onChange]);

    return null;
}

function RouteStateControls() {
    const navigate = useNavigate();

    return (
        <>
            <button type="button" onClick={() => navigate('/other')}>
                Go elsewhere
            </button>
            <button type="button" onClick={() => navigate(-1)}>
                Go back
            </button>
        </>
    );
}

function renderCreateFromFilterRoute({
    state,
    onLocationStateChange,
}: {
    state: unknown;
    onLocationStateChange?: (state: unknown) => void;
}) {
    return render(
        <QueryClientProvider client={createTestQueryClient()}>
            <MemoryRouter initialEntries={[{ pathname: '/wishlists', state }]}>
                <AuthProvider
                    initialUser={{ id: 'user-1', username: 'test', role: Role.Admin }}
                    initialPermissions={testEffectivePermissions()}
                >
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {onLocationStateChange ? <LocationStateProbe onChange={onLocationStateChange} /> : null}
                                <RouteStateControls />
                                <Routes>
                                    <Route path="/wishlists" element={<WishlistsPage />} />
                                    <Route path="/other" element={<div data-testid="other-route">Other route</div>} />
                                </Routes>
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>,
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

    it('does not render a count at the top of the page', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('My Movies')).toBeInTheDocument();
        });
        expect(screen.queryByTestId('wishlists-page__count')).not.toBeInTheDocument();
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

describe('WishlistsPage error vs empty', () => {
    const mockApiGet = vi.mocked(apiGet);

    function mockNonWishlistEndpoints(url: string) {
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
            return Promise.resolve({ items: [] });
        }
        return null;
    }

    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders an error state (not the empty state) when the wishlist query fails', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.reject(new Error('Internal Server Error'));
            }
            return mockNonWishlistEndpoints(url) ?? Promise.resolve([]);
        });

        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Failed to load wishlists')).toBeInTheDocument();
        });
        expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
        expect(screen.queryByText('No wishlists found')).not.toBeInTheDocument();
    });

    it('renders the empty state when the wishlist query succeeds with no data', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve([]);
            }
            return mockNonWishlistEndpoints(url) ?? Promise.resolve([]);
        });

        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('No wishlists found')).toBeInTheDocument();
        });
        expect(screen.queryByText('Failed to load wishlists')).not.toBeInTheDocument();
    });

    it('describes a wishlist to a role that can create one', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve([]);
            }
            return mockNonWishlistEndpoints(url) ?? Promise.resolve([]);
        });

        render(
            <TestWrapper role={Role.Requester}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('No wishlists found')).toBeInTheDocument();
        });
        expect(
            screen.getByText('A wishlist is a saved filter Magpie uses to automatically monitor for matching content.'),
        ).toBeInTheDocument();
    });

    it('drops the create call-to-action in the empty state for a role that cannot create', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve([]);
            }
            return mockNonWishlistEndpoints(url) ?? Promise.resolve([]);
        });

        render(
            <TestWrapper role={Role.Viewer}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('No wishlists found')).toBeInTheDocument();
        });
        expect(
            screen.queryByText(
                'A wishlist is a saved filter Magpie uses to automatically monitor for matching content.',
            ),
        ).not.toBeInTheDocument();
        expect(
            screen.getByText('Wishlists are saved filters Magpie uses to automatically monitor for matching content.'),
        ).toBeInTheDocument();
    });
});

describe('WishlistsPage status summary removed', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve(mockApiWishlists);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('does not render the status summary band', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('My Movies')).toBeInTheDocument();
        });
        expect(document.querySelector('[aria-label="Status summary"]')).not.toBeInTheDocument();
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
            expect(screen.getByText('Provider I Collection')).toBeInTheDocument();
            expect(screen.getByText('Provider H Watchlist')).toBeInTheDocument();
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

    it('renders an enabled or disabled pill on the row', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('My Movies')).toBeInTheDocument();
        });
        expect(screen.getAllByText('Enabled').length).toBeGreaterThan(0);
        expect(screen.getAllByText('Disabled').length).toBeGreaterThan(0);
    });

    it('applies disabled class to disabled items', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const disabledItem = screen.queryByTestId('wishlists-page__item--disabled');
            expect(disabledItem).toBeInTheDocument();
        });
    });

    it('shows each wishlist row its own monitoring setting', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('My Movies')).toBeInTheDocument();
        });
        expect(screen.getAllByText('Inherit').length).toBeGreaterThan(0);
        expect(screen.getByText('Unmonitored')).toBeInTheDocument();
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

    it('renders a single All/Mine ownership toggle and no clear button', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-component="wishlist-filters"]')).toBeInTheDocument();
        });

        const filterBar = document.querySelector('[data-component="wishlist-filters"]');
        expect(filterBar).toHaveClass('wishlists-page__ownership');
        expect(filterBar?.querySelector('[data-component="ownership-toggle"]')).toBeInTheDocument();

        expect(screen.getByRole('radio', { name: 'All' })).toBeInTheDocument();
        expect(screen.getByRole('radio', { name: 'Mine' })).toBeInTheDocument();
        expect(screen.queryByRole('combobox', { name: /filter by owner/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('combobox', { name: /filter by monitored status/i })).not.toBeInTheDocument();
        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();
    });

    it('defaults to All and narrows to the current user with Mine', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            expect(within(list).getAllByRole('listitem').length).toBe(4);
        });

        await user.click(screen.getByRole('radio', { name: 'Mine' }));

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            expect(within(list).getAllByRole('listitem').length).toBe(2);
        });
    });
});

describe('WishlistsPage Add Wishlist button', () => {
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

    it('passes enabled and monitoring when creating a wishlist', async () => {
        const user = userEvent.setup();
        mockApiPost.mockResolvedValue({
            id: 'wl-new',
            name: 'Unmonitored QA',
            contentFilterId: 'filter-new',
            qualityTierListId: null,
            ownerUserId: 'user-1',
            ownerLock: false,
            enabled: false,
            createdAt: '2024-01-10T00:00:00Z',
        });

        render(
            <TestWrapper role={Role.Requester}>
                <WishlistsPage />
            </TestWrapper>,
        );

        const addButtons = await screen.findAllByRole('button', { name: /add wishlist/i });
        await user.click(addButtons[0]);

        const dialog = await screen.findByRole('dialog');
        await user.type(within(dialog).getByLabelText(/name/i), 'Unmonitored QA');
        await user.click(within(dialog).getByLabelText(/enabled/i));
        await user.click(within(dialog).getByRole('radio', { name: /^Do not monitor:/ }));
        await user.click(within(dialog).getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith(
                '/wishlists',
                expect.objectContaining({
                    name: 'Unmonitored QA',
                    enabled: false,
                    monitoring: false,
                }),
            );
        });
    });
});

describe('WishlistsPage create-from-filter route state', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockFormatMagpieQlCriteria.mockResolvedValue({ query: 'genre has "Action"' });
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
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('opens the add form pre-seeded with the route-state criteria and name', async () => {
        const seededCriteria = { contentHierarchyItem: { and: [{}] } };

        renderCreateFromFilterRoute({
            state: { createCriteria: seededCriteria, createName: 'Action Movies' },
        });

        await waitFor(() => {
            expect(document.querySelector('[data-component="wishlist-form"]')).toBeInTheDocument();
        });
        const nameInput = document.querySelector('[data-field="name"]') as HTMLInputElement;
        expect(nameInput.value).toBe('Action Movies');
    });

    it('clears the route state after applying the create seed', async () => {
        const seededCriteria = { contentHierarchyItem: { and: [{}] } };
        const onLocationStateChange = vi.fn();

        renderCreateFromFilterRoute({
            state: { createCriteria: seededCriteria, createName: 'Action Movies' },
            onLocationStateChange,
        });

        await waitFor(() => {
            expect(document.querySelector('[data-component="wishlist-form"]')).toBeInTheDocument();
        });
        await waitFor(() => {
            expect(onLocationStateChange).toHaveBeenLastCalledWith(null);
        });
    });

    it('does not reopen the add form after cancelling and returning to the consumed history entry', async () => {
        const user = userEvent.setup();
        const seededCriteria = { contentHierarchyItem: { and: [{}] } };

        renderCreateFromFilterRoute({
            state: { createCriteria: seededCriteria, createName: 'Action Movies' },
        });

        await waitFor(() => {
            expect(document.querySelector('[data-component="wishlist-form"]')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        await waitFor(() => {
            expect(document.querySelector('[data-component="wishlist-form"]')).not.toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Go elsewhere' }));

        await waitFor(() => {
            expect(screen.getByTestId('other-route')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Go back' }));

        await waitFor(() => {
            expect(screen.getByText('My Movies')).toBeInTheDocument();
        });
        expect(document.querySelector('[data-component="wishlist-form"]')).not.toBeInTheDocument();
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

    it.each([Role.Requester, Role.Manager])('%s can delete only their own wishlists', async (role) => {
        render(
            <TestWrapper role={role} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('My Movies')).toBeInTheDocument();
        });

        expect(
            [...document.querySelectorAll('[data-action="delete"]')].map((button) =>
                button.closest('[data-wishlist-id]')?.getAttribute('data-wishlist-id'),
            ),
        ).toEqual(['wl-1', 'wl-2']);
    });

    it('Admin can delete every wishlist except locked ones owned by other users', async () => {
        render(
            <TestWrapper role={Role.Admin} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const deleteButtons = document.querySelectorAll('[data-action="delete"]');
            expect(deleteButtons.length).toBe(3);
        });
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

    it('shows the also-delete-filter toggle (off) when the wishlist has a backing filter', async () => {
        const user = userEvent.setup();

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

        const toggle = screen.getByRole('switch');
        expect(toggle).toBeInTheDocument();
        expect(toggle).toHaveAttribute('data-state', 'unchecked');
        expect(screen.getByText('Also delete its filter "Action Movies"')).toBeInTheDocument();
    });

    it('includes deleteFilter=true in the delete call when the toggle is on', async () => {
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

        await user.click(screen.getByRole('switch'));

        const confirmDeleteButton = document.querySelector('[data-action="confirm-delete"]') as HTMLButtonElement;
        await user.click(confirmDeleteButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/wishlists/wl-1?deleteFilter=true');
        });
    });

    it('omits deleteFilter when the toggle is left off', async () => {
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
            expect(mockApiDelete).toHaveBeenCalledWith('/wishlists/wl-1');
        });
    });

    it('disables the also-delete-filter toggle when the caller cannot delete the filter', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-action="delete"]')).toBeInTheDocument();
        });

        const deleteButton = document.querySelector(
            '[data-wishlist-id="wl-2"] [data-action="delete"]',
        ) as HTMLButtonElement;
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Wishlist')).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch');
        expect(toggle).toBeDisabled();
        expect(toggle.parentElement).toHaveAccessibleDescription('You do not have permission to delete this filter.');
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
    const mockApiPatch = vi.mocked(apiPatch);

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
        let completeUpdate!: () => void;
        mockApiPatch.mockReturnValue(
            new Promise<void>((resolve) => {
                completeUpdate = resolve;
            }),
        );

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
            expect(mockApiPatch).toHaveBeenCalledWith('/wishlists/wl-1', { ownerLock: { value: true } });
        });
        expect(lockButton).toBeDisabled();
        expect(document.querySelector('[data-wishlist-id="wl-2"] [data-action="unlock"]')).toBeEnabled();
        await act(async () => completeUpdate());
        expect(lockButton).toBeEnabled();
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

    it.each([
        [Role.Manager, false],
        [Role.Admin, true],
    ])('role %s sees Edit on an unlocked wishlist owned by another user: %s', async (role, shown) => {
        render(
            <TestWrapper role={role} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('My Movies')).toBeInTheDocument();
        });

        const wl4 = document.querySelector('[data-wishlist-id="wl-4"]');
        expect(wl4?.querySelector('[data-action="edit"]') != null).toBe(shown);
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

    it('navigates from a wishlist row with the keyboard when the title link is focused', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <Routes>
                    <Route path="/wishlists" element={<WishlistsPage />} />
                    <Route path="/wishlists/wl-1" element={<h1>Wishlist detail</h1>} />
                </Routes>
            </TestWrapper>,
        );
        const link = await screen.findByRole('link', { name: 'My Movies' });

        // Act
        link.focus();

        // Assert
        expect(link).toHaveFocus();
        expect(link).toHaveAttribute('href', '/wishlists/wl-1');
        expect(link.closest('[role="listitem"]')).not.toHaveAttribute('tabindex');
        await user.keyboard('{Enter}');
        expect(await screen.findByRole('heading', { name: 'Wishlist detail' })).toBeInTheDocument();
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

    it('the ownership toggle has an accessible label', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('radiogroup', { name: /filter wishlists by ownership/i })).toBeInTheDocument();
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

    it('pre-filters to the current user from scope=mine in the URL', async () => {
        render(
            <TestWrapper userId="user-1" initialEntries={['/wishlists?scope=mine']}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            expect(within(list).getAllByRole('listitem').length).toBe(2);
        });
        expect(screen.getByRole('radio', { name: 'Mine' })).toHaveAttribute('aria-checked', 'true');
    });

    it('shows all wishlists with no scope param', async () => {
        render(
            <TestWrapper userId="user-1" initialEntries={['/wishlists']}>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /wishlists/i });
            expect(within(list).getAllByRole('listitem').length).toBe(4);
        });
        expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('aria-checked', 'true');
    });
});

describe('WishlistsPage edit seeds the criteria builder from the wishlist response', () => {
    const mockApiGet = vi.mocked(apiGet);

    const seededCriteria = { contentHierarchyItem: { and: [{ genre: 'Action' }] } };
    const seededWishlist: ApiWishlist = {
        id: 'wl-seed',
        name: 'Seeded Wishlist',
        contentFilterId: 'backing-1',
        qualityTierListId: null,
        ownerUserId: 'user-1',
        ownerLock: false,
        enabled: true,
        lastSyncedAt: null,
        createdAt: '2024-01-05T00:00:00Z',
        contentFilter: { id: 'backing-1', name: 'Seeded (wishlist)', criteria: seededCriteria },
        qualityTierList: null,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockFormatMagpieQlCriteria.mockResolvedValue({ query: 'contentType = "movie" and genre has "Action"' });
        mockParseMagpieQl.mockResolvedValue({ criteria: seededCriteria });
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve([seededWishlist]);
            }
            if (url === '/content-filters' || url === '/content-filters/my') {
                return Promise.resolve([]);
            }
            if (url === '/content-types') {
                return Promise.resolve([{ slug: 'movie', displayName: 'Movies', levelDefinitions: [] }]);
            }
            if (url === '/subject-types') {
                return Promise.resolve([]);
            }
            if (url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 50, totalItems: 0 });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('seeds the builder from contentFilter.criteria when editing', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-wishlist-id="wl-seed"] [data-action="edit"]')).toBeInTheDocument();
        });

        const editButton = document.querySelector(
            '[data-wishlist-id="wl-seed"] [data-action="edit"]',
        ) as HTMLButtonElement;
        await user.click(editButton);

        await waitFor(() => {
            expect(screen.getByText('Edit Wishlist')).toBeInTheDocument();
        });

        await waitFor(() => {
            expect(mockFormatMagpieQlCriteria).toHaveBeenCalledWith(seededCriteria, 'content');
        });
        const dialog = await screen.findByRole('dialog');
        expect(await within(dialog).findByText(/genre has "Action"/)).toBeInTheDocument();
    });

    it('does not block saving a monitored wishlist after editing seeded criteria', async () => {
        const user = userEvent.setup();
        const mockApiPatch = vi.mocked(apiPatch);
        mockApiPatch.mockResolvedValue(undefined);

        render(
            <TestWrapper role={Role.Admin} userId="user-1">
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-wishlist-id="wl-seed"] [data-action="edit"]')).toBeInTheDocument();
        });

        const editButton = document.querySelector(
            '[data-wishlist-id="wl-seed"] [data-action="edit"]',
        ) as HTMLButtonElement;
        await user.click(editButton);

        const dialog = await screen.findByRole('dialog');
        await waitFor(() => expect(within(dialog).getByText(/genre has "Action"/)).toBeInTheDocument());

        const saveBtn = document.querySelector('[data-action="save"]') as HTMLButtonElement;
        await user.click(saveBtn);

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalled();
        });
        expect(screen.queryByText('Add filter criteria to monitor this wishlist')).not.toBeInTheDocument();
    });
});

describe('WishlistsPage row criteria query', () => {
    const mockApiGet = vi.mocked(apiGet);

    const wishlistWithCriteria: ApiWishlist = {
        id: 'wl-crit',
        name: 'Criteria Wishlist',
        contentFilterId: 'filter-crit',
        qualityTierListId: null,
        ownerUserId: 'user-1',
        ownerLock: false,
        enabled: true,
        lastSyncedAt: null,
        createdAt: '2024-01-06T00:00:00Z',
        contentFilter: {
            id: 'filter-crit',
            name: 'Criteria Filter',
            criteria: { contentHierarchyItem: { and: [{ contentType: { and: [{}] } }] } },
        },
        qualityTierList: null,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockFormatMagpieQlCriteria.mockResolvedValue({ query: 'genre = Action' });
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/wishlists') {
                return Promise.resolve([wishlistWithCriteria]);
            }
            if (url === '/content-filters' || url === '/content-filters/my' || url === '/quality-tier-lists') {
                return Promise.resolve([]);
            }
            if (url.startsWith('/tags')) {
                return Promise.resolve([]);
            }
            if (url === '/users') {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 50, totalItems: 0 });
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the formatted criteria query on the row', async () => {
        render(
            <TestWrapper>
                <WishlistsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('wishlists-page__item-query')).toHaveTextContent('genre = Action');
        });
    });
});
