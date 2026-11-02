import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, RouterProvider, createMemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useCallback, type ReactNode } from 'react';
import type { NavigateFunction, NavigateOptions, To } from 'react-router-dom';

const mockNavigate = vi.fn();
const mockOpenLocalPreview = vi.fn();

vi.mock('@/components/content/LocalContentPreview', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/components/content/LocalContentPreview')>()),
    useOpenLocalContentPreview: () => mockOpenLocalPreview,
}));

let mockCandidateSearchResponse: unknown;
let mockCandidateSearchError: unknown;

vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
    return {
        ...actual,
        useNavigate: () => {
            const navigate = actual.useNavigate();
            return useCallback<NavigateFunction>(
                (to: To | number, options?: NavigateOptions) => {
                    mockNavigate(to, options);
                    return typeof to === 'number' ? navigate(to) : navigate(to, options);
                },
                [navigate],
            );
        },
    };
});

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
        buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
            if (!params) {
                return path;
            }
            const searchParams = new URLSearchParams();
            for (const [key, value] of Object.entries(params)) {
                if (value !== undefined && value !== null) {
                    searchParams.set(key, String(value));
                }
            }
            const queryString = searchParams.toString();
            return queryString ? `${path}?${queryString}` : path;
        }),
    };
});

const mockUseLibraryTypes = vi.fn();

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();
    return {
        ...original,
        useLibraryTypes: () => mockUseLibraryTypes(),
    };
});

const mockParseMagpieQl = vi.fn();

vi.mock('@/lib/magpieQl', () => ({
    parseMagpieQl: (...args: unknown[]) => mockParseMagpieQl(...args),
    formatMagpieQlCriteria: vi.fn().mockResolvedValue({ query: '' }),
}));

const mockUseContentFilters = vi.fn();

vi.mock('@/hooks/useFilters', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/hooks/useFilters')>();
    return {
        ...original,
        useContentFilters: () => mockUseContentFilters(),
    };
});

import { apiGet, apiPost, ApiError } from '@/lib/api';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { LibraryAddPage } from './LibraryAddPage';

function rootFolder(id: string, name: string, contentType: string) {
    return {
        id,
        name,
        path: `/data/${name.toLowerCase()}`,
        contentType,
        defaultMonitoring: 'Monitored' as const,
        upgradeFileHandling: 'Replace' as const,
        createdAt: '2026-01-01T00:00:00Z',
    };
}

