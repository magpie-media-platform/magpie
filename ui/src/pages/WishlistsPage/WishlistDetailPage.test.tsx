import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport, ToastList } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
import { formatDateTime } from '@/lib/dateFormatting';
import { testEffectivePermissions } from '@/test/permissions';

import { WishlistDetailPage } from './WishlistDetailPage';

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

vi.mock('@/lib/magpieQl', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/magpieQl')>();
    return {
        ...actual,
        formatMagpieQlCriteria: async () => ({ query: 'contentType = "movie"' }),
        parseMagpieQl: async () => ({ criteria: populatedCriteria }),
    };
});

const populatedCriteria = {
    contentHierarchyItem: {
        and: [{ contentType: { and: [{ operator: 'Equals', value: 'movie' }] } }],
    },
};

import { apiGet, apiPatch, apiPost, ApiError } from '@/lib/api';
import type { ApiWishlist } from '@/hooks/useWishlists';

const mockApiWishlists: Record<string, ApiWishlist> = {
    'wl-1': {
        id: 'wl-1',
        name: 'My Movies',
        contentFilterId: 'filter-1',
        qualityTierListId: 'qtl-1',
        ownerUserId: 'user-1',
        ownerLock: false,
        enabled: true,
        lastSyncedAt: '2024-01-15T10:30:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        contentFilter: { id: 'filter-1', name: 'Action Movies', criteria: populatedCriteria },
        qualityTierList: { id: 'qtl-1', name: 'HD' },
    },
    'wl-2': {
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
    'wl-3': {
        id: 'wl-3',
        name: 'Provider I Collection',
        contentFilterId: 'filter-3',
        qualityTierListId: 'qtl-2',
        ownerUserId: 'user-2',
        ownerLock: true,
        enabled: true,
        lastSyncedAt: '2024-01-14T18:00:00Z',
        createdAt: '2024-01-03T00:00:00Z',
        contentFilter: { id: 'filter-3', name: 'Provider I List' },
        qualityTierList: { id: 'qtl-2', name: '4K Ultra HD' },
    },
    'wl-4': {
        id: 'wl-4',
        name: 'Unmonitored List',
        contentFilterId: 'filter-4',
        qualityTierListId: null,
        ownerUserId: 'user-1',
        ownerLock: false,
        enabled: false,
        lastSyncedAt: null,
        createdAt: '2024-01-04T00:00:00Z',
        contentFilter: { id: 'filter-4', name: 'Filter', criteria: populatedCriteria },
        qualityTierList: null,
    },
    'wl-5': {
        id: 'wl-5',
        name: 'Empty Wishlist',
        contentFilterId: 'filter-5',
        qualityTierListId: null,
        ownerUserId: 'user-1',
        ownerLock: false,
        enabled: true,
        lastSyncedAt: null,
        createdAt: '2024-01-05T00:00:00Z',
        contentFilter: { id: 'filter-5', name: 'Empty Filter', criteria: {} },
        qualityTierList: null,
    },
};

const MISSING_DETAIL_ID = '00000000-0000-0000-0000-000000000000';

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
    initialPath = '/wishlists/wl-1',
}: {
    children: ReactNode;
    role?: Role;
    userId?: string;
    initialPath?: string;
}) {
    const mockUser = { id: userId, username: 'test', role };
    const queryClient = createTestQueryClient();

    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[initialPath]}>
                <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <Routes>
                                    <Route path="/wishlists/:wishlistId" element={children} />
                                </Routes>
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

