import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { SubjectDetailPage } from './SubjectDetailPage';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPatch: vi.fn(),
        apiDelete: vi.fn(),
    };
});

import { apiGet } from '@/lib/api';
import type { ApiSubject } from '@/hooks/useSubjects';

const mockApiGet = apiGet as ReturnType<typeof vi.fn>;

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockApiSubject: ApiSubject = {
    id: 'subject-1',
    metadataContainerId: 'mc-1',
    kind: 'Show',
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
                providerType: 'TMDb',
                source: 'TMDb',
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
                    { id: 'eid1', value: 'tt0903747', source: 'IMDb', ordinal: 0 },
                    { id: 'eid2', value: '1396', source: 'TMDb', ordinal: 1 },
                    { id: 'eid3', value: '81189', source: 'TVDB', ordinal: 2 },
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
                    { type: 'poster', url: 'https://image.tmdb.org/t/p/w300/poster1.jpg', width: 300, height: 450 },
                    { type: 'backdrop', url: 'https://image.tmdb.org/t/p/w780/backdrop1.jpg', width: 780, height: 439 },
                    { type: 'poster', url: 'https://image.tmdb.org/t/p/w300/poster2.jpg', width: 300, height: 450 },
                    { type: 'backdrop', url: 'https://image.tmdb.org/t/p/w780/backdrop2.jpg', width: 780, height: 439 },
                ],
                relationships: [
                    { id: 'rel-1', kind: 'Actor', qualifier: 'Walter White', ordinal: 0 },
                    { id: 'rel-2', kind: 'Actor', qualifier: 'Jesse Pinkman', ordinal: 1 },
                    { id: 'rel-3', kind: 'Creator', qualifier: null, ordinal: 2 },
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

function renderWithRouter(subjectId: string, locationState?: Record<string, unknown>, search?: string) {
    const queryClient = createTestQueryClient();
    const pathname = `/subjects/${subjectId}`;
    const initialEntry = search ? { pathname, search, state: locationState } : { pathname, state: locationState };

    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[initialEntry]}>
                <Routes>
                    <Route path="/subjects/:subjectId" element={<SubjectDetailPage />} />
                </Routes>
            </MemoryRouter>
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
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/subjects/subject-1') {
                return Promise.resolve(mockApiSubject);
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

        it('renders subjects link in breadcrumbs', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                const subjectsLinks = screen.getAllByRole('link', { name: /subjects/i });
                expect(subjectsLinks.length).toBeGreaterThan(0);
                expect(subjectsLinks[0]).toHaveAttribute('href', '/library/subjects/all');
            });
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
                expect(screen.getByText('Show')).toBeInTheDocument();
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

        it('renders the description', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText(/chemistry teacher/i)).toBeInTheDocument();
            });
        });

        it('renders genres', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText('Drama')).toBeInTheDocument();
                expect(screen.getByText('Crime')).toBeInTheDocument();
                expect(screen.getByText('Thriller')).toBeInTheDocument();
            });
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

    describe('tabs', () => {
        it('renders the Overview tab', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /overview/i })).toBeInTheDocument();
            });
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

        it('shows Overview tab content by default', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                const tabPanel = screen.getByRole('tabpanel');
                expect(within(tabPanel).getByText('External IDs')).toBeInTheDocument();
            });
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

        it('defaults to overview when ?tab= param is absent', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                const overviewTab = screen.getByRole('tab', { name: /overview/i });
                expect(overviewTab).toHaveAttribute('data-state', 'active');
            });
        });
    });

    describe('Overview tab - External IDs', () => {
        it('renders external IDs section', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /External IDs/i })).toBeInTheDocument();
            });
        });

        it('renders external ID sources', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText('IMDb')).toBeInTheDocument();
                expect(screen.getByText('TMDb')).toBeInTheDocument();
                expect(screen.getByText('TVDB')).toBeInTheDocument();
            });
        });

        it('renders external ID links when URL is provided', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                const imdbLink = screen.getByRole('link', { name: /tt0903747/i });
                expect(imdbLink).toHaveAttribute('href', 'https://www.imdb.com/title/tt0903747/');
                expect(imdbLink).toHaveAttribute('target', '_blank');
                expect(imdbLink).toHaveAttribute('rel', 'noopener noreferrer');
            });
        });

        it('renders external ID value when no URL is built', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByText('81189')).toBeInTheDocument();
            });
        });
    });

    describe('Overview tab - Awards', () => {
        it('renders awards section', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /awards/i })).toBeInTheDocument();
            });
        });

        it('renders award items', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                const awards = screen.getAllByText(/Emmy Awards Outstanding Drama Series/);
                expect(awards.length).toBe(2);
            });
        });
    });

    describe('Overview tab - Related Content', () => {
        it('renders related content section heading', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                const heading = screen.getByRole('heading', { name: /^Related Content$/, level: 2 });
                expect(heading).toBeInTheDocument();
            });
        });

        it('renders related content list component', async () => {
            renderWithRouter('subject-1');
            await waitFor(() => {
                expect(document.querySelector('[data-component="related-content-list"]')).toBeInTheDocument();
            });
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

            expect(screen.getByText(/loading subject/i)).toBeInTheDocument();
        });
    });

    describe('not found state', () => {
        it('shows not found message for unknown subject ID', async () => {
            renderWithRouter('nonexistent-id');

            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Subject Not Found/i })).toBeInTheDocument();
                expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
            });
        });

        it('shows back to subjects link on not found page', async () => {
            renderWithRouter('nonexistent-id');

            await waitFor(() => {
                const backLink = screen.getByRole('link', { name: /subjects/i });
                expect(backLink).toBeInTheDocument();
                expect(backLink).toHaveAttribute('href', '/library/subjects/all');
            });
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

        it('renders the subject details tabs slot', async () => {
            renderWithRouter('subject-1');

            await waitFor(() => {
                const slotWrapper = document.querySelector('[data-slot="subject.details.tabs"]');
                expect(slotWrapper).toBeInTheDocument();
            });
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
                const imdbLink = screen.getByRole('link', { name: /tt0903747/i });
                expect(imdbLink).toHaveAttribute('target', '_blank');
                expect(imdbLink).toHaveAttribute('rel', 'noopener noreferrer');
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
