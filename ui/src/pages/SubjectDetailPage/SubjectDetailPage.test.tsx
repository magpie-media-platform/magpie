import { fireEvent, render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BinocularsIcon, FunnelIcon } from '@phosphor-icons/react';

const { mockCurrentRole } = vi.hoisted(() => ({
    mockCurrentRole: { current: 3 },
}));

vi.mock('@/auth', async () => {
    const authTypes = await vi.importActual<typeof import('@/auth/types')>('@/auth/types');
    const { TEST_PERMISSION_CATALOG } = await import('@/test/permissions');
    return {
        ...authTypes,
        useAuth: () => ({
            isLoading: false,
            isAuthenticated: true,
            user: { id: 'test-user', username: 'testuser', role: mockCurrentRole.current },
            hasPermission: (permission: import('@/auth/types').Permission) => {
                const required = TEST_PERMISSION_CATALOG[permission];
                return required !== undefined && mockCurrentRole.current >= required;
            },
            getRequiredRole: (permission: import('@/auth/types').Permission) => TEST_PERMISSION_CATALOG[permission],
            hasRole: (minRole: number) => mockCurrentRole.current >= minRole,
            signIn: vi.fn(),
            signOut: vi.fn(),
            login: vi.fn(),
        }),
    };
});

import { SubjectDetailPage } from './SubjectDetailPage';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPatch: vi.fn(),
        apiDelete: vi.fn(),
        apiPost: vi.fn(),
    };
});

import { ApiError, apiGet, apiPost } from '@/lib/api';
import { Role } from '@/auth';
import { ToastStateProvider } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { LiveAnnouncerProvider } from '@/components/ui/LiveAnnouncer';
import type { ApiSubject } from '@/hooks/useSubjects';
import { STANDARD_METADATA_FIELDS } from '@/hooks/useLibraryTypes';
import { isSimpleQuery } from '@/lib/simpleFilterQuery';

const mockApiGet = vi.mocked(apiGet);
const mockApiPost = vi.mocked(apiPost);
const MISSING_DETAIL_ID = '00000000-0000-0000-0000-000000000000';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockSubjectTypes = vi.fn();

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();
    return {
        ...original,
        useLibraryTypes: () => ({ subjectTypes: mockSubjectTypes(), contentTypes: [] }),
    };
});

const mockUseSubjectInspector = vi.fn();
const mockSubjectInspectorRefetch = vi.fn();
vi.mock('@/hooks/useSubjectMetadataInspector', () => ({
    useSubjectMetadataInspector: (id: string | undefined) => mockUseSubjectInspector(id),
    usePatchSubjectMetadataInspector: () => ({ mutate: vi.fn(), isPending: false }),
    useReorderSubjectMetadataContributions: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const mockApiSubject: ApiSubject = {
    id: 'subject-1',
    metadataContainerId: 'mc-1',
    kind: 'person',
    dateLastRefreshed: '2024-01-15T00:00:00Z',
    createdAt: '2024-01-01T00:00:00Z',
    metadataContainer: {
        id: 'mc-1',
        createdAt: '2024-01-01T00:00:00Z',
        contributions: [
            {
                id: 'contrib-1',
                ordinal: 0,
                locked: false,
                providerType: 'Provider A',
                source: 'Provider A',
                title: 'Breaking Bad',
                overview:
                    "A chemistry teacher diagnosed with inoperable lung cancer turns to manufacturing and selling methamphetamine with a former student in order to secure his family's future.",
                language: 'English',
                publishDate: '2008-01-20T00:00:00Z',
                productionStatus: 'Ended',
                genres: [
                    { id: 'g1', name: 'Drama', ordinal: 0 },
                    { id: 'g2', name: 'Crime', ordinal: 1 },
                    { id: 'g3', name: 'Thriller', ordinal: 2 },
                ],
                contentRatings: [{ id: 'cr1', body: 'MPAA', rating: 'TV-MA', ordinal: 0 }],
                externalIds: [
                    { id: 'eid1', value: 'tt0903747', source: 'Provider H', ordinal: 0 },
                    { id: 'eid2', value: '1396', source: 'Provider A', ordinal: 1 },
                    { id: 'eid3', value: '81189', source: 'PROVIDER-J', ordinal: 2 },
                ],
                awards: [
                    {
                        id: 'a1',
                        body: 'Emmy Awards',
                        name: 'Outstanding Drama Series',
                        year: 2013,
                        status: 'Won',
                        ordinal: 0,
                    },
                    {
                        id: 'a2',
                        body: 'Emmy Awards',
                        name: 'Outstanding Drama Series',
                        year: 2014,
                        status: 'Won',
                        ordinal: 1,
                    },
                ],
                images: [
                    {
                        type: 'poster',
                        url: 'https://image.provider-a.example/t/p/w300/poster1.jpg',
                        width: 300,
                        height: 450,
                    },
                    {
                        type: 'backdrop',
                        url: 'https://image.provider-a.example/t/p/w780/backdrop1.jpg',
                        width: 780,
                        height: 439,
                    },
                    {
                        type: 'poster',
                        url: 'https://image.provider-a.example/t/p/w300/poster2.jpg',
                        width: 300,
                        height: 450,
                    },
                    {
                        type: 'backdrop',
                        url: 'https://image.provider-a.example/t/p/w780/backdrop2.jpg',
                        width: 780,
                        height: 439,
                    },
                ],
                relationships: [
                    { id: 'rel-1', kind: 'Actor', qualifier: 'Walter White', ordinal: 0 },
                    { id: 'rel-2', kind: 'Actor', qualifier: 'Jesse Pinkman', ordinal: 1 },
                    { id: 'rel-3', kind: 'Creator', qualifier: null, ordinal: 2 },
                ],
                references: [
                    {
                        ordinal: 0,
                        kind: 'database',
                        source: 'IMDb',
                        url: 'https://www.imdb.com/name/walt',
                        displayName: 'IMDb profile',
                    },
                ],
            },
        ],
    },
};

const defaultMaterializedSubject = {
    containerId: 'mc-1',
    subjectId: 'subject-1',
    title: 'Breaking Bad',
    overview:
        "A chemistry teacher diagnosed with inoperable lung cancer turns to manufacturing and selling methamphetamine with a former student in order to secure his family's future.",
    publishDate: '2008-01-20T00:00:00Z',
    language: 'English',
    productionStatus: 'Ended',
    genres: [
        { id: 'g1', name: 'Drama', ordinal: 0 },
        { id: 'g2', name: 'Crime', ordinal: 1 },
        { id: 'g3', name: 'Thriller', ordinal: 2 },
    ],
    contentRatings: [{ id: 'cr1', body: 'MPAA', rating: 'TV-MA', ordinal: 0 }],
    externalIds: [
        { id: 'eid1', value: 'tt0903747', source: 'Provider H', ordinal: 0 },
        { id: 'eid2', value: '1396', source: 'Provider A', ordinal: 1 },
        { id: 'eid3', value: '81189', source: 'PROVIDER-J', ordinal: 2 },
        { id: 'eid4', value: 'technical-id', source: 'internal.magpie', ordinal: 3 },
    ],
    awards: [
        {
            id: 'a1',
            body: 'Emmy Awards',
            name: 'Outstanding Drama Series',
            year: 2013,
            status: 'Won',
            ordinal: 0,
        },
        {
            id: 'a2',
            body: 'Emmy Awards',
            name: 'Outstanding Drama Series',
            year: 2014,
            status: 'Won',
            ordinal: 1,
        },
    ],
    images: [
        {
            id: 'image-1',
            type: 'poster',
            url: 'https://image.provider-a.example/t/p/w300/poster1.jpg',
            width: 300,
            height: 450,
            ordinal: 0,
        },
        {
            id: 'image-2',
            type: 'backdrop',
            url: 'https://image.provider-a.example/t/p/w780/backdrop1.jpg',
            width: 780,
            height: 439,
            ordinal: 1,
        },
        {
            id: 'image-3',
            type: 'poster',
            url: 'https://image.provider-a.example/t/p/w300/poster2.jpg',
            width: 300,
            height: 450,
            ordinal: 2,
        },
        {
            id: 'image-4',
            type: 'backdrop',
            url: 'https://image.provider-a.example/t/p/w780/backdrop2.jpg',
            width: 780,
            height: 439,
            ordinal: 3,
        },
    ],
    relationships: [
        { id: 'rel-1', kind: 'Actor', qualifier: 'Walter White', ordinal: 0 },
        { id: 'rel-2', kind: 'Actor', qualifier: 'Jesse Pinkman', ordinal: 1 },
        { id: 'rel-3', kind: 'Creator', qualifier: null, ordinal: 2 },
    ],
    references: [
        {
            id: 'reference-1',
            ordinal: 0,
            kind: 'database',
            source: 'IMDb',
            url: 'https://www.imdb.com/name/walt',
            displayName: 'IMDb profile',
        },
    ],
};
const mockMaterializedSubject = { current: defaultMaterializedSubject as Record<string, unknown> };

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                refetchInterval: false,
                staleTime: Infinity,
            },
        },
    });
}

