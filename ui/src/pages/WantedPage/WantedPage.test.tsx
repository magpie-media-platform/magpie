import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

const mockMutateAsync = vi.fn();
const mockUseContentAncestors = vi.fn();
const mockHasPermission = vi.hoisted(() => vi.fn(() => true));

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
        const search = new URLSearchParams();
        for (const [key, value] of Object.entries(params ?? {})) {
            if (value !== undefined && value !== null) {
                search.set(key, String(value));
            }
        }
        const query = search.toString();
        return query ? `${path}?${query}` : path;
    }),
}));

vi.mock('@/auth', () => ({
    useAuth: () => ({
        isAuthenticated: true,
        hasPermission: mockHasPermission,
        hasRole: () => true,
    }),
}));

vi.mock('@/hooks/useContent', () => ({
    useContentAncestors: (...args: unknown[]) => mockUseContentAncestors(...args),
    useSearchContentReleases: () => ({ mutateAsync: mockMutateAsync }),
}));

import { apiGet } from '@/lib/api';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiContentHierarchyItem, PagedContentHierarchyItems } from '@/types/content';

import { WantedPage } from './WantedPage';

const show: ApiContentHierarchyItem = {
    id: 'show-1',
    metadataContainerId: 'mc-show',
    contentType: 'shows',
    level: 'show',
    slug: 'show',
    name: 'The Show',
    monitoring: 'Monitored',
    effectiveMonitoring: 'Monitored',
    createdAt: '2024-01-01T00:00:00Z',
    contentItem: {
        id: 'ci-show',
        contentHierarchyItemId: 'show-1',
        fulfillment: 'Partial',
        createdAt: '2024-01-01T00:00:00Z',
    },
};

const movie: ApiContentHierarchyItem = {
    id: 'movie-1',
    metadataContainerId: 'mc-movie',
    contentType: 'movies',
    level: 'movie',
    slug: 'movie',
    name: 'Missing Movie',
    monitoring: 'Monitored',
    effectiveMonitoring: 'Monitored',
    createdAt: '2024-01-01T00:00:00Z',
    contentItem: {
        id: 'ci-movie',
        contentHierarchyItemId: 'movie-1',
        fulfillment: 'Missing',
        createdAt: '2024-01-01T00:00:00Z',
    },
};

const upcomingMovie: ApiContentHierarchyItem = {
    ...movie,
    id: 'movie-2',
    name: 'Upcoming Movie',
    metadataContainer: {
        id: 'mc-movie-2',
        contributions: [{ ordinal: 0, publishDate: '2099-01-01T00:00:00Z' }],
    },
    contentItem: {
        ...movie.contentItem!,
        id: 'ci-movie-2',
        contentHierarchyItemId: 'movie-2',
    },
};

const season: ApiContentHierarchyItem = {
    ...show,
    id: 'season-1',
    parentId: 'show-1',
    level: 'season',
    slug: 'season-1',
    name: 'Season 1',
    contentItem: {
        ...show.contentItem!,
        id: 'ci-season',
        contentHierarchyItemId: 'season-1',
    },
};

const episodeOne: ApiContentHierarchyItem = {
    ...movie,
    id: 'episode-1',
    parentId: 'season-1',
    contentType: 'shows',
    level: 'episode',
    slug: 'episode-1',
    name: 'Episode 1',
    contentItem: {
        ...movie.contentItem!,
        id: 'ci-episode-1',
        contentHierarchyItemId: 'episode-1',
    },
};

const episodeTwo: ApiContentHierarchyItem = {
    ...episodeOne,
    id: 'episode-2',
    name: 'Episode 2',
    contentItem: {
        ...episodeOne.contentItem!,
        id: 'ci-episode-2',
        contentHierarchyItemId: 'episode-2',
    },
};

const unmonitoredShow: ApiContentHierarchyItem = {
    ...show,
    monitoring: 'Unmonitored',
    effectiveMonitoring: 'Unmonitored',
};

