import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Routes, Route, useNavigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport, ToastList } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});

import { apiGet } from '@/lib/api';
import { WishlistDiscoveredList } from './WishlistDiscoveredList';

const WISHLIST_ID = 'wl-1';

function response(overrides?: { items?: unknown[]; totalItems?: number; counts?: object }) {
    return {
        page: {
            items: overrides?.items ?? [
                {
                    discoveredId: 'discovered-1',
                    source: 'provider-h',
                    externalId: '1234',
                    title: 'Die Hard',
                    discoveredAt: '2024-01-01T00:00:00Z',
                    linkId: 'ci-1',
                    posterUrl: null,
                    publishDate: '1988-07-15T00:00:00Z',
                    addedAt: '2024-01-02T00:00:00Z',
                    status: 'Found',
                },
                {
                    discoveredId: 'discovered-2',
                    source: 'provider-h',
                    externalId: '5678',
                    title: 'A Removed Item',
                    discoveredAt: '2024-01-03T00:00:00Z',
                    linkId: null,
                    posterUrl: null,
                    publishDate: null,
                    addedAt: null,
                    status: null,
                },
            ],
            pageNumber: 1,
            pageSize: 25,
            totalItems: overrides?.totalItems ?? 2,
        },
        counts: overrides?.counts ?? {
            totalDiscovered: 2,
            inLibrary: 1,
            notInLibrary: 1,
            missing: 0,
        },
    };
}

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
}

function installMatchMedia(width: number) {
    Object.defineProperty(window, 'matchMedia', {
        writable: true,
        configurable: true,
        value: vi.fn((query: string) => {
            const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
            const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);

            return {
                matches:
                    (minWidth === null || width >= Number(minWidth[1])) &&
                    (maxWidth === null || width <= Number(maxWidth[1])),
                media: query,
                onchange: null,
                addListener: () => {},
                removeListener: () => {},
                addEventListener: () => {},
                removeEventListener: () => {},
                dispatchEvent: () => false,
            };
        }),
    });
}

function TestWrapper({
    children,
    initialPath = `/wishlists/${WISHLIST_ID}`,
}: {
    children: ReactNode;
    initialPath?: string;
}) {
    const queryClient = createTestQueryClient();
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[initialPath]}>
                <TooltipProvider>
                    <ToastProvider>
                        <ToastStateProvider>
                            <Routes>
                                <Route path="/wishlists/:wishlistId" element={children} />
                                <Route path="/library/:contentId" element={<div>Library Detail</div>} />
                            </Routes>
                            <ToastList />
                            <ToastViewport />
                        </ToastStateProvider>
                    </ToastProvider>
                </TooltipProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

