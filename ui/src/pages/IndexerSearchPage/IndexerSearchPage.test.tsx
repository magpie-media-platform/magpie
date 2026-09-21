import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';

vi.mock('@/lib/api', async () => {
    const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
        apiPatch: vi.fn(),
        apiDelete: vi.fn(),
    };
});

import { apiGet, apiPost } from '@/lib/api';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiIndexer, ApiIndexerConnection } from '@/hooks/useIndexers';
import type { IndexerSearchResult } from '@/hooks/useIndexerSearch';

import { IndexerSearchPage } from './IndexerSearchPage';

const indexerA: ApiIndexer = {
    id: 'idx-a',
    providerType: 'newznab',
    name: 'NZBGeek',
    availability: 'Private',
    createdAt: '2024-01-01T00:00:00Z',
    connectionCount: 1,
    searchFacets: [['q', 'imdbid']],
    supportsKeywordSearch: true,
};

const indexerB: ApiIndexer = {
    id: 'idx-b',
    providerType: 'torznab',
    name: 'TorrentLeech',
    availability: 'Private',
    createdAt: '2024-01-01T00:00:00Z',
    connectionCount: 1,
    searchFacets: [['q', 'imdbid']],
    supportsKeywordSearch: true,
};

const connectionA: ApiIndexerConnection = {
    id: 'conn-a',
    indexerId: 'idx-a',
    name: 'NZBGeek',
    enabled: true,
    status: 'Healthy',
    settings: {},
    createdAt: '2024-01-01T00:00:00Z',
};

const connectionB: ApiIndexerConnection = {
    id: 'conn-b',
    indexerId: 'idx-b',
    name: 'TorrentLeech',
    enabled: true,
    status: 'Healthy',
    settings: {},
    createdAt: '2024-01-01T00:00:00Z',
};

const searchResult: IndexerSearchResult = {
    candidates: [
        {
            key: '["identity","infohash","abc"]',
            release: {
                id: '00000000-0000-0000-0000-000000000000',
                protocol: 'torrent',
                externalId: 'a1',
                title: 'The.Salt.Roads.2019.1080p.BluRay',
                publishDate: '2024-02-01T00:00:00Z',
                size: 8_910_000_000,
                createdAt: '2024-02-01T00:00:00Z',
            },
            connectionIds: ['conn-a', 'conn-b'],
        },
    ],
    connections: [
        {
            connectionId: 'conn-a',
            indexerId: 'idx-a',
            connectionName: 'NZBGeek',
            enabled: true,
            status: 'Healthy',
            attempted: true,
        },
        {
            connectionId: 'conn-b',
            indexerId: 'idx-b',
            connectionName: 'TorrentLeech',
            enabled: true,
            status: 'Healthy',
            attempted: true,
        },
    ],
    connectionsAttempted: 2,
    connectionsSkipped: 0,
};

function installApiGet(
    indexers: ApiIndexer[],
    connections: Record<string, ApiIndexerConnection[]>,
    contentItems: unknown[] = [],
) {
    vi.mocked(apiGet).mockImplementation((path: string) => {
        if (path === '/indexers') {
            return Promise.resolve(indexers as never);
        }
        for (const [indexerId, rows] of Object.entries(connections)) {
            if (path === `/indexers/${indexerId}/connections`) {
                return Promise.resolve(rows as never);
            }
        }
        if (path.startsWith('/content')) {
            return Promise.resolve({
                items: contentItems,
                pageNumber: 1,
                pageSize: 25,
                totalItems: contentItems.length,
            } as never);
        }
        return Promise.resolve([] as never);
    });
}

function RouteSearch() {
    const location = useLocation();
    const navigate = useNavigate();
    return (
        <>
            <output data-testid="route-search">{location.search}</output>
            <button
                type="button"
                onClick={() => navigate('/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt')}
            >
                Set narrow route
            </button>
        </>
    );
}

function TestWrapper({ children, url }: { children: ReactNode; url: string }) {
    const [queryClient] = useState(
        () =>
            new QueryClient({
                defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
            }),
    );
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[url]}>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            <Routes>
                                <Route
                                    path="/admin/indexers/search"
                                    element={
                                        <>
                                            {children}
                                            <RouteSearch />
                                        </>
                                    }
                                />
                            </Routes>
                            <ToastList />
                            <ToastViewport />
                        </ToastProvider>
                    </TooltipProvider>
                </ToastStateProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