const unmonitoredSeason: ApiContentHierarchyItem = {
    ...season,
    effectiveMonitoring: 'Unmonitored',
};

const contentTypes = [
    {
        slug: 'shows',
        displayName: 'Show',
        pluralDisplayName: 'Shows',
        levelDefinitions: [
            { slug: 'show', displayName: 'Show', parentSlug: null },
            { slug: 'season', displayName: 'Season', parentSlug: 'show' },
            { slug: 'episode', displayName: 'Episode', parentSlug: 'season' },
        ],
    },
    {
        slug: 'movies',
        displayName: 'Movie',
        pluralDisplayName: 'Movies',
        levelDefinitions: [{ slug: 'movie', displayName: 'Movie', parentSlug: null }],
    },
];

function pageOf(items: ApiContentHierarchyItem[]): PagedContentHierarchyItems {
    return { items, pageNumber: 1, pageSize: 200, totalItems: items.length };
}

function testWrapper({ children, initialUrl = '/wanted' }: { children: ReactNode; initialUrl?: string }) {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
    });
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[initialUrl]}>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                        </ToastProvider>
                    </TooltipProvider>
                </ToastStateProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

function queryParams(url: string): URLSearchParams {
    return new URL(url, 'http://magpie.local').searchParams;
}

function createMatchMedia(width: number) {
    return vi.fn((query: string) => {
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
    });
}

function setContentResponse(itemsForScope: (parentId: string | null) => ApiContentHierarchyItem[]) {
    vi.mocked(apiGet).mockImplementation(async (url: string) => {
        if (url === '/content-types') {
            return contentTypes;
        }
        if (url === '/subject-types') {
            return [];
        }
        if (url.startsWith('/content?')) {
            const params = queryParams(url);
            return pageOf(itemsForScope(params.get('parentId')));
        }
        return pageOf([]);
    });
}