function createMockApiGet(wishlists: Record<string, import('@/hooks/useWishlists').ApiWishlist>) {
    return (url: string) => {
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

        if (url === '/wishlists') {
            return Promise.resolve(Object.values(wishlists));
        }

        if (/^\/wishlists\/[^/]+\/discovered/.test(url)) {
            return Promise.resolve({
                page: {
                    items: [
                        {
                            discoveredId: 'discovered-1',
                            source: 'provider-h',
                            externalId: 'tt0095016',
                            title: 'Die Hard',
                            discoveredAt: '2024-01-01T00:00:00Z',
                            linkId: 'content-1',
                            posterUrl: null,
                            publishDate: '1988-07-15T00:00:00Z',
                            addedAt: '2024-01-02T00:00:00Z',
                            status: 'Found',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                counts: { totalDiscovered: 1, inLibrary: 1, notInLibrary: 0, missing: 0 },
            });
        }

        const match = url.match(/^\/wishlists\/([^/]+)$/);
        if (match) {
            const wishlistId = match[1];
            const wishlist = wishlists[wishlistId];
            if (wishlist) {
                return Promise.resolve(wishlist);
            }
            return Promise.reject(new ApiError(404, 'Not found'));
        }
        return Promise.reject(new Error('Unknown endpoint'));
    };
}

describe('WishlistDetailPage', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with correct data-page attribute', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-page="wishlist-detail"]')).toBeInTheDocument();
        });
    });

    it('renders the wishlist title', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'My Movies', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders breadcrumb with wishlist name', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'My Movies', level: 1 })).toBeInTheDocument();
        });

        expect(screen.getByRole('navigation', { name: /breadcrumb/i })).toBeInTheDocument();
        const wishlistLinks = screen.getAllByRole('link', { name: /wishlists/i });
        expect(wishlistLinks.length).toBeGreaterThan(0);
    });

    it('renders enabled badge for enabled wishlist', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Enabled')).toBeInTheDocument();
        });
    });

    it('renders disabled badge for disabled wishlist', async () => {
        render(
            <TestWrapper initialPath="/wishlists/wl-4">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Disabled')).toBeInTheDocument();
        });
    });
});

describe('WishlistDetailPage metadata section', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders exactly the created and last refresh timestamps', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Created:')).toBeInTheDocument();
        });

        const metadata = document.querySelector('.wishlist-detail-page__meta') as HTMLElement;
        expect(metadata).toBeInTheDocument();
        expect(metadata.querySelectorAll('.wishlist-detail-page__meta-item')).toHaveLength(2);
        expect(within(metadata).getByText('Created:')).toBeInTheDocument();
        expect(within(metadata).getByText('Last refresh:')).toBeInTheDocument();
        expect(within(metadata).queryByText('Owner:')).not.toBeInTheDocument();
        expect(within(metadata).queryByText('Type:')).not.toBeInTheDocument();
        expect(within(metadata).queryByText('Quality:')).not.toBeInTheDocument();

        const created = within(metadata).getByTestId('wishlist-detail-page__created-at');
        const lastRefresh = within(metadata).getByTestId('wishlist-detail-page__last-refresh');
        expect(created).toHaveAttribute('datetime', '2024-01-01T00:00:00Z');
        expect(created).toHaveTextContent(formatDateTime('2024-01-01T00:00:00Z'));
        expect(lastRefresh).toHaveAttribute('datetime', '2024-01-15T10:30:00Z');
        expect(lastRefresh).toHaveTextContent(formatDateTime('2024-01-15T10:30:00Z'));
    });

    it('renders Never when the wishlist has not been refreshed', async () => {
        render(
            <TestWrapper initialPath="/wishlists/wl-2">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('wishlist-detail-page__last-refresh')).toHaveTextContent('Never');
        });
        expect(screen.getByTestId('wishlist-detail-page__last-refresh')).not.toHaveAttribute('datetime');
    });
});

describe('WishlistDetailPage discovered list', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the Discovered list with its counts header', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-component="wishlist-discovered"]')).toBeInTheDocument();
        });
        expect(await screen.findByTestId('wishlist-discovered-counts')).toBeInTheDocument();
        const discoveredLinks = await screen.findAllByRole('link', { name: 'Die Hard' });
        expect(discoveredLinks).not.toHaveLength(0);
        expect(discoveredLinks.every((link) => link.getAttribute('href') === '/library/content-1')).toBe(true);
    });

    it('no longer renders the old filter-preview "matching content" section', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-component="wishlist-discovered"]')).toBeInTheDocument();
        });
        expect(screen.queryByText('No matching content')).not.toBeInTheDocument();
        expect(screen.queryByTestId('wishlist-detail-page__summary')).not.toBeInTheDocument();
    });
});

