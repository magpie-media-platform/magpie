import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

vi.mock('@/lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/lib/api')>()),
    apiGet: vi.fn(),
    apiPost: vi.fn(),
}));

vi.mock('@/auth', async () => {
    const actual = await vi.importActual<typeof import('@/auth')>('@/auth');
    return {
        ...actual,
        useAuth: () => ({
            hasPermission: () => true,
            hasRole: () => true,
        }),
    };
});

import { ApiError, apiGet, apiPost } from '@/lib/api';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiContentHierarchyItem } from '@/types/content';
import type { InteractiveSearchCandidate } from '@/hooks/useInteractiveSearch';

import { InteractiveSearchPage } from './InteractiveSearchPage';

const ITEM_ID = 'content-1';

function installMatchMedia(width: number) {
    vi.stubGlobal(
        'matchMedia',
        vi.fn((query: string) => {
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
    );
}

const detailItem: ApiContentHierarchyItem = {
    id: ITEM_ID,
    metadataContainerId: 'mc-1',
    contentType: 'movies',
    level: 'movie',
    slug: 'the-movie',
    name: 'The Movie',
    monitoring: true,
    effectiveMonitoring: true,
    createdAt: '2024-01-01T00:00:00Z',
    contentItem: {
        id: 'ci-1',
        contentHierarchyItemId: ITEM_ID,
        fulfillment: 'Missing',
        dateLastSearched: null,
        createdAt: '2024-01-01T00:00:00Z',
    },
};

function candidate(overrides: Partial<InteractiveSearchCandidate> = {}): InteractiveSearchCandidate {
    return {
        release: {
            id: 'rel-1',
            protocol: 'torrent',
            title: 'The.Movie.2024.1080p.BluRay',
            publishDate: '2024-02-01T00:00:00Z',
            size: 1024 * 1024 * 1024,
            properties: '{"seeders":"50","leechers":"3"}',
            createdAt: '2024-02-01T00:00:00Z',
        },
        indexerName: 'TestIndexer',
        matchResult: 'Confident',
        score: 0,
        isAcceptable: true,
        meetsCutoff: true,
        ...overrides,
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

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = createTestQueryClient();
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[`/interactive-search/${ITEM_ID}`]}>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            <Routes>
                                <Route path="/interactive-search/:contentHierarchyItemId" element={children} />
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

describe('InteractiveSearchPage', () => {
    beforeEach(() => {
        installMatchMedia(1024);
        vi.mocked(apiGet).mockReset();
        vi.mocked(apiPost).mockReset();
        vi.mocked(apiGet).mockImplementation(async (url: string) => {
            if (url.includes('/content-types') || url.includes('/subject-types')) {
                return [];
            }
            return detailItem;
        });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('renders scored candidates with a grab action', async () => {
        vi.mocked(apiPost).mockResolvedValue([
            candidate(),
            candidate({
                release: {
                    id: 'rel-2',
                    protocol: 'torrent',
                    title: 'The.Movie.2024.720p.WEB',
                    publishDate: '2024-02-02T00:00:00Z',
                    size: 700 * 1024 * 1024,
                    createdAt: '2024-02-02T00:00:00Z',
                },
                matchResult: 'Indeterminate',
            }),
        ]);

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await waitFor(() => {
            expect(screen.getByText('The.Movie.2024.1080p.BluRay')).toBeInTheDocument();
        });
        expect(screen.getByText('The.Movie.2024.720p.WEB')).toBeInTheDocument();
        expect(screen.getAllByRole('button', { name: /grab/i }).length).toBeGreaterThan(0);
        expect(document.title).toBe('Interactive Search - Magpie');
        expect(screen.getByText('Confident')).toBeInTheDocument();
        expect(screen.getByText('Indeterminate')).toBeInTheDocument();
    });

    it('shows provider properties in generic release details', async () => {
        vi.mocked(apiPost).mockResolvedValue([candidate()]);
        const user = userEvent.setup();

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await user.click(await screen.findByRole('button', { name: 'The.Movie.2024.1080p.BluRay' }));

        const dialog = await screen.findByTestId('release-details-dialog');
        expect(within(dialog).getByText('seeders')).toBeInTheDocument();
        expect(within(dialog).getByText('50')).toBeInTheDocument();
        expect(within(dialog).getByText('leechers')).toBeInTheDocument();
        expect(within(dialog).getByText('3')).toBeInTheDocument();
    });

    it('applies compact row ordering and action placement at tablet width', async () => {
        installMatchMedia(900);
        vi.mocked(apiPost).mockResolvedValue([candidate()]);

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await screen.findByText('The.Movie.2024.1080p.BluRay');
        expect(document.querySelector('[data-page="interactive-search"]')).toHaveAttribute('data-layout', 'card');
        expect(document.querySelector(".interactive-search__table [data-column='action']")).toBeInTheDocument();
    });

    it('exposes a long release title as a tooltip while rendering it in the title cell', async () => {
        const longTitle = 'A'.repeat(240);
        const baseCandidate = candidate();
        vi.mocked(apiPost).mockResolvedValue([
            {
                ...baseCandidate,
                release: {
                    ...baseCandidate.release,
                    title: longTitle,
                },
            },
        ]);

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        const title = await screen.findByText(longTitle);
        expect(title).toHaveClass('interactive-search__title');
        expect(title).toHaveAttribute('title', longTitle);
        expect(title.closest("[data-column='title']")).toContainElement(title);
        expect(document.querySelector(".interactive-search__table [data-column='action']")).toBeInTheDocument();
    });

    it('shows an empty state when no candidates are returned', async () => {
        vi.mocked(apiPost).mockResolvedValue([]);

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await waitFor(() => {
            expect(screen.getByText('No releases found')).toBeInTheDocument();
        });
    });

    it('shows an in-progress affordance while the indexer search runs', async () => {
        vi.mocked(apiPost).mockImplementation((path: string) => {
            if (path.endsWith('/grab')) {
                return Promise.resolve('execution-1');
            }
            return new Promise(() => {});
        });

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await waitFor(() => {
            expect(screen.getByTestId('interactive-search-progress')).toBeInTheDocument();
        });
        expect(screen.getByTestId('interactive-search-progress')).toHaveTextContent(/searching indexers/i);
    });

    it('clears the searching spinner as soon as the response resolves, not on a timer', async () => {
        let resolveSearch: (value: InteractiveSearchCandidate[]) => void = () => {};
        vi.mocked(apiPost).mockImplementation((path: string) => {
            if (path.endsWith('/grab')) {
                return Promise.resolve('execution-1');
            }
            return new Promise<InteractiveSearchCandidate[]>((resolve) => {
                resolveSearch = resolve;
            });
        });

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await waitFor(() => {
            expect(screen.getByTestId('interactive-search-progress')).toBeInTheDocument();
        });

        resolveSearch([candidate()]);

        await waitFor(() => {
            expect(screen.queryByTestId('interactive-search-progress')).not.toBeInTheDocument();
        });
        expect(screen.getByText('The.Movie.2024.1080p.BluRay')).toBeInTheDocument();
    });

    it.each(['The download client timed out', 'No enabled download client is configured.'])(
        'reports the actual grab failure: %s',
        async (message) => {
            // Arrange
            const user = userEvent.setup();
            vi.mocked(apiPost).mockResolvedValueOnce([candidate()]).mockRejectedValueOnce(new Error(message));
            render(<InteractiveSearchPage />, { wrapper: TestWrapper });
            // Act
            await user.click(await screen.findByRole('button', { name: /^grab$/i }));
            // Assert
            expect(await screen.findByText('Grab failed')).toBeInTheDocument();
            expect(screen.getByText(message)).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /^grab$/i })).toBeEnabled();
        },
    );

    it('reports the no-download-client conflict as its own actionable condition', async () => {
        // Arrange
        const user = userEvent.setup();
        vi.mocked(apiPost)
            .mockResolvedValueOnce([candidate()])
            .mockRejectedValueOnce(new ApiError(409, 'No enabled download client is configured.'));
        render(<InteractiveSearchPage />, { wrapper: TestWrapper });
        // Act
        await user.click(await screen.findByRole('button', { name: /^grab$/i }));
        // Assert
        expect(await screen.findByText('No download client configured')).toBeInTheDocument();
        expect(
            screen.getByText('Add a download client on the Download Clients page to download releases.'),
        ).toBeInTheDocument();
        expect(screen.queryByText('Grab failed')).not.toBeInTheDocument();
    });

    it('grabs a matched candidate via the grab endpoint', async () => {
        vi.mocked(apiPost).mockImplementation(async (path: string) => {
            if (path.endsWith('/grab')) {
                return 'execution-1';
            }
            return [candidate()];
        });

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await waitFor(() => {
            expect(screen.getByText('The.Movie.2024.1080p.BluRay')).toBeInTheDocument();
        });

        await userEvent.click(screen.getByRole('button', { name: /grab/i }));

        await waitFor(() => {
            expect(vi.mocked(apiPost)).toHaveBeenCalledWith(
                `/interactive-search/${ITEM_ID}/grab`,
                expect.objectContaining({ id: 'rel-1' }),
            );
        });
        expect(await screen.findByText('Release grabbed')).toBeInTheDocument();
        expect(screen.getByText('Queued')).toBeInTheDocument();
    });

    it('submits one grab request for rapid repeated activation and leaves other releases actionable', async () => {
        vi.mocked(apiPost).mockImplementation((path: string) => {
            if (path.endsWith('/grab')) {
                return new Promise(() => {});
            }
            return Promise.resolve([
                candidate(),
                candidate({
                    release: {
                        ...candidate().release,
                        id: 'rel-2',
                        title: 'The.Movie.2024.720p.WEB',
                    },
                }),
            ]);
        });

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await screen.findAllByRole('button', { name: /^grab$/i });
        const firstReleaseButton = screen
            .getAllByRole('button', { name: /^grab$/i })
            .find((button) => button.getAttribute('data-release-id') === 'rel-1');
        expect(firstReleaseButton).toBeDefined();

        act(() => {
            firstReleaseButton!.click();
            firstReleaseButton!.click();
        });

        await waitFor(() => {
            const grabCalls = vi.mocked(apiPost).mock.calls.filter(([path]) => path.endsWith('/grab'));
            expect(grabCalls).toHaveLength(1);
        });
        const pendingReleaseButton = screen
            .getAllByRole('button', { name: /grabbing/i })
            .find((button) => button.getAttribute('data-release-id') === 'rel-1');
        expect(pendingReleaseButton).toBeDisabled();
        expect(pendingReleaseButton).toHaveAttribute('aria-busy', 'true');
        expect(pendingReleaseButton).toHaveTextContent('Grabbing...');

        const releaseTwoButton = screen
            .getAllByRole('button', { name: /^grab$/i })
            .find((button) => button.getAttribute('data-release-id') === 'rel-2');
        expect(releaseTwoButton).toBeEnabled();
    });

    it('shows the app confirm dialog for a loose candidate and grabs on confirm', async () => {
        const confirmSpy = vi.spyOn(window, 'confirm');
        vi.mocked(apiPost).mockImplementation(async (path: string) => {
            if (path.endsWith('/grab')) {
                return 'execution-1';
            }
            return [candidate({ matchResult: 'Indeterminate' })];
        });

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await waitFor(() => {
            expect(screen.getByText('The.Movie.2024.1080p.BluRay')).toBeInTheDocument();
        });

        await userEvent.click(screen.getByRole('button', { name: /grab/i }));

        expect(confirmSpy).not.toHaveBeenCalled();
        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).getByText(/Magpie classified this release as Indeterminate/i)).toBeInTheDocument();
        expect(vi.mocked(apiPost)).not.toHaveBeenCalledWith(`/interactive-search/${ITEM_ID}/grab`, expect.anything());

        await userEvent.click(within(dialog).getByRole('button', { name: /grab anyway/i }));

        await waitFor(() => {
            expect(vi.mocked(apiPost)).toHaveBeenCalledWith(
                `/interactive-search/${ITEM_ID}/grab`,
                expect.objectContaining({ id: 'rel-1' }),
            );
        });
        confirmSpy.mockRestore();
    });

    it('aborts a loose grab when the confirm dialog is cancelled', async () => {
        const confirmSpy = vi.spyOn(window, 'confirm');
        vi.mocked(apiPost).mockImplementation(async (path: string) => {
            if (path.endsWith('/grab')) {
                return 'execution-1';
            }
            return [candidate({ matchResult: 'NoMatch' })];
        });

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await waitFor(() => {
            expect(screen.getByText('The.Movie.2024.1080p.BluRay')).toBeInTheDocument();
        });

        await userEvent.click(screen.getByRole('button', { name: /grab/i }));

        const dialog = await screen.findByRole('dialog');
        await userEvent.click(within(dialog).getByRole('button', { name: /cancel/i }));

        expect(confirmSpy).not.toHaveBeenCalled();
        expect(vi.mocked(apiPost)).not.toHaveBeenCalledWith(`/interactive-search/${ITEM_ID}/grab`, expect.anything());
        confirmSpy.mockRestore();
    });
});