function SubjectDetailRoute() {
    const location = useLocation();

    return (
        <>
            <SubjectDetailPage />
            <output data-testid="subject-detail-page__location-search">{location.search}</output>
        </>
    );
}

function renderWithRouter(subjectId: string, locationState?: Record<string, unknown>, search?: string) {
    const queryClient = createTestQueryClient();
    const pathname = `/subjects/${subjectId}`;
    const initialEntry = search ? { pathname, search, state: locationState } : { pathname, state: locationState };

    return render(
        <QueryClientProvider client={queryClient}>
            <TooltipProvider>
                <ToastStateProvider>
                    <LiveAnnouncerProvider>
                        <MemoryRouter initialEntries={[initialEntry]}>
                            <Routes>
                                <Route path="/subjects/:subjectId" element={<SubjectDetailRoute />} />
                            </Routes>
                        </MemoryRouter>
                    </LiveAnnouncerProvider>
                </ToastStateProvider>
            </TooltipProvider>
        </QueryClientProvider>,
    );
}

describe('SubjectDetailPage', () => {
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

    beforeEach(() => {
        vi.clearAllMocks();
        mockCurrentRole.current = Role.Admin;
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockSubjectTypes.mockReturnValue([
            {
                slug: 'person',
                name: 'Person',
                pluralName: 'People',
                levels: [],
                standardMetadataFields: STANDARD_METADATA_FIELDS.filter((field) =>
                    [
                        'Language',
                        'ProductionStatus',
                        'BirthDate',
                        'DeathDate',
                        'Established',
                        'Dissolved',
                        'Genres',
                        'Previews',
                    ].includes(field.field),
                ),
                customMetadataFields: [],
            },
        ]);
        mockMaterializedSubject.current = defaultMaterializedSubject;
        const inspectorResult = {
            data: {
                get materialized() {
                    return mockMaterializedSubject.current;
                },
                contributions: [
                    {
                        id: 'contrib-1',
                        providerType: 'Provider A',
                        ordinal: 0,
                        source: 'Provider A',
                        dateLastRefreshed: null,
                        title: 'Breaking Bad',
                    },
                ],
                subjects: {},
            },
            isLoading: false,
            error: null,
            refetch: mockSubjectInspectorRefetch,
        };
        mockUseSubjectInspector.mockImplementation((id: string | undefined) =>
            id === 'subject-1'
                ? inspectorResult
                : {
                      data: undefined,
                      isLoading: false,
                      error: new ApiError(404, 'Not found'),
                      refetch: mockSubjectInspectorRefetch,
                  },
        );

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/subjects/subject-1') {
                return Promise.resolve(mockApiSubject);
            }
            if (url.startsWith('/content')) {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 25, totalItems: 0 });
            }
            return Promise.reject({ status: 404, message: 'Not found' });
        });
    });

    afterEach(() => {
        vi.resetAllMocks();
    });

    describe('page structure', () => {
        it('renders the page with correct data-page attribute', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(document.querySelector('[data-page="subject-detail"]')).toBeInTheDocument();
            });
        });

        it('renders breadcrumb navigation', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                const breadcrumbNav = screen.getByRole('navigation', { name: /breadcrumb/i });
                expect(breadcrumbNav).toBeInTheDocument();
            });
        });

        it('roots the breadcrumb at Library like content pages', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                const libraryLink = screen.getByRole('link', { name: 'Library' });
                expect(libraryLink).toHaveAttribute('href', '/library');
            });
        });

        it('renders a kind-scoped breadcrumb using the kind plural label', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                const breadcrumbLinks = screen.getAllByRole('link', { name: 'People' });
                expect(breadcrumbLinks.length).toBeGreaterThan(0);
                expect(breadcrumbLinks[0]).toHaveAttribute('href', '/library/subjects/person');
            });
        });

        it('prefers the backend display name for an extension-defined kind', async () => {
            mockSubjectTypes.mockReturnValue([
                { slug: 'network', name: 'Network', pluralName: 'Networks', levels: [] },
            ]);
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/subjects/subject-1') {
                    return Promise.resolve({ ...mockApiSubject, kind: 'network' });
                }
                return Promise.reject({ status: 404, message: 'Not found' });
            });
            renderWithRouter('subject-1');
            await waitFor(() => {
                const breadcrumbLinks = screen.getAllByRole('link', { name: 'Networks' });
                expect(breadcrumbLinks[0]).toHaveAttribute('href', '/library/subjects/network');
            });
        });
    });

    describe('subject actions', () => {
        it('shows only Search library for Viewer users', async () => {
            mockCurrentRole.current = Role.Viewer;

            renderWithRouter('subject-1');

            await screen.findByRole('heading', { name: /Breaking Bad/i, level: 1 });

            expect(screen.getByRole('link', { name: 'Search library' })).toBeInTheDocument();
            expect(screen.queryByRole('link', { name: 'Add content' })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /^refresh$/i })).not.toBeInTheDocument();
            expect(screen.queryByText('Create filter')).not.toBeInTheDocument();
        });

        it('shows Search library, Add content and Refresh for Requester users', async () => {
            mockCurrentRole.current = Role.Requester;

            renderWithRouter('subject-1');

            const searchLibrary = await screen.findByRole('link', { name: 'Search library' });
            const refresh = screen.getByRole('button', { name: /^refresh$/i });

            expect(searchLibrary).toBeInTheDocument();
            expect(screen.getByRole('link', { name: 'Add content' })).toBeInTheDocument();
            expect(refresh).toBeEnabled();
        });

        it('shows all three actions with their required icons for Manager users', async () => {
            mockCurrentRole.current = Role.Manager;

            renderWithRouter('subject-1');

            const searchLibrary = await screen.findByRole('link', { name: 'Search library' });
            const addContent = screen.getByRole('link', { name: 'Add content' });
            const refresh = screen.getByRole('button', { name: 'Refresh' });
            const { container: expectedIcons } = render(
                <>
                    <FunnelIcon weight="bold" aria-hidden="true" />
                    <BinocularsIcon weight="bold" aria-hidden="true" />
                </>,
            );
            const expectedFilterIcon = expectedIcons.querySelectorAll('svg').item(0);
            const expectedAddContentIcon = expectedIcons.querySelectorAll('svg').item(1);

            expect(searchLibrary.querySelector('svg')?.isEqualNode(expectedFilterIcon)).toBe(true);
            expect(addContent.querySelector('svg')?.isEqualNode(expectedAddContentIcon)).toBe(true);
            expect(refresh).toBeEnabled();
        });
    });

    describe('refresh', () => {
        it('renders the refresh button', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('button', { name: /refresh/i })).toBeInTheDocument();
            });
        });

        it('starts a subject refresh job when clicked', async () => {
            mockCurrentRole.current = Role.Requester;
            mockApiPost.mockResolvedValue('execution-1');
            renderWithRouter('subject-1');

            const button = await screen.findByRole('button', { name: /refresh/i });
            await userEvent.click(button);

            await waitFor(() => {
                expect(mockApiPost).toHaveBeenCalledWith('/jobs/execute-immediate', {
                    jobType: 'SubjectMetadataRefreshItem',
                    parameters: { subjectId: 'subject-1' },
                });
            });
        });
    });

    describe('subject searches', () => {
        it('navigates within the SPA to Library with the live relationship query', async () => {
            mockCurrentRole.current = Role.Viewer;
            const queryClient = createTestQueryClient();
            let capturedPathname = '';
            let capturedSearch = '';

            function LocationProbe() {
                const location = useLocation();
                capturedPathname = location.pathname;
                capturedSearch = location.search;
                return null;
            }

            render(
                <QueryClientProvider client={queryClient}>
                    <TooltipProvider>
                        <ToastStateProvider>
                            <MemoryRouter initialEntries={[{ pathname: '/subjects/subject-1' }]}>
                                <Routes>
                                    <Route path="/subjects/:subjectId" element={<SubjectDetailPage />} />
                                    <Route path="/library/all" element={<LocationProbe />} />
                                </Routes>
                            </MemoryRouter>
                        </ToastStateProvider>
                    </TooltipProvider>
                </QueryClientProvider>,
            );

            const searchLibrary = await screen.findByRole('link', { name: 'Search library' });
            await userEvent.click(searchLibrary);

            await waitFor(() => {
                expect(capturedPathname).toBe('/library/all');
            });
            expect(new URLSearchParams(capturedSearch).get('filter')).toBe(
                'Relationships has (Name contains "Breaking Bad" and Type = "person")',
            );
            expect(isSimpleQuery(new URLSearchParams(capturedSearch).get('filter') ?? '', 'content')).toBe(true);
            expect(mockApiPost).not.toHaveBeenCalled();
        });

        it('navigates within the SPA to Add content with the same live relationship query', async () => {
            mockCurrentRole.current = Role.Manager;
            const queryClient = createTestQueryClient();
            let capturedPathname = '';
            let capturedState: unknown = null;

            function LocationProbe() {
                const location = useLocation();
                capturedPathname = location.pathname;
                capturedState = location.state;
                return null;
            }

            render(
                <QueryClientProvider client={queryClient}>
                    <TooltipProvider>
                        <ToastStateProvider>
                            <MemoryRouter initialEntries={[{ pathname: '/subjects/subject-1' }]}>
                                <Routes>
                                    <Route path="/subjects/:subjectId" element={<SubjectDetailPage />} />
                                    <Route path="/library/add" element={<LocationProbe />} />
                                </Routes>
                            </MemoryRouter>
                        </ToastStateProvider>
                    </TooltipProvider>
                </QueryClientProvider>,
            );

            const addContent = await screen.findByRole('link', { name: 'Add content' });
            await userEvent.click(addContent);

            await waitFor(() => {
                expect(capturedPathname).toBe('/library/add');
            });
            expect(capturedState).toEqual({
                filterQuery: 'Relationships has (Name contains "Breaking Bad" and Type = "person")',
            });
            expect(mockApiPost).not.toHaveBeenCalled();
        });
    });

    describe('subject header display', () => {
        it('renders the subject title', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Breaking Bad/i, level: 1 })).toBeInTheDocument();
            });
        });

        it('renders the subject kind badge', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText('Person')).toBeInTheDocument();
            });
        });

        it('does not render the subject publish year', async () => {
            renderWithRouter('subject-1');
            await screen.findByRole('heading', { name: /Breaking Bad/i, level: 1 });
            expect(screen.queryByText('(2008)')).not.toBeInTheDocument();
        });

        it('renders the subject image', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByAltText('Breaking Bad image')).toBeInTheDocument();
            });
            fireEvent.error(screen.getByAltText('Breaking Bad image'));
            expect(screen.queryByAltText('Breaking Bad image')).not.toBeInTheDocument();
            expect(document.querySelector('.subject-detail-page__image--placeholder')).toBeInTheDocument();
        });

        it('frames the subject image over a blurred backdrop fill of itself', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByAltText('Breaking Bad image')).toBeInTheDocument();
            });

            const img = screen.getByAltText('Breaking Bad image');
            expect(img).toHaveClass('subject-detail-page__image-img');
            const media = img.closest('.subject-detail-page__image--media') as HTMLElement | null;
            expect(media).not.toBeNull();
            expect(media?.style.getPropertyValue('--hero-image')).toContain('poster1.jpg');
        });

        it('renders the description', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText(/chemistry teacher/i)).toBeInTheDocument();
            });
        });

        it('line-clamps long multiline descriptions in the header', async () => {
            const longDescription = [
                'First provider bio line with enough text to wrap inside the header.',
                'Second provider bio line continues the biography with more details.',
                'Third provider bio line adds career context and background.',
                'Fourth provider bio line should be contained by the clamp.',
                'Fifth provider bio line must not expand the header indefinitely.',
            ].join('\n');
            mockMaterializedSubject.current = { ...defaultMaterializedSubject, overview: longDescription };
            const container = mockApiSubject.metadataContainer!;
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/subjects/subject-1') {
                    return Promise.resolve({
                        ...mockApiSubject,
                        metadataContainer: {
                            ...container,
                            contributions: container.contributions.map((c) => ({ ...c, overview: longDescription })),
                        },
                    });
                }
                return Promise.reject({ status: 404, message: 'Not found' });
            });

            renderWithRouter('subject-1');

            const bio = await screen.findByText((_, element) => element?.textContent === longDescription);
            expect(bio).toHaveClass('subject-detail-page__bio');
            expect(bio.textContent).toBe(longDescription);
        });

        it('renders explicitly labeled genres below the hero in the shared aligned row', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText('Drama')).toBeInTheDocument();
                expect(screen.getByText('Crime')).toBeInTheDocument();
                expect(screen.getByText('Thriller')).toBeInTheDocument();
            });
            const genres = document.querySelector('[data-component="subject-genres"]') as HTMLElement;
            expect(genres).toHaveClass('detail-info-row');
            expect(genres).toHaveTextContent('Genres');
            expect(genres.querySelector('[data-component="collapsible-chip-list"]')).toBeInTheDocument();
            expect(genres.closest('header')).toBeNull();
            expect(document.querySelectorAll('[data-component="subject-genres"]')).toHaveLength(1);
        });

        it('does not render content ratings for subjects', async () => {
            renderWithRouter('subject-1');
            await screen.findByRole('heading', { name: /Breaking Bad/i, level: 1 });
            expect(screen.queryByText('TV-MA')).not.toBeInTheDocument();
        });

        it('renders the supported production status in Details', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');
            await waitFor(() => {
                expect(screen.getByText('Ended')).toBeInTheDocument();
            });
        });

        it('renders the supported language in Details', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');
            await waitFor(() => {
                expect(screen.getByText('English')).toBeInTheDocument();
            });
        });
    });

    describe('supporting info (Content Detail parity)', () => {
        function mockWithTags(tagNames: string[]) {
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/subjects/subject-1') {
                    return Promise.resolve(mockApiSubject);
                }
                if (url.startsWith('/tags')) {
                    return Promise.resolve(tagNames.map((name) => ({ name })));
                }
                return Promise.reject({ status: 404, message: 'Not found' });
            });
        }

        it('renders the supporting rows in Tags, Links, Genres order below the hero', async () => {
            mockWithTags(['favorite']);
            renderWithRouter('subject-1');

            const grid = await waitFor(() => {
                const node = document.querySelector('.detail-info-grid--under-header');
                expect(node).toBeInTheDocument();
                return node as HTMLElement;
            });

            expect(grid).toHaveClass('detail-info-grid');
            expect(grid.closest('header')).toBeNull();

            const rows = Array.from(grid.querySelectorAll(':scope > .detail-info-row'));
            const labels = rows.map((row) => row.querySelector('.detail-info-row__label')?.textContent);
            expect(labels).toEqual(['Tags', 'Links', 'Genres']);
        });

        it('renders the aligned Tags row with a visible text label for authorized users', async () => {
            mockWithTags(['favorite']);
            renderWithRouter('subject-1');

            const tagLink = await screen.findByTestId('tags-chip-strip-link-favorite');
            const tagsRow = screen.getByTestId('subject-tags-row');
            expect(tagsRow).toHaveClass('detail-info-row');
            expect(tagsRow.querySelector('.detail-info-row__label')?.textContent).toBe('Tags');
            expect(tagsRow).toContainElement(tagLink);
        });

        it('links a subject tag to the kind-scoped subject listing filtered by that tag', async () => {
            mockWithTags(['favorite']);
            renderWithRouter('subject-1');

            const tagLink = await screen.findByTestId('tags-chip-strip-link-favorite');
            expect(tagLink).toHaveAttribute(
                'href',
                `/library/subjects/person?filter=${encodeURIComponent('tag has "favorite"')}`,
            );
        });

        it('adds a free-text subject tag through the subject tags patch shape', async () => {
            mockWithTags([]);
            const mockApiPatch = vi.mocked((await import('@/lib/api')).apiPatch);
            mockApiPatch.mockResolvedValue(undefined);
            renderWithRouter('subject-1');

            const addButton = await screen.findByTestId('tags-chip-strip-add');
            await userEvent.click(addButton);
            const input = await screen.findByTestId('tags-chip-strip-input');
            await userEvent.type(input, 'noir{Enter}');

            await waitFor(() => {
                expect(mockApiPatch).toHaveBeenCalledWith('/subjects/subject-1', { tags: { add: ['noir'] } });
            });
        });

        it('removes a subject tag through the subject tags patch shape', async () => {
            mockWithTags(['favorite']);
            const mockApiPatch = vi.mocked((await import('@/lib/api')).apiPatch);
            mockApiPatch.mockResolvedValue(undefined);
            renderWithRouter('subject-1');

            const removeButton = await screen.findByTestId('tags-chip-strip-remove-favorite');
            await userEvent.click(removeButton);

            await waitFor(() => {
                expect(mockApiPatch).toHaveBeenCalledWith('/subjects/subject-1', { tags: { remove: ['favorite'] } });
            });
        });

        it('keeps an editable empty Tags row for authorized users', async () => {
            mockWithTags([]);
            renderWithRouter('subject-1');

            expect(await screen.findByTestId('tags-chip-strip-add')).toBeInTheDocument();
            expect(screen.getByTestId('subject-tags-row')).toBeInTheDocument();
        });

        it('shows an empty read-only Tags row for viewers', async () => {
            mockCurrentRole.current = Role.Viewer;
            mockWithTags([]);
            renderWithRouter('subject-1');

            await screen.findByRole('heading', { name: /Breaking Bad/i, level: 1 });
            expect(await screen.findByText('No tags')).toBeInTheDocument();
            expect(screen.getByTestId('subject-tags-row')).toBeInTheDocument();
        });

        it('collapses and expands the Genres row with the shared show-all control', async () => {
            const genreNames = Array.from({ length: 13 }, (_, i) => `Genre ${i + 1}`);
            mockMaterializedSubject.current = {
                ...defaultMaterializedSubject,
                genres: genreNames.map((name, ordinal) => ({ id: `g-${ordinal}`, name, ordinal })),
            };
            const container = mockApiSubject.metadataContainer!;
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/subjects/subject-1') {
                    return Promise.resolve({
                        ...mockApiSubject,
                        metadataContainer: {
                            ...container,
                            contributions: container.contributions.map((c) => ({
                                ...c,
                                genres: genreNames.map((name, ordinal) => ({ id: `g-${ordinal}`, name, ordinal })),
                            })),
                        },
                    });
                }
                if (url.startsWith('/tags')) {
                    return Promise.resolve([]);
                }
                return Promise.reject({ status: 404, message: 'Not found' });
            });
            renderWithRouter('subject-1');

            const genres = await waitFor(() => {
                const node = document.querySelector('[data-component="subject-genres"]');
                expect(node).toBeInTheDocument();
                return node as HTMLElement;
            });

            expect(within(genres).queryByText('Genre 13')).not.toBeInTheDocument();
            const toggle = within(genres).getByRole('button', { name: /show all/i });
            await userEvent.click(toggle);
            expect(within(genres).getByText('Genre 13')).toBeInTheDocument();
            await userEvent.click(within(genres).getByRole('button', { name: /show fewer/i }));
            expect(within(genres).queryByText('Genre 13')).not.toBeInTheDocument();
        });
    });

    describe('tabs', () => {
        it('renders the Related tab as the default (first) tab', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                const relatedTab = screen.getByRole('tab', { name: 'Content' });
                expect(relatedTab).toBeInTheDocument();
                expect(relatedTab).toHaveAttribute('data-state', 'active');
            });
            const tabs = screen.getAllByRole('tab');
            expect(tabs[0]).toHaveAccessibleName(/^content$/i);
            expect(tabs[1]).toHaveAccessibleName(/^details$/i);
        });

        it('keeps Details reachable via ?tab=details', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');
            await waitFor(() => {
                const detailsTab = screen.getByRole('tab', { name: /^details$/i });
                expect(detailsTab).toHaveAttribute('data-state', 'active');
            });
            const tabPanel = screen.getByRole('tabpanel');
            expect(tabPanel.querySelector('[data-component="subject-facts"]')).toBeInTheDocument();
        });

        it('does not render an Overview tab', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: 'Content' })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /^overview$/i })).not.toBeInTheDocument();
        });

        it('renders the Images tab with count', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /images/i })).toBeInTheDocument();
                expect(screen.getByText('4')).toBeInTheDocument();
            });
        });

        it('renders the Relationships tab with count', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /relationships/i })).toBeInTheDocument();
                expect(screen.getByText('3')).toBeInTheDocument();
            });
        });

        it('renders subject previews when the kind catalog supports them', async () => {
            mockMaterializedSubject.current = {
                ...defaultMaterializedSubject,
                previews: [
                    {
                        id: 'preview-1',
                        ordinal: 0,
                        kind: 'trailer',
                        url: 'https://www.youtube.com/watch?v=TRAILER1',
                    },
                ],
            };
            renderWithRouter('subject-1');

            expect(await screen.findByRole('tab', { name: /previews/i })).toHaveTextContent('1');
        });

        it('shows Related Content in its tab panel when selected', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: 'Content' })).toBeInTheDocument();
            });
            await user.click(screen.getByRole('tab', { name: 'Content' }));
            const tabPanel = screen.getByRole('tabpanel');
            expect(tabPanel.querySelector('[data-component="subject-content-tab"]')).toBeInTheDocument();
        });

        it('renders the strip as the single tab picker with no dropdown mirror', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('tablist')).toBeInTheDocument();
            });
            expect(screen.queryByTestId('subject-detail-page__tab-select')).not.toBeInTheDocument();
            expect(screen.queryByRole('combobox', { name: /select tab/i })).not.toBeInTheDocument();
            const tablist = screen.getByRole('tablist');
            expect(within(tablist).getAllByRole('tab').length).toBeGreaterThan(1);
        });

        it('restores active tab from ?tab= search param', async () => {
            renderWithRouter('subject-1', undefined, '?tab=images');

            await waitFor(() => {
                const imagesTab = screen.getByRole('tab', { name: /images/i });
                expect(imagesTab).toHaveAttribute('data-state', 'active');
            });

            const tabPanel = screen.getByRole('tabpanel');
            const images = within(tabPanel).getAllByRole('img');
            expect(images.length).toBe(4);
        });

        it('retains a valid deep link and unrelated query parameters', async () => {
            renderWithRouter('subject-1', undefined, '?tab=images&source=library');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /images/i })).toHaveAttribute('data-state', 'active');
            });

            expect(screen.getByTestId('subject-detail-page__location-search')).toHaveTextContent(
                '?tab=images&source=library',
            );
        });

        it('defaults to the related tab when ?tab= param is absent', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                const relatedTab = screen.getByRole('tab', { name: 'Content' });
                expect(relatedTab).toHaveAttribute('data-state', 'active');
            });
        });

        it('falls back to the default tab when ?tab= names a removed tab', async () => {
            renderWithRouter('subject-1', undefined, '?tab=overview&source=library');

            await waitFor(() => {
                const relatedTab = screen.getByRole('tab', { name: 'Content' });
                expect(relatedTab).toHaveAttribute('data-state', 'active');
                expect(screen.getByTestId('subject-detail-page__location-search')).toHaveTextContent('?source=library');
            });

            const tabPanel = screen.getByRole('tabpanel');
            expect(tabPanel.querySelector('[data-component="subject-content-tab"]')).toBeInTheDocument();
        });

        it('removes an explicit empty tab while preserving unrelated query parameters', async () => {
            renderWithRouter('subject-1', undefined, '?tab=&source=library');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: 'Content' })).toHaveAttribute('data-state', 'active');
                expect(screen.getByTestId('subject-detail-page__location-search')).toHaveTextContent('?source=library');
            });
        });

        it('falls back to the default tab when ?tab=images targets a hidden conditional tab', async () => {
            mockMaterializedSubject.current = { ...defaultMaterializedSubject, images: [] };
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/subjects/subject-1') {
                    const container = mockApiSubject.metadataContainer!;
                    return Promise.resolve({
                        ...mockApiSubject,
                        metadataContainer: {
                            ...container,
                            contributions: container.contributions.map((c) => ({ ...c, images: [] })),
                        },
                    });
                }
                return Promise.reject({ status: 404, message: 'Not found' });
            });
            renderWithRouter('subject-1', undefined, '?tab=images');

            await waitFor(() => {
                const relatedTab = screen.getByRole('tab', { name: 'Content' });
                expect(relatedTab).toHaveAttribute('data-state', 'active');
            });

            expect(screen.queryByRole('tab', { name: /images/i })).not.toBeInTheDocument();
            const tabPanel = screen.getByRole('tabpanel');
            expect(tabPanel.querySelector('[data-component="subject-content-tab"]')).toBeInTheDocument();
        });
    });

    describe('Metadata tab', () => {
        it('renders a Metadata tab that shows the inspector when selected', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            const metadataTab = await screen.findByRole('tab', { name: /metadata/i });
            await user.click(metadataTab);

            await waitFor(() => {
                expect(document.querySelector('[data-component="metadata-inspector"]')).toBeInTheDocument();
            });
            expect(screen.getByText('Contribution stack')).toBeInTheDocument();
        });
    });

    describe('Links section', () => {
        it('renders captured references as external links near the header', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                const link = screen.getByRole('link', { name: /IMDb profile/ });
                expect(link).toHaveAttribute('href', 'https://www.imdb.com/name/walt');
                expect(link).toHaveAttribute('target', '_blank');
                expect(link).toHaveAttribute('rel', 'noopener noreferrer');
            });
            expect(document.querySelector('[data-component="metadata-links"]')).toBeInTheDocument();
            expect(screen.getByTestId('subject-links-row')).toHaveTextContent('Links');
        });

        it('renders no Links section when the subject has no references', async () => {
            mockMaterializedSubject.current = {
                ...defaultMaterializedSubject,
                externalIds: [],
                references: [],
            };
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/subjects/subject-1') {
                    const container = mockApiSubject.metadataContainer!;
                    return Promise.resolve({
                        ...mockApiSubject,
                        metadataContainer: {
                            ...container,
                            contributions: container.contributions.map((c) => ({ ...c, references: [] })),
                        },
                    });
                }
                return Promise.reject({ status: 404, message: 'Not found' });
            });
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Breaking Bad/i })).toBeInTheDocument();
            });
            expect(document.querySelector('[data-component="metadata-links"]')).not.toBeInTheDocument();
        });
    });

    describe('Details tab - Details panel', () => {
        it('renders the exact supported scalar, alternate-text, and declared custom rows', async () => {
            mockSubjectTypes.mockReturnValue([
                {
                    slug: 'person',
                    name: 'Person',
                    pluralName: 'People',
                    levels: [],
                    standardMetadataFields: STANDARD_METADATA_FIELDS.filter((field) =>
                        [
                            'OriginalTitle',
                            'SortTitle',
                            'Language',
                            'ProductionStatus',
                            'Established',
                            'Dissolved',
                            'BirthDate',
                            'DeathDate',
                        ].includes(field.field),
                    ),
                    customMetadataFields: [{ key: 'ethnicity', displayName: 'Ethnicity' }],
                },
            ]);
            mockMaterializedSubject.current = {
                ...defaultMaterializedSubject,
                originalTitle: 'Metastasis',
                sortTitle: 'Breaking Bad, The',
                established: '2000-01-02T15:30:00-08:00',
                dissolved: '2001-02-03T00:00:00Z',
                birthDate: '1956-03-07T00:00:00Z',
                deathDate: '2020-12-01T00:00:00Z',
                alternateTexts: [
                    { id: 'alternate-2', ordinal: 2, kind: 'Title', text: 'Los Pollos', language: 'es' },
                    { id: 'alternate-1', ordinal: 1, kind: 'Description', text: 'Chimie', language: 'fr' },
                ],
                customMetadata: [
                    { id: 'custom-1', key: 'ethnicity', value: 'Caucasian' },
                    { id: 'custom-2', key: 'provider-secret', value: 'must stay in Metadata' },
                ],
            };

            renderWithRouter('subject-1', undefined, '?tab=details');

            const panel = await waitFor(() => {
                const node = document.querySelector('[data-component="subject-details-panel"]');
                expect(node).toBeInTheDocument();
                return node as HTMLElement;
            });

            expect(panel).toHaveClass('details-panel--facts');
            expect(panel).toBe(screen.getByRole('region', { name: 'Details' }));
            expect(within(panel).queryByRole('heading', { name: 'Details' })).not.toBeInTheDocument();

            const labels = within(panel)
                .getAllByRole('term')
                .map((node) => node.textContent);
            expect(labels).toEqual([
                'Original title',
                'Sort title',
                'Language',
                'Production status',
                'Established',
                'Dissolved',
                'Birth date',
                'Death date',
                'Ethnicity',
            ]);
            expect(
                within(screen.getByRole('heading', { name: 'Alternate texts' }).closest('section') as HTMLElement)
                    .getAllByRole('term')
                    .map((node) => node.textContent),
            ).toEqual(['Descriptionfr', 'Titlees']);

            expect(within(panel).getByText('Caucasian')).toBeInTheDocument();
            expect(within(panel).queryByText('must stay in Metadata')).not.toBeInTheDocument();
        });

        it('only renders rows whose value is present', async () => {
            mockMaterializedSubject.current = {
                containerId: 'mc-1',
                subjectId: 'subject-1',
                title: 'Breaking Bad',
                birthDate: '1956-03-07T00:00:00Z',
            };

            renderWithRouter('subject-1', undefined, '?tab=details');

            const panel = await waitFor(() => {
                const node = document.querySelector('[data-component="subject-details-panel"]');
                expect(node).toBeInTheDocument();
                return node as HTMLElement;
            });

            const labels = within(panel)
                .getAllByRole('term')
                .map((node) => node.textContent);
            expect(labels).toEqual(['Birth date']);
        });

        it('renders nothing when there are no scalars and no custom values', async () => {
            mockMaterializedSubject.current = {
                containerId: 'mc-1',
                subjectId: 'subject-1',
                title: 'Breaking Bad',
                externalIds: defaultMaterializedSubject.externalIds,
            };
            renderWithRouter('subject-1', undefined, '?tab=details');

            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /External IDs/i })).toBeInTheDocument();
            });

            expect(document.querySelector('[data-component="subject-details-panel"]')).not.toBeInTheDocument();
        });
    });

    describe('Details tab - External IDs', () => {
        it('renders external IDs section', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /External IDs/i })).toBeInTheDocument();
            });
        });

        it('renders external ID sources', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');
            await waitFor(() => {
                expect(screen.getAllByText('Provider H').length).toBeGreaterThan(0);
                expect(screen.getAllByText('Provider A').length).toBeGreaterThan(0);
                expect(screen.getAllByText('PROVIDER-J').length).toBeGreaterThan(0);
            });
        });

        it('renders external IDs as non-linked identifier badges', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');
            await waitFor(() => {
                expect(screen.getByText('tt0903747')).toBeInTheDocument();
                expect(screen.queryByRole('link', { name: /tt0903747/i })).not.toBeInTheDocument();
            });
        });

        it('renders external ID value when no URL is built', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');
            await waitFor(() => {
                expect(screen.getByText('81189')).toBeInTheDocument();
            });
        });

        it('keeps internal external IDs out of Links and Details', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');

            await screen.findByText('tt0903747');
            expect(screen.queryByText('technical-id')).not.toBeInTheDocument();
        });
    });

    describe('Awards', () => {
        it('groups award years in materialized order under the biography', async () => {
            renderWithRouter('subject-1');
            const panel = await waitFor(() => {
                const node = document.querySelector('[data-component="subject-awards-panel"]');
                expect(node).toBeInTheDocument();
                return node as HTMLElement;
            });

            expect(panel.closest('header')).toBe(screen.getByRole('banner'));
            expect(
                within(panel)
                    .getAllByRole('term')
                    .map((node) => node.textContent),
            ).toEqual(['Emmy Awards']);
            expect(
                within(panel)
                    .getAllByRole('definition')
                    .map((node) => node.textContent),
            ).toEqual(['Outstanding Drama Series 2013, 2014']);
        });
    });

    describe('Content tab', () => {
        it('renders a Content tab', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: 'Content' })).toBeInTheDocument();
            });
        });

        it('renders the content list in its tab panel when selected', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: 'Content' })).toBeInTheDocument();
            });
            await user.click(screen.getByRole('tab', { name: 'Content' }));
            const tabPanel = screen.getByRole('tabpanel');
            expect(tabPanel.querySelector('[data-component="subject-content-tab"]')).toBeInTheDocument();
        });
    });

    describe('Images tab', () => {
        it('displays image gallery when Images tab is clicked', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /images/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /images/i }));

            const tabPanel = screen.getByRole('tabpanel');
            const images = within(tabPanel).getAllByRole('img');
            expect(images.length).toBe(4);
        });

        it('displays image type badges', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /images/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /images/i }));

            const tabPanel = screen.getByRole('tabpanel');
            expect(within(tabPanel).getAllByText('poster').length).toBe(2);
            expect(within(tabPanel).getAllByText('backdrop').length).toBe(2);
        });

        it('displays image dimensions', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /images/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /images/i }));

            expect(screen.getAllByText('300 x 450').length).toBe(2);
            expect(screen.getAllByText('780 x 439').length).toBe(2);
        });

        it('hides set-primary actions for Viewer users', async () => {
            mockCurrentRole.current = Role.Viewer;
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            const imagesTab = await screen.findByRole('tab', { name: /images/i });
            await user.click(imagesTab);

            const tabPanel = screen.getByRole('tabpanel');
            expect(within(tabPanel).queryByRole('button', { name: /set as primary/i })).not.toBeInTheDocument();
        });

        it('renders enabled set-primary actions for Requester users', async () => {
            mockCurrentRole.current = Role.Requester;
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            const imagesTab = await screen.findByRole('tab', { name: /images/i });
            await user.click(imagesTab);

            const tabPanel = screen.getByRole('tabpanel');
            const setPrimaryButtons = within(tabPanel).getAllByRole('button', { name: /set as primary/i });
            expect(setPrimaryButtons.length).toBeGreaterThan(0);
            expect(setPrimaryButtons[0]).toBeEnabled();
        });

        it('gallery images link to full-size image', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /images/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /images/i }));

            const tabPanel = screen.getByRole('tabpanel');
            const links = within(tabPanel).getAllByRole('link');
            expect(links.length).toBeGreaterThan(0);
            expect(links[0]).toHaveAttribute('target', '_blank');
        });
    });

    describe('Relationships tab', () => {
        it('displays relationships when Relationships tab is clicked', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /relationships/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /relationships/i }));

            expect(screen.getAllByText('Actor').length).toBe(2);
            expect(screen.getByText('Creator')).toBeInTheDocument();
        });

        it('displays relationship qualifiers', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /relationships/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /relationships/i }));

            expect(screen.getByText('Walter White')).toBeInTheDocument();
            expect(screen.getByText('Jesse Pinkman')).toBeInTheDocument();
        });

        it('displays placeholder for relationship images', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /relationships/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /relationships/i }));

            const cards = document.querySelectorAll('[data-relationship-id]');
            expect(cards).toHaveLength(3);
            for (const card of cards) {
                expect(within(card as HTMLElement).queryByRole('img')).toBeNull();
            }
        });
    });

    describe('back link from content', () => {
        it('renders back link when navigated from content page', async () => {
            renderWithRouter('subject-1', {
                fromContent: { id: 'content-1', title: 'Breaking Bad S01E01' },
            });

            await waitFor(() => {
                const backLink = screen.getByRole('link', { name: /back to breaking bad s01e01/i });
                expect(backLink).toBeInTheDocument();
                expect(backLink).toHaveAttribute('href', '/library/content-1');
            });
        });

        it('does not render back link when navigated directly', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Breaking Bad/i })).toBeInTheDocument();
            });

            expect(screen.queryByRole('link', { name: /back to/i })).not.toBeInTheDocument();
        });
    });

    describe('loading state', () => {
        it('shows loading state while fetching', async () => {
            mockApiGet.mockImplementation(() => new Promise(() => {}));

            renderWithRouter('subject-1');

            expect(screen.getByText(/loading/i)).toBeInTheDocument();
        });
    });

    describe('not found state', () => {
        it('shows not found message for unknown subject ID', async () => {
            renderWithRouter('nonexistent-id', undefined, '?tab=diagnostics&source=library');

            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Subject Not Found/i })).toBeInTheDocument();
                expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
            });
            expect(screen.getByTestId('subject-detail-page__location-search')).toHaveTextContent(
                '?tab=diagnostics&source=library',
            );
        });

        it('shows back to subjects link on not found page', async () => {
            renderWithRouter('nonexistent-id');

            await waitFor(() => {
                const backLink = screen.getByRole('link', { name: /back to subjects/i });
                expect(backLink).toBeInTheDocument();
                expect(backLink).toHaveAttribute('href', '/library/subjects/all');
            });
        });

        it('shows not found state for the zero UUID subject route', async () => {
            mockApiGet.mockImplementation((url: string) => {
                if (url === `/subjects/${MISSING_DETAIL_ID}`) {
                    return Promise.reject(new ApiError(404, 'Not found'));
                }
                return Promise.reject(new Error(`Unexpected endpoint ${url}`));
            });

            renderWithRouter(MISSING_DETAIL_ID);

            await waitFor(() => {
                expect(screen.getByRole('heading', { name: 'Subject Not Found' })).toBeInTheDocument();
                expect(screen.getByRole('link', { name: /back to subjects/i })).toHaveAttribute(
                    'href',
                    '/library/subjects/all',
                );
            });
            expect(screen.queryByText(/loading/i)).not.toBeInTheDocument();
        });

        it('shows failed-load state for non-404 subject errors', async () => {
            mockApiGet.mockImplementation((url: string) => {
                if (url === `/subjects/${MISSING_DETAIL_ID}`) {
                    return Promise.reject(new ApiError(500, 'Internal Server Error'));
                }
                return Promise.reject(new Error(`Unexpected endpoint ${url}`));
            });

            renderWithRouter(MISSING_DETAIL_ID);

            await waitFor(() => {
                expect(screen.getByRole('heading', { name: 'Failed to Load Subject' })).toBeInTheDocument();
            });
            expect(screen.getByText('Internal Server Error')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
            expect(screen.getByRole('link', { name: /back to subjects/i })).toHaveAttribute(
                'href',
                '/library/subjects/all',
            );
            expect(screen.queryByRole('heading', { name: 'Subject Not Found' })).not.toBeInTheDocument();
        });

        it('shows a scoped metadata error beside the surviving page and retries the inspector query', async () => {
            mockUseSubjectInspector.mockReturnValue({
                data: undefined,
                isLoading: false,
                error: new ApiError(500, 'Inspector failed'),
                refetch: mockSubjectInspectorRefetch,
            });

            const user = userEvent.setup();
            renderWithRouter('subject-1');

            const alert = await screen.findByTestId('subject-metadata-error');
            expect(within(alert).getByText('Failed to Load Subject Metadata')).toBeInTheDocument();
            expect(within(alert).getByText('Inspector failed')).toBeInTheDocument();

            expect(screen.getByRole('banner')).toBeInTheDocument();
            expect(screen.getByRole('heading', { name: /Breaking Bad/i })).toBeInTheDocument();
            expect(screen.queryByRole('heading', { name: 'Subject Not Found' })).not.toBeInTheDocument();

            await user.click(within(alert).getByRole('button', { name: /retry/i }));
            expect(mockSubjectInspectorRefetch).toHaveBeenCalled();
        });

        it('shows not found rather than a metadata error when both queries 404', async () => {
            renderWithRouter('nonexistent-id');

            expect(await screen.findByRole('heading', { name: 'Subject Not Found' })).toBeInTheDocument();
            expect(screen.queryByTestId('subject-metadata-error')).not.toBeInTheDocument();
        });
    });

    describe('extension slots', () => {
        it('renders the subject details header slot', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                const slotWrapper = document.querySelector('[data-slot="subject.details.header"]');
                expect(slotWrapper).toBeInTheDocument();
            });
        });

        it('retains a valid subject tab extension deep link', async () => {
            mockUseSlotExtensions.mockImplementation((slot: string) => {
                if (slot === 'subject.details.tabs') {
                    return {
                        extensions: [
                            {
                                id: 'subject-diagnostics',
                                extensionId: 'diagnostics-extension',
                                name: 'Subject Diagnostics',
                                slot: 'subject.details.tabs',
                                modifier: 'After',
                                tagName: 'magpie-subject-diagnostics',
                                priority: 100,
                                attributes: { 'tab-label': 'Diagnostics' },
                            },
                        ],
                        isReady: true,
                        isLoading: false,
                    };
                }

                return noExtensions;
            });

            renderWithRouter('subject-1', undefined, '?tab=ext-subject-diagnostics&source=library');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /diagnostics/i })).toHaveAttribute('data-state', 'active');
            });

            expect(document.querySelector('[data-slot="subject.details.tabs"]')).not.toBeInTheDocument();
            expect(document.querySelectorAll('magpie-subject-diagnostics')).toHaveLength(1);
            expect(screen.getByTestId('subject-detail-page__location-search')).toHaveTextContent(
                '?tab=ext-subject-diagnostics&source=library',
            );
        });
    });

    describe('accessibility', () => {
        it('has header element for subject header', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                const header = screen.getByTestId('subject-detail-page__header');
                expect(header.tagName).toBe('HEADER');
            });
        });

        it('includes data attribute for subject ID', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                const header = document.querySelector('[data-subject-id="subject-1"]');
                expect(header).toBeInTheDocument();
            });
        });

        it('external links open in new tab', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                const link = screen.getByRole('link', { name: /IMDb profile/ });
                expect(link).toHaveAttribute('target', '_blank');
                expect(link).toHaveAttribute('rel', 'noopener noreferrer');
            });
        });

        it('has accessible tab list', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('tablist')).toBeInTheDocument();
            });
        });

        it('has navigation regions for breadcrumbs', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('navigation', { name: /breadcrumb/i })).toBeInTheDocument();
            });
        });

        it('image alt texts include subject title', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByAltText('Breaking Bad image')).toBeInTheDocument();
            });
        });

        it('relationship cards have data attributes', async () => {
            const user = userEvent.setup();
            renderWithRouter('subject-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /relationships/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /relationships/i }));

            const card = document.querySelector('[data-relationship-id="rel-1"]');
            expect(card).toBeInTheDocument();
        });
    });
});