describe('WantedPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockHasPermission.mockReturnValue(true);
        vi.stubGlobal('matchMedia', createMatchMedia(1024));
        mockMutateAsync.mockResolvedValue({ executionId: 'job-1' });
        mockUseContentAncestors.mockReturnValue({ ancestors: [], isLoading: false });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
        vi.unstubAllEnvs();
        vi.restoreAllMocks();
    });

    it('PageHeader_ShowsMissingOrIncompleteSubtitle_WhenPageRenders', async () => {
        // Arrange
        setContentResponse(() => [movie]);

        // Act
        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        // Assert
        expect(await screen.findByText('Monitored content that is missing or incomplete.')).toBeInTheDocument();
        expect(screen.queryByText(/same MQL query/)).not.toBeInTheDocument();
    });

    it('drills into a container but opens a leaf in library detail', async () => {
        setContentResponse(() => [show, movie]);

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        const showLink = (await screen.findAllByRole('link', { name: 'The Show' })).find(
            (link) => link.getAttribute('href') === '/wanted?parentId=show-1',
        );
        const movieLink = (await screen.findAllByRole('link', { name: /missing movie/i })).find(
            (link) => link.getAttribute('href') === '/library/movie-1',
        );
        expect(showLink).toBeDefined();
        expect(movieLink).toBeDefined();
        expect(showLink).toHaveAttribute('href', '/wanted?parentId=show-1');
        expect(movieLink).toHaveAttribute('href', '/library/movie-1');
        expect(screen.queryByText('Browse wanted')).not.toBeInTheDocument();
    });

    it('shows the number of missing descendants for a rolled-up container', async () => {
        setContentResponse((parentId) => {
            if (parentId === 'show-1') {
                return [season];
            }
            if (parentId === 'season-1') {
                return [episodeOne, episodeTwo];
            }
            return [show];
        });

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        expect(await screen.findByText('2 missing descendants')).toBeInTheDocument();
    });

    it('counts a monitored missing episode below an unmonitored show and season', async () => {
        setContentResponse((parentId) => {
            if (parentId === 'show-1') {
                return [unmonitoredSeason];
            }
            if (parentId === 'season-1') {
                return [episodeOne];
            }
            return [unmonitoredShow];
        });

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        expect(await screen.findByText('1 missing descendant')).toBeInTheDocument();
    });

    it('lists an unmonitored season that holds a monitored missing episode', async () => {
        setContentResponse((parentId) => (parentId === 'show-1' ? [unmonitoredSeason] : [episodeOne]));

        render(<WantedPage />, {
            wrapper: ({ children }) => testWrapper({ children, initialUrl: '/wanted?parentId=show-1' }),
        });

        expect((await screen.findAllByRole('link', { name: 'Season 1' })).length).toBeGreaterThan(0);
        expect(screen.queryByText('No wanted content under this item')).not.toBeInTheDocument();
    });

    it('explains that a container has no child items and offers no descendant search', async () => {
        setContentResponse((parentId) => (parentId === null ? [show] : []));

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        expect(await screen.findByText('No child items yet')).toBeInTheDocument();
        expect(
            screen.queryByRole('button', { name: 'Search releases for descendants of The Show' }),
        ).not.toBeInTheDocument();
    });

    it('explains that an item has no child items when drilling into it', async () => {
        setContentResponse(() => []);

        render(<WantedPage />, {
            wrapper: ({ children }) => testWrapper({ children, initialUrl: '/wanted?parentId=show-1' }),
        });

        expect(await screen.findByText('This item has no child items yet')).toBeInTheDocument();
        expect(screen.queryByText('No wanted content under this item')).not.toBeInTheDocument();
    });

    it('keeps the nothing-wanted state when drilling into a populated item with nothing wanted', async () => {
        vi.mocked(apiGet).mockImplementation(async (url: string) => {
            if (url === '/content-types') {
                return contentTypes;
            }
            if (url === '/subject-types') {
                return [];
            }
            return pageOf(queryParams(url).get('pageSize') === '1' ? [season] : []);
        });

        render(<WantedPage />, {
            wrapper: ({ children }) => testWrapper({ children, initialUrl: '/wanted?parentId=show-1' }),
        });

        expect(await screen.findByText('No wanted content under this item')).toBeInTheDocument();
        expect(screen.queryByText('This item has no child items yet')).not.toBeInTheDocument();
    });

    it('builds a scoped breadcrumb from the existing content ancestor hook', async () => {
        setContentResponse((parentId) => (parentId === 'show-1' ? [season] : []));
        mockUseContentAncestors.mockReturnValue({
            ancestors: [
                {
                    id: 'show-1',
                    title: 'The Show',
                    level: 'show',
                    levelLabel: 'Show',
                    contentType: 'shows',
                    monitoring: 'Monitored',
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'partial',
                    previews: [],
                    images: [],
                },
            ],
            isLoading: false,
        });

        render(<WantedPage />, {
            wrapper: ({ children }) => testWrapper({ children, initialUrl: '/wanted?parentId=show-1' }),
        });

        await screen.findAllByText('Season 1');
        expect(screen.getByRole('navigation', { name: 'Breadcrumb' })).toBeInTheDocument();
        expect(screen.getByText('Wanted in The Show')).toBeInTheDocument();
        expect(screen.getAllByRole('link', { name: 'Wanted' })[0]).toHaveAttribute('href', '/wanted');
        const seasonLink = screen
            .getAllByRole('link', { name: /season 1/i })
            .find((link) => link.getAttribute('href') === '/wanted?parentId=season-1');
        expect(seasonLink).toBeDefined();
    });

    it('expands a selected parent to deduplicated leaf searches through the existing leaf mutation', async () => {
        setContentResponse((parentId) => {
            if (parentId === 'show-1') {
                return [season];
            }
            if (parentId === 'season-1') {
                return [episodeOne, episodeTwo];
            }
            return [show];
        });
        const user = userEvent.setup();

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        await user.click(await screen.findByRole('checkbox', { name: 'Select The Show' }));
        expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).toHaveTextContent('1 selected');
        await user.click(document.querySelector('[data-action="bulk-search-releases"]') as HTMLButtonElement);

        await waitFor(() => {
            expect(mockMutateAsync).toHaveBeenCalledTimes(2);
        });
        expect(mockMutateAsync).toHaveBeenNthCalledWith(1, { id: 'episode-1', mode: 'Queue' });
        expect(mockMutateAsync).toHaveBeenNthCalledWith(2, { id: 'episode-2', mode: 'Queue' });
        expect(mockMutateAsync).not.toHaveBeenCalledWith({ id: 'show-1', mode: 'Queue' });
        expect(mockMutateAsync).not.toHaveBeenCalledWith({ id: 'season-1', mode: 'Queue' });
    });

    it('keeps the selected container available for retry after a partial search failure', async () => {
        // Arrange
        setContentResponse((parentId) => (parentId === 'show-1' ? [episodeOne, episodeTwo] : [show]));
        mockMutateAsync
            .mockResolvedValueOnce({ executionId: 'job-1' })
            .mockRejectedValueOnce(new Error('Queue failed'));
        const user = userEvent.setup();
        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });
        await user.click(await screen.findByRole('checkbox', { name: 'Select The Show' }));

        // Act
        await user.click(screen.getByRole('button', { name: 'Search releases' }));

        // Assert
        expect(await screen.findByText('Searches partially queued')).toBeInTheDocument();
        expect(screen.getByRole('checkbox', { name: 'Select The Show' })).toBeChecked();
        await user.click(screen.getByRole('button', { name: 'Search releases' }));
        await waitFor(() => expect(screen.getByRole('checkbox', { name: 'Select The Show' })).not.toBeChecked());
        expect(mockMutateAsync).toHaveBeenCalledWith({ id: 'episode-2', mode: 'Queue' });
    });

    it('keeps cached descendant counts when unrelated container fields change', async () => {
        // Arrange
        setContentResponse((parentId) => (parentId === 'show-1' ? [episodeOne] : [show]));
        const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        render(
            <QueryClientProvider client={client}>
                <MemoryRouter>
                    <ToastStateProvider>
                        <TooltipProvider>
                            <WantedPage />
                        </TooltipProvider>
                    </ToastStateProvider>
                </MemoryRouter>
            </QueryClientProvider>,
        );
        expect(await screen.findByText('1 missing descendant')).toBeInTheDocument();
        vi.mocked(apiGet).mockClear();

        // Act
        await act(() =>
            client.setQueryData(['wanted', 'list', { parentId: null }], {
                items: [{ ...show, contentItem: { ...show.contentItem, dateLastSearched: '2026-09-08T00:00:00Z' } }],
                totalItems: 1,
            }),
        );

        // Assert
        expect(screen.getByText('1 missing descendant')).toBeInTheDocument();
        await act(async () => {
            await Promise.resolve();
        });
        expect(apiGet).not.toHaveBeenCalled();
    });

    it('replaces broken wanted artwork with its placeholder', async () => {
        // Arrange
        setContentResponse(() => [
            {
                ...movie,
                metadataContainer: {
                    id: 'metadata',
                    contributions: [
                        {
                            id: 'contribution',
                            ordinal: 0,
                            locked: false,
                            providerType: 'provider-a',
                            images: [{ type: 'poster', url: '/broken.jpg' }],
                        },
                    ],
                },
            },
        ]);
        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });
        const poster = await screen.findByAltText('Missing Movie artwork');

        // Act
        fireEvent.error(poster);

        // Assert
        expect(screen.queryByAltText('Missing Movie artwork')).not.toBeInTheDocument();
        expect(document.querySelector('.wanted-page__poster--placeholder')).toBeInTheDocument();
    });

    it('offers page-scoped selection controls on mobile cards', async () => {
        setContentResponse(() => [movie]);
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        await user.click(await screen.findByRole('button', { name: 'Select' }));
        await user.click(screen.getByRole('button', { name: 'Select Missing Movie' }));
        expect(screen.getByText('1 selected')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Search releases' })).toBeInTheDocument();
    });

    it('hides desktop selection controls from read-only viewers', async () => {
        mockHasPermission.mockReturnValue(false);
        setContentResponse(() => [movie]);

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        await screen.findAllByText('Missing Movie');
        expect(document.querySelectorAll('[data-component="data-table"] [role="checkbox"]')).toHaveLength(0);
        expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).not.toBeInTheDocument();
    });

    it('hides mobile selection controls from read-only viewers', async () => {
        mockHasPermission.mockReturnValue(false);
        setContentResponse(() => [movie]);
        vi.stubGlobal('matchMedia', createMatchMedia(390));

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        await screen.findByText('Missing Movie');
        expect(screen.queryByRole('button', { name: 'Select' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /select missing movie/i })).not.toBeInTheDocument();
    });

    it('uses the URL sort state for mobile card order as well as the table', async () => {
        setContentResponse(() => [show, movie]);
        vi.stubGlobal('matchMedia', createMatchMedia(390));

        render(<WantedPage />, {
            wrapper: ({ children }) => testWrapper({ children, initialUrl: '/wanted?sort=title:asc' }),
        });

        await screen.findAllByText('Missing Movie');
        const mobileRows = Array.from(document.querySelectorAll('.wanted-page__mobile-row'));
        expect(mobileRows.map((row) => row.getAttribute('data-content-id'))).toEqual(['movie-1', 'show-1']);
    });

    it('keeps numeric title sort and page selection aligned in the table', async () => {
        const numberedMovies = Array.from({ length: 26 }, (_, index) => {
            const number = index + 1;
            return {
                ...movie,
                id: `movie-${number}`,
                name: `Episode ${number}`,
                contentItem: {
                    ...movie.contentItem!,
                    id: `ci-movie-${number}`,
                    contentHierarchyItemId: `movie-${number}`,
                },
            };
        });
        setContentResponse(() => numberedMovies);
        const user = userEvent.setup();

        render(<WantedPage />, {
            wrapper: ({ children }) => testWrapper({ children, initialUrl: '/wanted?sort=title:asc' }),
        });

        await screen.findAllByText('Episode 1');
        const tableIds = Array.from(document.querySelectorAll('.wanted-page__table [data-row-id]')).map((row) =>
            row.getAttribute('data-row-id'),
        );
        expect(tableIds).toHaveLength(25);

        const selectedId = tableIds[tableIds.length - 1];
        const selectedItem = numberedMovies.find((candidate) => candidate.id === selectedId);
        expect(selectedItem).toBeDefined();
        await user.click(screen.getByRole('checkbox', { name: `Select ${selectedItem!.name}` }));
        expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).toHaveTextContent('1 selected');
    });

    it('extends a page selection across every matching page with the shared banner', async () => {
        const numberedMovies = Array.from({ length: 26 }, (_, index) => ({
            ...movie,
            id: `movie-${index + 1}`,
            name: `Movie ${index + 1}`,
            contentItem: {
                ...movie.contentItem!,
                id: `ci-movie-${index + 1}`,
                contentHierarchyItemId: `movie-${index + 1}`,
            },
        }));
        setContentResponse(() => numberedMovies);
        const user = userEvent.setup();

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        await screen.findAllByText('Movie 1');
        await user.click(screen.getByRole('checkbox', { name: 'Select all visible wanted items' }));
        expect(screen.getByText(/all 25 on this page are selected/i)).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /select all 26 across all pages/i }));
        expect(screen.getByText(/all 26 matching items across all pages are selected/i)).toBeInTheDocument();
        expect(screen.getByText('26 selected')).toBeInTheDocument();

        await user.click(document.querySelector('[data-action="next-page"]') as HTMLButtonElement);
        expect(await screen.findByRole('checkbox', { name: 'Select Movie 26' })).toBeChecked();
    });

    it('renders only mobile cards without desktop headers at 390px', async () => {
        setContentResponse(() => [movie]);
        vi.stubGlobal('matchMedia', createMatchMedia(390));

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        await screen.findByText('Missing Movie');
        expect(document.querySelector('.wanted-page__table')).not.toBeInTheDocument();
        expect(screen.queryByRole('columnheader', { name: 'Title' })).not.toBeInTheDocument();
        expect(document.querySelector('.wanted-page__mobile-row')).toHaveAttribute('data-content-id', 'movie-1');
    });

    it('renders the purpose-built cards instead of DataTable rows at 768px', async () => {
        setContentResponse(() => [movie]);
        vi.stubGlobal('matchMedia', createMatchMedia(768));

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        await screen.findByText('Missing Movie');
        expect(screen.queryByRole('columnheader', { name: 'Title' })).not.toBeInTheDocument();
        expect(document.querySelector('.wanted-page__mobile-row')).toHaveAttribute('data-content-id', 'movie-1');
    });

    it.each([1024, 390])('labels a missing item with a future publish date as Upcoming at %ipx', async (width) => {
        // Arrange
        setContentResponse(() => [upcomingMovie, movie]);
        vi.stubGlobal('matchMedia', createMatchMedia(width));

        // Act
        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        // Assert
        expect(await screen.findByText('Upcoming')).toBeInTheDocument();
        expect(screen.getAllByText('Missing')).toHaveLength(1);
        expect(screen.getByRole('button', { name: 'Search releases for Upcoming Movie' })).toBeInTheDocument();
    });

    it.each([
        ['Day', 'Upcoming'],
        ['Instant', 'Missing'],
    ] as const)('labels a %s publish date of tomorrow in the local time zone as %s', async (precision, label) => {
        // Arrange
        vi.stubEnv('TZ', 'America/Los_Angeles');
        vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-10-07T18:00:00-07:00'));
        setContentResponse(() => [
            {
                ...movie,
                metadataContainer: {
                    id: 'mc-movie',
                    contributions: [
                        { ordinal: 0, publishDate: '2026-10-08T00:00:00Z', publishDatePrecision: precision },
                    ],
                },
            },
        ]);

        // Act
        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        // Assert
        expect(await screen.findByText(label)).toBeInTheDocument();
    });

    it('keeps mobile Search releases outside navigation controls', async () => {
        setContentResponse(() => [movie]);
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        const searchButton = await screen.findByRole('button', { name: 'Search releases for Missing Movie' });
        expect(searchButton.closest('a, [role="link"]')).toBeNull();
        expect(document.querySelector('.wanted-page__mobile-row')).not.toHaveAttribute('role', 'link');

        await user.click(searchButton);

        await waitFor(() => {
            expect(mockMutateAsync).toHaveBeenCalledWith({ id: 'movie-1', mode: 'Queue' });
        });
    });

    it('queues every selected leaf before any search runs', async () => {
        setContentResponse((parentId) => {
            if (parentId === 'show-1') {
                return [season];
            }
            if (parentId === 'season-1') {
                return [episodeOne, episodeTwo];
            }
            return [show];
        });
        const user = userEvent.setup();

        render(<WantedPage />, { wrapper: ({ children }) => testWrapper({ children }) });

        await user.click(await screen.findByRole('checkbox', { name: 'Select The Show' }));
        await user.click(document.querySelector('[data-action="bulk-search-releases"]') as HTMLButtonElement);

        expect(await screen.findByText('Searches queued')).toBeInTheDocument();
        expect(await screen.findByText('2 leaf searches queued.')).toBeInTheDocument();
        expect(mockMutateAsync).toHaveBeenCalledTimes(2);
    });
});