describe('WishlistDetailPage edit action', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows Edit button for owner', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
        });
    });

    it.each([
        [Role.Manager, false],
        [Role.Admin, true],
    ])('shows Edit to role %s on another user unlocked wishlist: %s', async (role, shown) => {
        render(
            <TestWrapper role={role} userId="user-other" initialPath="/wishlists/wl-1">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'My Movies', level: 1 })).toBeInTheDocument();
        });

        expect(screen.queryAllByRole('button', { name: 'Edit' }).length > 0).toBe(shown);
    });

    it('lists Refresh and Edit in the Wishlist Actions sheet for the owner', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper role={Role.Manager} userId="user-1" initialPath="/wishlists/wl-1">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await user.click(await screen.findByRole('button', { name: 'Wishlist Actions' }));

        const sheet = await screen.findByRole('dialog', { name: 'Wishlist Actions' });
        expect(
            within(within(sheet).getByRole('group', { name: 'Quick actions' }))
                .getAllByRole('button')
                .map((action) => action.textContent),
        ).toEqual(['Refresh', 'Edit']);
    });

    it('explains the disabled Refresh action in the Wishlist Actions sheet', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper initialPath="/wishlists/wl-4">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await user.click(await screen.findByRole('button', { name: 'Wishlist Actions' }));

        const sheet = await screen.findByRole('dialog', { name: 'Wishlist Actions' });
        const refresh = within(sheet).getByRole('button', { name: 'Refresh' });
        expect(refresh).toBeDisabled();
        expect(refresh).toHaveAccessibleDescription('Enable the wishlist to refresh it.');
    });

    it('shows no Wishlist Actions button to a manager who cannot refresh or edit the wishlist', async () => {
        render(
            <TestWrapper role={Role.Manager} userId="user-other" initialPath="/wishlists/wl-1">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await screen.findByRole('heading', { name: 'My Movies', level: 1 });
        expect(document.querySelector('.quick-actions-button')).toBeNull();
    });

    it('hides Edit button for non-owner on locked wishlist', async () => {
        render(
            <TestWrapper role={Role.Manager} userId="user-other" initialPath="/wishlists/wl-2">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Plex Watchlist', level: 1 })).toBeInTheDocument();
        });

        expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'View filter' })).toBeInTheDocument();
    });

    it('Edit button has data-action attribute', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
        });

        const editButton = screen.getByRole('button', { name: 'Edit' });
        expect(editButton).toHaveAttribute('aria-label', 'Edit');
        expect(editButton).toHaveAttribute('data-action', 'edit');
    });

    it('opens edit form when Edit button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
        });

        const editButton = screen.getByRole('button', { name: 'Edit' });
        await user.click(editButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
            expect(screen.getByText('Edit Wishlist')).toBeInTheDocument();
        });
    });

    it('waits for the wishlist tags before opening the edit form and keeps them on save', async () => {
        // Arrange
        const user = userEvent.setup();
        const mockApiPatch = vi.mocked(apiPatch);
        mockApiPatch.mockResolvedValue(undefined);
        let resolveEntityTags: (tags: { id: string; name: string }[]) => void = () => {};
        const defaultApiGet = createMockApiGet(mockApiWishlists);
        mockApiGet.mockImplementation((url: string) =>
            url.includes('entityId=')
                ? new Promise((resolve) => {
                      resolveEntityTags = resolve;
                  })
                : defaultApiGet(url),
        );
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );
        const editButton = await screen.findByRole('button', { name: 'Edit' });

        // Act
        await user.click(editButton);

        // Assert
        expect(screen.queryByText('Edit Wishlist')).not.toBeInTheDocument();

        // Act
        resolveEntityTags([{ id: 'tag-a', name: 'alpha' }]);
        await screen.findByText('Edit Wishlist');
        await user.type(screen.getByLabelText(/name/i), ' updated');
        await user.click(document.querySelector('[data-action="save"]') as HTMLElement);

        // Assert
        await waitFor(() => expect(mockApiPatch).toHaveBeenCalled());
        expect(mockApiPatch.mock.calls[0][1]).not.toHaveProperty('tags');
    });

    it('opens the wishlist editor (not a separate surface) from the discovered "Edit filter" affordance', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        const filterButton = await screen.findByRole('button', { name: 'Edit filter' });
        expect(filterButton).toHaveAttribute('data-action', 'edit-filter');
        await user.click(filterButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
            expect(screen.getByText('Edit Wishlist')).toBeInTheDocument();
        });
    });
});

