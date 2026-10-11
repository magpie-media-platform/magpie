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

import { apiGet, apiPost } from '@/lib/api';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiContentHierarchyItem } from '@/types/content';
import type { InteractiveSearchCandidate } from '@/hooks/useInteractiveSearch';

import { InteractiveSearchPage } from './InteractiveSearchPage';

const actualApi = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');

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
    monitoring: 'Monitored',
    effectiveMonitoring: 'Monitored',
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

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
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
    it('Search_RetainsAvailableCandidates_WhenResultIsIncomplete (healthy: true)', async () => {
        // Arrange
        vi.mocked(apiPost).mockResolvedValue({
            candidates: [candidate()],
            partial: true,
            warning: 'One or more upstream providers are throttled. The search is incomplete.',
            retryAt: '2027-01-01T00:00:00Z',
        });

        // Act
        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        // Assert
        expect(await screen.findByRole('alert')).toHaveTextContent('providers are throttled');
        expect(screen.getByRole('alert')).toHaveTextContent('Retry after');
        expect(screen.queryByText('No releases found')).not.toBeInTheDocument();
        expect(screen.getByText('The.Movie.2024.1080p.BluRay')).toBeInTheDocument();
    });

    it('Search_RetainsAvailableCandidates_WhenResultIsIncomplete (healthy: false)', async () => {
        // Arrange
        vi.mocked(apiPost).mockResolvedValue({
            candidates: [],
            partial: true,
            warning: 'One or more upstream providers are throttled. The search is incomplete.',
            retryAt: '2027-01-01T00:00:00Z',
        });

        // Act
        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        // Assert
        expect(await screen.findByRole('alert')).toHaveTextContent('providers are throttled');
        expect(screen.getByRole('alert')).toHaveTextContent('Retry after');
        expect(screen.queryByText('No releases found')).not.toBeInTheDocument();
        expect(screen.getByText('Search incomplete')).toBeInTheDocument();
    });

    beforeEach(() => {
        installMatchMedia(1024);
        vi.mocked(apiGet).mockReset();
        vi.mocked(apiPost).mockReset();
        vi.mocked(apiGet).mockImplementation(async (url: string) => {
            if (url.includes('/content-types') || url.includes('/subject-types')) {
                return [];
            }
            if (url === '/indexers') {
                return [{ id: 'indexer-1', connectionCount: 1 }];
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

    it.each(['Imported', 'Failed', 'Faulted'] as const)(
        'Status_ShowsTheEarlierDownloadAndKeepsGrab_WhenTheReleaseWas%s',
        async (status) => {
            // Arrange
            vi.mocked(apiPost).mockResolvedValue([candidate({ acquisitionStatus: status })]);

            // Act
            render(<InteractiveSearchPage />, { wrapper: TestWrapper });

            // Assert
            const badge = await screen.findByText(status);
            expect(badge.closest('[data-acquisition-status]')).toHaveAttribute('data-acquisition-status', status);
            expect(screen.getByRole('button', { name: /grab/i })).toBeEnabled();
        },
    );

    it.each([
        ['Failed', 'ImportFailed', 'Import Failed'],
        ['Faulted', 'DownloadStuck', 'Download Stuck'],
        ['Imported', 'ImportFailed', null],
    ] as const)(
        'Status_ShowsTheFailureReasonAsATooltip_WhenTheReleaseWas%s',
        async (status, failureReason, expectedTitle) => {
            // Arrange
            vi.mocked(apiPost).mockResolvedValue([candidate({ acquisitionStatus: status, failureReason })]);

            // Act
            render(<InteractiveSearchPage />, { wrapper: TestWrapper });

            // Assert
            const badge = (await screen.findByText(status)).closest('[data-acquisition-status]');
            expect(badge?.getAttribute('title')).toBe(expectedTitle);
        },
    );

    it('shows provider properties in generic release details', async () => {
        vi.mocked(apiPost).mockResolvedValue([candidate()]);
        const user = userEvent.setup();

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await user.click(await screen.findByText('The.Movie.2024.1080p.BluRay'));

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

    it('Table_LeavesTheMatchAndStatusWidthsToTheirContent_WhenTheLayoutIsTable', async () => {
        // Arrange
        installMatchMedia(1100);
        vi.mocked(apiPost).mockResolvedValue([candidate({ acquisitionStatus: 'Downloading' })]);

        // Act
        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        // Assert
        await screen.findByText('The.Movie.2024.1080p.BluRay');
        expect(document.querySelector<HTMLElement>("th[data-column='match']")?.style.width).toBe('');
        expect(document.querySelector<HTMLElement>("th[data-column='status']")?.style.width).toBe('');
    });

    it('Status_SitsInTheCardHeader_WhenTheLayoutIsCard', async () => {
        // Arrange
        installMatchMedia(390);
        vi.mocked(apiPost).mockResolvedValue([candidate({ acquisitionStatus: 'Imported' })]);

        // Act
        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        // Assert
        const badge = await screen.findByText('Imported');
        const cell = badge.closest("[data-column='status']");
        expect(cell).toHaveAttribute('data-mobile-role', 'header');
        expect(cell).toContainElement(badge);
    });

    it('Status_RendersAnEmptyCell_WhenTheLayoutIsCardAndNoEarlierDownloadExists', async () => {
        // Arrange
        installMatchMedia(390);
        vi.mocked(apiPost).mockResolvedValue([candidate({ acquisitionStatus: null })]);

        // Act
        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        // Assert
        await screen.findByText('The.Movie.2024.1080p.BluRay');
        expect(document.querySelector("[data-column='status'][data-mobile-role]")).toBeEmptyDOMElement();
    });

    it('renders a long release title in the title cell', async () => {
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
        expect(title.closest("[data-column='title']")).toContainElement(title);
        expect(document.querySelector(".interactive-search__table [data-column='action']")).toBeInTheDocument();
    });

    it.each([
        { title: 'The.Movie.2024.1080p.BluRay', breaks: 3 },
        { title: 'Oppenheimer.2023.2160p.UHD', breaks: 2 },
        { title: 'Movie.AAC2.0.H.264-GROUP', breaks: 2 },
        { title: 'Movie.TrueHD.Atmos.7.1-GROUP', breaks: 2 },
    ])('Title_OffersBreaksOnlyBetweenTokens_WhenTheReleaseNameIs $title', async ({ title, breaks }) => {
        // Arrange
        const baseCandidate = candidate();
        vi.mocked(apiPost).mockResolvedValue([{ ...baseCandidate, release: { ...baseCandidate.release, title } }]);

        // Act
        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        // Assert
        const titleButton = await screen.findByText(title);
        expect(titleButton.querySelectorAll('wbr')).toHaveLength(breaks);
    });

    it('shows an empty state when no candidates are returned', async () => {
        vi.mocked(apiPost).mockResolvedValue([]);

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        await waitFor(() => {
            expect(screen.getByText('No releases found')).toBeInTheDocument();
        });
    });

    it('explains that no indexer is configured instead of a no-matches result', async () => {
        vi.mocked(apiGet).mockImplementation(async (url: string) => {
            if (url.includes('/content-types') || url.includes('/subject-types')) {
                return [];
            }
            if (url === '/indexers') {
                return [{ id: 'definition-1', connectionCount: 0 }];
            }
            return detailItem;
        });
        vi.mocked(apiPost).mockResolvedValue([]);

        render(<InteractiveSearchPage />, { wrapper: TestWrapper });

        expect(await screen.findByText('No indexers configured')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Configure indexers' })).toHaveAttribute('href', '/admin/indexers');
        expect(screen.queryByText('No releases found')).not.toBeInTheDocument();
    });

    it('shows an in-progress affordance while the indexer search runs', async () => {
        vi.mocked(apiPost).mockImplementation((path: string) => {
            if (path.includes('/grab/')) {
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
            if (path.includes('/grab/')) {
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

    it.each([
        [
            'urn:magpie:problem:no-download-client',
            'No enabled download client is configured.',
            'No download client configured',
            'Add a download client on the Download Clients page to download releases.',
        ],
        [
            'urn:magpie:problem:no-download-client',
            'Release rel-1 is already being acquired.',
            'No download client configured',
            'Add a download client on the Download Clients page to download releases.',
        ],
        [
            'urn:magpie:problem:acquisition-in-progress',
            'Release rel-1 is already being acquired.',
            'Already downloading',
            'This release is already queued or downloading.',
        ],
        [
            'urn:magpie:problem:acquisition-in-progress',
            'No enabled download client is configured.',
            'Already downloading',
            'This release is already queued or downloading.',
        ],
    ])(
        'shows the guidance for the conflict problem type, not the detail text (%s, %s)',
        async (type, detail, title, description) => {
            // Arrange
            const user = userEvent.setup();
            vi.stubGlobal(
                'fetch',
                vi.fn().mockResolvedValue(
                    new Response(JSON.stringify({ type, title: 'Conflict', status: 409, detail }), {
                        status: 409,
                        headers: { 'content-type': 'application/problem+json' },
                    }),
                ),
            );
            vi.mocked(apiPost).mockResolvedValueOnce([candidate()]).mockImplementationOnce(actualApi.apiPost);
            render(<InteractiveSearchPage />, { wrapper: TestWrapper });

            // Act
            await user.click(await screen.findByRole('button', { name: /^grab$/i }));

            // Assert
            expect(await screen.findByText(title)).toBeInTheDocument();
            expect(screen.getByText(description)).toBeInTheDocument();
            expect(screen.queryByText(detail)).not.toBeInTheDocument();
            expect(screen.queryByText('Grab failed')).not.toBeInTheDocument();
            expect(screen.getByRole('button', { name: /^grab$/i })).toBeEnabled();
        },
    );

    it.each([undefined, 'https://tools.ietf.org/html/rfc9110#section-15.5.10', 'urn:magpie:problem:unknown'])(
        'reports a conflict with an unrecognized problem type as a grab failure (%s)',
        async (type) => {
            // Arrange
            const user = userEvent.setup();
            vi.stubGlobal(
                'fetch',
                vi.fn().mockResolvedValue(
                    new Response(
                        JSON.stringify({
                            type,
                            title: 'Conflict',
                            status: 409,
                            detail: 'No enabled download client is configured.',
                        }),
                        { status: 409, headers: { 'content-type': 'application/problem+json' } },
                    ),
                ),
            );
            vi.mocked(apiPost).mockResolvedValueOnce([candidate()]).mockImplementationOnce(actualApi.apiPost);
            render(<InteractiveSearchPage />, { wrapper: TestWrapper });

            // Act
            await user.click(await screen.findByRole('button', { name: /^grab$/i }));

            // Assert
            expect(await screen.findByText('Grab failed')).toBeInTheDocument();
            expect(screen.getByText('No enabled download client is configured.')).toBeInTheDocument();
            expect(screen.queryByText('No download client configured')).not.toBeInTheDocument();
            expect(screen.queryByText('Already downloading')).not.toBeInTheDocument();
            expect(screen.getByRole('button', { name: /^grab$/i })).toBeEnabled();
        },
    );
    it('grabs a matched candidate via the grab endpoint', async () => {
        vi.mocked(apiPost).mockImplementation(async (path: string) => {
            if (path.includes('/grab/')) {
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
            expect(vi.mocked(apiPost)).toHaveBeenCalledWith(`/interactive-search/${ITEM_ID}/grab/rel-1`);
        });
        expect(await screen.findByText('Release grabbed')).toBeInTheDocument();
        expect(screen.getByText('Queued')).toBeInTheDocument();
    });

    it('submits one grab request for rapid repeated activation and leaves other releases actionable', async () => {
        vi.mocked(apiPost).mockImplementation((path: string) => {
            if (path.includes('/grab/')) {
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
            const grabCalls = vi.mocked(apiPost).mock.calls.filter(([path]) => path.includes('/grab/'));
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
            if (path.includes('/grab/')) {
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
        expect(vi.mocked(apiPost)).not.toHaveBeenCalledWith(`/interactive-search/${ITEM_ID}/grab/rel-1`);

        await userEvent.click(within(dialog).getByRole('button', { name: /grab anyway/i }));

        await waitFor(() => {
            expect(vi.mocked(apiPost)).toHaveBeenCalledWith(`/interactive-search/${ITEM_ID}/grab/rel-1`);
        });
        confirmSpy.mockRestore();
    });

    it('aborts a loose grab when the confirm dialog is cancelled', async () => {
        const confirmSpy = vi.spyOn(window, 'confirm');
        vi.mocked(apiPost).mockImplementation(async (path: string) => {
            if (path.includes('/grab/')) {
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
        expect(vi.mocked(apiPost)).not.toHaveBeenCalledWith(`/interactive-search/${ITEM_ID}/grab/rel-1`);
        confirmSpy.mockRestore();
    });
});
