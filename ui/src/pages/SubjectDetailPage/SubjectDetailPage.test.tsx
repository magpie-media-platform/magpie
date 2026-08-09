import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

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
import type { ApiSubject } from '@/hooks/useSubjects';

const mockApiGet = vi.mocked(apiGet);
const mockApiPost = vi.mocked(apiPost);
const MISSING_DETAIL_ID = '00000000-0000-0000-0000-000000000000';
const subjectDetailPageCss = readFileSync(
    resolve(dirname(fileURLToPath(import.meta.url)), 'SubjectDetailPage.css'),
    'utf8',
);

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
vi.mock('@/hooks/useSubjectMetadataInspector', () => ({
    useSubjectMetadataInspector: () => mockUseSubjectInspector(),
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
                    <MemoryRouter initialEntries={[initialEntry]}>
                        <Routes>
                            <Route path="/subjects/:subjectId" element={<SubjectDetailRoute />} />
                        </Routes>
                    </MemoryRouter>
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
        mockSubjectTypes.mockReturnValue([{ slug: 'person', name: 'Person', pluralName: 'People', levels: [] }]);
        mockUseSubjectInspector.mockReturnValue({
            data: {
                materialized: { containerId: 'mc-1', subjectId: 'subject-1', title: 'Breaking Bad' },
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
        });

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

    describe('requester-only actions', () => {
        it('hides subject actions for Viewer users', async () => {
            mockCurrentRole.current = Role.Viewer;

            renderWithRouter('subject-1');

            await screen.findByRole('heading', { name: /Breaking Bad/i, level: 1 });

            expect(screen.queryByRole('button', { name: /create filter/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /^refresh$/i })).not.toBeInTheDocument();
        });

        it('renders enabled subject actions for Requester users', async () => {
            mockCurrentRole.current = Role.Requester;

            renderWithRouter('subject-1');

            const createFilter = await screen.findByRole('button', { name: /create filter/i });
            const refresh = screen.getByRole('button', { name: /^refresh$/i });

            expect(createFilter).toBeEnabled();
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

    describe('create filter', () => {
        it('navigates to the Filters page with a query scoping content to this subject', async () => {
            mockCurrentRole.current = Role.Requester;
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
                                    <Route path="/filters" element={<LocationProbe />} />
                                </Routes>
                            </MemoryRouter>
                        </ToastStateProvider>
                    </TooltipProvider>
                </QueryClientProvider>,
            );

            const createFilter = await screen.findByRole('button', { name: /create filter/i });
            await userEvent.click(createFilter);

            await waitFor(() => {
                expect(capturedPathname).toBe('/filters');
            });
            expect(capturedState).toEqual(
                expect.objectContaining({
                    createName: 'Breaking Bad',
                    createQuery: expect.stringContaining('Type = "person"'),
                }),
            );
            expect((capturedState as { createQuery: string }).createQuery).toContain('Name = "Breaking Bad"');
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

        it('renders the year', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText('(2008)')).toBeInTheDocument();
            });
        });

        it('renders the subject image', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByAltText('Breaking Bad image')).toBeInTheDocument();
            });
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
            const bioRules = Array.from(
                subjectDetailPageCss.matchAll(/\.subject-detail-page__bio\s*\{([^}]*)\}/g),
                (match) => match[1],
            );
            expect(bio).toHaveClass('subject-detail-page__bio');
            expect(bio.textContent).toBe(longDescription);
            expect(bioRules[0]).toContain('display: -webkit-box;');
            expect(bioRules[0]).toContain('-webkit-line-clamp: 3;');
            expect(bioRules[0]).toContain('-webkit-box-orient: vertical;');
            expect(bioRules[0]).toContain('overflow: hidden;');
            expect(bioRules).toContainEqual(expect.stringContaining('-webkit-line-clamp: 4;'));
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

        it('renders content rating badge', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText('TV-MA')).toBeInTheDocument();
            });
        });

        it('renders status badge', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText('Ended')).toBeInTheDocument();
            });
        });

        it('renders language', async () => {
            renderWithRouter('subject-1');
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

        it('omits an empty read-only Tags row for viewers', async () => {
            mockCurrentRole.current = Role.Viewer;
            mockWithTags([]);
            renderWithRouter('subject-1');

            await screen.findByRole('heading', { name: /Breaking Bad/i, level: 1 });
            expect(screen.queryByTestId('subject-tags-row')).not.toBeInTheDocument();
        });

        it('collapses and expands the Genres row with the shared show-all control', async () => {
            const genreNames = Array.from({ length: 13 }, (_, i) => `Genre ${i + 1}`);
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
        function withContribution(overrides: Record<string, unknown>) {
            const container = mockApiSubject.metadataContainer!;
            return {
                ...mockApiSubject,
                metadataContainer: {
                    ...container,
                    contributions: container.contributions.map((c, i) => (i === 0 ? { ...c, ...overrides } : c)),
                },
            };
        }

        it('renders built-in scalar dates before custom values', async () => {
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/subjects/subject-1') {
                    return Promise.resolve(
                        withContribution({
                            birthDate: '1956-03-07T00:00:00Z',
                            deathDate: '2020-12-01T00:00:00Z',
                            customMetadata: [
                                { key: 'Ethnicity', value: 'Caucasian' },
                                { key: 'Height', value: '170 cm' },
                            ],
                        }),
                    );
                }
                return Promise.reject({ status: 404, message: 'Not found' });
            });

            renderWithRouter('subject-1', undefined, '?tab=details');

            const panel = await waitFor(() => {
                const node = document.querySelector('[data-component="subject-details-panel"]');
                expect(node).toBeInTheDocument();
                return node as HTMLElement;
            });

            const labels = within(panel)
                .getAllByRole('term')
                .map((node) => node.textContent);
            expect(labels).toEqual(['Birth date', 'Death date', 'Ethnicity', 'Height']);

            expect(within(panel).getByText('Caucasian')).toBeInTheDocument();
        });

        it('only renders rows whose value is present', async () => {
            mockApiGet.mockImplementation((url: string) => {
                if (url === '/subjects/subject-1') {
                    return Promise.resolve(withContribution({ birthDate: '1956-03-07T00:00:00Z' }));
                }
                return Promise.reject({ status: 404, message: 'Not found' });
            });

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
    });

    describe('Details tab - Awards', () => {
        it('renders awards section', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /awards/i })).toBeInTheDocument();
            });
        });

        it('renders award items', async () => {
            renderWithRouter('subject-1', undefined, '?tab=details');
            await waitFor(() => {
                const awards = screen.getAllByText(/Emmy Awards Outstanding Drama Series/);
                expect(awards.length).toBe(2);
            });
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

            const placeholders = screen.getAllByTestId('subject-detail-page__person-image--placeholder');
            expect(placeholders.length).toBe(3);
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