function mockApiGetByPath(options: { rootFolders?: unknown[] }) {
    vi.mocked(apiGet).mockImplementation((path: string) => {
        if (path.startsWith('/content-search')) {
            if (mockCandidateSearchError !== undefined) {
                return Promise.reject(mockCandidateSearchError);
            }
            return Promise.resolve(mockCandidateSearchResponse);
        }
        if (path.startsWith('/root-folders')) {
            return Promise.resolve(options.rootFolders ?? []);
        }
        return Promise.resolve([]);
    });
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
            <MemoryRouter>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            {children}
                            <ToastViewport />
                        </ToastProvider>
                    </TooltipProvider>
                </ToastStateProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

function renderLive(filterQuery = 'genre has "Action"') {
    return render(
        <QueryClientProvider client={createTestQueryClient()}>
            <MemoryRouter initialEntries={[{ pathname: '/library/add', state: { filterQuery } }]}>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            <LibraryAddPage />
                            <ToastList />
                            <ToastViewport />
                        </ToastProvider>
                    </TooltipProvider>
                </ToastStateProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

const candidateSearchedCandidate = {
    providerType: 'provider-a',
    externalIdSource: 'provider-a.movie',
    externalId: '603',
    contentType: 'movies',
    title: 'The Matrix',
    year: 1999,
    releaseDate: '1999-03-31T00:00:00Z',
    overview: 'A hacker learns the truth.',
    thumbnailUrl: 'https://example.com/matrix.jpg',
};

const candidatePreview = {
    providerType: 'provider-a',
    contentType: 'movies',
    levelSlug: 'movie',
    title: 'The Matrix',
    year: 1999,
    genres: [],
    contentRatings: [],
    images: [],
    previews: [],
    externalIds: [],
    relatedSubjects: [],
};

const mockContentTypes: LibraryTypeDefinition[] = [
    {
        id: 'movies',
        name: 'Movies',
        pluralName: 'Movies',
        slug: 'movies',
        levels: [
            {
                slug: 'movie',
                displayName: 'Movie',
                parentSlug: null,
                standardFactFields: [],
                standardMetadataFields: [],
                customMetadataFields: [],
            },
        ],
        standardMetadataFields: [],
        customMetadataFields: [],
    },
    {
        id: 'shows',
        name: 'TV Shows',
        pluralName: 'TV Shows',
        slug: 'shows',
        levels: [
            {
                slug: 'show',
                displayName: 'Show',
                parentSlug: null,
                standardFactFields: [],
                standardMetadataFields: [],
                customMetadataFields: [],
            },
            {
                slug: 'episode',
                displayName: 'Episode',
                parentSlug: 'show',
                standardFactFields: [],
                standardMetadataFields: [],
                customMetadataFields: [],
            },
        ],
        standardMetadataFields: [],
        customMetadataFields: [],
    },
];

function resultsTable() {
    return within(screen.getByTestId('library-add-page__result-list'));
}

function candidateSearchResult(
    candidates: Array<Record<string, unknown>>,
    overrides: {
        pageItemCount?: number;
        existsInLibraryCount?: number;
        hasMore?: boolean;
        requestedPage?: number;
        warnings?: string[];
    } = {},
) {
    const pageItemCount = overrides.pageItemCount ?? candidates.length;
    return {
        matches: candidates.map(({ inLibrary, existingContentHierarchyItemId, ...candidate }) => ({
            candidate,
            inLibrary: inLibrary === true,
            existingContentHierarchyItemId,
        })),
        pageItemCount,
        existsInLibraryCount: overrides.existsInLibraryCount ?? candidates.filter((c) => c.inLibrary === true).length,
        pageInfo: {
            requestedPage: overrides.requestedPage ?? 1,
            hasMore: overrides.hasMore ?? false,
        },
        warnings: overrides.warnings ?? [],
    };
}

function installMatchMedia(width: number) {
    vi.stubGlobal(
        'matchMedia',
        vi.fn((query: string) => {
            const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);
            return {
                matches: maxWidth !== null && width <= Number(maxWidth[1]),
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

describe('LibraryAddPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        localStorage.clear();
        installMatchMedia(1024);
        mockUseLibraryTypes.mockReturnValue({
            contentTypes: mockContentTypes,
            subjectTypes: [],
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });
        mockUseContentFilters.mockReturnValue({
            filters: [],
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });
        mockParseMagpieQl.mockResolvedValue({ criteria: { contentHierarchyItem: { and: [] } } });
        mockCandidateSearchResponse = candidateSearchResult([]);
        mockCandidateSearchError = undefined;
        mockApiGetByPath({});
    });

    afterEach(() => {
        vi.unstubAllGlobals();
        vi.restoreAllMocks();
    });

    it.each([
        ['List', 1280],
        ['List', 390],
        ['Gallery', 1280],
        ['Gallery', 390],
    ] as const)('Add_CompletesIndependently_WhenRequestsOverlapIn%sAt%i', async (view, width) => {
        // Arrange
        installMatchMedia(width);
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([
            candidateSearchedCandidate,
            { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' },
        ]);
        let resolveFirst!: (id: string) => void;
        let resolveSecond!: (id: string) => void;
        vi.mocked(apiPost)
            .mockReset()
            .mockReturnValueOnce(new Promise<string>((resolve) => (resolveFirst = resolve)))
            .mockReturnValueOnce(new Promise<string>((resolve) => (resolveSecond = resolve)));
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(screen.getByRole('tab', { name: view }));
        const first = '[data-action="add-content"][data-result-key="provider-a:provider-a.movie:603"]';
        const second = '[data-action="add-content"][data-result-key="provider-a:provider-a.movie:604"]';

        // Act
        await user.dblClick(document.querySelector(first) as HTMLElement);

        // Assert
        expect(document.querySelector(first)).toBeDisabled();
        expect(document.querySelector(second)).toBeEnabled();
        expect(apiPost).toHaveBeenCalledTimes(1);

        // Act
        await user.dblClick(document.querySelector(second) as HTMLElement);

        // Assert
        expect(document.querySelector(first)).toBeDisabled();
        expect(document.querySelector(second)).toBeDisabled();
        expect(apiPost).toHaveBeenCalledTimes(2);

        // Act
        await act(async () => resolveSecond('added-604'));

        // Assert
        expect(document.querySelector(first)).toBeDisabled();
        expect(
            document.querySelector('[data-action="in-library"][data-result-key="provider-a:provider-a.movie:604"]'),
        ).toBeInTheDocument();

        // Act
        await act(async () => resolveFirst('added-603'));

        // Assert
        expect(document.querySelectorAll('[data-action="in-library"]')).toHaveLength(2);
        expect(document.querySelector('[data-action="add-content"]')).not.toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledTimes(2);
    });

    it('Add_RetriesOnlyTheFailedItem_WhenAnotherAddIsStillPending', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([
            candidateSearchedCandidate,
            { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' },
        ]);
        let rejectFirst!: (error: Error) => void;
        let resolveSecond!: (id: string) => void;
        vi.mocked(apiPost)
            .mockReset()
            .mockReturnValueOnce(new Promise<string>((_resolve, reject) => (rejectFirst = reject)))
            .mockReturnValueOnce(new Promise<string>((resolve) => (resolveSecond = resolve)))
            .mockResolvedValueOnce('added-603');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        const first = '[data-action="add-content"][data-result-key="provider-a:provider-a.movie:603"]';
        const second = '[data-action="add-content"][data-result-key="provider-a:provider-a.movie:604"]';
        await user.click(document.querySelector(first) as HTMLElement);
        await user.click(document.querySelector(second) as HTMLElement);

        // Act
        await act(async () => rejectFirst(new Error('Unavailable')));

        // Assert
        expect(await screen.findByText('Failed to add content')).toBeInTheDocument();
        expect(document.querySelector(first)).toBeEnabled();
        expect(document.querySelector(second)).toBeDisabled();

        // Act
        await user.click(document.querySelector(first) as HTMLElement);

        // Assert
        expect(await resultsTable().findByRole('link', { name: 'The Matrix' })).toHaveAttribute(
            'href',
            '/library/added-603',
        );
        expect(document.querySelector(second)).toBeDisabled();

        // Act
        await act(async () => resolveSecond('added-604'));

        // Assert
        expect(resultsTable().getAllByRole('button', { name: 'In Library' })).toHaveLength(2);
        expect(apiPost).toHaveBeenCalledTimes(3);
    });

    it('BulkAdd_SkipsThePendingItem_WhenAnotherItemIsAddedIndividually', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([
            candidateSearchedCandidate,
            { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' },
        ]);
        let resolveAdd!: (id: string) => void;
        vi.mocked(apiPost)
            .mockReset()
            .mockReturnValueOnce(new Promise<string>((resolve) => (resolveAdd = resolve)))
            .mockResolvedValueOnce('added-604');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(resultsTable().getAllByRole('button', { name: 'Add' })[0]);

        // Assert
        expect(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' })).toBeDisabled();
        expect(resultsTable().getByRole('checkbox', { name: 'Select Reloaded' })).toBeEnabled();

        // Act
        await user.click(resultsTable().getByRole('checkbox', { name: 'Select this page' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledTimes(2);
        expect(resultsTable().getByRole('button', { name: 'Add' })).toBeDisabled();

        // Act
        await act(async () => resolveAdd('added-603'));

        // Assert
        expect(resultsTable().getAllByRole('button', { name: 'In Library' })).toHaveLength(2);
    });

    it('Add_KeepsAnotherPreviewOpen_WhenAnEarlierAddCompletes', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([
            candidateSearchedCandidate,
            { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' },
        ]);
        let resolveAdd!: (id: string) => void;
        vi.mocked(apiPost)
            .mockReset()
            .mockReturnValueOnce(new Promise<string>((resolve) => (resolveAdd = resolve)))
            .mockResolvedValueOnce({ ...candidatePreview, title: 'Reloaded' });
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(resultsTable().getAllByRole('button', { name: 'Add' })[0]);
        await user.click(resultsTable().getByRole('button', { name: 'Reloaded' }));
        await screen.findByRole('dialog', { name: /Reloaded/ });

        // Act
        await act(async () => resolveAdd('added-603'));

        // Assert
        expect(screen.getByRole('dialog', { name: /Reloaded/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Add to library' })).toBeEnabled();
    });

    it.each([
        ['success', 'All content added', '2 added, 0 failed.'],
        ['partial', 'Some content added', '1 added, 1 failed.'],
        ['failure', 'Failed to add content', '0 added, 2 failed.'],
    ])('BulkAdd_ReportsOneSummary_WhenOutcomeIs%s', async (outcome, title, counts) => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([
            candidateSearchedCandidate,
            { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' },
            { ...candidateSearchedCandidate, externalId: '605', title: 'Already added', inLibrary: true },
        ]);
        vi.mocked(apiPost).mockImplementation((_path, request) => {
            const id = (request as { externalId: string }).externalId;
            return outcome === 'failure' || (outcome === 'partial' && id === '604')
                ? Promise.reject(new Error('Rejected'))
                : Promise.resolve(`new-${id}`);
        });
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' });

        // Act
        await user.click(resultsTable().getByRole('checkbox', { name: 'Select this page' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        expect(await screen.findByText(counts)).toBeInTheDocument();
        expect(screen.getByText(title)).toBeInTheDocument();
        expect(document.querySelectorAll('[data-component="toast"]')).toHaveLength(1);
        expect(apiPost).toHaveBeenCalledTimes(2);
        expect(resultsTable().getByRole('checkbox', { name: 'Select Already added' })).toBeDisabled();
        expect(screen.queryByText(/select all.*across/i)).not.toBeInTheDocument();
        expect(screen.queryByRole('link', { name: 'View settings' })).not.toBeInTheDocument();
    });

    it.each([390, 1280])('Gallery_SelectsWithCircles_AtWidth%i', async (width) => {
        // Arrange
        installMatchMedia(width);
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        vi.mocked(apiPost).mockResolvedValue('added-603');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');

        // Act
        await user.click(screen.getByRole('tab', { name: 'Gallery' }));
        const gallery = await screen.findByRole('list', { name: 'Content gallery' });
        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(within(gallery).getByRole('button', { name: 'Select The Matrix' }));

        // Assert
        expect(within(gallery).getByRole('button', { name: 'Select The Matrix' })).toHaveAttribute(
            'aria-pressed',
            'true',
        );
        expect(gallery.querySelector('.selection-circle-toggle')).not.toBeNull();
        expect(gallery.querySelector('[data-slot="content.gallery.poster"]')).not.toBeNull();
        expect(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        ).toBeEnabled();
        expect(localStorage.getItem('library-add-view')).toBe('"gallery"');

        // Act
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledExactlyOnceWith(
            '/content/from-external-id?rootFolderId=rf-movies',
            expect.objectContaining({ externalId: '603' }),
        );
    });

    it.each([390, 1280])('Gallery_KeepsAddedItemsPreviewableWithoutAddButtons_AtWidth%i', async (width) => {
        // Arrange
        installMatchMedia(width);
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        vi.mocked(apiPost).mockResolvedValue('added-603');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(screen.getByRole('tab', { name: 'Gallery' }));
        const gallery = await screen.findByRole('list', { name: 'Content gallery' });

        // Act
        await user.click(within(gallery).getByRole('button', { name: 'Add The Matrix' }));

        // Assert
        expect(await screen.findByText('Added to library')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledExactlyOnceWith(
            '/content/from-external-id?rootFolderId=rf-movies',
            expect.objectContaining({ externalId: '603' }),
        );
        expect(screen.queryByTestId('preview-drawer')).not.toBeInTheDocument();
        expect(within(gallery).queryByRole('button', { name: 'Add The Matrix' })).not.toBeInTheDocument();
        expect(within(gallery).getByRole('button', { name: 'Preview The Matrix (in library)' })).toBeEnabled();
        expect(gallery.querySelector('[data-content-id="added-603"]')).toHaveAttribute('data-muted', 'true');

        // Act
        await user.click(within(gallery).getByRole('button', { name: 'Preview The Matrix (in library)' }));
        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(within(gallery).getAllByRole('button', { name: 'Preview The Matrix' })[0]);

        // Assert
        expect(mockOpenLocalPreview).toHaveBeenCalledTimes(2);
        expect(mockOpenLocalPreview).toHaveBeenLastCalledWith('added-603', expect.any(HTMLElement));
        expect(within(gallery).queryByRole('button', { name: 'Select The Matrix' })).not.toBeInTheDocument();
    });

    it.each([390, 1280])('Gallery_ShowsTileSkeletonsAndClearsSelection_WhenPagingAtWidth%i', async (width) => {
        // Arrange
        installMatchMedia(width);
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate], { hasMore: true }),
            nextContinuation: 'page-two',
        };
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(screen.getByRole('tab', { name: 'Gallery' }));
        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(screen.getByRole('button', { name: 'Select The Matrix' }));
        vi.mocked(apiGet).mockImplementation((path: string) =>
            path.startsWith('/content-search') ? new Promise(() => {}) : Promise.resolve([]),
        );

        // Act
        await user.click(screen.getByRole('button', { name: /next page/i }));

        // Assert
        const loader = await screen.findByTestId('library-add-page__loading');
        expect(loader.querySelector('.content-gallery-grid')).not.toBeNull();
        expect(loader.querySelectorAll('.content-gallery-tile__poster')).toHaveLength(12);
        expect(loader.querySelector('.library-add-page__skeleton')).toBeNull();
        expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Select all' })).toBeDisabled();
        expect(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        ).toBeDisabled();

        // Act
        await user.click(screen.getByRole('button', { name: 'Cancel search' }));

        // Assert
        expect(await screen.findByRole('button', { name: 'Select The Matrix' })).toHaveAttribute(
            'aria-pressed',
            'false',
        );
    });

    it('Gallery_PreservesTableSelectionAndClearsIt_WhenSelectionModeIsExited', async () => {
        // Arrange
        installMatchMedia(1280);
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' }));

        // Act
        await user.click(screen.getByRole('tab', { name: 'Gallery' }));

        // Assert
        const gallery = screen.getByRole('list', { name: 'Content gallery' });
        expect(within(gallery).getByRole('button', { name: 'Select The Matrix' })).toHaveAttribute(
            'aria-pressed',
            'true',
        );
        expect(screen.getByRole('button', { name: 'Select' })).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByRole('region', { name: 'Add content selection actions' })).toHaveTextContent('1 selected');

        // Act
        await user.click(screen.getByRole('button', { name: 'Select' }));

        // Assert
        expect(screen.queryByRole('region', { name: 'Add content selection actions' })).not.toBeInTheDocument();
        expect(within(gallery).queryByRole('button', { name: 'Select The Matrix' })).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Select' })).toHaveAttribute('aria-pressed', 'false');

        // Act
        await user.click(screen.getByRole('tab', { name: 'List' }));

        // Assert
        expect(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' })).not.toBeChecked();
    });

    it.each([
        ['List', 'checkbox'],
        ['Gallery', 'button'],
    ] as const)('BulkAdd_ClearsSelections_WhenPagingIn%sView', async (view, selectorRole) => {
        // Arrange
        installMatchMedia(1280);
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate], { hasMore: true }),
            nextContinuation: 'page-two',
        };
        vi.mocked(apiPost).mockResolvedValue('added');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(screen.getByRole('tab', { name: view }));
        if (view === 'Gallery') await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(await screen.findByRole(selectorRole, { name: 'Select The Matrix' }));

        // Act
        mockCandidateSearchResponse = candidateSearchResult(
            [{ ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' }],
            { requestedPage: 2 },
        );
        await user.click(screen.getByRole('button', { name: /next page/i }));
        await screen.findByText('Reloaded');

        // Assert
        expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
        expect(screen.getByRole(selectorRole, { name: 'Select Reloaded' })).not.toHaveAttribute(
            view === 'Gallery' ? 'aria-pressed' : 'aria-checked',
            'true',
        );

        // Act
        await user.click(screen.getByRole(selectorRole, { name: 'Select Reloaded' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledExactlyOnceWith(
            '/content/from-external-id?rootFolderId=rf-movies',
            expect.objectContaining({ externalId: '604' }),
        );

        // Act
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate], { hasMore: true });
        await user.click(screen.getByRole('button', { name: /previous page/i }));
        await screen.findByText('The Matrix');

        // Assert
        expect(screen.getByRole(selectorRole, { name: 'Select The Matrix' })).not.toHaveAttribute(
            view === 'Gallery' ? 'aria-pressed' : 'aria-checked',
            'true',
        );
    });

    it('BulkAdd_UsesTheDefaultFolder_WhenSeveralExist', async () => {
        // Arrange
        mockApiGetByPath({
            rootFolders: [
                rootFolder('archive', 'Archive', 'movies'),
                { ...rootFolder('default', 'Movies', 'movies'), isDefault: true },
            ],
        });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        vi.mocked(apiPost).mockResolvedValue('added');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');

        // Act
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledWith('/content/from-external-id?rootFolderId=default', expect.any(Object));
        expect(screen.queryByRole('dialog', { name: 'Add selected content' })).not.toBeInTheDocument();
    });

    it('BulkAdd_RequestsFolderChoice_WhenNoDefaultResolvesTheDestination', async () => {
        // Arrange
        mockApiGetByPath({
            rootFolders: [rootFolder('archive', 'Archive', 'movies'), rootFolder('movies', 'Movies', 'movies')],
        });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        vi.mocked(apiPost).mockResolvedValue('added');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' }));

        // Act
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        const dialog = await screen.findByRole('dialog', { name: 'Add selected content' });
        expect(apiPost).not.toHaveBeenCalled();
        expect(within(dialog).getByRole('button', { name: 'Add' })).toBeDisabled();

        // Act
        await user.click(within(dialog).getByRole('combobox', { name: 'Root folder for Movies' }));
        await user.click(await screen.findByRole('option', { name: 'Archive' }));
        await user.click(within(dialog).getByRole('button', { name: 'Add' }));

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledExactlyOnceWith(
            '/content/from-external-id?rootFolderId=archive',
            expect.objectContaining({ externalId: '603', contentType: 'movies' }),
        );
    });

    it('BulkAdd_DiscardsFolderChoices_WhenTheDialogIsCancelled', async () => {
        // Arrange
        mockApiGetByPath({
            rootFolders: [rootFolder('archive', 'Archive', 'movies'), rootFolder('movies', 'Movies', 'movies')],
        });
        mockCandidateSearchResponse = candidateSearchResult([
            candidateSearchedCandidate,
            { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' },
        ]);
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );
        const dialog = await screen.findByRole('dialog', { name: 'Add selected content' });
        await user.click(within(dialog).getByRole('combobox', { name: 'Root folder for Movies' }));
        await user.click(await screen.findByRole('option', { name: 'Archive' }));

        // Act
        await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
        await user.click(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' }));
        await user.click(resultsTable().getByRole('checkbox', { name: 'Select Reloaded' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        const reopened = await screen.findByRole('dialog', { name: 'Add selected content' });
        expect(within(reopened).getByRole('combobox', { name: 'Root folder for Movies' })).toHaveTextContent(
            'Choose a root folder',
        );
        expect(within(reopened).getByRole('button', { name: 'Add' })).toBeDisabled();
        expect(apiPost).not.toHaveBeenCalled();
    });

    it('BulkAdd_UsesEachTypesDefault_WhenTheSelectionContainsMixedTypes', async () => {
        // Arrange
        mockApiGetByPath({
            rootFolders: [
                rootFolder('movie-archive', 'Movie archive', 'movies'),
                { ...rootFolder('movie-default', 'Movies', 'movies'), isDefault: true },
                rootFolder('show-archive', 'Show archive', 'shows'),
                { ...rootFolder('show-default', 'Shows', 'shows'), isDefault: true },
            ],
        });
        mockCandidateSearchResponse = candidateSearchResult([
            candidateSearchedCandidate,
            {
                ...candidateSearchedCandidate,
                externalId: 'show-1',
                contentType: 'shows',
                externalIdSource: 'provider-a.show',
                title: 'A Show',
            },
        ]);
        vi.mocked(apiPost).mockResolvedValue('added');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');

        // Act
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select this page' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        expect(await screen.findByText('2 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledTimes(2);
        expect(apiPost).toHaveBeenCalledWith(
            '/content/from-external-id?rootFolderId=movie-default',
            expect.objectContaining({ externalId: '603', contentType: 'movies' }),
        );
        expect(apiPost).toHaveBeenCalledWith(
            '/content/from-external-id?rootFolderId=show-default',
            expect.objectContaining({ externalId: 'show-1', contentType: 'shows' }),
        );
        expect(screen.queryByRole('dialog', { name: 'Add selected content' })).not.toBeInTheDocument();
    });

    it('BulkAdd_DropsPreviousSelections_WhenAFreshSearchRuns', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        vi.mocked(apiPost).mockResolvedValue('added');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' }));

        // Act
        mockCandidateSearchResponse = candidateSearchResult([
            { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' },
        ]);
        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await screen.findByText('Reloaded');

        // Assert
        expect(screen.queryByRole('region', { name: 'Add content selection actions' })).not.toBeInTheDocument();

        // Act
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select Reloaded' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledExactlyOnceWith(
            '/content/from-external-id?rootFolderId=rf-movies',
            expect.objectContaining({ externalId: '604' }),
        );
    });

    it('BulkAdd_RetriesOnlyFailedItems_WhenTheUserAddsAgain', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([
            candidateSearchedCandidate,
            { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' },
        ]);
        vi.mocked(apiPost).mockImplementation((_path, request) =>
            (request as { externalId: string }).externalId === '604'
                ? Promise.reject(new Error('Rejected'))
                : Promise.resolve('added-603'),
        );
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select this page' }));

        // Act
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );
        await screen.findByText('1 added, 1 failed.');

        // Assert
        expect(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' })).toBeDisabled();
        expect(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' })).not.toBeChecked();
        expect(resultsTable().getByRole('checkbox', { name: 'Select Reloaded' })).toBeChecked();

        // Act
        vi.mocked(apiPost).mockClear().mockResolvedValue('added-604');
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledExactlyOnceWith(
            '/content/from-external-id?rootFolderId=rf-movies',
            expect.objectContaining({ externalId: '604' }),
        );
        expect(screen.queryByRole('region', { name: 'Add content selection actions' })).not.toBeInTheDocument();
    });

    it('BulkAdd_SubmitsEachItemOnce_WhenTheUserClicksAgainDuringTheRequest', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate], { hasMore: true }),
            nextContinuation: 'page-two',
        };
        let resolveAdd!: (id: string) => void;
        vi.mocked(apiPost).mockReturnValue(new Promise<string>((resolve) => (resolveAdd = resolve)));
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' }));
        const bulkAdd = within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole(
            'button',
            { name: 'Add' },
        );

        // Act
        await user.dblClick(bulkAdd);

        // Assert
        expect(apiPost).toHaveBeenCalledTimes(1);
        expect(bulkAdd).toBeDisabled();
        expect(screen.getByRole('button', { name: /^search$/i })).toBeDisabled();
        expect(resultsTable().getByRole('button', { name: 'Add' })).toBeDisabled();
        expect(screen.getByRole('button', { name: /next page/i })).toBeDisabled();
        expect(resultsTable().queryByRole('button', { name: /sort by title/i })).not.toBeInTheDocument();

        // Act
        await user.click(screen.getByRole('tab', { name: 'Gallery' }));

        // Assert
        expect(screen.getByRole('combobox', { name: 'Sort by field' })).toBeDisabled();

        // Act
        await act(async () => resolveAdd('added-603'));

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledTimes(1);
        expect(document.querySelectorAll('[data-component="toast"]')).toHaveLength(1);
    });

    it('Cancel_RestoresAddableResults_WhenAFreshSearchIsCancelled', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        vi.mocked(apiPost).mockResolvedValue('added-603');
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        vi.mocked(apiGet).mockImplementation((path: string) =>
            path.startsWith('/content-search') ? new Promise(() => {}) : Promise.resolve([]),
        );

        // Act
        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await screen.findByTestId('library-add-page__loading');
        await user.click(screen.getByRole('button', { name: 'Cancel search' }));
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' }));

        // Assert
        const bulkAdd = within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole(
            'button',
            { name: 'Add' },
        );
        expect(bulkAdd).toBeEnabled();

        // Act
        await user.click(bulkAdd);

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledExactlyOnceWith(
            '/content/from-external-id?rootFolderId=rf-movies',
            expect.objectContaining({ externalId: '603' }),
        );
    });

    it('BulkAdd_KeepsTheSearch_WhenTheFilterIsSubmittedWithEnterDuringTheRequest', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        let resolveAdd!: (id: string) => void;
        vi.mocked(apiPost).mockReturnValue(new Promise<string>((resolve) => (resolveAdd = resolve)));
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );
        vi.mocked(apiGet).mockClear();

        // Act
        await user.type(screen.getByLabelText('Tag'), '{Enter}');

        // Assert
        expect(vi.mocked(apiGet).mock.calls.filter(([path]) => path.startsWith('/content-search'))).toHaveLength(0);
        expect(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' })).toBeChecked();
        expect(screen.getAllByText('1 selected').length).toBeGreaterThan(0);

        // Act
        await act(async () => resolveAdd('added-603'));

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
    });

    it('BulkAdd_LocksSelectionControls_WhenTheRequestIsPending', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([
            candidateSearchedCandidate,
            { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' },
        ]);
        let resolveAdd!: (id: string) => void;
        vi.mocked(apiPost).mockReturnValue(new Promise<string>((resolve) => (resolveAdd = resolve)));
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');
        await user.click(await resultsTable().findByRole('checkbox', { name: 'Select The Matrix' }));
        await user.click(
            within(screen.getByRole('region', { name: 'Add content selection actions' })).getByRole('button', {
                name: 'Add',
            }),
        );

        // Act
        await user.click(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' }));
        await user.click(resultsTable().getByRole('checkbox', { name: 'Select Reloaded' }));
        await user.click(resultsTable().getByRole('checkbox', { name: 'Select this page' }));
        await user.click(screen.getByRole('button', { name: 'Select all' }));
        await user.click(screen.getByRole('button', { name: 'Clear selection' }));

        // Assert
        expect(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' })).toBeChecked();
        expect(resultsTable().getByRole('checkbox', { name: 'Select Reloaded' })).not.toBeChecked();
        expect(screen.getAllByText('1 selected').length).toBeGreaterThan(0);
        expect(apiPost).toHaveBeenCalledTimes(1);

        // Act
        await act(async () => resolveAdd('added-603'));

        // Assert
        expect(await screen.findByText('1 added, 0 failed.')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledTimes(1);
    });

    it('Cancel_ReleasesBusyState_WhenLiveSearchIsPending', async () => {
        // Arrange
        vi.mocked(apiGet).mockImplementation((path: string) => {
            if (path.startsWith('/content-search')) return new Promise(() => {});
            return Promise.resolve([]);
        });
        const user = userEvent.setup();
        renderLive();
        await screen.findByTestId('library-add-page__loading');

        // Act
        await user.click(screen.getByRole('button', { name: 'Cancel search' }));

        // Assert
        expect(screen.queryByTestId('library-add-page__loading')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^search$/i })).toBeEnabled();
        expect(vi.mocked(apiGet)).toHaveBeenCalledWith(
            expect.stringContaining('/content-search'),
            expect.objectContaining({ signal: expect.any(AbortSignal) }),
        );
        const searchOptions = vi.mocked(apiGet).mock.calls.find(([url]) => url.startsWith('/content-search'))?.[1];
        expect(searchOptions?.signal?.aborted).toBe(true);
    });

    it('Cancel_PrecedesSearch_WhenLiveSearchIsPending', async () => {
        // Arrange
        vi.mocked(apiGet).mockImplementation((path: string) => {
            if (path.startsWith('/content-search')) return new Promise(() => {});
            return Promise.resolve([]);
        });
        renderLive();

        // Act
        await screen.findByTestId('library-add-page__loading');

        // Assert
        const search = screen.getByRole('button', { name: /^search$/i });
        expect(search.previousElementSibling).toBe(screen.getByRole('button', { name: 'Cancel search' }));
        expect(search.nextElementSibling).toBeNull();
    });

    it('Cancel_RestoresDeliveredPage_WhenLiveContinuationIsPending', async () => {
        // Arrange
        let finishCancelled!: (value: unknown) => void;
        let searchCallCount = 0;
        vi.mocked(apiGet).mockImplementation((path: string) => {
            if (!path.startsWith('/content-search')) return Promise.resolve([]);
            searchCallCount++;
            if (searchCallCount === 1) {
                return Promise.resolve({
                    ...candidateSearchResult([candidateSearchedCandidate], { hasMore: true }),
                    continuation: 'first-page',
                    nextContinuation: 'second-page',
                });
            }
            if (searchCallCount === 2) return new Promise((resolve) => (finishCancelled = resolve));
            return Promise.resolve({
                ...candidateSearchResult([{ ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' }], {
                    requestedPage: 2,
                }),
                continuation: 'second-page',
            });
        });
        const user = userEvent.setup();
        renderLive();
        expect(await screen.findByText('The Matrix')).toBeInTheDocument();

        // Act
        await user.click(screen.getByRole('button', { name: /next page/i }));
        await screen.findByTestId('library-add-page__loading');
        const cancelledSignal = vi
            .mocked(apiGet)
            .mock.calls.filter(([path]) => path.startsWith('/content-search'))[1][1]?.signal;
        await user.click(screen.getByRole('button', { name: 'Cancel search' }));
        finishCancelled(candidateSearchResult([{ ...candidateSearchedCandidate, externalId: '999', title: 'Stale' }]));

        // Assert
        expect(cancelledSignal?.aborted).toBe(true);
        expect(await resultsTable().findByText('The Matrix')).toBeInTheDocument();
        expect(screen.queryByText('Stale')).not.toBeInTheDocument();
        expect(screen.getByText('Page 1')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: /next page/i }));
        expect(await resultsTable().findByText('Reloaded')).toBeInTheDocument();
        expect(
            vi
                .mocked(apiGet)
                .mock.calls.filter(([path]) => path.startsWith('/content-search'))
                .map(([path]) => path),
        ).toEqual([
            '/content-search?query=genre+has+%22Action%22&page=1',
            '/content-search?query=genre+has+%22Action%22&page=2&continuation=second-page',
            '/content-search?query=genre+has+%22Action%22&page=2&continuation=second-page',
        ]);
    });

    it('shows the page empty state when more pages exist', async () => {
        // Arrange
        mockCandidateSearchResponse = {
            ...candidateSearchResult([], { hasMore: true }),
            continuation: 'first-page',
            nextContinuation: 'second-page',
        };
        const user = userEvent.setup();
        renderLive();

        // Act
        await user.click(screen.getByRole('button', { name: /^search$/i }));

        // Assert
        expect(await screen.findByText('No matches on this page')).toBeInTheDocument();
        expect(screen.getByText('Continue to the next page to see more results.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /next page/i })).toBeEnabled();

        // Act
        mockCandidateSearchResponse = candidateSearchResult([], { hasMore: false, requestedPage: 2 });
        await user.click(screen.getByRole('button', { name: /next page/i }));

        // Assert
        expect(await screen.findByText('Return to a previous page to see earlier results.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /previous page/i })).toBeEnabled();
        expect(screen.getByRole('button', { name: /next page/i })).toBeDisabled();
    });

    it('NextPage_ShowsMissingProviderResults_WhenEmptyPageHasWarnings', async () => {
        // Arrange
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate], {
                hasMore: true,
                warnings: ['tmdb: TMDb search is unavailable because the provider is not configured.'],
            }),
            continuation: 'first-page',
            nextContinuation: 'second-page',
        };
        const user = userEvent.setup();
        renderLive();
        await user.click(screen.getByRole('button', { name: /^search$/i }));
        expect(await resultsTable().findByText('The Matrix')).toBeInTheDocument();
        expect(screen.queryByTestId('library-add-page__empty-no-results')).not.toBeInTheDocument();
        mockCandidateSearchResponse = {
            ...candidateSearchResult([], {
                hasMore: true,
                requestedPage: 2,
                warnings: ['tmdb: TMDb search is unavailable because the provider is not configured.'],
            }),
            continuation: 'second-page',
            nextContinuation: 'third-page',
        };

        // Act
        await user.click(screen.getByRole('button', { name: /next page/i }));

        // Assert
        const empty = await screen.findByTestId('library-add-page__empty-no-results');
        expect(empty).toHaveTextContent('No matches on this page');
        expect(empty).toHaveTextContent('Results from some providers are missing; the warnings below say why.');
        expect(screen.queryByText('Continue to the next page to see more results.')).not.toBeInTheDocument();
        expect(screen.getByTestId('library-add-page__warnings')).toHaveTextContent(
            'tmdb: TMDb search is unavailable because the provider is not configured.',
        );
        expect(screen.getByTestId('library-add-page__warnings')).toHaveTextContent('Some results may be missing');
        expect(screen.getByRole('button', { name: /next page/i })).toBeEnabled();
    });

    it('LastPage_ShowsMissingProviderResults_WhenEmptyPageHasWarnings', async () => {
        // Arrange
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate], {
                hasMore: true,
                warnings: ['tmdb: TMDb search is unavailable because the provider is not configured.'],
            }),
            continuation: 'first-page',
            nextContinuation: 'second-page',
        };
        const user = userEvent.setup();
        renderLive();
        await user.click(screen.getByRole('button', { name: /^search$/i }));
        expect(await resultsTable().findByText('The Matrix')).toBeInTheDocument();
        mockCandidateSearchResponse = candidateSearchResult([], {
            hasMore: false,
            requestedPage: 2,
            warnings: ['tmdb: TMDb search is unavailable because the provider is not configured.'],
        });

        // Act
        await user.click(screen.getByRole('button', { name: /next page/i }));

        // Assert
        const empty = await screen.findByTestId('library-add-page__empty-no-results');
        expect(empty).toHaveTextContent('Results from some providers are missing; the warnings below say why.');
        expect(screen.queryByText('Return to a previous page to see earlier results.')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: /next page/i })).toBeDisabled();
    });

    it.each([
        [400, 'Choose a writable root folder.'],
        [503, 'The provider is busy. Try again later.'],
    ])('Add_ShowsServerMessage_WhenRejected table %i', async (status, message) => {
        // Arrange
        mockApiGetByPath({
            rootFolders: [rootFolder('rf-archive', 'Archive', candidateSearchedCandidate.contentType)],
        });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        vi.mocked(apiPost).mockRejectedValue(new ApiError(status, message, { status, detail: message }));
        const user = userEvent.setup();
        renderLive();
        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await screen.findByText('The Matrix');

        // Act
        await user.click(resultsTable().getByRole('button', { name: /^add$/i }));

        // Assert
        expect(await screen.findByText(message)).toBeInTheDocument();
    });

    it.each([
        [400, 'Choose a writable root folder.'],
        [503, 'The provider is busy. Try again later.'],
    ])('Add_ShowsServerMessage_WhenRejected drawer %i', async (status, message) => {
        // Arrange
        mockApiGetByPath({
            rootFolders: [rootFolder('rf-archive', 'Archive', candidateSearchedCandidate.contentType)],
        });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        vi.mocked(apiPost).mockRejectedValue(new ApiError(status, message, { status, detail: message }));
        const user = userEvent.setup();
        renderLive();
        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await screen.findByText('The Matrix');

        // Act
        vi.mocked(apiPost).mockResolvedValueOnce(candidatePreview);
        await user.click(resultsTable().getByRole('button', { name: 'The Matrix' }));
        await user.click(await screen.findByRole('button', { name: /^add to library$/i }));

        // Assert
        expect(await screen.findByText(message)).toBeInTheDocument();
    });

    it('renders the page header and the initial empty state', () => {
        mockApiGetByPath({});
        render(
            <TestWrapper>
                <LibraryAddPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('heading', { name: 'Add content', level: 1 })).toBeInTheDocument();
        expect(screen.getByText('Search for new content to add to the library')).toBeInTheDocument();
        expect(screen.getByTestId('library-add-page__empty-initial')).toBeInTheDocument();
    });

    it('InitialEmptyState_ShowsHeadingOnly_WhenLiveTabIsOpen', () => {
        // Arrange
        mockApiGetByPath({});

        // Act
        render(
            <TestWrapper>
                <LibraryAddPage />
            </TestWrapper>,
        );

        // Assert
        const empty = screen.getByTestId('library-add-page__empty-initial');
        expect(within(empty).getByText('Search for new content')).toBeInTheDocument();
        expect(screen.queryByText(/searching external metadata providers/i)).not.toBeInTheDocument();
    });

    it('does not fetch provider candidate search on plain page entry', async () => {
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);

        render(
            <TestWrapper>
                <LibraryAddPage />
            </TestWrapper>,
        );

        expect(screen.getByTestId('library-add-page__empty-initial')).toBeInTheDocument();
        await waitFor(() => {
            expect(apiGet).not.toHaveBeenCalledWith(expect.stringContaining('/content-search'));
        });
    });

    it('shows the full release date in the Published column, not just the year', async () => {
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        const user = userEvent.setup();

        renderLive();

        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await waitFor(() => {
            expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
        });
        expect(resultsTable().getByText('Mar 31, 1999')).toBeInTheDocument();
    });

    it('Published_ShowsYearOnly_WhenReleaseDatePrecisionIsYear', async () => {
        // Arrange
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([
            { ...candidateSearchedCandidate, releaseDate: '1975-01-01T00:00:00Z', releaseDatePrecision: 'Year' },
        ]);
        const user = userEvent.setup();

        renderLive();

        // Act
        await user.click(screen.getByRole('button', { name: /^search$/i }));

        // Assert
        await waitFor(() => {
            expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
        });
        const row = document.querySelector('[data-row-id$=":603"]') as HTMLElement;
        expect(within(row).getByText('1975', { selector: '.library-add-page__published' })).toBeInTheDocument();
    });

    it('Published_UsesSecondaryPrecision_WhenMergedRowTakesTheSecondaryReleaseDate', async () => {
        // Arrange
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([
            { ...candidateSearchedCandidate, level: 'movie', releaseDate: undefined },
            {
                ...candidateSearchedCandidate,
                providerType: 'provider-b',
                externalIdSource: 'provider-b.movie',
                externalId: 'Q83495',
                level: 'movie',
                releaseDate: '1999-01-01T00:00:00Z',
                releaseDatePrecision: 'Year',
            },
        ]);
        const user = userEvent.setup();

        renderLive();

        // Act
        await user.click(screen.getByRole('button', { name: /^search$/i }));

        // Assert
        await waitFor(() => {
            expect(resultsTable().getAllByText('The Matrix')).toHaveLength(1);
        });
        const row = document.querySelector('[data-row-id$=":603"]') as HTMLElement;
        expect(within(row).getByText('1999', { selector: '.library-add-page__published' })).toBeInTheDocument();
    });

    it('shows one row when two providers return the same root-level content', async () => {
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([
            { ...candidateSearchedCandidate, level: 'movie' },
            {
                ...candidateSearchedCandidate,
                providerType: 'provider-b',
                externalIdSource: 'provider-b.movie',
                externalId: 'Q83495',
                level: 'movie',
            },
            {
                ...candidateSearchedCandidate,
                providerType: 'provider-b',
                externalIdSource: 'provider-b.movie',
                externalId: 'Q3111633',
                level: 'movie',
                title: 'The Matrix Reloaded',
                year: 2003,
            },
        ]);
        const user = userEvent.setup();

        renderLive();

        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await waitFor(() => {
            expect(resultsTable().getByText('The Matrix Reloaded')).toBeInTheDocument();
        });
        expect(resultsTable().getAllByText('The Matrix')).toHaveLength(1);
    });

    it('shows both rows when equal titles are not root-level content', async () => {
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([
            {
                ...candidateSearchedCandidate,
                externalId: 'ep-1',
                contentType: 'shows',
                level: 'episode',
                title: 'Pilot',
            },
            {
                ...candidateSearchedCandidate,
                providerType: 'provider-b',
                externalIdSource: 'provider-b.episode',
                externalId: 'ep-2',
                contentType: 'shows',
                level: 'episode',
                title: 'Pilot',
            },
        ]);
        const user = userEvent.setup();

        renderLive();

        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await waitFor(() => {
            expect(resultsTable().getAllByText('Pilot')).toHaveLength(2);
        });
    });

    it('marks a merged row as in library when either provider result is in library', async () => {
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([
            { ...candidateSearchedCandidate, level: 'movie' },
            {
                ...candidateSearchedCandidate,
                providerType: 'provider-b',
                externalIdSource: 'provider-b.movie',
                externalId: 'Q83495',
                level: 'movie',
                inLibrary: true,
            },
        ]);
        const user = userEvent.setup();

        renderLive();

        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await waitFor(() => {
            expect(resultsTable().getAllByText('The Matrix')).toHaveLength(1);
        });
        expect(resultsTable().getAllByRole('button', { name: 'In Library' })).toHaveLength(1);
    });

    it('Results_ShowNoPageNoticeForLibraryMatches_WhenResultsAreInLibrary', async () => {
        // Arrange
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([
            { ...candidateSearchedCandidate, level: 'movie', inLibrary: true },
        ]);
        const user = userEvent.setup();

        renderLive();

        // Act
        await user.click(screen.getByRole('button', { name: /^search$/i }));

        // Assert
        await waitFor(() => {
            expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
        });
        expect(screen.queryByText(/already in your library/i)).not.toBeInTheDocument();
        expect(screen.queryByRole('status')).not.toBeInTheDocument();
    });

    it('does not render the source identifier beside the Source column', async () => {
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        const user = userEvent.setup();

        renderLive();

        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await waitFor(() => {
            expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
        });
        expect(resultsTable().getAllByText('provider-a').length).toBeGreaterThan(0);
        expect(resultsTable().queryByText('provider-a.movie:603')).not.toBeInTheDocument();
    });

    describe('drop by-title', () => {
        it('renders Live filter and Saved filter tabs and no title-search control', () => {
            mockApiGetByPath({});
            renderLive();

            expect(screen.getByRole('tab', { name: /live filter/i })).toBeInTheDocument();
            expect(screen.getByRole('tab', { name: /saved filter/i })).toBeInTheDocument();
            expect(screen.queryByRole('tab', { name: /by title/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('searchbox', { name: /search by title/i })).not.toBeInTheDocument();
        });

        it('does not expose a content-type dropdown', () => {
            mockApiGetByPath({});
            renderLive();

            expect(document.querySelector('[data-field="content-type"]')).not.toBeInTheDocument();
            expect(document.querySelector('#sfb-content-type')).toBeInTheDocument();
        });

        it('does not expose a page-level root folder control', () => {
            mockApiGetByPath({});
            renderLive();

            expect(document.querySelector('[data-field="root-folder"]')).not.toBeInTheDocument();
        });
    });

    describe('Live filter tab', () => {
        it('disables Search with no criteria and shows no hint, firing no parse or provider candidate search', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            render(
                <TestWrapper>
                    <LibraryAddPage />
                </TestWrapper>,
            );

            const search = screen.getByRole('button', { name: /^search$/i }) as HTMLButtonElement;
            expect(search.disabled).toBe(true);
            expect(screen.queryByText(/add a filter criterion/i)).not.toBeInTheDocument();

            await user.click(search);
            expect(mockParseMagpieQl).not.toHaveBeenCalled();
            expect(apiGet).not.toHaveBeenCalledWith(expect.stringContaining('/content-search'));
            expect(screen.queryByTestId('library-add-page__error')).not.toBeInTheDocument();
        });

        it('Search_ReturnsCandidates_WhenLiveCriterionIsPresent', async () => {
            // Arrange
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive('genre has "Action"');

            // Assert
            const search = screen.getByRole('button', { name: /^search$/i }) as HTMLButtonElement;
            await waitFor(() => {
                expect(search.disabled).toBe(false);
            });
            vi.mocked(apiGet).mockClear();
            // Act
            await user.click(search);
            // Assert
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=genre+has+%22Action%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
        });

        it('Search_ShowsError_WhenLiveCriteriaSubmittedWithEnter', async () => {
            // Arrange
            mockCandidateSearchError = {
                name: 'ApiError',
                status: 400,
                message: 'Bad request',
                details: { message: 'Unexpected token', position: 3, expected: [] },
            };
            const user = userEvent.setup();

            render(
                <TestWrapper>
                    <LibraryAddPage />
                </TestWrapper>,
            );

            // Act
            const tag = screen.getByLabelText('Tag');
            await user.type(tag, 'favorite');
            await user.type(tag, '{Enter}');

            // Assert
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=tag+has+%22favorite%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
                expect(screen.getByTestId('library-add-page__error')).toBeInTheDocument();
            });
            expect(screen.getByText(/unexpected token/i)).toBeInTheDocument();
        });

        it('Search_RendersCandidates_WhenLiveCriteriaAreEdited', async () => {
            // Arrange
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive();

            // Act
            await user.click(screen.getByRole('button', { name: /^search$/i }));

            // Assert
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=genre+has+%22Action%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
            });
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
            expect(resultsTable().getByText('1999')).toBeInTheDocument();
            expect(resultsTable().getByText('Mar 31, 1999')).toBeInTheDocument();
            // Act
            fireEvent.error(screen.getByTestId('library-add-page__result-image'));
            // Assert
            expect(screen.queryByTestId('library-add-page__result-image')).not.toBeInTheDocument();
            expect(document.querySelector('.library-add-page__poster--placeholder')).toBeInTheDocument();
        });

        it('renders candidate cards without a DataTable at tablet width', async () => {
            installMatchMedia(900);
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            const cards = await screen.findByTestId('library-add-page__mobile-list');
            expect(within(cards).getByText('The Matrix')).toBeInTheDocument();
            expect(screen.queryByTestId('library-add-page__result-list')).not.toBeInTheDocument();
            expect(document.querySelector('[data-component="data-table"]')).not.toBeInTheDocument();
        });

        it('InLibraryButton_ReplacesAdd_WhenCardResultIsInLibrary', async () => {
            // Arrange
            installMatchMedia(390);
            mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
            mockCandidateSearchResponse = candidateSearchResult([
                {
                    ...candidateSearchedCandidate,
                    inLibrary: true,
                    existingContentHierarchyItemId: '3db97837-8a29-4b92-937b-13f948bfd62d',
                },
            ]);
            const user = userEvent.setup();

            renderLive();

            // Act
            await user.click(screen.getByRole('button', { name: /^search$/i }));

            // Assert
            const cards = await screen.findByTestId('library-add-page__mobile-list');
            const inLibrary = within(cards).getByRole('button', { name: 'In Library' });
            expect(inLibrary).toBeDisabled();
            expect(within(cards).queryByRole('button', { name: /^add$/i })).not.toBeInTheDocument();
            expect(within(cards).queryByText('In library')).not.toBeInTheDocument();
        });

        it('renders a Markdown provider summary as readable text in the table', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([
                {
                    ...candidateSearchedCandidate,
                    overview:
                        '**Pride and Prejudice** is a novel by [Jane Austen](https://openlibrary.org/authors/OL21594A). ([source][1])\n\n----------\n\n[1]: https://openlibrary.org/works/OL66554W',
                },
            ]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            expect(
                await resultsTable().findByText('Pride and Prejudice is a novel by Jane Austen. (source)'),
            ).toBeInTheDocument();
        });

        it('renders a Markdown provider summary as readable text on a card', async () => {
            installMatchMedia(390);
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([
                {
                    ...candidateSearchedCandidate,
                    overview:
                        '**Pride and Prejudice** is a novel by [Jane Austen](https://openlibrary.org/authors/OL21594A). ([source][1])\n\n----------\n\n[1]: https://openlibrary.org/works/OL66554W',
                },
            ]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            const cards = await screen.findByTestId('library-add-page__mobile-list');
            expect(
                within(cards).getByText('Pride and Prejudice is a novel by Jane Austen. (source)'),
            ).toBeInTheDocument();
        });

        it('surfaces unapplied-criteria warnings near the results', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate], {
                warnings: ['TMDb could not apply the Awards filter.'],
            });
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            await waitFor(() => {
                expect(screen.getByTestId('library-add-page__warnings')).toBeInTheDocument();
            });
            expect(screen.getByText(/TMDb could not apply the Awards filter/i)).toBeInTheDocument();
            expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
        });

        it('renders no warnings notice when provider candidate search returns none', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
            expect(screen.queryByTestId('library-add-page__warnings')).not.toBeInTheDocument();
        });

        it('adds provider warning context to the no-results state', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([], {
                warnings: ['TMDb search is too broad. Add more search details.'],
            });
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            expect(await screen.findByTestId('library-add-page__empty-no-results')).toHaveTextContent(
                'Results from some providers are missing; the warnings below say why.',
            );
            expect(screen.getByTestId('library-add-page__warnings')).toHaveTextContent(
                'This search could not return results',
            );
        });

        it('Search_ShowsParseDetails_WhenCandidateRequestFailsValidation', async () => {
            // Arrange
            mockApiGetByPath({});
            mockCandidateSearchError = {
                name: 'ApiError',
                status: 400,
                message: 'Bad request',
                details: { message: 'Unexpected token', position: 3, expected: [] },
            };
            const user = userEvent.setup();

            renderLive();

            // Act
            await user.click(screen.getByRole('button', { name: /^search$/i }));

            // Assert
            await waitFor(() => {
                expect(screen.getByTestId('library-add-page__error')).toBeInTheDocument();
            });
            expect(screen.getByText(/unexpected token/i)).toBeInTheDocument();
            expect(mockParseMagpieQl).not.toHaveBeenCalled();
            expect(apiGet).toHaveBeenCalledWith(
                '/content-search?query=genre+has+%22Action%22&page=1',
                expect.objectContaining({ signal: expect.any(AbortSignal) }),
            );
        });

        it('surfaces a bad-request ProblemDetails detail from provider search', async () => {
            mockApiGetByPath({});
            mockCandidateSearchError = new ApiError(
                400,
                'The provider could not settle a stable result page after 5 requests.',
                { detail: 'The provider could not settle a stable result page after 5 requests.' },
            );
            const user = userEvent.setup();

            renderLive();
            await user.click(screen.getByRole('button', { name: /^search$/i }));

            expect(
                await screen.findByText('The provider could not settle a stable result page after 5 requests.'),
            ).toBeInTheDocument();
        });

        it('keeps server-error details hidden', async () => {
            mockApiGetByPath({});
            mockCandidateSearchError = new ApiError(500, 'Database connection string exposed.', {
                detail: 'Database connection string exposed.',
            });
            const user = userEvent.setup();

            renderLive();
            await user.click(screen.getByRole('button', { name: /^search$/i }));

            expect(await screen.findByText('Search failed. Please try again.')).toBeInTheDocument();
            expect(screen.queryByText('Database connection string exposed.')).not.toBeInTheDocument();
        });

        it('shows no-results when provider candidate search returns nothing', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            await waitFor(() => {
                expect(screen.getByTestId('library-add-page__empty-no-results')).toBeInTheDocument();
            });
        });

        it('keeps Next enabled on a short page when the provider reports more results', async () => {
            mockApiGetByPath({});
            const shortPage = Array.from({ length: 6 }, (_, i) => ({
                ...candidateSearchedCandidate,
                externalId: String(1000 + i),
            }));
            mockCandidateSearchResponse = candidateSearchResult(shortPage, { hasMore: true });
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            await waitFor(() => {
                expect(screen.getByTestId('library-add-page__pagination')).toBeInTheDocument();
            });
            expect(screen.queryByTestId('library-add-page__empty-no-results')).not.toBeInTheDocument();
            const nextButton = document.querySelector('[data-action="next-page"]') as HTMLButtonElement;
            expect(nextButton.disabled).toBe(false);
        });

        it('renders in-library rows with a disabled In Library button and addable titles as preview buttons', async () => {
            mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
            const owned = {
                ...candidateSearchedCandidate,
                externalId: '550',
                title: 'Fight Club',
                inLibrary: true,
                existingContentHierarchyItemId: '3db97837-8a29-4b92-937b-13f948bfd62d',
            };
            mockCandidateSearchResponse = candidateSearchResult([owned, candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            await waitFor(() => {
                expect(resultsTable().getByText('Fight Club')).toBeInTheDocument();
            });
            expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();

            const table = document.querySelector('[data-testid="library-add-page__result-list"]') as HTMLElement;

            const ownedRow = table.querySelector('[data-row-id$=":550"]') as HTMLElement;
            expect(ownedRow).not.toBeNull();
            expect(within(ownedRow).getByRole('button', { name: 'In Library' })).toBeDisabled();
            expect(within(ownedRow).queryByText('In library')).not.toBeInTheDocument();
            expect(within(ownedRow).getByText('provider-a')).toBeInTheDocument();
            expect(ownedRow.querySelector('[data-action="add-content"]')).toBeNull();
            expect(ownedRow.querySelector('[data-action="preview-content"]')).toBeNull();
            expect(ownedRow.querySelector('[data-action="open-library-item"]')).toHaveAttribute(
                'href',
                '/library/3db97837-8a29-4b92-937b-13f948bfd62d',
            );

            const addableRow = table.querySelector('[data-row-id$=":603"]') as HTMLElement;
            expect(addableRow).not.toBeNull();
            const addableAdd = addableRow.querySelector('[data-action="add-content"]') as HTMLButtonElement;
            expect(addableAdd).not.toBeNull();
            expect(addableAdd.disabled).toBe(false);
            expect(addableRow.querySelector('[data-action="open-library-item"]')).toBeNull();
            expect(within(addableRow).getByRole('button', { name: 'The Matrix' })).toHaveAttribute(
                'data-action',
                'preview-content',
            );
            expect(within(addableRow).queryByRole('button', { name: 'Details' })).not.toBeInTheDocument();
            expect(screen.queryByText(/already in your library/i)).not.toBeInTheDocument();
        });

        it('saves the current live filter via save-as', async () => {
            mockApiGetByPath({});
            vi.mocked(apiPost).mockResolvedValue('saved-filter-id');
            const user = userEvent.setup();

            render(
                <QueryClientProvider client={createTestQueryClient()}>
                    <MemoryRouter
                        initialEntries={[{ pathname: '/library/add', state: { filterQuery: 'genre has "Action"' } }]}
                    >
                        <ToastStateProvider>
                            <TooltipProvider>
                                <ToastProvider>
                                    <LibraryAddPage />
                                    <ToastViewport />
                                </ToastProvider>
                            </TooltipProvider>
                        </ToastStateProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
            );

            const saveButton = await waitFor(() => {
                const btn = document.querySelector('[data-action="save-filter"]') as HTMLElement | null;
                expect(btn).toBeInTheDocument();
                return btn!;
            });
            await user.click(saveButton);

            const dialog = await screen.findByRole('dialog');
            const nameInput = await screen.findByLabelText(/filter name/i);
            await user.type(nameInput, 'My Action Filter');
            const confirm = within(dialog).getByRole('button', { name: /save filter/i });
            await user.click(confirm);

            await waitFor(() => {
                expect(apiPost).toHaveBeenCalledWith(
                    '/content-filters',
                    expect.objectContaining({ name: 'My Action Filter' }),
                );
            });
        });

        it('shows a query parse error on the query when saving a filter', async () => {
            mockParseMagpieQl.mockResolvedValue({ error: { message: 'Expected value' } });
            const user = userEvent.setup();

            renderLive();

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
            expect(apiPost).not.toHaveBeenCalledWith('/content-filters', expect.anything());
        });

        it('adds a table candidate in place', async () => {
            mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            vi.mocked(apiPost).mockResolvedValue('new-matrix-id');
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });

            await user.click(resultsTable().getByRole('button', { name: /^add$/i }));

            await waitFor(() => {
                expect(apiPost).toHaveBeenCalledWith('/content/from-external-id?rootFolderId=rf-movies', {
                    providerType: 'provider-a',
                    externalIdKey: 'provider-a.movie',
                    externalId: '603',
                    contentType: 'movies',
                });
            });
            const openLibraryItem = await resultsTable().findByRole('link', { name: 'The Matrix' });
            expect(openLibraryItem).toHaveAttribute('href', '/library/new-matrix-id');
            expect(resultsTable().getByRole('button', { name: 'In Library' })).toBeDisabled();
            expect(resultsTable().queryByRole('button', { name: /^add$/i })).not.toBeInTheDocument();
            expect(screen.getByRole('tab', { name: /live filter/i })).toHaveAttribute('aria-selected', 'true');
            expect(document.querySelector('[data-component="filter-bar"]')).toBeInTheDocument();
            expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            expect(mockNavigate).not.toHaveBeenCalledWith('/library/new-matrix-id');
        });

        it('ViewSettings_IsNotOffered_WhenAddSucceeds', async () => {
            // Arrange
            mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            vi.mocked(apiPost).mockResolvedValue('new-matrix-id');
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            // Act
            await user.click(await resultsTable().findByRole('button', { name: /^add$/i }));

            // Assert
            expect(await screen.findByText('Added to library')).toBeInTheDocument();
            expect(screen.queryByRole('link', { name: 'View settings' })).not.toBeInTheDocument();
        });

        it('ViewSettings_IsNotOffered_WhenAddFails', async () => {
            // Arrange
            mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            vi.mocked(apiPost).mockRejectedValue(new Error('boom'));
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));

            // Act
            await user.click(await resultsTable().findByRole('button', { name: /^add$/i }));

            // Assert
            expect(await screen.findByText('Failed to add content')).toBeInTheDocument();
            expect(screen.queryByRole('link', { name: 'View settings' })).not.toBeInTheDocument();
        });

        it('returns focus to the table preview button after the preview drawer closes', async () => {
            mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            vi.mocked(apiPost).mockResolvedValueOnce(candidatePreview);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));
            const previewButton = await resultsTable().findByRole('button', { name: 'The Matrix' });
            previewButton.focus();
            await user.keyboard('{Enter}');
            await screen.findByTestId('preview-drawer');
            await user.keyboard('{Escape}');

            await waitFor(() => expect(screen.queryByTestId('preview-drawer')).not.toBeInTheDocument());
            expect(previewButton).toBeInTheDocument();
            expect(previewButton).toHaveFocus();
        });

        it('applies the same retained Added state after adding from the preview drawer', async () => {
            mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            vi.mocked(apiPost).mockResolvedValueOnce(candidatePreview).mockResolvedValueOnce('drawer-matrix-id');
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });

            const previewButton = resultsTable().getByRole('button', { name: 'The Matrix' });
            previewButton.focus();
            expect(previewButton).toHaveFocus();
            await user.keyboard('{Enter}');
            expect(await screen.findByTestId('preview-drawer')).toBeInTheDocument();
            await user.click(screen.getByRole('button', { name: /^add to library$/i }));

            const openLibraryItem = await resultsTable().findByRole('link', { name: 'The Matrix' });
            expect(openLibraryItem).toHaveAttribute('href', '/library/drawer-matrix-id');
            expect(screen.queryByTestId('preview-drawer')).not.toBeInTheDocument();
            expect(screen.getByRole('tab', { name: /live filter/i })).toHaveAttribute('aria-selected', 'true');
            expect(mockNavigate).not.toHaveBeenCalledWith('/library/drawer-matrix-id');
        });

        it.each(['List', 'Gallery'])('Add_OffersFolderChoices_WhenSeveralFoldersMatchIn%sView', async (view) => {
            mockApiGetByPath({
                rootFolders: [
                    rootFolder('rf-movies-a', 'Movies A', 'movies'),
                    rootFolder('rf-movies-b', 'Movies B', 'movies'),
                ],
            });
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });

            expect(document.querySelector('[data-field="root-folder"]')).not.toBeInTheDocument();

            await user.click(screen.getByRole('tab', { name: view }));
            const addButton = screen.getByRole('button', {
                name: view === 'Gallery' ? 'Add The Matrix' : /^add$/i,
            }) as HTMLButtonElement;
            expect(addButton.disabled).toBe(false);
            await user.click(addButton);
            expect(await screen.findByRole('menuitem', { name: 'Movies A' })).toBeInTheDocument();
            expect(await screen.findByRole('menuitem', { name: 'Movies B' })).toBeInTheDocument();
        });

        it('blocks Add when the content type has no root folders configured', async () => {
            mockApiGetByPath({ rootFolders: [] });
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });

            const addButton = resultsTable().getByRole('button', { name: /^add$/i }) as HTMLButtonElement;
            expect(addButton.disabled).toBe(true);
            expect(resultsTable().queryByTestId('library-add-page__add-blocked')).not.toBeInTheDocument();
            const trigger = addButton.parentElement as HTMLElement;
            expect(trigger.tabIndex).toBe(0);
            expect(trigger).toHaveAccessibleDescription(/configure a root folder/i);

            await user.hover(addButton);
            expect(await screen.findByRole('tooltip')).toHaveTextContent(/configure a root folder/i);
        });
    });

    describe('result sorting', () => {
        const matrix = {
            providerType: 'provider-a',
            externalIdSource: 'provider-a.movie',
            externalId: '603',
            contentType: 'movies',
            title: 'The Matrix',
            year: 1999,
            overview: null,
            thumbnailUrl: null,
        };
        const alien = {
            providerType: 'provider-a',
            externalIdSource: 'provider-a.movie',
            externalId: '348',
            contentType: 'movies',
            title: 'Alien',
            year: 1979,
            overview: null,
            thumbnailUrl: null,
        };

        const undated = {
            providerType: 'provider-a',
            externalIdSource: 'provider-a.movie',
            externalId: '999',
            contentType: 'movies',
            title: 'Zodiac',
            year: null,
            overview: null,
            thumbnailUrl: null,
        };

        function renderedTitles(): string[] {
            const list = screen.getByTestId('library-add-page__result-list');
            return within(list)
                .getAllByRole('heading', { level: 3 })
                .map((h) => h.querySelector('a')?.textContent ?? h.textContent ?? '')
                .map((t) => t.replace(/\d{4}$/, '').trim());
        }

        it('defaults to relevance order without reporting an active sort', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([matrix, undated, alien]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await waitFor(() => {
                expect(resultsTable().getByText('Alien')).toBeInTheDocument();
            });

            expect(renderedTitles()).toEqual(['The Matrix', 'Zodiac', 'Alien']);
            expect(resultsTable().getByRole('columnheader', { name: /title/i })).not.toHaveAttribute('aria-sort');
        });

        it.each([390, 1280])('GallerySort_PreservesProviderOrder_UntilSelectedAtWidth%i', async (width) => {
            // Arrange
            installMatchMedia(width);
            mockCandidateSearchResponse = candidateSearchResult([matrix, undated, alien]);
            const user = userEvent.setup();
            renderLive();
            await screen.findByText('The Matrix');

            // Act
            await user.click(screen.getByRole('tab', { name: 'Gallery' }));

            // Assert
            expect(screen.getByRole('combobox', { name: 'Sort by field' })).toHaveTextContent('Sort by');
            expect(screen.getByRole('button', { name: 'Sort ascending', pressed: false })).toBeDisabled();
            expect(screen.getByRole('button', { name: 'Sort descending', pressed: false })).toBeDisabled();
            expect(
                Array.from(document.querySelectorAll('.content-gallery-tile__title'), (title) => title.textContent),
            ).toEqual(['The Matrix (1999)', 'Zodiac', 'Alien (1979)']);

            // Act
            await user.click(screen.getByRole('combobox', { name: 'Sort by field' }));
            await user.click(screen.getByRole('option', { name: 'Title' }));

            // Assert
            expect(screen.getByRole('button', { name: 'Sort ascending', pressed: true })).toBeEnabled();
            expect(
                Array.from(document.querySelectorAll('.content-gallery-tile__title'), (title) => title.textContent),
            ).toEqual(['Alien (1979)', 'The Matrix (1999)', 'Zodiac']);
        });

        it('PublishedSort_UsesSameOrder_WhenLayoutChanges 640', async () => {
            // Arrange
            installMatchMedia(640);
            mockCandidateSearchResponse = candidateSearchResult([
                alien,
                matrix,
                { ...matrix, externalId: 'same-year', title: 'Another title' },
                undated,
            ]);
            const user = userEvent.setup();
            renderLive();
            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await screen.findByText('The Matrix');

            expect(screen.getByRole('combobox', { name: 'Sort by field' })).toHaveTextContent('Sort by');
            expect(screen.getByRole('button', { name: 'Sort ascending', pressed: false })).toBeDisabled();
            expect(screen.getByRole('button', { name: 'Sort descending', pressed: false })).toBeDisabled();

            // Act
            await user.click(screen.getByRole('combobox', { name: 'Sort by field' }));
            await user.click(screen.getByRole('option', { name: 'Published' }));

            // Assert
            expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
                'The Matrix1999',
                'Another title1999',
                'Alien1979',
                'Zodiac',
            ]);

            // Act
            await user.click(screen.getByRole('button', { name: 'Sort ascending' }));

            // Assert
            expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
                'Alien1979',
                'The Matrix1999',
                'Another title1999',
                'Zodiac',
            ]);
        });

        it('PublishedSort_UsesSameOrder_WhenLayoutChanges 1024', async () => {
            // Arrange
            installMatchMedia(1024);
            mockCandidateSearchResponse = candidateSearchResult([
                alien,
                matrix,
                { ...matrix, externalId: 'same-year', title: 'Another title' },
                undated,
            ]);
            const user = userEvent.setup();
            renderLive();
            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await screen.findByText('The Matrix');

            // Act
            await user.click(screen.getByRole('button', { name: /sort by published/i }));

            // Assert
            expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
                'The Matrix1999',
                'Another title1999',
                'Alien1979',
                'Zodiac',
            ]);

            // Act
            await user.click(screen.getByRole('button', { name: 'Sort by Published' }));

            // Assert
            expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
                'Alien1979',
                'The Matrix1999',
                'Another title1999',
                'Zodiac',
            ]);
        });

        it('sorts by title only after a click and restores relevance order when cleared', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([matrix, undated, alien]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });

            await user.click(resultsTable().getByRole('button', { name: /sort by title/i }));
            await waitFor(() => {
                expect(renderedTitles()).toEqual(['Alien', 'The Matrix', 'Zodiac']);
            });

            await user.click(resultsTable().getByRole('button', { name: /sort by title/i }));
            await waitFor(() => {
                expect(renderedTitles()).toEqual(['Zodiac', 'The Matrix', 'Alien']);
            });

            await user.click(resultsTable().getByRole('button', { name: /sort by title/i }));
            await waitFor(() => {
                expect(renderedTitles()).toEqual(['The Matrix', 'Zodiac', 'Alien']);
            });
        });
    });

    describe('root folder chosen at add time', () => {
        it('sends the RootFolderId picked from the Add menu', async () => {
            mockApiGetByPath({
                rootFolders: [
                    rootFolder('rf-movies-a', 'Movies A', 'movies'),
                    rootFolder('rf-movies-b', 'Movies B', 'movies'),
                ],
            });
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            vi.mocked(apiPost).mockResolvedValue('new-content-uuid');
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });

            await user.click(resultsTable().getByRole('button', { name: /^add$/i }));
            await user.click(await screen.findByRole('menuitem', { name: 'Movies B' }));

            await waitFor(() => {
                expect(apiPost).toHaveBeenCalledWith('/content/from-external-id?rootFolderId=rf-movies-b', {
                    providerType: 'provider-a',
                    externalIdKey: 'provider-a.movie',
                    externalId: '603',
                    contentType: 'movies',
                });
            });
            const openLibraryItem = await resultsTable().findByRole('link', { name: 'The Matrix' });
            expect(openLibraryItem).toHaveAttribute('href', '/library/new-content-uuid');
            expect(mockNavigate).not.toHaveBeenCalledWith('/library/new-content-uuid');
        });

        it('does not fire the add request until a folder is chosen from the Add menu', async () => {
            mockApiGetByPath({
                rootFolders: [
                    rootFolder('rf-movies-a', 'Movies A', 'movies'),
                    rootFolder('rf-movies-b', 'Movies B', 'movies'),
                ],
            });
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            vi.mocked(apiPost).mockResolvedValue('new-content-uuid');
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });

            await user.click(resultsTable().getByRole('button', { name: /^add$/i }));
            expect(await screen.findByRole('menuitem', { name: 'Movies A' })).toBeInTheDocument();

            const addCall = vi.mocked(apiPost).mock.calls.find((c) => c[0] === '/content/from-external-id');
            expect(addCall).toBeUndefined();
        });
    });

    describe('Saved filter tab', () => {
        const sharedFilter = {
            id: 'shared-1',
            name: 'Action Hits',
            ownerId: null,
            criteriaCount: 1,
            ownerLock: false,
            query: 'genre has "Action"',
            criteria: { contentHierarchyItem: { and: [] } },
            createdAt: '2026-01-01T00:00:00Z',
            modifiedAt: null,
        };

        beforeEach(() => {
            mockUseContentFilters.mockReturnValue({
                filters: [sharedFilter],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
        });

        it('Cancel_PrecedesSearch_WhenSavedFilterSearchIsPending', async () => {
            // Arrange
            vi.mocked(apiGet).mockImplementation((path: string) => {
                if (path.startsWith('/content-search')) return new Promise(() => {});
                return Promise.resolve([]);
            });
            const user = userEvent.setup();
            renderLive('');
            await user.click(screen.getByRole('tab', { name: /saved filter/i }));
            await user.click(document.querySelector('[data-field="saved-filter"]') as HTMLElement);
            await user.click(await screen.findByRole('option', { name: 'Action Hits' }));

            // Act
            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await screen.findByTestId('library-add-page__loading');

            // Assert
            const search = screen.getByRole('button', { name: /^search$/i });
            expect(search.previousElementSibling).toBe(screen.getByRole('button', { name: 'Cancel search' }));
            expect(search.nextElementSibling).toBeNull();
        });

        it('Cancel_AllowsFreshSearch_WhenSavedFilterSearchIsPending', async () => {
            // Arrange
            let searchCallCount = 0;
            vi.mocked(apiGet).mockImplementation((path: string) => {
                if (!path.startsWith('/content-search')) return Promise.resolve([]);
                searchCallCount++;
                if (searchCallCount === 1) return new Promise(() => {});
                return Promise.resolve(candidateSearchResult([candidateSearchedCandidate]));
            });
            const user = userEvent.setup();
            renderLive('');
            await user.click(screen.getByRole('tab', { name: /saved filter/i }));
            await user.click(document.querySelector('[data-field="saved-filter"]') as HTMLElement);
            await user.click(await screen.findByRole('option', { name: 'Action Hits' }));
            await user.click(screen.getByRole('button', { name: /^search$/i }));
            await screen.findByTestId('library-add-page__loading');
            const cancelledSignal = vi
                .mocked(apiGet)
                .mock.calls.find(([path]) => path.startsWith('/content-search'))?.[1]?.signal;

            // Act
            await user.click(screen.getByRole('button', { name: 'Cancel search' }));
            await user.click(screen.getByRole('button', { name: /^search$/i }));

            // Assert
            expect(cancelledSignal?.aborted).toBe(true);
            expect(await resultsTable().findByText('The Matrix')).toBeInTheDocument();
            expect(screen.queryByTestId('library-add-page__error')).not.toBeInTheDocument();
            expect(searchCallCount).toBe(2);
        });

        it('Search_RendersReadOnlyCandidates_WhenSavedFilterRuns', async () => {
            // Arrange
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive();

            // Assert
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
            vi.mocked(apiGet).mockClear();

            // Act
            await user.click(screen.getByRole('tab', { name: /saved filter/i }));

            // Assert
            expect(document.querySelector('[data-component="filter-bar"]')).not.toBeInTheDocument();

            // Act
            const savedTrigger = document.querySelector('[data-field="saved-filter"]') as HTMLElement;
            await user.click(savedTrigger);
            await user.click(await screen.findByRole('option', { name: 'Action Hits' }));

            // Assert
            expect(apiGet).not.toHaveBeenCalledWith(expect.stringContaining('/content-search'));
            // Act
            await user.click(screen.getByRole('button', { name: /^search$/i }));

            // Assert
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=genre+has+%22Action%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
            });
            const savedCall = vi
                .mocked(apiPost)
                .mock.calls.find((c) => String(c[0]).includes('/search/saved/shared-1'));
            expect(String(savedCall?.[0])).not.toContain('contentType=');

            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
        });

        it('does not show stale saved-filter results after switching back to the Live tab', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive('genre has "Drama"');

            await user.click(screen.getByRole('tab', { name: /saved filter/i }));
            const savedTrigger = document.querySelector('[data-field="saved-filter"]') as HTMLElement;
            await user.click(savedTrigger);
            await user.click(await screen.findByRole('option', { name: 'Action Hits' }));
            await user.click(screen.getByRole('button', { name: /^search$/i }));

            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /live filter/i }));

            expect(screen.queryByText('The Matrix')).not.toBeInTheDocument();
            expect(screen.getByTestId('library-add-page__empty-initial')).toBeInTheDocument();
        });

        it('links to the filter editor on the Filters page', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('tab', { name: /saved filter/i }));
            const savedTrigger = document.querySelector('[data-field="saved-filter"]') as HTMLElement;
            await user.click(savedTrigger);
            await user.click(await screen.findByRole('option', { name: 'Action Hits' }));

            const openEditor = await screen.findByRole('link', { name: 'Edit filter' });

            expect(openEditor).toHaveAttribute('href', '/filters?edit=shared-1');
        });

        it('Panel_GroupsPickerEditQueryAndSearch_WhenSavedFilterIsSelected', async () => {
            // Arrange
            mockApiGetByPath({});
            const user = userEvent.setup();

            renderLive();

            // Act
            await user.click(screen.getByRole('tab', { name: /saved filter/i }));
            await user.click(document.querySelector('[data-field="saved-filter"]') as HTMLElement);
            await user.click(await screen.findByRole('option', { name: 'Action Hits' }));

            // Assert
            const panel = within(screen.getByRole('region', { name: 'Saved filter controls' }));
            expect(panel.getByLabelText('Saved filter')).toBeInTheDocument();
            expect(panel.getByRole('link', { name: 'Edit filter' })).toHaveAttribute('href', '/filters?edit=shared-1');
            expect(panel.getByTestId('library-add-page__saved-query')).toHaveTextContent('genre has "Action"');
            expect(panel.getByRole('button', { name: /^search$/i })).toBeInTheDocument();
        });

        it('InitialEmptyState_ShowsHeadingOnly_WhenSavedFilterTabIsOpen', async () => {
            // Arrange
            mockApiGetByPath({});
            const user = userEvent.setup();

            renderLive();

            // Act
            await user.click(screen.getByRole('tab', { name: /saved filter/i }));

            // Assert
            const empty = screen.getByTestId('library-add-page__empty-initial');
            expect(within(empty).getByText('Use a saved filter')).toBeInTheDocument();
            expect(empty.querySelector('.empty-state__description')).not.toBeInTheDocument();
        });

        it('QueryStrip_ShowsQueryWithoutLabel_WhenSavedFilterIsSelected', async () => {
            // Arrange
            mockApiGetByPath({});
            const user = userEvent.setup();

            renderLive();

            // Act
            await user.click(screen.getByRole('tab', { name: /saved filter/i }));
            await user.click(document.querySelector('[data-field="saved-filter"]') as HTMLElement);
            await user.click(await screen.findByRole('option', { name: 'Action Hits' }));

            // Assert
            const panel = within(screen.getByRole('region', { name: 'Saved filter controls' }));
            expect(panel.getByTestId('library-add-page__saved-query')).toHaveTextContent('genre has "Action"');
            expect(panel.queryByText('Query:')).not.toBeInTheDocument();
        });

        it('Search_SitsInFilterPanel_WhenLiveFilterIsShown', () => {
            // Arrange
            mockApiGetByPath({});

            // Act
            renderLive('genre has "Action"');

            // Assert
            const panel = within(screen.getByRole('region', { name: 'Filter options' }));
            expect(panel.getByRole('button', { name: /^search$/i })).toBeInTheDocument();
        });

        it('Search_EnablesButton_WhenSavedFilterIsSelected', async () => {
            // Arrange
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            renderLive();

            // Assert
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
            vi.mocked(apiGet).mockClear();

            // Act
            await user.click(screen.getByRole('tab', { name: /saved filter/i }));

            // Assert
            const initialSearch = screen.getByRole('button', { name: /^search$/i }) as HTMLButtonElement;
            expect(initialSearch).toBeInTheDocument();
            expect(initialSearch.disabled).toBe(true);

            // Act
            const savedTrigger = document.querySelector('[data-field="saved-filter"]') as HTMLElement;
            await user.click(savedTrigger);
            await user.click(await screen.findByRole('option', { name: 'Action Hits' }));

            // Assert
            expect((screen.getByRole('button', { name: /^search$/i }) as HTMLButtonElement).disabled).toBe(false);
            expect(apiGet).not.toHaveBeenCalledWith(expect.stringContaining('/content-search'));

            // Act
            await user.click(screen.getByRole('button', { name: /^search$/i }));
            // Assert
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=genre+has+%22Action%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
            });
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
        });

        it('offers a "create a wishlist from this filter" CTA on no results that seeds the criteria', async () => {
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([]);
            const user = userEvent.setup();

            renderLive();

            await user.click(screen.getByRole('tab', { name: /saved filter/i }));
            const savedTrigger = document.querySelector('[data-field="saved-filter"]') as HTMLElement;
            await user.click(savedTrigger);
            await user.click(await screen.findByRole('option', { name: 'Action Hits' }));
            await user.click(screen.getByRole('button', { name: /^search$/i }));

            await waitFor(() => {
                expect(screen.getByTestId('library-add-page__empty-no-results')).toBeInTheDocument();
            });

            const cta = document.querySelector('[data-action="create-wishlist-from-filter"]') as HTMLElement;
            expect(cta).toBeInTheDocument();
            await user.click(cta);

            expect(mockNavigate).toHaveBeenCalledWith(
                '/wishlists',
                expect.objectContaining({
                    state: expect.objectContaining({
                        createCriteria: sharedFilter.criteria,
                        createName: 'Action Hits',
                    }),
                }),
            );
        });
    });

    describe('preload deep-link', () => {
        function renderWithState(state: Record<string, unknown>, reactStrictMode = false) {
            return render(
                <QueryClientProvider client={createTestQueryClient()}>
                    <MemoryRouter initialEntries={[{ pathname: '/library/add', state }]}>
                        <ToastStateProvider>
                            <TooltipProvider>
                                <ToastProvider>
                                    <LibraryAddPage />
                                    <ToastViewport />
                                </ToastProvider>
                            </TooltipProvider>
                        </ToastStateProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
                { reactStrictMode },
            );
        }

        it('Preload_StartsLiveSearch_WhenStrictModeReplaysEffects', async () => {
            // Arrange
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);

            // Act
            renderWithState({ filterQuery: 'contentType = "shows"' }, true);

            // Assert
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /live filter/i })).toHaveAttribute('aria-selected', 'true');
            });
            expect(document.querySelector('[data-component="filter-bar"]')).toBeInTheDocument();

            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=contentType+%3D+%22shows%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
            });
            expect(
                vi.mocked(apiGet).mock.calls.find(([path]) => path.startsWith('/content-search'))?.[1]?.signal?.aborted,
            ).toBe(false);
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
        });

        it('Preload_StartsAFreshSearch_WhenANewPreloadArrivesWhileThePageIsOpen', async () => {
            // Arrange
            let searchCallCount = 0;
            vi.mocked(apiGet).mockImplementation((path: string) => {
                if (!path.startsWith('/content-search')) return Promise.resolve([]);
                searchCallCount++;
                return Promise.resolve({
                    ...candidateSearchResult([candidateSearchedCandidate], {
                        hasMore: searchCallCount === 1,
                        requestedPage: searchCallCount === 2 ? 2 : 1,
                    }),
                    continuation: searchCallCount === 2 ? 'second-page' : 'first-page',
                    nextContinuation: searchCallCount === 1 ? 'second-page' : null,
                });
            });
            const router = createMemoryRouter([{ path: '/library/add', element: <LibraryAddPage /> }], {
                initialEntries: [{ pathname: '/library/add', state: { filterQuery: 'title contains "dark"' } }],
            });
            const user = userEvent.setup();
            render(
                <QueryClientProvider client={createTestQueryClient()}>
                    <ToastStateProvider>
                        <TooltipProvider>
                            <ToastProvider>
                                <RouterProvider router={router} />
                                <ToastViewport />
                            </ToastProvider>
                        </TooltipProvider>
                    </ToastStateProvider>
                </QueryClientProvider>,
            );
            await waitFor(() => expect(resultsTable().getByText('The Matrix')).toBeInTheDocument());
            await user.click(screen.getByRole('button', { name: /next page/i }));
            await waitFor(() => expect(searchCallCount).toBe(2));

            // Act
            await act(() => router.navigate('/library/add', { state: { filterQuery: 'title contains "matrix"' } }));

            // Assert
            await waitFor(() => expect(searchCallCount).toBe(3));
            expect(
                vi
                    .mocked(apiGet)
                    .mock.calls.filter(([path]) => path.startsWith('/content-search'))
                    .map(([path]) => path),
            ).toEqual([
                '/content-search?query=title+contains+%22dark%22&page=1',
                '/content-search?query=title+contains+%22dark%22&page=2&continuation=second-page',
                '/content-search?query=title+contains+%22matrix%22&page=1',
            ]);
            expect(await screen.findByText('Page 1')).toBeInTheDocument();
        });

        it('Search_UsesPrefilledContentType_WhenLiveDeepLinkIsSubmitted', async () => {
            // Arrange
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
            const user = userEvent.setup();

            // Act
            render(
                <QueryClientProvider client={createTestQueryClient()}>
                    <MemoryRouter initialEntries={['/library/add?contentType=movies']}>
                        <ToastStateProvider>
                            <TooltipProvider>
                                <ToastProvider>
                                    <LibraryAddPage />
                                    <ToastViewport />
                                </ToastProvider>
                            </TooltipProvider>
                        </ToastStateProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
            );

            // Assert
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /live filter/i })).toHaveAttribute('aria-selected', 'true');
            });
            expect(apiGet).not.toHaveBeenCalledWith(expect.stringContaining('/content-search'));
            expect(screen.getByTestId('library-add-page__empty-initial')).toBeInTheDocument();

            // Act
            await user.click(screen.getByRole('button', { name: /^search$/i }));
            // Assert
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=contentType+%3D+%22movies%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
            });
            expect(mockParseMagpieQl).not.toHaveBeenCalled();
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
        });

        it('Preload_StartsOwnedSavedSearch_WhenStrictModeReplaysEffects', async () => {
            // Arrange
            mockUseContentFilters.mockReturnValue({
                filters: [
                    {
                        id: 'my-1',
                        name: 'My Action',
                        ownerId: 'user-1',
                        criteriaCount: 1,
                        ownerLock: false,
                        query: 'genre has "Action"',
                        criteria: { contentHierarchyItem: { and: [{}] } },
                        createdAt: '2026-01-01T00:00:00Z',
                        modifiedAt: null,
                    },
                ],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);

            // Act
            renderWithState({ filterQuery: 'genre has "Action"', savedFilterId: 'my-1' }, true);

            // Assert
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /saved filter/i })).toHaveAttribute('aria-selected', 'true');
            });
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=genre+has+%22Action%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
            });
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
        });

        it('Preload_WaitsForFormattedQuery_WhenOwnedFilterDeepLinkResolves', async () => {
            // Arrange
            const ownedUnresolved = {
                id: 'my-1',
                name: 'My Action',
                ownerId: 'user-1',
                criteriaCount: 1,
                ownerLock: false,
                query: '',
                criteria: { contentHierarchyItem: { and: [{}] } },
                createdAt: '2026-01-01T00:00:00Z',
                modifiedAt: null,
            };
            mockUseContentFilters.mockReturnValue({
                filters: [ownedUnresolved],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);

            const ui = (
                <QueryClientProvider client={createTestQueryClient()}>
                    <MemoryRouter
                        initialEntries={[
                            {
                                pathname: '/library/add',
                                state: { filterQuery: 'genre has "Action"', savedFilterId: 'my-1' },
                            },
                        ]}
                    >
                        <ToastStateProvider>
                            <TooltipProvider>
                                <ToastProvider>
                                    <LibraryAddPage />
                                    <ToastViewport />
                                </ToastProvider>
                            </TooltipProvider>
                        </ToastStateProvider>
                    </MemoryRouter>
                </QueryClientProvider>
            );
            // Act
            const { rerender } = render(ui);

            // Assert
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /saved filter/i })).toHaveAttribute('aria-selected', 'true');
            });
            expect(mockParseMagpieQl).not.toHaveBeenCalledWith('', 'content');
            expect(apiGet).not.toHaveBeenCalledWith(expect.stringContaining('/content-search'));
            expect(screen.queryByTestId('library-add-page__error')).not.toBeInTheDocument();

            // Act
            mockUseContentFilters.mockReturnValue({
                filters: [{ ...ownedUnresolved, query: 'genre has "Action"' }],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            rerender(ui);

            // Assert
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=genre+has+%22Action%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
            });
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
            expect(mockParseMagpieQl).not.toHaveBeenCalledWith('', 'content');
        });

        it('Preload_SelectsSavedTab_WhenSavedFilterListLoads', async () => {
            // Arrange
            mockUseContentFilters.mockReturnValue({
                filters: [],
                isLoading: true,
                error: null,
                refetch: vi.fn(),
            });
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);

            const ui = (
                <QueryClientProvider client={createTestQueryClient()}>
                    <MemoryRouter
                        initialEntries={[
                            {
                                pathname: '/library/add',
                                state: { filterQuery: 'genre has "Action"', savedFilterId: 'shared-1' },
                            },
                        ]}
                    >
                        <ToastStateProvider>
                            <TooltipProvider>
                                <ToastProvider>
                                    <LibraryAddPage />
                                    <ToastViewport />
                                </ToastProvider>
                            </TooltipProvider>
                        </ToastStateProvider>
                    </MemoryRouter>
                </QueryClientProvider>
            );
            // Act
            const { rerender } = render(ui);

            // Assert
            expect(apiPost).not.toHaveBeenCalled();

            // Act
            mockUseContentFilters.mockReturnValue({
                filters: [
                    {
                        id: 'shared-1',
                        name: 'Action Hits',
                        ownerId: null,
                        criteriaCount: 1,
                        ownerLock: false,
                        query: 'genre has "Action"',
                        criteria: { contentHierarchyItem: { and: [{}] } },
                        createdAt: '2026-01-01T00:00:00Z',
                        modifiedAt: null,
                    },
                ],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            rerender(ui);

            // Assert
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /saved filter/i })).toHaveAttribute('aria-selected', 'true');
            });
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=genre+has+%22Action%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
            });
        });

        it('Preload_StartsSharedSavedSearch_WhenStrictModeReplaysEffects', async () => {
            // Arrange
            mockUseContentFilters.mockReturnValue({
                filters: [
                    {
                        id: 'shared-1',
                        name: 'Action Hits',
                        ownerId: null,
                        criteriaCount: 1,
                        ownerLock: false,
                        query: 'genre has "Action"',
                        criteria: { contentHierarchyItem: { and: [] } },
                        createdAt: '2026-01-01T00:00:00Z',
                        modifiedAt: null,
                    },
                ],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            mockApiGetByPath({});
            mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);

            // Act
            renderWithState({ filterQuery: 'genre has "Action"', savedFilterId: 'shared-1' }, true);

            // Assert
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /saved filter/i })).toHaveAttribute('aria-selected', 'true');
            });
            expect(document.querySelector('[data-component="filter-bar"]')).not.toBeInTheDocument();
            await waitFor(() => {
                expect(apiGet).toHaveBeenCalledWith(
                    '/content-search?query=genre+has+%22Action%22&page=1',
                    expect.objectContaining({ signal: expect.any(AbortSignal) }),
                );
            });
            await waitFor(() => {
                expect(resultsTable().getByText('The Matrix')).toBeInTheDocument();
            });
        });
    });
    it('Search_RestoresResultPages_WhenOpeningAUrlAndUsingBackAndForward', async () => {
        // Arrange
        mockApiGetByPath({});
        mockCandidateSearchResponse = {
            ...candidateSearchResult([{ ...candidateSearchedCandidate, title: 'Second page' }], { requestedPage: 2 }),
            continuation: 'second-page',
        };
        const router = createMemoryRouter([{ path: '/library/add', element: <LibraryAddPage /> }], {
            initialEntries: [
                '/library/add?filter=genre+has+%22Action%22&query=genre+has+%22Action%22&continuation=first-page&view=gallery',
                '/library/add?filter=genre+has+%22Action%22&query=genre+has+%22Action%22&continuation=second-page&page=2&sort=title:desc&view=gallery',
            ],
        });
        render(
            <QueryClientProvider client={createTestQueryClient()}>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            <RouterProvider router={router} />
                        </ToastProvider>
                    </TooltipProvider>
                </ToastStateProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(await screen.findByText('Second page')).toBeInTheDocument();
        expect(screen.getByText('Page 2')).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: 'Gallery' })).toHaveAttribute('aria-selected', 'true');
        expect(apiGet).toHaveBeenCalledWith(
            '/content-search?query=genre+has+%22Action%22&page=2&continuation=second-page',
            expect.objectContaining({ signal: expect.any(AbortSignal) }),
        );

        // Act
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate], { hasMore: true }),
            continuation: 'first-page',
            nextContinuation: 'second-page',
        };
        await act(() => router.navigate(-1));

        // Assert
        expect(await screen.findByText('The Matrix')).toBeInTheDocument();
        expect(screen.getByText('Page 1')).toBeInTheDocument();
        expect(router.state.location.search).not.toContain('page=2');

        // Act
        mockCandidateSearchResponse = {
            ...candidateSearchResult([{ ...candidateSearchedCandidate, title: 'Second page' }], { requestedPage: 2 }),
            continuation: 'second-page',
        };
        await act(() => router.navigate(1));

        // Assert
        expect(await screen.findByText('Second page')).toBeInTheDocument();
        expect(screen.getByText('Page 2')).toBeInTheDocument();
        expect(router.state.location.search).toContain('sort=title:desc');
        expect(vi.mocked(apiGet).mock.calls.filter(([url]) => url.startsWith('/content-search'))).toHaveLength(3);
    });

    it('Sorting_KeepsTheCurrentBatchAndSelection_WhenChangedOnTheSecondPage', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = {
            ...candidateSearchResult(
                [candidateSearchedCandidate, { ...candidateSearchedCandidate, externalId: '604', title: 'Reloaded' }],
                { requestedPage: 2 },
            ),
            continuation: 'second-page',
        };
        const user = userEvent.setup();
        const router = createMemoryRouter([{ path: '/library/add', element: <LibraryAddPage /> }], {
            initialEntries: ['/library/add?query=sample&page=2&continuation=second-page&view=list'],
        });
        render(
            <QueryClientProvider client={createTestQueryClient()}>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            <RouterProvider router={router} />
                        </ToastProvider>
                    </TooltipProvider>
                </ToastStateProvider>
            </QueryClientProvider>,
        );
        await screen.findByText('The Matrix');
        await user.click(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' }));

        // Act
        await user.click(resultsTable().getByRole('button', { name: /sort by title/i }));

        // Assert
        expect(new URLSearchParams(router.state.location.search).get('page')).toBe('2');
        expect(new URLSearchParams(router.state.location.search).get('continuation')).toBe('second-page');
        expect(resultsTable().getByRole('checkbox', { name: 'Select The Matrix' })).toBeChecked();
        expect(resultsTable().getAllByRole('row')[1]).toHaveTextContent('Reloaded');
        expect(vi.mocked(apiGet).mock.calls.filter(([url]) => url.startsWith('/content-search'))).toHaveLength(1);
    });

    it('Gallery_ShowsAnInLibraryStatusWithoutPreviewControls_WhenTheExistingIdIsMissing', async () => {
        // Arrange
        mockApiGetByPath({});
        mockCandidateSearchResponse = candidateSearchResult([{ ...candidateSearchedCandidate, inLibrary: true }]);
        const user = userEvent.setup();
        renderLive();
        await screen.findByText('The Matrix');

        // Act
        await user.click(screen.getByRole('tab', { name: 'Gallery' }));

        // Assert
        expect(screen.getByRole('button', { name: 'The Matrix (in library)' })).toBeDisabled();
        expect(screen.queryByRole('button', { name: /Preview The Matrix/ })).not.toBeInTheDocument();
        expect(screen.queryByRole('link', { name: /The Matrix/ })).not.toBeInTheDocument();
    });

    it('Search_RecordsTheSubmittedQueryAndCheckpoint_WhenAUrlFilterIsApplied', async () => {
        // Arrange
        mockApiGetByPath({});
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate]),
            continuation: 'first-page',
        };
        const user = userEvent.setup();
        const router = createMemoryRouter([{ path: '/library/add', element: <LibraryAddPage /> }], {
            initialEntries: ['/library/add?filter=genre+has+%22Action%22&view=list'],
        });
        render(
            <QueryClientProvider client={createTestQueryClient()}>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            <RouterProvider router={router} />
                        </ToastProvider>
                    </TooltipProvider>
                </ToastStateProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(screen.getByTestId('library-add-page__empty-initial')).toBeInTheDocument();
        expect(vi.mocked(apiGet).mock.calls.filter(([url]) => url.startsWith('/content-search'))).toHaveLength(0);

        // Act
        await user.click(screen.getByRole('button', { name: /^search$/i }));

        // Assert
        expect(await screen.findByText('The Matrix')).toBeInTheDocument();
        await waitFor(() =>
            expect(new URLSearchParams(router.state.location.search).get('continuation')).toBe('first-page'),
        );
        expect(new URLSearchParams(router.state.location.search).get('query')).toBe('genre has "Action"');
        expect(new URLSearchParams(router.state.location.search).get('view')).toBe('list');
        expect(vi.mocked(apiGet).mock.calls.filter(([url]) => url.startsWith('/content-search'))).toHaveLength(1);

        // Act
        await act(() => router.navigate(-1));

        // Assert
        expect(await screen.findByTestId('library-add-page__empty-initial')).toBeInTheDocument();
        expect(new URLSearchParams(router.state.location.search).get('filter')).toBe('genre has "Action"');
        expect(new URLSearchParams(router.state.location.search).has('query')).toBe(false);
    });
    it('Search_RetriesTheSameCheckpoint_WhenRetryIsClickedAfterFailure', async () => {
        // Arrange
        mockCandidateSearchError = new Error('offline');
        const user = userEvent.setup();
        renderLive();
        await screen.findByTestId('library-add-page__error');
        mockCandidateSearchError = undefined;
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);

        // Act
        await user.click(screen.getByRole('button', { name: /retry/i }));

        // Assert
        expect(await screen.findByText('The Matrix')).toBeInTheDocument();
        expect(vi.mocked(apiGet).mock.calls.filter(([url]) => url.startsWith('/content-search'))).toHaveLength(2);
    });

    it('SavedFilter_ClearsPreviousQueryPageAndContinuation_WhenAnotherFilterIsSelected', async () => {
        // Arrange
        mockApiGetByPath({});
        mockUseContentFilters.mockReturnValue({
            filters: [
                {
                    id: 'shared-1',
                    name: 'Action Hits',
                    ownerId: null,
                    criteriaCount: 1,
                    ownerLock: false,
                    query: 'genre has "Action"',
                    criteria: { contentHierarchyItem: { and: [] } },
                    createdAt: '2026-01-01T00:00:00Z',
                    modifiedAt: null,
                },
                {
                    id: 'shared-2',
                    name: 'Drama Picks',
                    ownerId: null,
                    criteriaCount: 1,
                    ownerLock: false,
                    query: 'genre has "Drama"',
                    criteria: { contentHierarchyItem: { and: [] } },
                    createdAt: '2026-01-01T00:00:00Z',
                    modifiedAt: null,
                },
            ],
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate], { requestedPage: 2, hasMore: true }),
            continuation: 'page-2-token',
        };
        const user = userEvent.setup();
        const router = createMemoryRouter([{ path: '/library/add', element: <LibraryAddPage /> }], {
            initialEntries: [
                '/library/add?tab=saved&savedFilter=shared-1&query=genre+has+%22Action%22&page=2&continuation=page-2-token',
            ],
        });
        render(
            <QueryClientProvider client={createTestQueryClient()}>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            <RouterProvider router={router} />
                        </ToastProvider>
                    </TooltipProvider>
                </ToastStateProvider>
            </QueryClientProvider>,
        );
        await screen.findByText('The Matrix');

        // Act
        await user.click(document.querySelector('[data-field="saved-filter"]') as HTMLElement);
        await user.click(await screen.findByRole('option', { name: 'Drama Picks' }));

        // Assert
        const params = new URLSearchParams(router.state.location.search);
        expect(params.get('savedFilter')).toBe('shared-2');
        expect(params.has('query')).toBe(false);
        expect(params.has('page')).toBe(false);
        expect(params.has('continuation')).toBe(false);
        expect(screen.getByTestId('library-add-page__empty-initial')).toBeInTheDocument();
    });

    it('RowAdd_MovesFocusToOpenLibraryItemControl_WhenInlineAddSucceeds', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('rf-movies', 'Movies', 'movies')] });
        mockCandidateSearchResponse = candidateSearchResult([candidateSearchedCandidate]);
        vi.mocked(apiPost).mockResolvedValue('new-matrix-id');
        const user = userEvent.setup();
        renderLive();
        await user.click(screen.getByRole('button', { name: /^search$/i }));
        await screen.findByText('The Matrix');
        const addButton = resultsTable().getByRole('button', { name: /^add$/i });

        // Act
        addButton.focus();
        await user.keyboard('{Enter}');

        // Assert
        const openLibraryItem = await resultsTable().findByRole('link', { name: 'The Matrix' });
        await waitFor(() => expect(openLibraryItem).toHaveFocus());
    });

    it('Selection_Clears_WhenBackRestoresAnotherSearchOnTheSamePage', async () => {
        // Arrange
        mockApiGetByPath({ rootFolders: [rootFolder('movie-root', 'Movies', 'movies')] });
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate]),
            continuation: 'second-search',
        };
        const user = userEvent.setup();
        const router = createMemoryRouter([{ path: '/library/add', element: <LibraryAddPage /> }], {
            initialEntries: [
                '/library/add?filter=genre+has+%22Action%22&query=genre+has+%22Action%22&continuation=first-search&view=gallery',
                '/library/add?filter=title+contains+%22Matrix%22&query=title+contains+%22Matrix%22&continuation=second-search&view=gallery',
            ],
        });
        render(
            <QueryClientProvider client={createTestQueryClient()}>
                <ToastStateProvider>
                    <TooltipProvider>
                        <ToastProvider>
                            <RouterProvider router={router} />
                        </ToastProvider>
                    </TooltipProvider>
                </ToastStateProvider>
            </QueryClientProvider>,
        );
        await screen.findByText('The Matrix');
        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(screen.getByRole('button', { name: 'Select The Matrix' }));
        expect(screen.getAllByText('1 selected').length).toBeGreaterThan(0);

        // Act
        mockCandidateSearchResponse = {
            ...candidateSearchResult([candidateSearchedCandidate]),
            continuation: 'first-search',
        };
        await act(() => router.navigate(-1));

        // Assert
        expect(await screen.findByText('The Matrix')).toBeInTheDocument();
        expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
        expect(new URLSearchParams(router.state.location.search).get('query')).toBe('genre has "Action"');
    });
});
