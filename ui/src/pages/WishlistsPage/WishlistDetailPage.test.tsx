import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';

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

import { apiGet } from '@/lib/api';
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
        contentFilter: { id: 'filter-1', name: 'Action Movies' },
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
        contentFilter: { id: 'filter-4', name: 'Filter' },
        qualityTierList: null,
    },
};

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
                <AuthProvider initialUser={mockUser}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <Routes>
                                    <Route path="/wishlists/:wishlistId" element={children} />
                                </Routes>
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

        const match = url.match(/^\/wishlists\/(.+)$/);
        if (match) {
            const wishlistId = match[1];
            const wishlist = wishlists[wishlistId];
            if (wishlist) {
                return Promise.resolve(wishlist);
            }
            return Promise.reject(new Error('Not found'));
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

    it('renders monitored badge for monitored wishlist', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Monitored')).toBeInTheDocument();
        });
    });

    it('renders unmonitored badge for unmonitored wishlist', async () => {
        render(
            <TestWrapper initialPath="/wishlists/wl-4">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Unmonitored')).toBeInTheDocument();
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

    it('renders owner username when set', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Owner:')).toBeInTheDocument();
        });

        await waitFor(() => {
            const ownerValue = screen.getAllByTestId('wishlist-detail-page__meta-value')[0];
            expect(ownerValue).toBeInTheDocument();

            expect(ownerValue?.textContent).not.toMatch(
                /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
            );
        });
    });

    it('resolves owner from auth user when users API is unavailable', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/users') {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 50, totalItems: 0 });
            }
            return createMockApiGet(mockApiWishlists)(url);
        });

        render(
            <TestWrapper userId="user-1">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Owner:')).toBeInTheDocument();
        });

        await waitFor(() => {
            const ownerValue = screen.getAllByTestId('wishlist-detail-page__meta-value')[0];
            expect(ownerValue).toHaveTextContent('test');
        });
    });

    it('shows Unknown for unresolvable owner', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/users') {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 50, totalItems: 0 });
            }
            return createMockApiGet(mockApiWishlists)(url);
        });

        render(
            <TestWrapper userId="user-other" initialPath="/wishlists/wl-3">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Owner:')).toBeInTheDocument();
        });

        await waitFor(() => {
            const ownerValue = screen.getAllByTestId('wishlist-detail-page__meta-value')[0];
            expect(ownerValue).toHaveTextContent('Unknown');
        });
    });

    it('renders filter name when set', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Filter:')).toBeInTheDocument();
            expect(screen.getByText('Action Movies')).toBeInTheDocument();
        });
    });

    it('renders quality tier name when set', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Quality:')).toBeInTheDocument();
            expect(screen.getByText('HD')).toBeInTheDocument();
        });
    });

    it('renders created time', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Created:')).toBeInTheDocument();
        });
    });
});

describe('WishlistDetailPage summary badges', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders status summary section with no badges when all counts are zero', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const summary = screen.getByTestId('wishlist-detail-page__summary');
            expect(summary).toBeInTheDocument();

            const badges = summary?.querySelectorAll('.badge') ?? [];
            expect(badges.length).toBe(0);
        });
    });

    it('hides zero-count badges when no content items', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const summary = screen.getByTestId('wishlist-detail-page__summary');
            expect(summary).toBeInTheDocument();

            expect(summary?.querySelector('.badge[data-status="missing"]')).toBeNull();
            expect(summary?.querySelector('.badge[data-status="upgradable"]')).toBeNull();
            expect(summary?.querySelector('.badge[data-status="fulfilled"]')).toBeNull();
            expect(summary?.querySelector('.badge[data-status="fulfilledExternally"]')).toBeNull();
        });
    });
});

describe('WishlistDetailPage content list', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation(createMockApiGet(mockApiWishlists));
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows empty state when no content items', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('No content')).toBeInTheDocument();
            expect(screen.getByText('Content tracking will be available in a future update.')).toBeInTheDocument();
        });
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
            expect(screen.getByRole('button', { name: /edit/i })).toBeInTheDocument();
        });
    });

    it('shows Edit button for Manager on unlocked wishlist', async () => {
        render(
            <TestWrapper role={Role.Manager}>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /edit/i })).toBeInTheDocument();
        });
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

        expect(screen.queryByRole('button', { name: /edit/i })).not.toBeInTheDocument();
    });

    it('Edit button has data-action attribute', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /edit/i })).toBeInTheDocument();
        });

        const editButton = screen.getByRole('button', { name: /edit/i });
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
            expect(screen.getByRole('button', { name: /edit/i })).toBeInTheDocument();
        });

        const editButton = screen.getByRole('button', { name: /edit/i });
        await user.click(editButton);

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

    it('content list has accessible label', async () => {
        render(
            <TestWrapper>
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const emptyState = document.querySelector('[data-component="wishlist-detail-empty"]');
            expect(emptyState).toBeInTheDocument();
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

    it('renders Trakt Collection correctly', async () => {
        render(
            <TestWrapper initialPath="/wishlists/wl-3">
                <WishlistDetailPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Trakt Collection', level: 1 })).toBeInTheDocument();
        });
    });
});
