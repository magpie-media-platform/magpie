import { useEffect, useState } from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { LibraryPage } from './LibraryPage';
import { ToastProvider, ToastStateProvider, ToastList, ToastViewport } from '@/components/ui/Toast';
import { AuthProvider, Role } from '@/auth';
import { testEffectivePermissions } from '@/test/permissions';
import type { ContentListResult } from '@/types';
import type { ContentListItem } from '@/types/content';
import { ApiError } from '@/lib/api';
import { TooltipProvider } from '@/components/ui/Tooltip';

const mockNavigate = vi.fn();

function createMatchMedia(width: number) {
    return vi.fn((query: string) => {
        const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);

        return {
            matches: minWidth !== null && width >= Number(minWidth[1]),
            media: query,
            onchange: null,
            addListener: () => {},
            removeListener: () => {},
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => false,
        };
    });
}

vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual('react-router-dom');
    return {
        ...actual,
        useNavigate: () => mockNavigate,
    };
});

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockApiTags = [{ name: 'Favorites' }, { name: 'Watch Later' }, { name: 'Family' }, { name: 'Action' }];

const mockContentItems: ContentListItem[] = [
    {
        id: 'content-1',
        title: 'Breaking Bad',
        posterUrl: 'https://example.com/poster1.jpg',
        level: 'show',
        levelLabel: 'Show',
        contentType: 'tv',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'fulfilled',
        year: 2008,
        publishDate: '2008-01-20T00:00:00Z',
        overview: 'A high school chemistry teacher turned methamphetamine manufacturer.',
        dateAdded: '2024-01-15T10:30:00Z',
        lastSearched: '2024-01-20T14:00:00Z',
    },
    {
        id: 'content-2',
        title: 'The Office',
        posterUrl: 'https://example.com/poster2.jpg',
        level: 'show',
        levelLabel: 'Show',
        contentType: 'tv',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'upgradable',
        year: 2005,
        dateAdded: '2024-01-10T08:00:00Z',
        lastSearched: '2024-01-18T09:30:00Z',
    },
    {
        id: 'content-3',
        title: 'Dune: Part Two',
        posterUrl: 'https://example.com/poster3.jpg',
        level: 'movie',
        levelLabel: 'Movie',
        contentType: 'movie',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'missing',
        year: 2024,
        dateAdded: '2024-01-25T16:45:00Z',
        lastSearched: '2024-01-26T12:00:00Z',
    },
    {
        id: 'content-4',
        title: 'Shogun',
        posterUrl: 'https://example.com/poster4.jpg',
        level: 'show',
        levelLabel: 'Show',
        contentType: 'tv',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'fulfilled',
        year: 2024,
        dateAdded: '2024-01-22T11:00:00Z',
        lastSearched: '2024-01-24T15:30:00Z',
    },
    {
        id: 'content-5',
        title: 'The Gentlemen',
        posterUrl: 'https://example.com/poster5.jpg',
        level: 'show',
        levelLabel: 'Show',
        contentType: 'tv',
        monitoring: 'Unmonitored',
        effectiveMonitoring: 'Unmonitored',
        fulfillment: 'missing',
        year: 2024,
        dateAdded: '2024-01-20T09:15:00Z',
        lastSearched: undefined,
    },
    {
        id: 'content-6',
        title: '3 Body Problem',
        posterUrl: 'https://example.com/poster6.jpg',
        level: 'show',
        levelLabel: 'Show',
        contentType: 'tv',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'upgradable',
        year: 2024,
        dateAdded: '2024-01-23T14:20:00Z',
        lastSearched: '2024-01-25T08:00:00Z',
    },
    {
        id: 'content-7',
        title: 'Oppenheimer',
        posterUrl: 'https://example.com/poster7.jpg',
        level: 'movie',
        levelLabel: 'Movie',
        contentType: 'movie',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'fulfilled',
        year: 2023,
        dateAdded: '2024-01-05T12:00:00Z',
        lastSearched: '2024-01-15T10:00:00Z',
    },
    {
        id: 'content-8',
        title: 'Poor Things',
        posterUrl: 'https://example.com/poster8.jpg',
        level: 'movie',
        levelLabel: 'Movie',
        contentType: 'movie',
        monitoring: 'Unmonitored',
        effectiveMonitoring: 'Unmonitored',
        fulfillment: 'fulfilledExternally',
        year: 2023,
        dateAdded: '2024-01-12T17:30:00Z',
        lastSearched: '2024-01-12T17:30:00Z',
    },
    {
        id: 'content-9',
        title: 'Fallout',
        level: 'show',
        levelLabel: 'Show',
        contentType: 'tv',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'fulfilled',
        year: 2024,
        dateAdded: '2024-01-24T10:00:00Z',
        lastSearched: '2024-01-26T09:00:00Z',
    },
    {
        id: 'content-10',
        title: 'The Bear',
        posterUrl: 'https://example.com/poster10.jpg',
        level: 'show',
        levelLabel: 'Show',
        contentType: 'tv',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'fulfilled',
        year: 2022,
        dateAdded: '2024-01-08T13:45:00Z',
        lastSearched: '2024-01-22T11:15:00Z',
    },
];

const mockUseContentList = vi.fn();
const mockUseTags = vi.fn();
const mockUseLibraryTypes = vi.fn();
const mockUseContentLevelCounts = vi.fn();
const mockUseContentFilters = vi.fn();
const mockCreateFilterMutateAsync = vi.fn();
const mockParseMagpieQl = vi.fn();
const mockBulkPatchContent = vi.fn();
const mockBulkDeleteContent = vi.fn();

vi.mock('@/hooks/useFilters', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useFilters')>();
    return {
        ...actual,
        useContentFilters: () => mockUseContentFilters(),
        useCreateContentFilter: () => ({ mutateAsync: mockCreateFilterMutateAsync, isPending: false }),
        useUpdateContentFilter: () => ({ mutateAsync: vi.fn(), isPending: false }),
    };
});

vi.mock('@/lib/magpieQl', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/magpieQl')>();
    return {
        ...actual,
        parseMagpieQl: (...args: unknown[]) => mockParseMagpieQl(...args),
    };
});

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();
    return {
        ...actual,
        useLibraryTypes: () => mockUseLibraryTypes(),
        useContentLevelCounts: (contentType: string, levels: unknown) => mockUseContentLevelCounts(contentType, levels),
    };
});

vi.mock('@/hooks/useContent', () => ({
    useContentList: (params: unknown, options?: { enabled?: boolean }) =>
        options?.enabled === false ? mockUseContentList(params, options) : mockUseContentList(params),
    useContentTags: () => mockUseTags(),
    contentQueryKeys: {
        all: ['content'],
        list: () => ['content', 'list'],
        tags: () => ['content', 'tags'],
    },
    useBulkPatchContent: () => ({
        mutateAsync: mockBulkPatchContent,
        isPending: false,
        error: null,
    }),
    useBulkDeleteContent: () => ({
        mutateAsync: mockBulkDeleteContent,
        isPending: false,
        error: null,
    }),
}));

const mockUseRootFolders = vi.fn();
const mockUseQualityTierLists = vi.fn();

vi.mock('@/hooks/useRootFolders', () => ({
    useRootFolders: () => mockUseRootFolders(),
}));

vi.mock('@/hooks/useQualityTierLists', () => ({
    useQualityTierLists: () => mockUseQualityTierLists(),
}));

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });
}

function renderLibraryPage(role: Role = Role.Admin, initialEntries: string[] = ['/']) {
    const queryClient = createTestQueryClient();
    const mockUser = { id: 'user-1', username: 'testuser', role };

    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={initialEntries}>
                <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                    <ToastProvider>
                        <ToastStateProvider>
                            <LibraryPage />
                            <ToastList />
                            <ToastViewport />
                        </ToastStateProvider>
                    </ToastProvider>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>,
        { wrapper: TooltipProvider },
    );
}

function findColumnFilterTrigger(column: string): HTMLElement | undefined {
    const triggers = Array.from(document.querySelectorAll<HTMLElement>('[data-action="column-filter"]'));
    const pattern = new RegExp(column, 'i');
    return triggers.find((t) => pattern.test(t.getAttribute('aria-label') ?? ''));
}