describe('WishlistDiscoveredList', () => {
    const mockApiGet = vi.mocked(apiGet);

    const findTable = () => screen.findByTestId('wishlist-discovered-table');

    beforeEach(() => {
        vi.clearAllMocks();
        installMatchMedia(1024);
        mockApiGet.mockResolvedValue(response());
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the counts header', async () => {
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('wishlist-discovered-counts')).toHaveTextContent(
                'Discovered 2 · In library 1 · Not in library 1 · Missing 0',
            );
        });
    });

    it('renders a discovered row from the backend contract', async () => {
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        const table = await findTable();
        const titleLink = within(table).getByRole('link', { name: 'Die Hard' });
        expect(titleLink).toHaveAttribute('href', '/library/ci-1');

        const row = titleLink.closest('[data-row-id="discovered-1"]') as HTMLElement;
        expect(within(row).queryByText('(not in library)')).not.toBeInTheDocument();
        expect(within(row).getByText('Fulfilled')).toBeInTheDocument();
    });

    it.each([
        { width: 1024, host: 'wishlist-discovered-table', published: '1975' },
        { width: 390, host: 'wishlist-discovered__mobile-list', published: 'Published 1975' },
    ])('PublishedDate_ShowsYearOnly_WhenPrecisionIsYearAt$width', async ({ width, host, published }) => {
        // Arrange
        installMatchMedia(width);
        mockApiGet.mockResolvedValue(
            response({
                items: [
                    {
                        discoveredId: 'discovered-year',
                        source: 'provider-h',
                        externalId: '1234',
                        title: 'Year Only',
                        discoveredAt: '2024-01-01T00:00:00Z',
                        linkId: 'ci-year',
                        posterUrl: null,
                        publishDate: '1975-01-01T00:00:00Z',
                        publishDatePrecision: 'Year',
                        addedAt: '2024-01-02T00:00:00Z',
                        status: 'Found',
                    },
                ],
                totalItems: 1,
            }),
        );

        // Act
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        // Assert
        const list = await screen.findByTestId(host);
        expect(within(list).getByText(published)).toBeInTheDocument();
    });

    it('renders the placeholder and (not in library) marker for a not-in-library row', async () => {
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await findTable();

        const row = document.querySelector('[data-row-id="discovered-2"]') as HTMLElement;
        expect(within(row).getByText('(not in library)')).toBeInTheDocument();
        expect(within(row).getByTestId('discovered-poster-placeholder')).toBeInTheDocument();
        expect(within(row).queryByRole('link', { name: 'A Removed Item' })).not.toBeInTheDocument();
    });

    it('keeps full discovered titles available through native title tooltips', async () => {
        const linkedTitle = 'The'.concat('Very'.repeat(25), 'LongLinkedWishlistDiscoveryTitle');
        const unlinkedTitle = 'The'.concat('Very'.repeat(25), 'LongUnlinkedWishlistDiscoveryTitle');
        mockApiGet.mockResolvedValue(
            response({
                items: [
                    {
                        discoveredId: 'discovered-long-linked',
                        source: 'provider-h',
                        externalId: 'long-linked',
                        title: linkedTitle,
                        discoveredAt: '2024-01-01T00:00:00Z',
                        linkId: 'ci-long',
                        posterUrl: null,
                        publishDate: '1988-07-15T00:00:00Z',
                        addedAt: '2024-01-02T00:00:00Z',
                        status: 'Found',
                    },
                    {
                        discoveredId: 'discovered-long-unlinked',
                        source: 'provider-h',
                        externalId: 'long-unlinked',
                        title: unlinkedTitle,
                        discoveredAt: '2024-01-03T00:00:00Z',
                        linkId: null,
                        posterUrl: null,
                        publishDate: null,
                        addedAt: null,
                        status: null,
                    },
                ],
            }),
        );

        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        const table = await findTable();
        expect(within(table).getByRole('link', { name: linkedTitle })).toBeInTheDocument();
        expect(within(table).getByTitle(linkedTitle)).toBeInTheDocument();
        expect(within(table).getByTitle(unlinkedTitle)).toHaveTextContent(unlinkedTitle);
    });

    it('drives the standard library-presence filter into the query params', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await findTable();

        await user.click(screen.getByRole('button', { name: 'Filter by Library presence' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Not in library' }));

        await waitFor(() => {
            const urls = mockApiGet.mock.calls.map((c) => c[0] as string);
            expect(urls.some((u) => u.includes('inLibrary=false'))).toBe(true);
        });
        expect(screen.getByRole('button', { name: 'Library presence filtered, edit filter' })).toHaveAttribute(
            'aria-pressed',
            'true',
        );
        expect(screen.getByRole('button', { name: 'Clear filters' })).toBeInTheDocument();
    });

    it('FilterCompactCards_UpdatesUrl_WhenStatusIsApplied', async () => {
        // Arrange
        installMatchMedia(900);
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        expect(await screen.findByTestId('wishlist-discovered__mobile-list')).toBeInTheDocument();
        expect(screen.queryByTestId('wishlist-discovered-table')).not.toBeInTheDocument();
        expect(document.querySelector('.wishlist-discovered__controls')).not.toBeInTheDocument();
        expect(document.querySelector('[data-component="in-library-toggle"]')).not.toBeInTheDocument();

        // Act
        await user.click(screen.getByRole('button', { name: 'Filters, 0 active' }));
        await user.selectOptions(await screen.findByRole('combobox', { name: 'Status' }), 'Missing');
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        // Assert
        await waitFor(() => {
            const latest = mockApiGet.mock.calls.at(-1)?.[0] as string;
            expect(latest).toContain('status=Missing');
        });
        expect(screen.getByRole('button', { name: 'Filters, 1 active' })).toBeInTheDocument();
    });

    it('defaults to publishDate-descending in the query params', async () => {
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await findTable();

        const urls = mockApiGet.mock.calls.map((c) => c[0] as string);
        expect(urls.some((u) => u.includes('sort=PublishDate') && u.includes('sortDescending=true'))).toBe(true);
    });

    it('drives column-header sorting into the sort + dir query params', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await findTable();

        await user.click(screen.getByRole('button', { name: 'Sort by Title' }));

        await waitFor(() => {
            const urls = mockApiGet.mock.calls.map((c) => c[0] as string);
            expect(urls.some((u) => u.includes('sort=Title') && u.includes('sortDescending=false'))).toBe(true);
        });

        await user.click(screen.getByRole('button', { name: 'Sort by Title' }));

        await waitFor(() => {
            const urls = mockApiGet.mock.calls.map((c) => c[0] as string);
            expect(urls.some((u) => u.includes('sort=Title') && u.includes('sortDescending=true'))).toBe(true);
        });
    });

    it('sorts a desc-natural column descending on the first header click', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await findTable();

        await user.click(screen.getByRole('button', { name: 'Sort by Title' }));
        await waitFor(() => {
            const latest = mockApiGet.mock.calls.at(-1)?.[0] as string;
            expect(latest).toContain('sort=Title');
            expect(latest).toContain('sortDescending=false');
        });

        await user.click(screen.getByRole('button', { name: 'Sort by Discovered' }));
        await waitFor(() => {
            const latest = mockApiGet.mock.calls.at(-1)?.[0] as string;
            expect(latest).toContain('sort=Discovered');
            expect(latest).toContain('sortDescending=true');
        });
    });

    it('drives the missing/found status filter into the query params', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await findTable();

        await user.click(screen.getByRole('button', { name: 'Filter by Status' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Missing' }));

        await waitFor(() => {
            const urls = mockApiGet.mock.calls.map((c) => c[0] as string);
            expect(urls.some((u) => u.includes('status=Missing'))).toBe(true);
        });
    });

    it('writes the standard title filter into the query params', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await findTable();

        await user.click(screen.getByRole('button', { name: 'Filter by Title' }));
        await user.type(await screen.findByRole('textbox', { name: 'Title' }), 'hard');
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        await waitFor(() => {
            const urls = mockApiGet.mock.calls.map((c) => c[0] as string);
            expect(urls.some((u) => u.includes('search=hard'))).toBe(true);
        });
    });

    it('re-syncs the search box to ?search= on browser back/forward', async () => {
        const user = userEvent.setup();

        function BackButton() {
            const navigate = useNavigate();
            return (
                <button type="button" onClick={() => navigate(-1)}>
                    history back
                </button>
            );
        }

        render(
            <TestWrapper>
                <>
                    <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
                    <BackButton />
                </>
            </TestWrapper>,
        );

        await findTable();

        await user.click(screen.getByRole('button', { name: 'Filter by Title' }));
        await user.type(await screen.findByRole('textbox', { name: 'Title' }), 'hard');
        await user.click(screen.getByRole('button', { name: 'Apply' }));
        await waitFor(() => {
            const urls = mockApiGet.mock.calls.map((c) => c[0] as string);
            expect(urls.some((u) => u.includes('search=hard'))).toBe(true);
        });

        await user.click(screen.getByRole('button', { name: 'history back' }));

        await waitFor(() =>
            expect(screen.getByRole('button', { name: 'Filter by Title' })).toHaveAttribute('aria-pressed', 'false'),
        );
        await user.click(screen.getByRole('button', { name: 'Filter by Title' }));
        expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveValue('');
    });

    it('composes filters, resets pagination, and clears them through the standard control', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper initialPath={`/wishlists/${WISHLIST_ID}?page=3`}>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await findTable();

        await user.click(screen.getByRole('button', { name: 'Filter by Library presence' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'In library' }));
        await user.click(screen.getByRole('button', { name: 'Filter by Status' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Fulfilled' }));

        await waitFor(() => {
            const latest = mockApiGet.mock.calls.at(-1)?.[0] as string;
            expect(latest).toContain('pageNumber=1');
            expect(latest).toContain('inLibrary=true');
            expect(latest).toContain('status=Found');
        });

        expect(screen.queryByRole('button', { name: /remove .* from discovered/i })).not.toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Clear filters' }));

        await waitFor(() => {
            const latest = mockApiGet.mock.calls.at(-1)?.[0] as string;
            expect(latest).toContain('pageNumber=1');
            expect(latest).not.toContain('inLibrary=');
            expect(latest).not.toContain('status=');
            expect(latest).not.toContain('search=');
        });
        expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
    });

    it('invokes onOpenFilter when the Edit filter affordance is clicked', async () => {
        const user = userEvent.setup();
        const onOpenFilter = vi.fn();

        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} onOpenFilter={onOpenFilter} />
            </TestWrapper>,
        );

        await findTable();

        const filterButton = screen.getByRole('button', { name: 'Edit filter' });
        expect(filterButton).toHaveAttribute('data-action', 'edit-filter');
        await user.click(filterButton);
        expect(onOpenFilter).toHaveBeenCalledTimes(1);
    });

    it('labels the filter affordance "View filter" when the user cannot edit', async () => {
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} onOpenFilter={vi.fn()} canEditFilter={false} />
            </TestWrapper>,
        );

        await findTable();

        expect(screen.getByRole('button', { name: 'View filter' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Edit filter' })).not.toBeInTheDocument();
    });

    it.each([
        [true, 'Edit filter'],
        [false, 'View filter'],
    ])(
        'keeps the filter affordance name when its label is hidden and canEditFilter is %s',
        async (canEditFilter, name) => {
            // Arrange
            render(
                <TestWrapper>
                    <WishlistDiscoveredList
                        wishlistId={WISHLIST_ID}
                        onOpenFilter={vi.fn()}
                        canEditFilter={canEditFilter}
                    />
                </TestWrapper>,
            );
            await findTable();

            // Act
            screen.getByText(name).style.display = 'none';

            // Assert
            expect(screen.getByRole('button', { name })).toHaveAttribute('data-action', 'edit-filter');
        },
    );

    it('displays a found row as "Fulfilled" in the library vocabulary', async () => {
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        const table = await findTable();
        const row = within(table)
            .getByRole('link', { name: 'Die Hard' })
            .closest('[data-row-id="discovered-1"]') as HTMLElement;
        expect(within(row).getByText('Fulfilled')).toBeInTheDocument();
        expect(within(row).queryByText('Found')).not.toBeInTheDocument();
    });

    it('uses the library fulfillment vocabulary in the status filter (Fulfilled, not Found)', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await findTable();

        await user.click(screen.getByRole('button', { name: 'Filter by Status' }));
        expect(await screen.findByRole('menuitemradio', { name: 'All' })).toBeInTheDocument();
        expect(screen.getByRole('menuitemradio', { name: 'Fulfilled' })).toBeInTheDocument();
        expect(screen.queryByRole('menuitemradio', { name: 'Found' })).not.toBeInTheDocument();
    });

    it('renders an empty state when there are no discovered rows', async () => {
        mockApiGet.mockResolvedValue(
            response({
                items: [],
                totalItems: 0,
                counts: { totalDiscovered: 0, inLibrary: 0, notInLibrary: 0, missing: 0 },
            }),
        );

        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Nothing discovered yet')).toBeInTheDocument();
        });
        expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
    });

    it('renders a retryable error state instead of the empty state when the request fails', async () => {
        mockApiGet.mockRejectedValueOnce(new Error('network failed')).mockResolvedValueOnce(response());
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <WishlistDiscoveredList wishlistId={WISHLIST_ID} />
            </TestWrapper>,
        );

        expect(await screen.findByText('Failed to load discovered items')).toBeInTheDocument();
        expect(screen.queryByText('Nothing discovered yet')).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Retry' }));

        expect(await screen.findAllByRole('link', { name: 'Die Hard' })).not.toHaveLength(0);
    });
});