describe('WishlistDetailPage lock indicator', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows locked icon for owner-locked wishlist', async () => {
        render(
            <TestWrapper initialPath="/wishlists/wl-2">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const lockIcon = screen.getByTestId('wishlist-detail-page__lock--locked');
            expect(lockIcon).toBeInTheDocument();
        });
    });

    it('shows unlocked icon for unlocked wishlist when not owner', async () => {
        render(
            <TestWrapper userId="user-other">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const lockIcon = document.querySelector(
                '.wishlist-detail-page__lock:not(.wishlist-detail-page__lock--locked)',
            );
            expect(lockIcon).toBeInTheDocument();
        });
    });
});

describe('WishlistDetailPage not found state', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows not found message for invalid wishlist ID', async () => {
        render(
            <TestWrapper initialPath="/wishlists/invalid-id">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Wishlist Not Found' })).toBeInTheDocument();
            expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
        });
    });

    it('shows back link in not found state', async () => {
        render(
            <TestWrapper initialPath="/wishlists/invalid-id">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('link', { name: /back to wishlists/i })).toBeInTheDocument();
        });
    });

    it('shows not found state for the zero UUID wishlist route', async () => {
        render(
            <TestWrapper initialPath={`/wishlists/${MISSING_DETAIL_ID}`}>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Wishlist Not Found' })).toBeInTheDocument();
            expect(screen.getByRole('link', { name: /back to wishlists/i })).toHaveAttribute('href', '/wishlists');
        });
        expect(document.querySelector('.wishlist-detail-page__loading')).not.toBeInTheDocument();
    });

    it('shows an error state (not the not-found state) when the request fails with a 500', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (/^\/wishlists\/[^/]+$/.test(url)) {
                return Promise.reject(new ApiError(500, 'Internal Server Error'));
            }
            return createMockApiGet(mockApiWishlists)(url);
        });

        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Failed to load wishlist')).toBeInTheDocument();
        });
        expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Wishlist Not Found' })).not.toBeInTheDocument();
    });
});

describe('WishlistDetailPage accessibility', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('discovered list is labelled as a region', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('region', { name: 'Discovered' })).toBeInTheDocument();
        });
    });
});

describe('WishlistDetailPage different wishlists', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders Plex Watchlist correctly', async () => {
        render(
            <TestWrapper initialPath="/wishlists/wl-2">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Plex Watchlist', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders Provider I Collection correctly', async () => {
        render(
            <TestWrapper initialPath="/wishlists/wl-3">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Provider I Collection', level: 1 })).toBeInTheDocument();
        });
    });
});

describe('WishlistDetailPage criteria display', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the formatted criteria query for a wishlist with criteria', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('wishlist-detail-page__query')).toHaveTextContent('contentType = "movie"');
        });
    });

    it('shows a no-criteria placeholder when the criteria are empty', async () => {
        render(
            <TestWrapper initialPath="/wishlists/wl-5">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('wishlist-detail-page__criteria-empty')).toBeInTheDocument();
        });
        expect(screen.queryByTestId('wishlist-detail-page__query')).not.toBeInTheDocument();
    });
});