describe('IndexerSearchPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        installApiGet([indexerA, indexerB], { 'idx-a': [connectionA], 'idx-b': [connectionB] });
        vi.mocked(apiPost).mockResolvedValue(searchResult as never);
    });

    it('shows the selected connections and the shared query mode', async () => {
        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b">{children}</TestWrapper>
            ),
        });

        expect(await screen.findByText('NZBGeek')).toBeInTheDocument();
        expect(screen.getByText('TorrentLeech')).toBeInTheDocument();
        expect(screen.getByText('Searching 2 indexers')).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: 'Query mode' })).toHaveTextContent('Keyword');
        const chip = screen.getAllByText('NZBGeek').find((element) => element.dataset.component === 'badge');
        expect(chip).toHaveAttribute('data-variant', 'default');
        expect(chip?.querySelector('.badge__dot')).toBeNull();
    });

    it('shows the empty state when no selected indexer supports search', async () => {
        // Arrange
        installApiGet([{ ...indexerA, supportsKeywordSearch: false, searchFacets: [] }], { 'idx-a': [connectionA] });

        // Act
        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a">{children}</TestWrapper>
            ),
        });

        // Assert
        expect(await screen.findByText('No search modes available')).toBeInTheDocument();
        expect(screen.getByText('The selected indexers do not offer any search fields.')).toBeInTheDocument();
    });

    it('submits to all connections when only one supports the mode', async () => {
        installApiGet([indexerA, { ...indexerB, searchFacets: [['tvdbid']], supportsKeywordSearch: false }], {
            'idx-a': [connectionA],
            'idx-b': [connectionB],
        });

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b">{children}</TestWrapper>
            ),
        });

        // Act
        const user = userEvent.setup();
        await user.type(await screen.findByLabelText('Search'), 'sample');
        await user.click(screen.getByRole('button', { name: 'Search' }));

        // Assert
        await waitFor(() =>
            expect(apiPost).toHaveBeenCalledWith(
                '/indexer-search',
                { connectionIds: ['conn-a', 'conn-b'], query: { title: 'sample', criteria: {} } },
                expect.any(Object),
            ),
        );
        await user.click(screen.getByRole('combobox', { name: 'Query mode' }));
        expect(screen.getByRole('option', { name: 'TVDB' })).toBeInTheDocument();
    });

    it('shares query modes whose identifier spellings differ', async () => {
        installApiGet(
            [
                { ...indexerA, searchFacets: [['tvdb-id']], supportsKeywordSearch: false },
                { ...indexerB, searchFacets: [['tvdbid']], supportsKeywordSearch: false },
            ],
            { 'idx-a': [connectionA], 'idx-b': [connectionB] },
        );

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b">{children}</TestWrapper>
            ),
        });

        await waitFor(() => expect(screen.getByRole('combobox', { name: 'Query mode' })).toHaveTextContent('TVDB'));
        expect(screen.getByLabelText('TVDB')).toBeInTheDocument();
    });

    it('runs the search of the route state and shows the provenance of a coalesced release', async () => {
        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt%20roads">
                    {children}
                </TestWrapper>
            ),
        });

        await waitFor(() => expect(screen.getByText('+1 more')).toBeInTheDocument());
        expect(screen.getByText('The.Salt.Roads.2019.1080p.BluRay')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledWith(
            '/indexer-search',
            { connectionIds: ['conn-a', 'conn-b'], query: { title: 'salt roads', criteria: {} } },
            expect.objectContaining({ signal: expect.anything() }),
        );
        expect(screen.getByText('2 of 2 indexers searched')).toBeInTheDocument();
    });

    it('shows the no-eligible-indexers state when every selected connection is disabled', async () => {
        vi.mocked(apiPost).mockResolvedValue({
            candidates: [],
            connections: [
                {
                    connectionId: 'conn-a',
                    indexerId: 'idx-a',
                    connectionName: 'NZBGeek',
                    enabled: false,
                    status: 'Healthy',
                    attempted: false,
                    skippedReason: 'disabled',
                },
            ],
            connectionsAttempted: 0,
            connectionsSkipped: 1,
        } as never);

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a&mode=q&v_q=salt">{children}</TestWrapper>
            ),
        });

        expect(await screen.findByText('No eligible indexers were searched')).toBeInTheDocument();
        expect(
            screen.getByText('Every selected indexer connection is disabled. Enable a connection to search it.'),
        ).toBeInTheDocument();
    });

    it('explains when every selected connection is unhealthy', async () => {
        vi.mocked(apiPost).mockResolvedValue({
            candidates: [],
            connections: [
                {
                    ...searchResult.connections[0],
                    attempted: false,
                    skippedReason: 'unhealthy',
                },
            ],
            connectionsAttempted: 0,
            connectionsSkipped: 1,
        } as never);

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a&mode=q&v_q=salt">{children}</TestWrapper>
            ),
        });

        expect(
            await screen.findByText(
                'Every selected indexer connection is unhealthy. Test or fix the connections and try again.',
            ),
        ).toBeInTheDocument();
    });

    it('composes indexer and size range filters and shows the filtered empty state', async () => {
        const user = userEvent.setup();
        vi.mocked(apiPost).mockResolvedValue({
            ...searchResult,
            candidates: [
                {
                    ...searchResult.candidates[0],
                    key: 'one-gb',
                    release: { ...searchResult.candidates[0].release, title: 'One GB', size: 1 * 1024 ** 3 },
                    connectionIds: ['conn-a'],
                },
                {
                    ...searchResult.candidates[0],
                    key: 'five-gb',
                    release: { ...searchResult.candidates[0].release, title: 'Five GB', size: 5 * 1024 ** 3 },
                    connectionIds: ['conn-a'],
                },
                {
                    ...searchResult.candidates[0],
                    key: 'ten-gb',
                    release: { ...searchResult.candidates[0].release, title: 'Ten GB', size: 10 * 1024 ** 3 },
                    connectionIds: ['conn-b'],
                },
            ],
        } as never);
        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt">
                    {children}
                </TestWrapper>
            ),
        });

        await user.click(await screen.findByRole('button', { name: 'Filter by Indexer(s)' }));
        await user.click(screen.getByRole('option', { name: 'NZBGeek' }));
        await user.click(screen.getByRole('button', { name: 'Filter by Size' }));
        await user.type(screen.getByLabelText('Size minimum (GB)'), '4');
        await user.type(screen.getByLabelText('Size maximum (GB)'), '9');
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        expect(screen.getByText('Five GB')).toBeInTheDocument();
        expect(screen.queryByText('One GB')).not.toBeInTheDocument();
        expect(screen.queryByText('Ten GB')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Size filtered, edit filter' })).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Size filtered, edit filter' }));
        await user.clear(screen.getByLabelText('Size minimum (GB)'));
        await user.type(screen.getByLabelText('Size minimum (GB)'), '9');
        await user.clear(screen.getByLabelText('Size maximum (GB)'));
        await user.type(screen.getByLabelText('Size maximum (GB)'), '11');
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        expect(screen.getByText('No releases match the filters')).toBeInTheDocument();
    });

    it('searches again when the route query is unchanged', async () => {
        const user = userEvent.setup();
        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?v_q=salt&mode=q&connections=conn-a,conn-b">
                    {children}
                </TestWrapper>
            ),
        });

        await waitFor(() => expect(apiPost).toHaveBeenCalledTimes(1));
        await user.click(screen.getByRole('button', { name: 'Search' }));

        await waitFor(() => expect(apiPost).toHaveBeenCalledTimes(2));
    });

    it('writes a wider query mode to the route when its extra field is blank', async () => {
        const user = userEvent.setup();
        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=imdb,q&v_q=salt">
                    {children}
                </TestWrapper>
            ),
        });

        await user.click(screen.getByRole('button', { name: 'Set narrow route' }));
        await waitFor(() => expect(screen.getByTestId('route-search')).toHaveTextContent('mode=q'));
        await user.click(screen.getByRole('button', { name: 'Search' }));

        await waitFor(() => expect(screen.getByTestId('route-search')).toHaveTextContent('mode=imdb%2Cq'));
    });

    it('keeps a grabbed release queued when a filter changes its row position', async () => {
        const user = userEvent.setup();
        installApiGet([indexerA, indexerB], { 'idx-a': [connectionA], 'idx-b': [connectionB] }, [
            {
                id: 'content-1',
                title: 'The Salt Roads',
                level: 'movie',
                levelLabel: 'Movie',
                contentType: 'movie',
                monitoring: true,
                effectiveMonitoring: true,
                fulfillment: 'missing',
            },
        ]);
        const candidates = [5, 5, 10].map((size, index) => ({
            ...searchResult.candidates[0],
            key: `candidate-${index}`,
            release: {
                ...searchResult.candidates[0].release,
                externalId: undefined,
                title: index < 2 ? 'Duplicate title' : 'Ten GB',
                size: size * 1024 ** 3,
            },
            connectionIds: ['conn-a'],
        }));
        vi.mocked(apiPost).mockImplementation((path: string) =>
            Promise.resolve(
                path === '/indexer-search' ? { ...searchResult, candidates } : ({ executionId: null } as never),
            ),
        );

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt">
                    {children}
                </TestWrapper>
            ),
        });

        await screen.findByText('Ten GB');
        const duplicateRows = screen.getAllByRole('row').filter((row) => row.textContent?.includes('Duplicate title'));
        expect(duplicateRows).toHaveLength(2);
        expect(duplicateRows[0]).not.toHaveAttribute('data-row-id', duplicateRows[1].dataset.rowId);
        const targetRow = screen.getAllByRole('row').find((row) => row.textContent?.includes('Ten GB'))!;
        await user.click(within(targetRow).getByRole('button', { name: 'Grab' }));
        await user.click(await screen.findByRole('button', { name: 'Grab' }));
        await user.click(screen.getByRole('button', { name: 'Grab anyway' }));
        await waitFor(() => expect(within(targetRow).getByText('Queued')).toBeInTheDocument());

        await user.click(screen.getByRole('button', { name: 'Filter by Size' }));
        await user.type(screen.getByLabelText('Size minimum (GB)'), '4');
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        const filteredTargetRow = screen.getAllByRole('row').find((row) => row.textContent?.includes('Ten GB'))!;
        expect(within(filteredTargetRow).getByText('Queued')).toBeInTheDocument();
    });

    it('keeps matching usenet posts from different indexers as distinct grab rows', async () => {
        const user = userEvent.setup();
        installApiGet([indexerA, indexerB], { 'idx-a': [connectionA], 'idx-b': [connectionB] }, [
            {
                id: 'content-1',
                title: 'The Salt Roads',
                level: 'movie',
                levelLabel: 'Movie',
                contentType: 'movie',
                monitoring: true,
                effectiveMonitoring: true,
                fulfillment: 'missing',
            },
        ]);
        const candidates = [
            {
                ...searchResult.candidates[0],
                key: '["source","idx-a","same-post"]',
                release: {
                    ...searchResult.candidates[0].release,
                    indexerId: 'idx-a',
                    protocol: 'usenet',
                    externalId: 'same-post',
                    title: 'Same usenet post',
                },
                connectionIds: ['conn-a'],
            },
            {
                ...searchResult.candidates[0],
                key: '["source","idx-b","same-post"]',
                release: {
                    ...searchResult.candidates[0].release,
                    indexerId: 'idx-b',
                    protocol: 'usenet',
                    externalId: 'same-post',
                    title: 'Same usenet post',
                },
                connectionIds: ['conn-b'],
            },
        ];
        vi.mocked(apiPost).mockImplementation((path: string) =>
            Promise.resolve(
                path === '/indexer-search' ? { ...searchResult, candidates } : ({ executionId: null } as never),
            ),
        );

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt">
                    {children}
                </TestWrapper>
            ),
        });

        await screen.findAllByText('Same usenet post');
        const rows = screen.getAllByRole('row').filter((row) => row.textContent?.includes('Same usenet post'));
        expect(rows).toHaveLength(2);
        expect(rows[0]).not.toHaveAttribute('data-row-id', rows[1].dataset.rowId);

        await user.click(within(rows[0]).getByRole('button', { name: 'Grab' }));
        await user.click(await screen.findByRole('button', { name: 'Grab' }));
        await user.click(screen.getByRole('button', { name: 'Grab anyway' }));

        await waitFor(() => expect(within(rows[0]).getByText('Queued')).toBeInTheDocument());
        expect(within(rows[1]).getByRole('button', { name: 'Grab' })).toBeInTheDocument();
        expect(
            vi.mocked(apiPost).mock.calls.filter(([path]) => path === '/interactive-search/content-1/grab'),
        ).toHaveLength(1);
    });

    it('keeps a grabbed release queued when a new search result shifts its row position', async () => {
        const user = userEvent.setup();
        installApiGet([indexerA, indexerB], { 'idx-a': [connectionA], 'idx-b': [connectionB] }, [
            {
                id: 'content-1',
                title: 'The Salt Roads',
                level: 'movie',
                levelLabel: 'Movie',
                contentType: 'movie',
                monitoring: true,
                effectiveMonitoring: true,
                fulfillment: 'missing',
            },
        ]);
        let searchCount = 0;
        vi.mocked(apiPost).mockImplementation((path: string) => {
            if (path !== '/indexer-search') {
                return Promise.resolve({ executionId: null } as never);
            }
            searchCount += 1;
            return Promise.resolve(
                searchCount === 1
                    ? searchResult
                    : {
                          ...searchResult,
                          candidates: [
                              {
                                  ...searchResult.candidates[0],
                                  key: 'new-release',
                                  release: {
                                      ...searchResult.candidates[0].release,
                                      externalId: 'new-release',
                                      title: 'New release',
                                  },
                              },
                              searchResult.candidates[0],
                          ],
                      },
            ) as never;
        });

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt">
                    {children}
                </TestWrapper>
            ),
        });

        await screen.findByText('The.Salt.Roads.2019.1080p.BluRay');
        const targetRow = screen
            .getAllByRole('row')
            .find((row) => row.textContent?.includes('The.Salt.Roads.2019.1080p.BluRay'))!;
        await user.click(within(targetRow).getByRole('button', { name: 'Grab' }));
        await user.click(await screen.findByRole('button', { name: 'Grab' }));
        await user.click(screen.getByRole('button', { name: 'Grab anyway' }));
        await waitFor(() => expect(within(targetRow).getByText('Queued')).toBeInTheDocument());

        await user.click(screen.getByRole('button', { name: 'Search' }));

        await screen.findByText('New release');
        const shiftedRow = screen
            .getAllByRole('row')
            .find((row) => row.textContent?.includes('The.Salt.Roads.2019.1080p.BluRay'))!;
        expect(within(shiftedRow).getByText('Queued')).toBeInTheDocument();
        expect(within(shiftedRow).queryByRole('button', { name: 'Grab' })).not.toBeInTheDocument();
        expect(
            vi.mocked(apiPost).mock.calls.filter(([path]) => path === '/interactive-search/content-1/grab'),
        ).toHaveLength(1);
    });

    it('keeps a grabbed release queued when one reporting connection drops out', async () => {
        const user = userEvent.setup();
        installApiGet([indexerA, indexerB], { 'idx-a': [connectionA], 'idx-b': [connectionB] }, [
            {
                id: 'content-1',
                title: 'The Salt Roads',
                level: 'movie',
                levelLabel: 'Movie',
                contentType: 'movie',
                monitoring: true,
                effectiveMonitoring: true,
                fulfillment: 'missing',
            },
        ]);
        let searchCount = 0;
        vi.mocked(apiPost).mockImplementation((path: string) => {
            if (path !== '/indexer-search') {
                return Promise.resolve({ executionId: null } as never);
            }
            searchCount += 1;
            return Promise.resolve(
                searchCount === 1
                    ? searchResult
                    : {
                          ...searchResult,
                          candidates: [
                              {
                                  ...searchResult.candidates[0],
                                  release: {
                                      ...searchResult.candidates[0].release,
                                      publishDate: '2024-02-02T00:00:00Z',
                                  },
                                  connectionIds: ['conn-a'],
                              },
                          ],
                          connections: [
                              searchResult.connections[0],
                              {
                                  ...searchResult.connections[1],
                                  warning: 'The indexer timed out.',
                              },
                          ],
                          connectionsAttempted: 1,
                      },
            ) as never;
        });

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt">
                    {children}
                </TestWrapper>
            ),
        });

        await screen.findByText('The.Salt.Roads.2019.1080p.BluRay');
        const targetRow = screen
            .getAllByRole('row')
            .find((row) => row.textContent?.includes('The.Salt.Roads.2019.1080p.BluRay'))!;
        await user.click(within(targetRow).getByRole('button', { name: 'Grab' }));
        await user.click(await screen.findByRole('button', { name: 'Grab' }));
        await user.click(screen.getByRole('button', { name: 'Grab anyway' }));
        await waitFor(() => expect(within(targetRow).getByText('Queued')).toBeInTheDocument());

        await user.click(screen.getByRole('button', { name: 'Search' }));

        expect(await screen.findByText('TorrentLeech: The indexer timed out.')).toBeInTheDocument();
        const refreshedRow = screen
            .getAllByRole('row')
            .find((row) => row.textContent?.includes('The.Salt.Roads.2019.1080p.BluRay'))!;
        expect(within(refreshedRow).getByText('Queued')).toBeInTheDocument();
        expect(within(refreshedRow).queryByRole('button', { name: 'Grab' })).not.toBeInTheDocument();
        expect(
            vi.mocked(apiPost).mock.calls.filter(([path]) => path === '/interactive-search/content-1/grab'),
        ).toHaveLength(1);
    });

    it('counts failed and disabled connections in the mixed empty state', async () => {
        vi.mocked(apiPost).mockResolvedValue({
            candidates: [],
            connections: [
                { ...searchResult.connections[0], attempted: false, skippedReason: 'disabled' },
                { ...searchResult.connections[1], attempted: false, warning: 'Invalid definition.' },
            ],
            connectionsAttempted: 0,
            connectionsSkipped: 1,
        } as never);

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt">
                    {children}
                </TestWrapper>
            ),
        });

        expect(
            await screen.findByText(
                '2 selected indexer connections were not searched. Review their status and query support.',
            ),
        ).toBeInTheDocument();
    });

    it('shows a warning for each failed connection and keeps the successful results', async () => {
        vi.mocked(apiPost).mockResolvedValue({
            ...searchResult,
            connections: [
                searchResult.connections[0],
                { ...searchResult.connections[1], attempted: true, warning: 'The indexer refused the query.' },
            ],
            connectionsAttempted: 1,
            connectionsSkipped: 0,
        } as never);

        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt">
                    {children}
                </TestWrapper>
            ),
        });

        expect(await screen.findByText('TorrentLeech: The indexer refused the query.')).toBeInTheDocument();
        expect(screen.getByText('The.Salt.Roads.2019.1080p.BluRay')).toBeInTheDocument();
    });

    it('opens the content picker when the administrator matches a release', async () => {
        const user = userEvent.setup();
        installApiGet([indexerA, indexerB], { 'idx-a': [connectionA], 'idx-b': [connectionB] }, [
            {
                id: 'content-1',
                name: 'Sample',
                level: 'entry',
                contentType: 'catalog',
                metadataContainer: {
                    contributions: [{ ordinal: 0, images: [{ type: 'poster', url: '/broken.jpg' }] }],
                },
            },
        ]);
        render(<IndexerSearchPage />, {
            wrapper: ({ children }) => (
                <TestWrapper url="/admin/indexers/search?connections=conn-a,conn-b&mode=q&v_q=salt">
                    {children}
                </TestWrapper>
            ),
        });

        await waitFor(() => expect(screen.getByText('+1 more')).toBeInTheDocument());
        await user.click(screen.getByRole('button', { name: 'Match' }));

        await waitFor(() => expect(screen.getByRole('dialog')).toBeInTheDocument());
        expect(screen.getByText('Select content')).toBeInTheDocument();
        const dialog = screen.getByRole('dialog');
        await waitFor(() => expect(dialog.querySelector('img.content-picker__poster')).toBeInTheDocument());
        fireEvent.error(dialog.querySelector('img.content-picker__poster')!);
        expect(dialog.querySelector('img.content-picker__poster')).not.toBeInTheDocument();
        expect(dialog.querySelector('div.content-picker__poster')).toHaveAttribute('aria-hidden', 'true');
    });
});