describe('LibraryPage', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    const defaultContentListResult: ContentListResult = {
        items: mockContentItems,
        pageNumber: 1,
        pageSize: 100,
        totalItems: mockContentItems.length,
    };

    beforeEach(() => {
        mockNavigate.mockClear();
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);

        mockUseContentList.mockReturnValue({
            data: defaultContentListResult,
            isLoading: false,
            error: null,
        });

        mockBulkPatchContent.mockResolvedValue({ count: 1, error: null });
        mockBulkDeleteContent.mockResolvedValue({ count: 0, error: null });
        mockUseRootFolders.mockReturnValue({
            folders: [{ id: 'root-1', name: 'Movies', path: '/media/movies', contentType: 'movies' }],
        });
        mockUseQualityTierLists.mockReturnValue({
            tierLists: [{ id: 'tier-1', name: 'HD-1080p', mimeTypes: [] }],
        });

        mockUseTags.mockReturnValue({
            data: mockApiTags,
            isLoading: false,
            error: null,
        });

        mockUseLibraryTypes.mockReturnValue({
            contentTypes: [
                {
                    id: 'movie',
                    name: 'Movies',
                    pluralName: 'Movies',
                    slug: 'movie',
                    levels: [{ slug: 'movie', displayName: 'Movie', parentSlug: null }],
                },
                {
                    id: 'tv',
                    name: 'TV',
                    pluralName: 'TV Shows',
                    slug: 'tv',
                    levels: [
                        { slug: 'show', displayName: 'Show', parentSlug: null },
                        { slug: 'series', displayName: 'Series', parentSlug: 'show' },
                    ],
                },
                {
                    id: 'foo',
                    name: 'Foo',
                    pluralName: 'Foo',
                    slug: 'foo',
                    levels: [
                        { slug: 'foo-a', displayName: 'Alpha', parentSlug: null },
                        { slug: 'foo-a-child', displayName: 'Alpha child', parentSlug: 'foo-a' },
                        { slug: 'foo-b', displayName: 'Beta', parentSlug: null },
                        { slug: 'foo-c', displayName: 'Gamma', parentSlug: null },
                    ],
                },
            ],
            subjectTypes: [],
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });

        mockUseContentLevelCounts.mockReturnValue({
            counts: { 'foo-a': 1240, 'foo-b': 86, 'foo-c': 42 },
        });

        mockUseContentFilters.mockReturnValue({ filters: [], isLoading: false, error: null, refetch: vi.fn() });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the library page with correct data-page attribute', () => {
        renderLibraryPage();
        expect(document.querySelector('[data-page="library"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        renderLibraryPage();
        expect(screen.getByRole('heading', { name: 'Library', level: 1 })).toBeInTheDocument();
    });

    it('renders the content list component', () => {
        renderLibraryPage();
        expect(document.querySelector('[data-component="content-list"]')).toBeInTheDocument();
    });

    it('shows loading state while fetching data', () => {
        mockUseContentList.mockReturnValue({
            data: undefined,
            isLoading: true,
            error: null,
        });

        renderLibraryPage();

        expect(screen.getByTestId('library-page__loading')).toBeInTheDocument();
    });

    it('shows error state when fetch fails', () => {
        mockUseContentList.mockReturnValue({
            data: undefined,
            isLoading: false,
            error: new Error('Failed to fetch'),
        });

        renderLibraryPage();
        expect(screen.getByText('Failed to load content')).toBeInTheDocument();
    });

    it('renders the server reason and no Retry for an invalid-filter (400) error', () => {
        mockUseContentList.mockReturnValue({
            data: undefined,
            isLoading: false,
            error: new ApiError(400, "Operator 'Contains' is not supported for field 'Url'"),
        });

        renderLibraryPage();

        expect(screen.getByText('Invalid filter')).toBeInTheDocument();
        expect(screen.getByText("Operator 'Contains' is not supported for field 'Url'")).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Edit filter' })).toBeInTheDocument();
        expect(screen.queryByText('Failed to load content')).not.toBeInTheDocument();
    });

    it('shows Retry for a transient (500) error', () => {
        const refetch = vi.fn();
        mockUseContentList.mockReturnValue({
            data: undefined,
            isLoading: false,
            error: new ApiError(500, 'Internal Server Error'),
            refetch,
        });

        renderLibraryPage();

        expect(screen.getByText('Failed to load content')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
        expect(screen.queryByText('Invalid filter')).not.toBeInTheDocument();
    });

    it('shows the normal empty state for a valid filter that returns no items', () => {
        mockUseContentList.mockReturnValue({
            data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
            isLoading: false,
            error: null,
        });

        renderLibraryPage(Role.Admin, ['/library/content/movie/movie?filter=genre%20has%20%22Nonexistent%22']);

        expect(screen.getByText('No results found')).toBeInTheDocument();
        expect(screen.queryByText('Invalid filter')).not.toBeInTheDocument();
        expect(screen.queryByText('Failed to load content')).not.toBeInTheDocument();
    });

    describe('content items', () => {
        it('renders content items', () => {
            renderLibraryPage();

            expect(screen.getAllByText('Breaking Bad').length).toBeGreaterThan(0);
            expect(screen.getAllByText('The Office').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Dune: Part Two').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Shogun').length).toBeGreaterThan(0);
        });

        it('renders the publish date in the Published column', () => {
            renderLibraryPage();

            expect(screen.getByText('Jan 20, 2008')).toBeInTheDocument();
        });

        it('renders fulfillment status badges', () => {
            renderLibraryPage();

            expect(screen.getAllByText('Fulfilled').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Missing').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Upgradable').length).toBeGreaterThan(0);
        });

        it('shows fulfillment for an excluded item', () => {
            // Arrange
            // Act
            renderLibraryPage();

            // Assert
            expect(screen.getAllByText('External').length).toBeGreaterThan(0);
        });

        it('renders level badges by hierarchy level, not collapsed content type', () => {
            vi.spyOn(window, 'matchMedia').mockImplementation(createMatchMedia(375));

            renderLibraryPage();

            expect(screen.getAllByText('Show').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Movie').length).toBeGreaterThan(0);
        });
    });

    describe('navigation', () => {
        it('navigates to content details when row is clicked', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const table = document.querySelector('.library-page__table') as HTMLElement;
            const row = within(table).getByText('Breaking Bad').closest('tr');
            expect(row).toBeInTheDocument();

            await user.click(row!);

            expect(mockNavigate).toHaveBeenCalledWith('/library/content-1');
        });

        it('renders each mobile card as a router link to the item detail page', () => {
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            const firstCard = within(mobileList).getByText('Breaking Bad').closest('a');
            expect(firstCard).toHaveAttribute('href', '/library/content-1');
        });

        it('renders a truncated description on the mobile card when an overview is present', () => {
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            const overview = within(mobileList).getByText(
                /A high school chemistry teacher turned methamphetamine manufacturer/,
            );
            expect(overview).toHaveClass('line-clamp-2');
        });
    });

    describe('accessibility', () => {
        it('has icon marked as decorative', () => {
            renderLibraryPage();

            const icon = screen.getByTestId('library-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });

        it('renders a table with grid role', () => {
            renderLibraryPage();

            expect(screen.getByRole('grid')).toBeInTheDocument();
        });
    });

    describe('extension slot', () => {
        it('renders the content list slot', () => {
            renderLibraryPage();

            const slotWrapper = document.querySelector('[data-slot="content.list"]');
            expect(slotWrapper).toBeInTheDocument();
        });
    });

    describe('top-level browse scoping', () => {
        it('requests top-level-only items by default so children are not listed flat', () => {
            renderLibraryPage();

            expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ topLevelOnly: true }));
        });

        it('lists matching items at every level when the all-levels scope has a filter', async () => {
            renderLibraryPage(Role.Admin, ['/library/all?filter=title%20contains%20%22dark%22&levels=all']);

            await waitFor(() =>
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'title contains "dark"' }),
                ),
            );
            expect(mockUseContentList.mock.lastCall?.[0]).not.toHaveProperty('topLevelOnly');
            expect(screen.getByText('Includes matching child items, not only top-level items.')).toBeInTheDocument();
        });

        it('lists items of the linked fulfillment at every level', async () => {
            renderLibraryPage(Role.Admin, ['/library/all?fulfillment=missing&levels=all']);

            await waitFor(() =>
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'fulfillment = Missing' }),
                ),
            );
            expect(mockUseContentList.mock.lastCall?.[0]).not.toHaveProperty('topLevelOnly');
        });

        it('keeps the top-level scope when the all-levels scope has no filter', () => {
            renderLibraryPage(Role.Admin, ['/library/all?levels=all']);

            expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ topLevelOnly: true }));
        });
    });

    describe('type-scoped content-type prefill', () => {
        function renderTypeScoped(contentType: string) {
            const queryClient = createTestQueryClient();
            const mockUser = { id: 'user-1', username: 'testuser', role: Role.Admin };
            return render(
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={[`/library/content/${contentType}`]}>
                        <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                            <ToastProvider>
                                <ToastStateProvider>
                                    <Routes>
                                        <Route path="/library/content/:contentType" element={<LibraryPage />} />
                                    </Routes>
                                    <ToastList />
                                </ToastStateProvider>
                            </ToastProvider>
                        </AuthProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
                { wrapper: TooltipProvider },
            );
        }

        it('pre-selects the route content type in the filter without marking the page filtered', () => {
            renderTypeScoped('movie');

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Movies');

            expect(document.querySelector('.filter-bar--active')).not.toBeInTheDocument();
            const lastCall = mockUseContentList.mock.calls.at(-1)?.[0];
            expect(lastCall).toMatchObject({ contentType: 'movie' });
            expect(lastCall).not.toHaveProperty('filter');
        });

        it('redirects an invalid route content type without scoping the API query or add flow to it', async () => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderTypeScoped('not-a-type');

            await waitFor(() => {
                expect(mockNavigate).toHaveBeenCalledWith('/library/all', { replace: true });
            });
            expect(mockUseContentList.mock.calls.map(([params]) => params)).not.toContainEqual(
                expect.objectContaining({ contentType: 'not-a-type' }),
            );
            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.not.objectContaining({ contentType: 'not-a-type' }),
                { enabled: false },
            );
            expect(document.querySelector('a[href="/library/add?contentType=not-a-type"]')).not.toBeInTheDocument();
            expect(screen.queryByRole('link', { name: /add content/i })).not.toBeInTheDocument();
        });
    });

    describe('route-scoped content-type filter', () => {
        function renderRouteScoped(initialEntry: string) {
            const queryClient = createTestQueryClient();
            const mockUser = { id: 'user-1', username: 'testuser', role: Role.Admin };
            return render(
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={[initialEntry]}>
                        <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                            <ToastProvider>
                                <ToastStateProvider>
                                    <Routes>
                                        <Route path="/library/all" element={<LibraryPage />} />
                                        <Route path="/library/content/:contentType" element={<LibraryPage />} />
                                        <Route
                                            path="/library/content/:contentType/:levelSlug"
                                            element={<LibraryPage />}
                                        />
                                    </Routes>
                                    <ToastList />
                                </ToastStateProvider>
                            </ToastProvider>
                        </AuthProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
                { wrapper: TooltipProvider },
            );
        }

        it('scopes the filter dropdown to the route type and drops other types and Any', async () => {
            const user = userEvent.setup();
            renderRouteScoped('/library/content/movie');

            await user.click(screen.getByRole('combobox', { name: 'Content Type' }));

            expect(await screen.findByRole('option', { name: 'Movies' })).toBeInTheDocument();
            expect(screen.queryByRole('option', { name: 'Any' })).not.toBeInTheDocument();
            expect(screen.queryByRole('option', { name: 'TV' })).not.toBeInTheDocument();
        });

        it('scopes the table type funnel to the route type and drops other types and Any', async () => {
            const user = userEvent.setup();
            renderRouteScoped('/library/content/movie');

            await user.click(findColumnFilterTrigger('type')!);

            expect(await screen.findByRole('menuitemradio', { name: 'Movies' })).toBeInTheDocument();
            expect(screen.queryByRole('menuitemradio', { name: 'Any' })).not.toBeInTheDocument();
            expect(screen.queryByRole('menuitemradio', { name: 'TV' })).not.toBeInTheDocument();
        });

        it('locks the filter dropdown to the level on a leaf route', () => {
            renderRouteScoped('/library/content/foo/foo-a');

            const trigger = screen.getByRole('combobox', { name: 'Content Type' });
            expect(trigger).toHaveTextContent('Foo > Alpha');
            expect(trigger).toHaveAttribute('data-disabled');
        });

        it('locks the table type funnel on a leaf route', () => {
            renderRouteScoped('/library/content/foo/foo-a');

            expect(findColumnFilterTrigger('type')).toBeDisabled();
        });

        it('keeps all content types and the Any option on the all-content route', async () => {
            const user = userEvent.setup();
            renderRouteScoped('/library/all');

            await user.click(screen.getByRole('combobox', { name: 'Content Type' }));

            expect(await screen.findByRole('option', { name: 'Any' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Movies' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'TV' })).toBeInTheDocument();
        });
    });

    describe('header sort (server-side)', () => {
        it('keeps desktop header sorting and uses the shared toolbar for mobile cards', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-component="content-sorting"]')).not.toBeInTheDocument();
            const mobileList = screen.getByTestId('library-page__mobile-list');
            expect(within(mobileList).getByRole('combobox', { name: 'Sort by field' })).toBeInTheDocument();
            expect(screen.getAllByRole('button', { name: /sort by/i }).length).toBeGreaterThan(0);
        });

        it('passes publish-date descending sort params to the server by default', () => {
            renderLibraryPage();

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ sortBy: 'PublishDate', sortDirection: 'Descending' }),
            );
        });

        it('toggles the Published header to ascending and sends ascending sort params', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const headers = screen.getAllByRole('button', { name: /sort by/i });
            const published = headers.find((h) => /published/i.test(h.getAttribute('aria-label') ?? ''));
            await user.click(published!);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ sortBy: 'PublishDate', sortDirection: 'Ascending' }),
                );
            });
        });

        it('clicking the Title header sorts by Name on the server', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const headers = screen.getAllByRole('button', { name: /sort by/i });
            const title = headers.find((h) => /title/i.test(h.getAttribute('aria-label') ?? ''));
            await user.click(title!);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ sortBy: 'Name' }));
            });
        });

        it('clicking the Added header sorts by CreatedAt on the server', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const headers = screen.getAllByRole('button', { name: /sort by/i });
            const added = headers.find((h) => /added/i.test(h.getAttribute('aria-label') ?? ''));
            await user.click(added!);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ sortBy: 'CreatedAt' }));
            });
        });

        it('clicking the Fulfilled header sorts by FulfilledAt on the server', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const headers = screen.getAllByRole('button', { name: /sort by/i });
            const fulfilled = headers.find((header) => /fulfilled/i.test(header.getAttribute('aria-label') ?? ''));
            await user.click(fulfilled!);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ sortBy: 'FulfilledAt' }));
            });
        });

        it('restores fulfilled-date sorting from the URL', () => {
            renderLibraryPage(Role.Admin, ['/?sort=fulfilledDate&sortDir=desc']);

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ sortBy: 'FulfilledAt', sortDirection: 'Descending' }),
            );
        });
    });

    describe('content filters', () => {
        it('renders the filter bar component', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-component="filter-bar"]')).toBeInTheDocument();
        });

        it('renders filter toggle button', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-action="toggle-filter"]')).toBeInTheDocument();
        });
    });

    describe('removed redundant controls', () => {
        it('no longer renders the header title-search box', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-component="content-search"]')).not.toBeInTheDocument();
            expect(screen.queryByRole('searchbox', { name: 'Search content by title' })).not.toBeInTheDocument();
        });

        it('no longer renders the standalone tag dropdown', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-filter="tag"]')).not.toBeInTheDocument();
        });

        it('no longer renders the standalone fulfillment status control (Status column funnel owns it)', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-component="fulfillment-control"]')).not.toBeInTheDocument();
            expect(document.querySelector('.library-page__status')).not.toBeInTheDocument();
        });
    });

    describe('header column filters (single query source of truth)', () => {
        async function openMonitoringFacet(user: ReturnType<typeof userEvent.setup>) {
            const monitoring = findColumnFilterTrigger('monitoring');
            await user.click(monitoring!);
        }

        it('writes a boolean facet selection into params.filter as a query predicate', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            await openMonitoringFacet(user);
            const unmonitored = await screen.findByRole('menuitemradio', { name: 'Unmonitored' });
            await user.click(unmonitored);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'monitoring = Unmonitored' }),
                );
            });
        });

        it('reflects the header-filter predicate in the FilterBar (one query string)', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            await openMonitoringFacet(user);
            const active = await screen.findByRole('menuitemradio', { name: 'Monitored' });
            await user.click(active);

            await waitFor(() => {
                const filterBar = document.querySelector('[data-component="filter-bar"]');
                expect(filterBar).toHaveAttribute('data-expanded', 'true');
            });
            expect(document.querySelector('[data-component="simple-filter-builder"]')).toBeInTheDocument();
            expect(document.querySelector('#sfb-monitoring')).not.toBeInTheDocument();
            const preview = document.querySelector('.filter-bar__query-text');
            expect(preview).toHaveTextContent('monitoring != Unmonitored');
        });

        it('clearing the facet removes the predicate from params.filter', async () => {
            const user = userEvent.setup();
            renderLibraryPage(Role.Admin, ['/?filter=monitoring%20%3D%20Unmonitored']);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'monitoring = Unmonitored' }),
                );
            });

            const user2 = user;
            const monitoring = findColumnFilterTrigger('monitoring');
            await user2.click(monitoring!);
            const any = await screen.findByRole('menuitemradio', { name: 'Any' });
            await user2.click(any);

            await waitFor(() => {
                const lastCall = mockUseContentList.mock.calls.at(-1)?.[0] as { filter?: string };
                expect(lastCall.filter).toBeUndefined();
            });
        });

        it('round-trips: a URL query with a predicate shows the header filter active', async () => {
            renderLibraryPage(Role.Admin, ['/?filter=monitoring%20!%3D%20Unmonitored']);

            await waitFor(() => {
                const monitoring = findColumnFilterTrigger('monitoring');
                expect(monitoring).toHaveAttribute('data-active', 'true');
            });
        });

        it('a Kind facet selection writes a contentType predicate into the query', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const kind = findColumnFilterTrigger('type');
            await user.click(kind!);

            const movies = await screen.findByRole('menuitemradio', { name: 'Movies' });
            await user.click(movies);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'contentType = "movie"' }),
                );
            });
        });

        it('lists root levels as flat siblings and writes a composite selection', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            await user.click(findColumnFilterTrigger('type')!);

            expect(await screen.findByRole('menuitemradio', { name: 'Foo > Alpha' })).toBeInTheDocument();
            const beta = screen.getByRole('menuitemradio', { name: 'Foo > Beta' });
            expect(beta).toBeInTheDocument();
            expect(screen.getByRole('menuitemradio', { name: 'Foo > Gamma' })).toBeInTheDocument();
            expect(screen.queryByRole('menuitemradio', { name: /Alpha child/ })).not.toBeInTheDocument();

            await user.click(beta);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'contentType = "foo" and level = "foo-b"' }),
                );
            });
        });

        it('reads and clears a composite Type facet without disturbing another predicate', async () => {
            const user = userEvent.setup();
            const query = 'title contains "dune" and contentType = "foo" and level = "foo-c"';
            renderLibraryPage(Role.Admin, [`/?filter=${encodeURIComponent(query)}`]);

            await user.click(findColumnFilterTrigger('type')!);

            expect(await screen.findByRole('menuitemradio', { name: 'Foo > Gamma' })).toHaveAttribute(
                'aria-checked',
                'true',
            );
            await user.click(screen.getByRole('menuitemradio', { name: 'Any' }));

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'title contains "dune"' }),
                );
            });
        });

        it('a Status facet selection edits MQL without a separate fulfillment condition', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const status = findColumnFilterTrigger('status');
            await user.click(status!);

            const missing = await screen.findByRole('menuitemradio', { name: 'Missing' });
            await user.click(missing);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'fulfillment = Missing' }),
                );
            });
            const lastCall = mockUseContentList.mock.calls.at(-1)?.[0] as { fulfillment?: string };
            expect(lastCall.fulfillment).toBeUndefined();
        });

        it('hydrates the Status funnel active state from the ?fulfillment= param', async () => {
            renderLibraryPage(Role.Admin, ['/?fulfillment=Missing']);

            await waitFor(() => {
                const status = findColumnFilterTrigger('status');
                expect(status).toHaveAttribute('data-active', 'true');
            });
        });
    });

    describe('Status funnel drives file status (no separate control)', () => {
        it('reflects the ?fulfillment= param as the funnel menu active item', async () => {
            const user = userEvent.setup();
            renderLibraryPage(Role.Admin, ['/?fulfillment=Fulfilled']);

            const status = await waitFor(() => {
                const trigger = findColumnFilterTrigger('status');
                expect(trigger).toHaveAttribute('data-active', 'true');
                return trigger!;
            });
            await user.click(status);

            const active = await screen.findByRole('menuitemradio', { name: 'Fulfilled' });
            expect(active).toHaveAttribute('aria-checked', 'true');
        });

        it('clears the param back to All when the funnel "Any" item is picked', async () => {
            const user = userEvent.setup();
            renderLibraryPage(Role.Admin, ['/?fulfillment=Missing']);

            const status = findColumnFilterTrigger('status');
            await user.click(status!);
            await user.click(await screen.findByRole('menuitemradio', { name: 'Any' }));

            await waitFor(() => {
                const lastCall = mockUseContentList.mock.calls.at(-1)?.[0] as { fulfillment?: string };
                expect(lastCall.fulfillment).toBeUndefined();
            });
        });
    });

    describe('filter application', () => {
        it('forwards a filter from the URL to the server so results narrow', async () => {
            renderLibraryPage(Role.Admin, ['/?filter=title%20contains%20%22Jump%22']);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'title contains "Jump"' }),
                );
            });
        });

        it('forwards a tag filter from the all-content route to the server', async () => {
            renderLibraryPage(Role.Admin, [`/library/all?filter=${encodeURIComponent('tag has "Action Picks"')}`]);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'tag has "Action Picks"' }),
                );
            });
        });
    });

    describe('multi-select', () => {
        it('renders checkboxes for row selection', () => {
            renderLibraryPage();

            const checkboxes = screen.getAllByRole('checkbox');
            expect(checkboxes.length).toBeGreaterThan(1);
        });

        it('keeps the bulk actions toolbar hidden and inert initially', () => {
            renderLibraryPage();

            const toolbar = document.querySelector('[data-component="bulk-actions-toolbar"]');
            expect(toolbar).toBeInTheDocument();
            expect(toolbar).toHaveAttribute('data-visible', 'false');
            expect(toolbar).toHaveAttribute('aria-hidden', 'true');
            expect(toolbar).toHaveAttribute('inert');
        });

        it('shows bulk actions toolbar when items are selected', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            await waitFor(() => {
                const toolbar = document.querySelector('[data-component="bulk-actions-toolbar"]');
                expect(toolbar).toHaveAttribute('data-visible', 'true');
                expect(toolbar).toHaveAttribute('aria-hidden', 'false');
                expect(toolbar).not.toHaveAttribute('inert');
            });
        });

        it('shows the mobile selection bar when a card is toggled in selection mode', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));
            await user.click(within(mobileList).getByRole('button', { name: /select breaking bad card/i }));

            const bar = await screen.findByRole('region', { name: 'Selection actions' });
            expect(within(bar).getByText('1 selected')).toBeInTheDocument();
        });

        it('shows selected count in bulk actions toolbar', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox1 = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox1);

            await waitFor(() => {
                expect(screen.getByText('1 selected')).toBeInTheDocument();
            });

            const checkbox2 = screen.getByLabelText('Select The Office');
            await user.click(checkbox2);

            await waitFor(() => {
                expect(screen.getByText('2 selected')).toBeInTheDocument();
            });
        });

        it('clears selection when clear button is clicked', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            await waitFor(() => {
                expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).toBeInTheDocument();
            });

            const clearButton = document.querySelector('[data-action="clear-selection"]') as HTMLElement;
            await user.click(clearButton);

            await waitFor(() => {
                const toolbar = document.querySelector('[data-component="bulk-actions-toolbar"]');
                expect(toolbar).toHaveAttribute('data-visible', 'false');
                expect(toolbar).toHaveAttribute('aria-hidden', 'true');
                expect(toolbar).toHaveAttribute('inert');
                expect(toolbar).not.toHaveTextContent('0 selected');
                expect(toolbar).not.toHaveTextContent('Clear');
                expect(toolbar?.querySelectorAll('[data-action]')).toHaveLength(0);
                expect(toolbar?.querySelectorAll('button:not(:disabled)')).toHaveLength(0);
                expect(checkbox).not.toBeChecked();
            });
        });

        it('has Assign Tags button in bulk actions', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            await waitFor(() => {
                const tagButton = document.querySelector('[data-action="bulk-tags"]');
                expect(tagButton).toBeInTheDocument();
            });
        });

        it('opens bulk tag assignment sheet when Assign Tags is clicked', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            const tagButton = await waitFor(() => {
                return document.querySelector('[data-action="bulk-tags"]') as HTMLElement;
            });
            await user.click(tagButton);

            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Assign Tags/i })).toBeInTheDocument();
            });
        });

        it('has Root Folder and Quality Profile buttons in bulk actions', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            await user.click(screen.getByLabelText('Select Breaking Bad'));

            await waitFor(() => {
                expect(document.querySelector('[data-action="bulk-root-folder"]')).toBeInTheDocument();
                expect(document.querySelector('[data-action="bulk-quality-profile"]')).toBeInTheDocument();
            });
        });

        it('pluralizes the bulk delete fallback when the selected row is on another page', async () => {
            const user = userEvent.setup();
            const firstPageItems = [mockContentItems[0]];
            const secondPageItems = [mockContentItems[1]];
            mockUseContentList.mockImplementation((params: { pageNumber: number }) => ({
                data: {
                    items: params.pageNumber === 1 ? firstPageItems : secondPageItems,
                    pageNumber: params.pageNumber,
                    pageSize: 100,
                    totalItems: 200,
                },
                isLoading: false,
                error: null,
            }));
            renderLibraryPage();

            await user.click(screen.getByLabelText('Select Breaking Bad'));
            const table = document.querySelector('.library-page__table') as HTMLElement;
            await user.click(within(table).getByRole('button', { name: 'Go to next page' }));
            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 }));
            });
            await user.click(screen.getByRole('button', { name: 'Delete' }));

            expect(screen.getByRole('dialog')).toHaveTextContent(
                'Delete 1 item? This cannot be undone. Managed files will remain on disk.',
            );
        });

        it('completes a successful bulk delete using the returned count', async () => {
            const user = userEvent.setup();
            mockBulkDeleteContent.mockResolvedValue({ count: 1, error: null });
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);
            await user.click(await waitFor(() => document.querySelector('[data-action="bulk-delete"]') as HTMLElement));

            expect(await screen.findByRole('heading', { name: 'Delete Content' })).toBeInTheDocument();
            expect(screen.getByRole('switch', { name: 'Also delete files from disk' })).toHaveAttribute(
                'aria-checked',
                'false',
            );
            expect(screen.getByText('Managed files will remain on disk.', { exact: false })).toBeInTheDocument();
            await user.click(screen.getByRole('button', { name: /^Delete$/ }));

            await waitFor(() => {
                expect(mockBulkDeleteContent).toHaveBeenCalledWith({ ids: ['content-1'], deleteFromLibrary: false });
                expect(screen.getByText('1 item deleted')).toBeInTheDocument();
                expect(screen.queryByRole('heading', { name: 'Delete Content' })).not.toBeInTheDocument();
                expect(checkbox).not.toBeChecked();
            });
        });

        it('deletes files when the bulk delete toggle is on', async () => {
            const user = userEvent.setup();
            mockBulkDeleteContent.mockResolvedValue({ count: 1, error: null });
            renderLibraryPage();

            await user.click(screen.getByLabelText('Select Breaking Bad'));
            await user.click(await waitFor(() => document.querySelector('[data-action="bulk-delete"]') as HTMLElement));
            await user.click(screen.getByRole('switch', { name: 'Also delete files from disk' }));
            expect(screen.getByText('Managed files will be deleted from disk.', { exact: false })).toBeInTheDocument();
            await user.click(screen.getByRole('button', { name: /^Delete$/ }));

            await waitFor(() => {
                expect(mockBulkDeleteContent).toHaveBeenCalledWith({ ids: ['content-1'], deleteFromLibrary: true });
            });
        });

        it('assigns a root folder to the selection via the picker', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            await user.click(screen.getByLabelText('Select Breaking Bad'));
            const button = await waitFor(
                () => document.querySelector('[data-action="bulk-root-folder"]') as HTMLElement,
            );
            await user.click(button);

            await user.click(await screen.findByTestId('bulk-value-picker__select'));
            await user.click(await screen.findByRole('option', { name: /Movies/ }));
            await user.click(screen.getByRole('button', { name: /Apply/i }));

            await waitFor(() => expect(mockBulkPatchContent).toHaveBeenCalledTimes(1));
            const payload = mockBulkPatchContent.mock.calls[0][0];
            expect(payload.patch).toEqual({ rootFolderId: { value: 'root-1' } });
            expect(payload.ids).toEqual(['content-1']);
        });

        it('resets the quality profile to inherit (null) via the picker', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            await user.click(screen.getByLabelText('Select Breaking Bad'));
            const button = await waitFor(
                () => document.querySelector('[data-action="bulk-quality-profile"]') as HTMLElement,
            );
            await user.click(button);

            await user.click(await screen.findByRole('button', { name: /Apply/i }));

            await waitFor(() => expect(mockBulkPatchContent).toHaveBeenCalledTimes(1));
            const payload = mockBulkPatchContent.mock.calls[0][0];
            expect(payload.patch).toEqual({ qualityTierListId: { value: null } });
        });

        it('selects all items when select-all checkbox is clicked', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const selectAllCheckbox = screen.getByLabelText('Select all');
            await user.click(selectAllCheckbox);

            await waitFor(() => {
                expect(screen.getByText('10 selected')).toBeInTheDocument();
            });
        });

        it('selects all visible mobile cards via the selection bar select-all', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));

            const bar = await screen.findByRole('region', { name: 'Selection actions' });
            await user.click(within(bar).getByRole('button', { name: /^select all$/i }));

            await waitFor(() => {
                expect(within(bar).getByText('10 selected')).toBeInTheDocument();
            });
            expect(within(mobileList).getByRole('button', { name: /select breaking bad card/i })).toHaveAttribute(
                'aria-pressed',
                'true',
            );
            expect(within(mobileList).getByRole('button', { name: /select the office card/i })).toHaveAttribute(
                'aria-pressed',
                'true',
            );
        });

        it('selecting row does not navigate to content details', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            expect(mockNavigate).not.toHaveBeenCalled();
        });
    });

    describe('mobile selection mode', () => {
        it('renders each mobile card as a navigable link (not a toggle) when out of selection mode', () => {
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            const card = within(mobileList).getByText('Breaking Bad').closest('a');
            expect(card).toHaveAttribute('href', '/library/content-1');
            expect(
                within(mobileList).queryByRole('button', { name: /select breaking bad card/i }),
            ).not.toBeInTheDocument();
        });

        it('does not show the selection bar until selection mode is entered', () => {
            renderLibraryPage();

            expect(screen.queryByRole('region', { name: 'Selection actions' })).not.toBeInTheDocument();
        });

        it('keeps selection controls but hides bulk actions when selection mode starts at zero', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));

            const bar = await screen.findByRole('region', { name: 'Selection actions' });
            expect(within(bar).getByText('0 selected')).toBeInTheDocument();
            expect(within(bar).getByRole('button', { name: /clear selection/i })).toBeDisabled();
            expect(within(bar).getByRole('button', { name: /^select all$/i })).toBeInTheDocument();
            expect(within(mobileList).getByRole('button', { name: 'Select', pressed: true })).toBeInTheDocument();
            expect(bar.querySelector('.library-page__mobile-selection-actions')).not.toBeInTheDocument();
            expect(bar.querySelector('[data-action="bulk-tags"]')).not.toBeInTheDocument();
        });

        it('shows enabled bulk actions after one card is selected in selection mode', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));
            await user.click(within(mobileList).getByRole('button', { name: /select breaking bad card/i }));

            const bar = await screen.findByRole('region', { name: 'Selection actions' });
            expect(within(bar).getByText('1 selected')).toBeInTheDocument();
            expect(bar.querySelector('.library-page__mobile-selection-actions')).toBeInTheDocument();
            expect(within(bar).getByRole('button', { name: /assign tags/i })).toBeEnabled();
        });

        it('names every bulk action at every responsive label treatment', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));
            await user.click(within(mobileList).getByRole('button', { name: /select breaking bad card/i }));

            const bar = await screen.findByRole('region', { name: 'Selection actions' });
            expect(within(bar).getByRole('button', { name: 'Assign Tags' })).toHaveAttribute(
                'data-action',
                'bulk-tags',
            );
            expect(within(bar).getByRole('button', { name: 'Monitor' })).toHaveAttribute('data-action', 'bulk-monitor');
            expect(within(bar).getByRole('button', { name: 'Unmonitor' })).toHaveAttribute(
                'data-action',
                'bulk-unmonitor',
            );
            expect(within(bar).getByRole('button', { name: 'Root Folder' })).toHaveAttribute(
                'data-action',
                'bulk-root-folder',
            );
            expect(within(bar).getByRole('button', { name: 'Quality' })).toHaveAttribute(
                'data-action',
                'bulk-quality-profile',
            );
            expect(within(bar).getByRole('button', { name: 'Delete' })).toHaveAttribute('data-action', 'bulk-delete');
        });

        it('hides bulk actions but remains in selection mode when the final card is deselected', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));
            await user.click(within(mobileList).getByRole('button', { name: /select breaking bad card/i }));

            const bar = await screen.findByRole('region', { name: 'Selection actions' });
            expect(bar.querySelector('.library-page__mobile-selection-actions')).toBeInTheDocument();

            await user.click(within(mobileList).getByRole('button', { name: /select breaking bad card/i }));

            expect(within(bar).getByText('0 selected')).toBeInTheDocument();
            expect(within(bar).getByRole('button', { name: /clear selection/i })).toBeDisabled();
            expect(within(bar).getByRole('button', { name: /^select all$/i })).toBeInTheDocument();
            expect(within(mobileList).getByRole('button', { name: 'Select', pressed: true })).toBeInTheDocument();
            expect(bar.querySelector('.library-page__mobile-selection-actions')).not.toBeInTheDocument();
            expect(within(mobileList).getByRole('button', { name: /select breaking bad card/i })).toHaveAttribute(
                'aria-pressed',
                'false',
            );
        });

        it('enters selection mode and selects the card on long-press', () => {
            vi.useFakeTimers();
            try {
                renderLibraryPage();

                const mobileList = screen.getByTestId('library-page__mobile-list');
                const card = within(mobileList).getByText('Breaking Bad').closest('a') as HTMLElement;

                fireEvent.pointerDown(card, { clientX: 0, clientY: 0 });
                act(() => {
                    vi.advanceTimersByTime(500);
                });
                fireEvent.pointerUp(card);

                const bar = screen.getByRole('region', { name: 'Selection actions' });
                expect(within(bar).getByText('1 selected')).toBeInTheDocument();
                expect(within(mobileList).getByRole('button', { name: /select breaking bad card/i })).toHaveAttribute(
                    'aria-pressed',
                    'true',
                );
            } finally {
                vi.useRealTimers();
            }
        });

        it('toggles a card on tap once in selection mode', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));

            const card = within(mobileList).getByRole('button', { name: /select breaking bad card/i });
            expect(card).toHaveAttribute('aria-pressed', 'false');

            await user.click(card);
            expect(within(mobileList).getByRole('button', { name: /select breaking bad card/i })).toHaveAttribute(
                'aria-pressed',
                'true',
            );

            await user.click(within(mobileList).getByRole('button', { name: /select breaking bad card/i }));
            expect(within(mobileList).getByRole('button', { name: /select breaking bad card/i })).toHaveAttribute(
                'aria-pressed',
                'false',
            );
            expect(mockNavigate).not.toHaveBeenCalled();
        });

        it('keeps the Select control in place and uses it to exit selection mode', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            const selectButton = within(mobileList).getByRole('button', { name: /^select$/i });
            await user.click(selectButton);
            await user.click(within(mobileList).getByRole('button', { name: /select breaking bad card/i }));

            const stopSelectingButton = within(mobileList).getByRole('button', { name: 'Select', pressed: true });
            expect(stopSelectingButton).toHaveClass('mobile-list-toolbar__selection');
            await user.click(stopSelectingButton);

            await waitFor(() => {
                expect(screen.queryByRole('region', { name: 'Selection actions' })).not.toBeInTheDocument();
            });
            expect(within(mobileList).getByRole('button', { name: /^select$/i })).toHaveClass(
                'mobile-list-toolbar__selection',
            );
            expect(within(mobileList).getByText('Breaking Bad').closest('a')).toHaveAttribute(
                'href',
                '/library/content-1',
            );
        });

        it('clears selected cards without leaving selection mode', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));
            await user.click(within(mobileList).getByRole('button', { name: /select breaking bad card/i }));

            const bar = await screen.findByRole('region', { name: 'Selection actions' });
            const clearButton = within(bar).getByRole('button', { name: /clear selection/i });
            expect(clearButton).toBeEnabled();
            await user.click(clearButton);

            expect(within(bar).getByText('0 selected')).toBeInTheDocument();
            expect(within(mobileList).getByRole('button', { name: 'Select', pressed: true })).toBeInTheDocument();
            expect(within(mobileList).getByRole('button', { name: /select breaking bad card/i })).toHaveAttribute(
                'aria-pressed',
                'false',
            );
        });

        it('leaves the desktop selection checkbox column intact while mobile uses selection mode', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));

            const table = document.querySelector('.library-page__table') as HTMLElement;
            expect(within(table).getByLabelText('Select Breaking Bad')).toBeInTheDocument();
            expect(within(table).getByLabelText('Select all')).toBeInTheDocument();
        });
    });

    describe('select all across pages', () => {
        function renderPagedLibrary() {
            mockUseContentList.mockReturnValue({
                data: { items: mockContentItems, pageNumber: 1, pageSize: 10, totalItems: 250 },
                isLoading: false,
                error: null,
            });
            return renderLibraryPage();
        }

        it('offers to extend the selection to every matching row once the page is fully checked', async () => {
            const user = userEvent.setup();
            renderPagedLibrary();

            await user.click(screen.getByLabelText('Select all'));

            await waitFor(() => {
                expect(screen.getByRole('button', { name: /select all 250 across all pages/i })).toBeInTheDocument();
            });
        });

        it('offers to extend mobile selection to every matching row once the page is fully checked', async () => {
            const user = userEvent.setup();
            renderPagedLibrary();

            const mobileList = screen.getByTestId('library-page__mobile-list');
            await user.click(within(mobileList).getByRole('button', { name: /^select$/i }));

            const bar = await screen.findByRole('region', { name: 'Selection actions' });
            await user.click(within(bar).getByRole('button', { name: /^select all$/i }));

            await waitFor(() => {
                expect(
                    within(bar).getByRole('button', { name: /select all 250 across all pages/i }),
                ).toBeInTheDocument();
            });
        });

        it('sends the filter (not ids) when a bulk action runs across all pages', async () => {
            const user = userEvent.setup();
            renderPagedLibrary();

            await user.click(screen.getByLabelText('Select all'));
            await user.click(await screen.findByRole('button', { name: /select all 250 across all pages/i }));

            await user.click(
                await waitFor(() => document.querySelector('[data-action="bulk-monitor"]') as HTMLElement),
            );

            await waitFor(() => {
                expect(mockBulkPatchContent).toHaveBeenCalledTimes(1);
            });
            const payload = mockBulkPatchContent.mock.calls[0][0];
            expect(payload.ids).toBeUndefined();
            expect(payload.patch).toEqual({ monitoring: { value: 'Monitored' } });
            expect(payload.filter).toBe('ParentId null');
        });

        it('refuses to widen the target set when a row is deselected inside a select-all', async () => {
            const user = userEvent.setup();
            renderPagedLibrary();

            await user.click(screen.getByLabelText('Select all'));
            await user.click(await screen.findByRole('button', { name: /select all 250 across all pages/i }));

            await user.click(screen.getByLabelText('Select The Office'));

            await user.click(
                await waitFor(() => document.querySelector('[data-action="bulk-monitor"]') as HTMLElement),
            );

            expect(await screen.findByText('Cannot apply to a partial select-all')).toBeInTheDocument();
            expect(mockBulkPatchContent).not.toHaveBeenCalled();
        });

        it('sends the filter (not ids) when Assign Tags runs across all pages', async () => {
            const user = userEvent.setup();
            renderPagedLibrary();

            await user.click(screen.getByLabelText('Select all'));
            await user.click(await screen.findByRole('button', { name: /select all 250 across all pages/i }));

            await user.click(await waitFor(() => document.querySelector('[data-action="bulk-tags"]') as HTMLElement));

            await user.click(await waitFor(() => document.querySelector('[data-tag-id="Favorites"]') as HTMLElement));
            await user.click(await waitFor(() => document.querySelector('[data-action="apply"]') as HTMLElement));

            await waitFor(() => {
                expect(mockBulkPatchContent).toHaveBeenCalledTimes(1);
            });
            const payload = mockBulkPatchContent.mock.calls[0][0];
            expect(payload.ids).toBeUndefined();
            expect(payload.patch).toEqual({ tags: { add: ['Favorites'] } });
            expect(payload.filter).toBe('ParentId null');
        });

        it('shows the full cross-page count in the Assign Tags header', async () => {
            const user = userEvent.setup();
            renderPagedLibrary();

            await user.click(screen.getByLabelText('Select all'));
            await user.click(await screen.findByRole('button', { name: /select all 250 across all pages/i }));

            await user.click(await waitFor(() => document.querySelector('[data-action="bulk-tags"]') as HTMLElement));

            await waitFor(() => {
                expect(screen.getByText('Modify tags for 250 selected items')).toBeInTheDocument();
            });
        });
    });

    describe('bulk-action availability by role', () => {
        it.each([
            ['a Viewer', Role.Viewer],
            ['a Requester', Role.Requester],
        ])('renders no selection checkboxes or toolbar for %s', (_label, role) => {
            renderLibraryPage(role);

            expect(screen.queryByLabelText('Select Breaking Bad')).not.toBeInTheDocument();
            expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).not.toBeInTheDocument();
            expect(document.querySelector('[data-component="library-mobile-select-toolbar"]')).not.toBeInTheDocument();
            expect(screen.queryByLabelText(/select breaking bad card/i)).not.toBeInTheDocument();
        });

        it('keeps selection checkboxes and the (mounted) toolbar for a Manager', () => {
            renderLibraryPage(Role.Manager);

            expect(screen.getByLabelText('Select Breaking Bad')).toBeInTheDocument();
            expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).toBeInTheDocument();
            expect(
                within(screen.getByTestId('library-page__mobile-list')).getByRole('button', { name: /^select$/i }),
            ).toBeInTheDocument();
        });

        it('shows a Manager the bulk actions toolbar once a row is selected', async () => {
            const user = userEvent.setup();
            renderLibraryPage(Role.Manager);

            await user.click(screen.getByLabelText('Select Breaking Bad'));

            await waitFor(() => {
                const toolbar = document.querySelector('[data-component="bulk-actions-toolbar"]');
                expect(toolbar).toHaveAttribute('data-visible', 'true');
            });
            expect(document.querySelector('[data-action="bulk-tags"]')).toBeInTheDocument();
        });
    });

    describe('URL search params persistence', () => {
        function renderWithParams(params: string) {
            const queryClient = createTestQueryClient();

            return render(
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={[`/library/all${params}`]}>
                        <ToastStateProvider>
                            <ToastProvider>
                                <LibraryPage />
                                <ToastList />
                                <ToastViewport />
                            </ToastProvider>
                        </ToastStateProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
                { wrapper: TooltipProvider },
            );
        }

        it('pre-populates filter from URL param', () => {
            renderWithParams(`?filter=${encodeURIComponent('genre has "Action"')}`);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });

        it('auto-expands filter bar when URL param is present', () => {
            renderWithParams(`?filter=${encodeURIComponent('year >= 2020')}`);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });

        it('filter bar is expanded in the desktop table layout when no URL param', () => {
            const matchMedia = createMatchMedia(1280);
            vi.spyOn(window, 'matchMedia').mockImplementation(matchMedia);

            renderLibraryPage();

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(matchMedia).toHaveBeenCalledWith('(min-width: 1024px)');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });

        it('restores sort field from URL param and sends it to the server', () => {
            renderWithParams('?sort=dateAdded');

            expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ sortBy: 'CreatedAt' }));
        });

        it('restores sort direction from URL param and sends it to the server', () => {
            renderWithParams('?sortDir=asc');

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ sortBy: 'PublishDate', sortDirection: 'Ascending' }),
            );
        });

        it('maps a lowercase fulfillment URL value to the API status', async () => {
            const user = userEvent.setup();
            renderWithParams('?fulfillment=missing');

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'fulfillment = Missing' }),
                );
            });
            const lastCall = mockUseContentList.mock.calls.at(-1)?.[0] as { fulfillment?: string };
            expect(lastCall.fulfillment).toBeUndefined();
            const status = findColumnFilterTrigger('status');
            expect(status).toHaveAttribute('data-active', 'true');
            await user.click(status!);
            expect(await screen.findByRole('menuitemradio', { name: 'Missing' })).toHaveAttribute(
                'aria-checked',
                'true',
            );
        });

        it('uses default sort when URL params are absent', () => {
            renderLibraryPage();

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ sortBy: 'PublishDate', sortDirection: 'Descending' }),
            );
        });

        it('ignores invalid sort field in URL and falls back to the default', () => {
            renderWithParams('?sort=invalid');

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ sortBy: 'PublishDate', sortDirection: 'Descending' }),
            );
        });

        it('ignores invalid sort direction in URL and falls back to descending', () => {
            renderWithParams('?sortDir=invalid');

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ sortBy: 'PublishDate', sortDirection: 'Descending' }),
            );
        });

        it('persists a header sort click to the server params', async () => {
            const user = userEvent.setup();
            renderWithParams('');

            const headers = screen.getAllByRole('button', { name: /sort by/i });
            const added = headers.find((h) => /added/i.test(h.getAttribute('aria-label') ?? ''));
            await user.click(added!);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ sortBy: 'CreatedAt' }));
            });
        });
    });

    describe('empty states', () => {
        it('shows onboarding empty state with an Add content CTA when library is empty and no filters are active', () => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderLibraryPage();

            expect(screen.getByText('Your library is empty')).toBeInTheDocument();
            const addLink = screen.getByRole('link', { name: /add content/i });
            expect(addLink).toHaveAttribute('href', '/library/add');
            expect(screen.queryByRole('link', { name: /wishlist/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /clear search and filters/i })).not.toBeInTheDocument();
        });

        it.each([
            [Role.Viewer, 0],
            [Role.Requester, 1],
        ])('gates the empty-library Add content CTA on content.add for role %s', (role, expected) => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderLibraryPage(role);

            expect(screen.getByText('Your library is empty')).toBeInTheDocument();
            expect(screen.queryAllByRole('link', { name: /add content/i })).toHaveLength(expected);
        });

        it('scopes the Add content CTA to the content type being browsed', () => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            const queryClient = createTestQueryClient();
            const mockUser = { id: 'user-1', username: 'testuser', role: Role.Admin };
            render(
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={['/library/content/tv']}>
                        <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                            <ToastProvider>
                                <ToastStateProvider>
                                    <Routes>
                                        <Route path="/library/content/:contentType" element={<LibraryPage />} />
                                    </Routes>
                                    <ToastList />
                                </ToastStateProvider>
                            </ToastProvider>
                        </AuthProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
                { wrapper: TooltipProvider },
            );

            expect(screen.getByText('Your library is empty')).toBeInTheDocument();
            const addLink = screen.getByRole('link', { name: /add content/i });
            expect(addLink).toHaveAttribute('href', '/library/add?contentType=tv');
        });

        it('shows no-results empty state with clear button when a filter returns no results', async () => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderLibraryPage(Role.Admin, ['/?filter=title%20contains%20%22nonexistent%22']);

            await waitFor(() => {
                expect(screen.getByText('No results found')).toBeInTheDocument();
            });
            expect(screen.getByText(/No content matches your current search or filters/)).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /clear search and filters/i })).toBeInTheDocument();
        });

        it('clears the filter when the clear button is clicked', async () => {
            const user = userEvent.setup();
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderLibraryPage(Role.Admin, ['/?filter=title%20contains%20%22nonexistent%22']);

            await waitFor(() => {
                expect(screen.getByText('No results found')).toBeInTheDocument();
            });

            const clearButton = screen.getByRole('button', { name: /clear search and filters/i });
            await user.click(clearButton);

            await waitFor(() => {
                expect(screen.getByText('Your library is empty')).toBeInTheDocument();
            });
        });

        it('keeps the column-header funnels live above the no-results state when a filter matches nothing', async () => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderLibraryPage(Role.Admin, ['/?filter=title%20contains%20%22nonexistent%22']);

            await waitFor(() => {
                expect(screen.getByText('No results found')).toBeInTheDocument();
            });
            expect(screen.getByRole('button', { name: /title filtered, edit filter/i })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /filter by status/i })).toBeInTheDocument();
        });

        it('hides the column-header funnels on the cold-start empty library', () => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderLibraryPage();

            expect(screen.getByText('Your library is empty')).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /filter by title/i })).not.toBeInTheDocument();
        });
    });

    describe('type picker (multi-root content types)', () => {
        function renderAtRoute(initialEntry: string) {
            const queryClient = createTestQueryClient();
            const mockUser = { id: 'user-1', username: 'testuser', role: Role.Admin };

            return render(
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={[initialEntry]}>
                        <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                            <ToastProvider>
                                <ToastStateProvider>
                                    <Routes>
                                        <Route path="/library/content/:contentType" element={<LibraryPage />} />
                                        <Route
                                            path="/library/content/:contentType/:levelSlug"
                                            element={<LibraryPage />}
                                        />
                                        <Route path="/library/all" element={<LibraryPage />} />
                                    </Routes>
                                    <ToastList />
                                </ToastStateProvider>
                            </ToastProvider>
                        </AuthProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
                { wrapper: TooltipProvider },
            );
        }

        it('renders the list-page heading as the content type plural name', () => {
            renderAtRoute('/library/content/tv');

            expect(document.querySelector('[data-page="library"]')).toBeInTheDocument();
            expect(screen.getByRole('heading', { name: 'TV Shows', level: 1 })).toBeInTheDocument();
            expect(screen.queryByRole('heading', { name: 'TV', level: 1 })).not.toBeInTheDocument();
        });

        it('renders one count card per root level for a multi-root content type', () => {
            renderAtRoute('/library/content/foo');

            expect(document.querySelector('[data-page="library-type-picker"]')).toBeInTheDocument();
            expect(document.querySelector('[data-page="library"]')).not.toBeInTheDocument();

            const cards = document.querySelectorAll('[data-component="level-card"]');
            expect(cards).toHaveLength(3);

            expect(screen.getByText('Alpha')).toBeInTheDocument();
            expect(screen.getByText('Beta')).toBeInTheDocument();
            expect(screen.getByText('Gamma')).toBeInTheDocument();

            expect(screen.getByText('1,240 items')).toBeInTheDocument();
            expect(screen.getByText('86 items')).toBeInTheDocument();
            expect(screen.getByText('42 items')).toBeInTheDocument();
        });

        it('links each card to its level drill-in route', () => {
            renderAtRoute('/library/content/foo');

            const alphaCard = document.querySelector('[data-level-slug="foo-a"]');
            expect(alphaCard).toHaveAttribute('href', '/library/content/foo/foo-a');
        });

        it('drills into a single level as the flat list with a back link', () => {
            renderAtRoute('/library/content/foo/foo-a');

            expect(document.querySelector('[data-page="library"]')).toBeInTheDocument();
            expect(document.querySelector('[data-page="library-type-picker"]')).not.toBeInTheDocument();

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ contentType: 'foo', level: 'foo-a', topLevelOnly: true }),
            );

            const backLink = document.querySelector('[data-action="back-to-categories"]');
            expect(backLink).toHaveAttribute('href', '/library/content/foo');

            expect(screen.getByRole('heading', { name: 'Alpha', level: 1 })).toBeInTheDocument();
        });

        it('renders the flat list (no picker) for a single-root content type', () => {
            renderAtRoute('/library/content/movie');

            expect(document.querySelector('[data-page="library"]')).toBeInTheDocument();
            expect(document.querySelector('[data-page="library-type-picker"]')).not.toBeInTheDocument();
            expect(document.querySelector('[data-component="content-list"]')).toBeInTheDocument();

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ contentType: 'movie', topLevelOnly: true }),
            );
        });
    });

    describe('save-filter update affordance (capability-gated)', () => {
        function makeFilter(overrides: Partial<{ ownerId: string | null; ownerLock: boolean }>) {
            return {
                id: 'filter-1',
                name: 'My Active',
                ownerId: 'user-1',
                ownerLock: false,
                query: 'monitoring != Unmonitored',
                criteria: { contentHierarchyItem: { and: [{ field: 'monitoring' }] } },
                criteriaCount: 1,
                createdAt: '2024-01-01T00:00:00Z',
                modifiedAt: null,
                wishlistId: null,
                wishlistName: null,
                ...overrides,
            };
        }

        async function editDirtyAndOpenSaveDialog(user: ReturnType<typeof userEvent.setup>) {
            const monitoring = findColumnFilterTrigger('monitoring');
            await user.click(monitoring!);
            const excluded = await screen.findByRole('menuitemradio', { name: 'Unmonitored' });
            await user.click(excluded);

            const saveButton = await waitFor(
                () => document.querySelector('[data-action="save-filter"]') as HTMLElement,
            );
            await user.click(saveButton);
        }

        const DEEP_LINK = '/library/all?savedFilter=filter-1';

        it('offers the Update path for a modified personal filter the user owns', async () => {
            const user = userEvent.setup();
            mockUseContentFilters.mockReturnValue({
                filters: [makeFilter({ ownerId: 'user-1' })],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });

            renderLibraryPage(Role.Requester, [DEEP_LINK]);
            await editDirtyAndOpenSaveDialog(user);

            expect(document.querySelector('[data-action="update-saved-filter"]')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /Update "My Active"/i })).toBeInTheDocument();
        });

        it('shows a query parse error on the query when updating a saved filter', async () => {
            const user = userEvent.setup();
            mockUseContentFilters.mockReturnValue({
                filters: [makeFilter({ ownerId: 'user-1' })],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });

            renderLibraryPage(Role.Requester, [DEEP_LINK]);
            await editDirtyAndOpenSaveDialog(user);
            mockParseMagpieQl.mockResolvedValue({ error: { message: 'Expected value' } });
            const dialog = screen.getByRole('dialog');
            await user.click(within(dialog).getByRole('button', { name: /Update "My Active"/i }));

            const alert = await within(dialog).findByRole('alert');
            expect(alert).toHaveTextContent('The filter query is not valid');
            expect(alert).toHaveTextContent('Expected value');
            expect(within(dialog).getByLabelText(/new filter name/i)).not.toHaveAttribute('aria-invalid', 'true');
        });

        it('hides the Update path for a modified shared filter when the user is not a Manager', async () => {
            const user = userEvent.setup();
            mockUseContentFilters.mockReturnValue({
                filters: [makeFilter({ ownerId: null })],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });

            renderLibraryPage(Role.Requester, [DEEP_LINK]);
            await editDirtyAndOpenSaveDialog(user);

            expect(document.querySelector('[data-action="update-saved-filter"]')).not.toBeInTheDocument();
            expect(document.querySelector('[data-action="save-new-filter"]')).toBeInTheDocument();
        });

        it.each([
            [Role.Manager, false],
            [Role.Admin, true],
        ])('offers the Update path for a modified shared filter to role %s: %s', async (role, offered) => {
            const user = userEvent.setup();
            mockUseContentFilters.mockReturnValue({
                filters: [makeFilter({ ownerId: null })],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });

            renderLibraryPage(role, [DEEP_LINK]);
            await editDirtyAndOpenSaveDialog(user);

            expect(document.querySelector('[data-action="update-saved-filter"]') !== null).toBe(offered);
        });

        it('labels the save control "Save filter" (not "Save changes") for a non-updatable shared filter', async () => {
            const user = userEvent.setup();
            mockUseContentFilters.mockReturnValue({
                filters: [makeFilter({ ownerId: null })],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });

            renderLibraryPage(Role.Requester, [DEEP_LINK]);

            const monitoring = findColumnFilterTrigger('monitoring');
            await user.click(monitoring!);
            const excluded = await screen.findByRole('menuitemradio', { name: 'Unmonitored' });
            await user.click(excluded);

            const saveButton = await waitFor(
                () => document.querySelector('[data-action="save-filter"]') as HTMLElement,
            );
            expect(saveButton).toHaveTextContent('Save filter');
            expect(saveButton).not.toHaveTextContent('Save changes');
        });
    });

    describe('saved-filter identity (?savedFilter= URL param)', () => {
        function makeSavedFilter(overrides: Partial<{ ownerId: string | null; ownerLock: boolean }> = {}) {
            return {
                id: 'filter-1',
                name: 'My Active',
                ownerId: 'user-1',
                ownerLock: false,
                query: 'monitoring != Unmonitored',
                criteria: { contentHierarchyItem: { and: [{ field: 'monitoring' }] } },
                criteriaCount: 1,
                createdAt: '2024-01-01T00:00:00Z',
                modifiedAt: null,
                wishlistId: null,
                wishlistName: null,
                ...overrides,
            };
        }

        function LocationProbe() {
            const location = useLocation();
            return <div data-testid="location-probe">{`${location.pathname}${location.search}`}</div>;
        }

        function renderWithProbe(role: Role = Role.Admin, initialEntries: string[] = ['/library/all']) {
            const queryClient = createTestQueryClient();
            const mockUser = { id: 'user-1', username: 'testuser', role };

            return render(
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={initialEntries}>
                        <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                            <ToastProvider>
                                <ToastStateProvider>
                                    <LibraryPage />
                                    <LocationProbe />
                                    <ToastList />
                                    <ToastViewport />
                                </ToastStateProvider>
                            </ToastProvider>
                        </AuthProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
                { wrapper: TooltipProvider },
            );
        }

        beforeEach(() => {
            mockUseContentFilters.mockReturnValue({
                filters: [makeSavedFilter()],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
        });

        it('recognizes a ?savedFilter= deep link as the applied saved filter', async () => {
            renderLibraryPage(Role.Requester, ['/library/all?savedFilter=filter-1']);

            const banner = await screen.findByTestId('library-page__applied-saved-filter');
            expect(banner).toHaveTextContent('My Active');
            expect(banner).not.toHaveAttribute('data-dirty');
            expect(screen.queryByTestId('library-page__saved-filter-modified')).not.toBeInTheDocument();
            expect(document.querySelector('[data-action="save-filter"]')).not.toBeInTheDocument();
        });

        it('keeps a failed saved filter selected without querying the unrestricted library and retries it', async () => {
            const user = userEvent.setup();
            const refetch = vi.fn();
            mockUseContentFilters.mockReturnValue({
                filters: [{ ...makeSavedFilter(), query: '', queryError: new Error('Format failed') }],
                isLoading: false,
                error: new Error('Format failed'),
                listError: null,
                refetch,
            });

            renderWithProbe(Role.Requester, ['/library/all?savedFilter=filter-1']);

            expect(await screen.findByRole('alert')).toHaveTextContent('Could not load saved filter');
            expect(screen.getByTestId('location-probe')).toHaveTextContent('savedFilter=filter-1');
            expect(mockUseContentList.mock.calls.every((call) => call[1]?.enabled === false)).toBe(true);
            expect(screen.queryByText('Saved filter not found')).not.toBeInTheDocument();
            await user.click(screen.getByRole('button', { name: 'Retry saved filter' }));
            expect(refetch).toHaveBeenCalledOnce();
            expect(mockUseContentList.mock.calls.every((call) => call[1]?.enabled === false)).toBe(true);
            await user.click(screen.getByRole('button', { name: 'Clear saved filter' }));
            expect(screen.getByTestId('location-probe')).not.toHaveTextContent('savedFilter=');
            expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ topLevelOnly: true }));
        });

        it('does not mistake a failed filter-list request for a deleted saved filter', async () => {
            mockUseContentFilters.mockReturnValue({
                filters: [],
                isLoading: false,
                error: new Error('List failed'),
                listError: new Error('List failed'),
                refetch: vi.fn(),
            });

            renderWithProbe(Role.Requester, ['/library/all?savedFilter=filter-1']);

            expect(await screen.findByRole('alert')).toHaveTextContent('Could not load saved filter');
            expect(screen.getByTestId('location-probe')).toHaveTextContent('savedFilter=filter-1');
            expect(mockUseContentList.mock.calls.every((call) => call[1]?.enabled === false)).toBe(true);
            expect(screen.queryByText('Saved filter not found')).not.toBeInTheDocument();
        });

        it('does not block a resolved saved filter when another filter fails to format', async () => {
            mockUseContentFilters.mockReturnValue({
                filters: [
                    makeSavedFilter(),
                    { ...makeSavedFilter(), id: 'filter-2', query: '', queryError: new Error('Format failed') },
                ],
                isLoading: false,
                error: new Error('Format failed'),
                listError: null,
                refetch: vi.fn(),
            });

            renderWithProbe(Role.Requester, ['/library/all?savedFilter=filter-1']);

            await waitFor(() =>
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'monitoring != Unmonitored' }),
                ),
            );
            expect(screen.queryByText('Could not load saved filter')).not.toBeInTheDocument();
        });

        it('does not strip the id or fire the not-found toast while the saved-filter expression formats on first load', async () => {
            const baseFilter = makeSavedFilter();
            const refetch = vi.fn();
            const stillFormatting = { filters: [{ ...baseFilter, query: '' }], isLoading: false, error: null, refetch };
            const formatted = {
                filters: [{ ...baseFilter, query: 'monitoring != Unmonitored' }],
                isLoading: false,
                error: null,
                refetch,
            };
            mockUseContentFilters.mockImplementation(() => {
                const [done, setDone] = useState(false);
                useEffect(() => {
                    setDone(true);
                }, []);
                return done ? formatted : stillFormatting;
            });

            renderWithProbe(Role.Requester, ['/library/all?savedFilter=filter-1']);

            await waitFor(() => {
                const calledWithSavedQuery = mockUseContentList.mock.calls.some(
                    (c) => (c[0] as { filter?: string } | undefined)?.filter === 'monitoring != Unmonitored',
                );
                expect(calledWithSavedQuery).toBe(true);
            });

            expect(screen.getByTestId('location-probe')).toHaveTextContent('savedFilter=filter-1');
            expect(await screen.findByTestId('library-page__applied-saved-filter')).toHaveTextContent('My Active');
            expect(screen.queryByText('Saved filter not found')).not.toBeInTheDocument();
        });

        it('resolves the saved query from the id alone and sends it to the server', async () => {
            renderLibraryPage(Role.Requester, ['/library/all?savedFilter=filter-1']);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ filter: 'monitoring != Unmonitored' }),
                );
            });
        });

        it('drops the id and carries the expression once the query diverges', async () => {
            const user = userEvent.setup();
            renderWithProbe(Role.Requester, ['/library/all?savedFilter=filter-1']);

            await screen.findByTestId('library-page__applied-saved-filter');

            const monitoring = findColumnFilterTrigger('monitoring');
            await user.click(monitoring!);
            const excluded = await screen.findByRole('menuitemradio', { name: 'Unmonitored' });
            await user.click(excluded);

            await waitFor(() => {
                const probe = screen.getByTestId('location-probe');
                expect(probe).toHaveTextContent('filter=monitoring+%3D+Unmonitored');
                expect(probe).not.toHaveTextContent('savedFilter=');
            });
        });

        it('marks the saved filter modified when the query diverges and offers the update path', async () => {
            const user = userEvent.setup();
            renderLibraryPage(Role.Requester, ['/library/all?savedFilter=filter-1']);

            await screen.findByTestId('library-page__applied-saved-filter');

            const monitoring = findColumnFilterTrigger('monitoring');
            await user.click(monitoring!);
            const excluded = await screen.findByRole('menuitemradio', { name: 'Unmonitored' });
            await user.click(excluded);

            const banner = await waitFor(() => {
                const el = screen.getByTestId('library-page__applied-saved-filter');
                expect(el).toHaveAttribute('data-dirty', 'true');
                return el;
            });
            expect(banner).toBeInTheDocument();
            expect(screen.getByTestId('library-page__saved-filter-modified')).toBeInTheDocument();

            const saveButton = await waitFor(() => {
                const btn = document.querySelector('[data-action="save-filter"]') as HTMLElement | null;
                expect(btn).toBeInTheDocument();
                return btn!;
            });
            expect(saveButton).toHaveTextContent('Save changes');
            await user.click(saveButton);

            expect(await screen.findByRole('button', { name: /Update "My Active"/i })).toBeInTheDocument();
        });

        it('closes the save changes dialog from the Cancel action after a saved filter diverges', async () => {
            const user = userEvent.setup();
            renderLibraryPage(Role.Requester, ['/library/all?savedFilter=filter-1']);

            await screen.findByTestId('library-page__applied-saved-filter');

            const monitoring = findColumnFilterTrigger('monitoring');
            await user.click(monitoring!);
            const excluded = await screen.findByRole('menuitemradio', { name: 'Unmonitored' });
            await user.click(excluded);

            const saveButton = await waitFor(() => {
                const btn = document.querySelector('[data-action="save-filter"]') as HTMLElement | null;
                expect(btn).toBeInTheDocument();
                return btn!;
            });
            expect(saveButton).toHaveTextContent('Save changes');
            await user.click(saveButton);

            const dialog = await screen.findByRole('dialog', { name: /save filter changes/i });
            const footer = dialog.querySelector('.save-filter-dialog__footer');
            expect(footer).toBeInTheDocument();

            await user.click(within(footer as HTMLElement).getByRole('button', { name: 'Cancel' }));

            await waitFor(() => {
                expect(screen.queryByRole('dialog', { name: /save filter changes/i })).not.toBeInTheDocument();
            });
        });

        it('degrades gracefully when the ?savedFilter= id no longer resolves', async () => {
            mockUseContentFilters.mockReturnValue({
                filters: [makeSavedFilter()],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });

            renderWithProbe(Role.Requester, ['/library/all?savedFilter=deleted-filter']);

            expect(await screen.findByText('Saved filter not found')).toBeInTheDocument();
            expect(screen.queryByTestId('library-page__applied-saved-filter')).not.toBeInTheDocument();
            await waitFor(() => {
                expect(screen.getByTestId('location-probe')).not.toHaveTextContent('savedFilter=');
            });
        });

        it('keeps the live filter editor visible while a saved filter is applied via deep link', async () => {
            renderLibraryPage(Role.Requester, ['/library/all?savedFilter=filter-1']);

            await screen.findByTestId('library-page__applied-saved-filter');
            expect(document.querySelector('[data-component="filter-bar"]')).toBeInTheDocument();
        });

        it('adopts a newly created filter as the applied saved filter after save-as-new', async () => {
            const user = userEvent.setup();
            mockUseContentFilters.mockReturnValue({ filters: [], isLoading: false, error: null, refetch: vi.fn() });
            mockParseMagpieQl.mockResolvedValue({ criteria: { contentHierarchyItem: { and: [{}] } } });
            mockCreateFilterMutateAsync.mockResolvedValue({ id: 'new-filter-1', name: 'Fresh Filter' });

            renderWithProbe(Role.Requester, [
                '/library/all?filter=monitoring%20!%3D%20Unmonitored%20and%20fulfillment%20%3D%20Missing',
            ]);

            const saveButton = await waitFor(() => {
                const btn = document.querySelector('[data-action="save-filter"]') as HTMLElement | null;
                expect(btn).toBeInTheDocument();
                return btn!;
            });
            await user.click(saveButton);

            const dialog = await screen.findByRole('dialog');
            await user.type(within(dialog).getByLabelText(/filter name/i), 'Fresh Filter');
            await user.click(within(dialog).getByRole('button', { name: /save filter/i }));

            await waitFor(() => {
                expect(screen.getByTestId('location-probe')).toHaveTextContent('savedFilter=new-filter-1');
            });
            await user.click(screen.getByRole('radio', { name: /^Any:/ }));
            expect(screen.getByRole('combobox', { name: 'Status filter' })).toHaveTextContent('Missing');
            expect(screen.getByTestId('location-probe')).toHaveTextContent('fulfillment');
        });

        it('shows a query parse error on the query and keeps the name valid', async () => {
            const user = userEvent.setup();
            mockUseContentFilters.mockReturnValue({ filters: [], isLoading: false, error: null, refetch: vi.fn() });
            mockParseMagpieQl.mockResolvedValue({ error: { message: 'Expected value' } });

            renderLibraryPage(Role.Requester, ['/library/all?filter=monitoring%20!%3D%20Unmonitored']);

            const saveButton = await waitFor(() => {
                const btn = document.querySelector('[data-action="save-filter"]') as HTMLElement | null;
                expect(btn).toBeInTheDocument();
                return btn!;
            });
            await user.click(saveButton);

            const dialog = await screen.findByRole('dialog');
            const nameInput = within(dialog).getByLabelText(/filter name/i);
            await user.type(nameInput, 'Valid Name');
            await user.click(within(dialog).getByRole('button', { name: /save filter/i }));

            const alert = await within(dialog).findByRole('alert');
            expect(alert).toHaveTextContent('The filter query is not valid');
            expect(alert).toHaveTextContent('Expected value');
            expect(nameInput).not.toHaveAttribute('aria-invalid', 'true');
            expect(nameInput).toHaveValue('Valid Name');
            expect(mockCreateFilterMutateAsync).not.toHaveBeenCalled();
        });
    });

    describe('single live-filter surface (no in-page saved picker)', () => {
        function makeSavedFilter(query = 'monitoring != Unmonitored') {
            return {
                id: 'filter-1',
                name: 'My Active',
                ownerId: 'user-1',
                ownerLock: false,
                query,
                criteria: { contentHierarchyItem: { and: [{ field: 'monitoring' }] } },
                criteriaCount: 1,
                createdAt: '2024-01-01T00:00:00Z',
                modifiedAt: null,
                wishlistId: null,
                wishlistName: null,
            };
        }

        it('renders no Live/Saved filter-mode tabs', () => {
            renderLibraryPage();

            expect(screen.queryByRole('tab', { name: 'Live filter' })).not.toBeInTheDocument();
            expect(screen.queryByRole('tab', { name: 'Saved filter' })).not.toBeInTheDocument();
        });

        it('shows the live filter editor directly, with no in-page saved-filter picker', () => {
            mockUseContentFilters.mockReturnValue({
                filters: [makeSavedFilter()],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });

            renderLibraryPage();

            expect(document.querySelector('[data-component="filter-bar"]')).toBeInTheDocument();
            expect(document.querySelector('[data-field="saved-filter"]')).not.toBeInTheDocument();
        });

        it('applies a deep-linked saved filter and keeps the live editor visible', async () => {
            const query = 'monitoring != Unmonitored or title contains "critical"';
            mockUseContentFilters.mockReturnValue({
                filters: [makeSavedFilter(query)],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });

            renderLibraryPage(Role.Requester, ['/library/all?savedFilter=filter-1']);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(expect.objectContaining({ filter: query }));
            });
            expect(await screen.findByTestId('library-page__applied-saved-filter')).toHaveTextContent('My Active');
            expect(await screen.findByRole('textbox', { name: 'Filter query' })).toHaveTextContent(query);
        });
    });

    describe('empty states cont', () => {
        it('shows no-results empty state when filter returns no results', () => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            const queryClient = createTestQueryClient();
            render(
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={['/library/all?filter=genre%20has%20%22Action%22']}>
                        <ToastProvider>
                            <ToastStateProvider>
                                <LibraryPage />
                                <ToastList />
                            </ToastStateProvider>
                        </ToastProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
                { wrapper: TooltipProvider },
            );

            expect(screen.getByText('No results found')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /clear search and filters/i })).toBeInTheDocument();
        });
    });

    describe('gallery view', () => {
        afterEach(() => {
            localStorage.clear();
        });

        it('defaults to list when nothing is stored', () => {
            renderLibraryPage();

            expect(screen.queryByTestId('library-page__gallery')).not.toBeInTheDocument();
            expect(document.querySelector('[data-component="content-list"]')).toBeInTheDocument();
        });

        it('restores gallery from local storage', () => {
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            renderLibraryPage();

            expect(screen.getByTestId('library-page__gallery')).toBeInTheDocument();
            expect(document.querySelector('[data-component="content-list"]')).not.toBeInTheDocument();
        });

        it('falls back to list when the stored value is not a known view', () => {
            localStorage.setItem('library-view', JSON.stringify('mosaic'));

            renderLibraryPage();

            expect(screen.queryByTestId('library-page__gallery')).not.toBeInTheDocument();
        });

        it('falls back to list when the stored value is malformed json', () => {
            localStorage.setItem('library-view', '{not json');

            renderLibraryPage();

            expect(screen.queryByTestId('library-page__gallery')).not.toBeInTheDocument();
        });

        it('persists gallery when the switch is used', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            await user.click(screen.getByRole('tab', { name: /gallery/i }));

            expect(screen.getByTestId('library-page__gallery')).toBeInTheDocument();
            expect(localStorage.getItem('library-view')).toBe(JSON.stringify('gallery'));
        });

        it('keeps an explicit list role on the gallery', () => {
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            renderLibraryPage();

            expect(screen.getByRole('list', { name: 'Content gallery' })).toHaveAttribute('role', 'list');
        });

        it('renders separate poster and text navigation per item outside selection mode', () => {
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            renderLibraryPage();

            const grid = screen.getByRole('list', { name: 'Content gallery' });
            const links = within(grid).getAllByRole('link');

            expect(links).toHaveLength(mockContentItems.length * 2);
            expect(links[0]).toHaveAttribute('href', '/library/content-1');
            expect(within(links[0]).queryByRole('link')).not.toBeInTheDocument();
        });

        it('renders one gallery poster slot per tile', () => {
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            renderLibraryPage();

            const grid = screen.getByRole('list', { name: 'Content gallery' });

            expect(grid.querySelectorAll('[data-slot="content.gallery.poster"]')).toHaveLength(mockContentItems.length);
        });

        it('renders no gallery poster slot in list view', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-component="content-list"]')).toBeInTheDocument();
            expect(document.querySelectorAll('[data-slot="content.gallery.poster"]')).toHaveLength(0);
        });

        it('names each tile with its monitoring and fulfillment while leaving the poster decorative', () => {
            // Arrange
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            // Act
            renderLibraryPage();

            // Assert
            const grid = screen.getByRole('list', { name: 'Content gallery' });

            expect(within(grid).getByText('Breaking Bad')).toBeInTheDocument();
            expect(
                within(grid).getByRole('link', { name: 'Monitored Breaking Bad (2008) Fulfilled' }),
            ).toBeInTheDocument();
            expect(within(grid).queryByAltText(/artwork/)).not.toBeInTheDocument();
        });

        it('renders the placeholder instead of an image when artwork is missing', () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [{ ...mockContentItems[0], posterUrl: undefined }],
                    pageNumber: 1,
                    pageSize: 100,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            renderLibraryPage();

            const grid = screen.getByRole('list', { name: 'Content gallery' });

            expect(grid.querySelector('img')).not.toBeInTheDocument();
            expect(grid.querySelector('.library-gallery-tile__poster--placeholder')).toBeInTheDocument();
        });

        it('swaps tiles to selection targets without dropping the toolbar', async () => {
            const user = userEvent.setup();
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            renderLibraryPage();

            const gallery = screen.getByTestId('library-page__gallery');
            expect(within(gallery).getByRole('button', { name: /^select$/i })).toBeInTheDocument();

            await user.click(within(gallery).getByRole('button', { name: /^select$/i }));

            const grid = screen.getByRole('list', { name: 'Content gallery' });
            expect(within(grid).queryAllByRole('link')).toHaveLength(0);
            expect(within(grid).getAllByRole('button', { name: /^Select /, pressed: false })).toHaveLength(
                mockContentItems.length,
            );
            expect(within(grid).getAllByRole('button', { name: /^Preview / })).toHaveLength(mockContentItems.length);
            expect(within(grid).getAllByRole('button', { name: /^Select / })[0]).toHaveClass('selection-circle-toggle');
            expect(within(gallery).getByRole('button', { name: 'Select', pressed: true })).toBeInTheDocument();
        });

        it('toggles selection when a tile is activated in selection mode', async () => {
            const user = userEvent.setup();
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            renderLibraryPage();

            const gallery = screen.getByTestId('library-page__gallery');
            await user.click(within(gallery).getByRole('button', { name: /^select$/i }));

            const grid = screen.getByRole('list', { name: 'Content gallery' });
            const poster = grid.querySelector('.library-gallery-tile__poster-target') as HTMLElement;

            await user.click(poster);

            expect(within(grid).getAllByRole('button', { name: /^Select / })[0]).toHaveAttribute(
                'aria-pressed',
                'true',
            );
        });

        it('offers both sort controls and flips direction from the gallery toolbar', async () => {
            const user = userEvent.setup();
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            renderLibraryPage();

            const gallery = screen.getByTestId('library-page__gallery');
            expect(within(gallery).getByLabelText('Sort by field')).toBeInTheDocument();

            const ascending = within(gallery).getByLabelText('Sort ascending');
            const descending = within(gallery).getByLabelText('Sort descending');
            expect(ascending).toHaveAttribute('aria-pressed', 'false');
            expect(descending).toHaveAttribute('aria-pressed', 'true');

            await user.click(ascending);

            await waitFor(() => {
                expect(within(gallery).getByLabelText('Sort ascending')).toHaveAttribute('aria-pressed', 'true');
                expect(within(gallery).getByLabelText('Sort descending')).toHaveAttribute('aria-pressed', 'false');
            });
        });

        it('declares a two-position switch with the active view selected', () => {
            localStorage.setItem('library-view', JSON.stringify('gallery'));

            renderLibraryPage();

            const swtch = screen.getByTestId('library-page__view-switch');

            expect(swtch).toHaveAttribute('aria-label', 'Content view');
            expect(within(swtch).getAllByRole('tab')).toHaveLength(2);
            expect(within(swtch).getByRole('tab', { name: /gallery/i })).toHaveAttribute('aria-selected', 'true');
            expect(within(swtch).getByRole('tab', { name: /list/i })).toHaveAttribute('aria-selected', 'false');
        });
    });
});