describe('WishlistDetailPage refresh action', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
        mockApiPost.mockResolvedValue(undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders a Refresh button for the owner instead of a Preview button', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        const refreshButton = await screen.findByRole('button', { name: 'Refresh' });
        expect(refreshButton).toHaveAttribute('aria-label', 'Refresh');
        expect(screen.queryByRole('button', { name: /preview/i })).not.toBeInTheDocument();
    });

    it('starts an owner refresh job when the owner clicks Refresh', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        const refreshButton = await screen.findByRole('button', { name: /refresh/i });
        await user.click(refreshButton);

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/jobs/execute-immediate', {
                jobType: 'WishlistRefreshItem',
                parameters: { wishlistId: 'wl-1' },
            });
        });
    });

    it("hides Refresh for admin viewing another user's wishlist", async () => {
        render(
            <TestWrapper role={Role.Admin} userId="user-1" initialPath="/wishlists/wl-3">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Provider I Collection', level: 1 })).toBeInTheDocument();
        });

        expect(screen.queryByRole('button', { name: /refresh/i })).not.toBeInTheDocument();
        expect(document.querySelector('[data-action="refresh"]')).not.toBeInTheDocument();
        expect(mockApiPost).not.toHaveBeenCalledWith(
            '/jobs/execute-immediate',
            expect.objectContaining({
                jobType: 'WishlistRefreshItem',
                parameters: expect.objectContaining({ wishlistId: 'wl-3' }),
            }),
        );
    });

    it('shows a success toast after a successful refresh', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        const refreshButton = await screen.findByRole('button', { name: /refresh/i });
        await user.click(refreshButton);

        expect(await screen.findByText('Refresh Queued')).toBeInTheDocument();
        expect(screen.queryByText('Searching for matches.')).not.toBeInTheDocument();
    });

    it('disables Refresh and explains why when the wishlist is disabled', async () => {
        render(
            <TestWrapper initialPath="/wishlists/wl-4">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        const refreshButton = await screen.findByRole('button', { name: 'Refresh' });
        expect(refreshButton).toBeDisabled();
        expect(screen.getByText('Enable the wishlist to refresh it.')).toBeInTheDocument();
    });

    it('shows the reason and keeps the discoveries when the refresh does not complete', async () => {
        const user = userEvent.setup();
        const reason = 'The wishlist search was incomplete. Its completed matches were retained. Retry the refresh.';
        mockApiPost.mockResolvedValue({ executionId: 'job-1', status: 'Queued' });
        const wishlistApiGet = createMockApiGet(mockApiWishlists);
        mockApiGet.mockImplementation((url: string) =>
            url === '/jobs/job-1'
                ? Promise.resolve({ id: 'job-1', status: 'Failed', statusMessage: reason })
                : wishlistApiGet(url),
        );

        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await user.click(await screen.findByRole('button', { name: 'Refresh' }));

        expect(await screen.findByTestId('wishlist-detail-page__refresh-outcome')).toHaveTextContent(reason);
        expect(screen.getByText('Die Hard')).toBeInTheDocument();
    });

    it('shows the reason, not a queue failure, when the refresh runs at once and does not complete', async () => {
        const user = userEvent.setup();
        const reason = 'The wishlist search was incomplete. Its completed matches were retained. Retry the refresh.';
        mockApiPost.mockResolvedValue({ executionId: 'job-1', status: 'Failed', statusMessage: reason });

        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await user.click(await screen.findByRole('button', { name: 'Refresh' }));

        expect(await screen.findByTestId('wishlist-detail-page__refresh-outcome')).toHaveTextContent(reason);
        expect(screen.queryByText('Failed to queue a refresh for this wishlist.')).not.toBeInTheDocument();
        expect(screen.getByText('Die Hard')).toBeInTheDocument();
    });

    it('reports a queue failure when the refresh request itself fails', async () => {
        const user = userEvent.setup();
        mockApiPost.mockRejectedValue(new Error('Network error'));

        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await user.click(await screen.findByRole('button', { name: 'Refresh' }));

        expect(await screen.findByText('Failed to queue a refresh for this wishlist.')).toBeInTheDocument();
        expect(screen.queryByTestId('wishlist-detail-page__refresh-outcome')).not.toBeInTheDocument();
    });
});
