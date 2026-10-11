import { render, screen, within, fireEvent, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BooksIcon } from '@phosphor-icons/react';

import { LibraryLandingPage } from './LibraryLandingPage';
import { AuthProvider, Role } from '@/auth';
import { testEffectivePermissions } from '@/test/permissions';
import type { LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import * as api from '@/lib/api';

const mockContentTypes: LibraryTypeDefinition[] = [
    { id: 'movies', name: 'Movie', pluralName: 'Movies', slug: 'movies', levels: [] },
    { id: 'shows', name: 'TV Show', pluralName: 'TV Shows', slug: 'shows', levels: [] },
];

const mockSubjectTypes: LibraryTypeDefinition[] = [
    { id: 'actors', name: 'Actor', pluralName: 'Actors', slug: 'actors', levels: [] },
    { id: 'studios', name: 'Studio', pluralName: 'Studios', slug: 'studios', levels: [] },
];

const mockUseLibraryTypes = vi.fn();
const mockUseLibraryCounts = vi.fn();
const mockUseActualLibraryCounts = vi.fn(() => false);

vi.mock('@/lib/api', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...original,
        apiGet: vi.fn(),
    };
});

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();

    return {
        ...original,
        useLibraryTypes: () => mockUseLibraryTypes(),
        useLibraryCounts: (...args: Parameters<typeof original.useLibraryCounts>) =>
            mockUseActualLibraryCounts() ? original.useLibraryCounts(...args) : mockUseLibraryCounts(...args),
        useTypeIconName: () => () => undefined,
    };
});

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

function renderLibraryLanding(role: Role = Role.Admin) {
    const queryClient = createTestQueryClient();
    const mockUser = { id: 'user-1', username: 'testuser', role };

    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                    <LibraryLandingPage />
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('LibraryLandingPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(api.apiGet).mockReset();
        mockUseActualLibraryCounts.mockReturnValue(false);
        mockUseLibraryTypes.mockReturnValue({
            contentTypes: mockContentTypes,
            subjectTypes: mockSubjectTypes,
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });
        mockUseLibraryCounts.mockReturnValue({
            counts: { movies: 42, shows: 150, actors: 85, studios: 12 },
        });
    });

    it('renders the library landing page with correct data-page attribute', () => {
        renderLibraryLanding();

        expect(document.querySelector('[data-page="library-landing"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        renderLibraryLanding();

        expect(screen.getByRole('heading', { name: 'Library', level: 1 })).toBeInTheDocument();
    });

    it('renders the subtitle', () => {
        // Arrange
        // Act
        renderLibraryLanding();

        // Assert
        expect(screen.getByText("Browse what's in the library.")).toBeInTheDocument();
    });

    describe('Content Types Section', () => {
        it('renders content section heading', () => {
            renderLibraryLanding();

            expect(screen.getByRole('heading', { name: 'Content', level: 2 })).toBeInTheDocument();
        });

        it('renders content type cards', () => {
            renderLibraryLanding();

            const contentSection = screen.getByRole('region', { name: 'Content' });

            expect(within(contentSection).getByText('Movies')).toBeInTheDocument();
            expect(within(contentSection).getByText('TV Shows')).toBeInTheDocument();
        });

        it('creates correct links to content type pages', () => {
            renderLibraryLanding();

            const moviesLink = screen.getByRole('link', { name: /movies/i });
            const showsLink = screen.getByRole('link', { name: /tv shows/i });

            expect(moviesLink).toHaveAttribute('href', '/library/content/movies');
            expect(showsLink).toHaveAttribute('href', '/library/content/shows');
        });

        it('renders an All content card that links to all content', () => {
            // Act
            renderLibraryLanding();

            // Assert
            const allContentLink = screen.getByRole('link', { name: 'All content 192 items' });
            expect(allContentLink).toHaveAttribute('href', '/library/all');
            expect(
                within(allContentLink)
                    .getByTestId('library-landing__all-content-icon')
                    .closest('[data-component="card"]'),
            ).toHaveAttribute('data-interactive', 'true');
        });

        it('renders the All content card with the Library navigation icon', () => {
            // Arrange
            renderLibraryLanding();
            const { container } = render(<BooksIcon weight="duotone" />);

            // Act
            const cardIcon = screen.getByTestId('library-landing__all-content-icon').querySelector('svg');

            // Assert
            expect(cardIcon?.innerHTML).toBe(container.querySelector('svg')?.innerHTML);
        });

        it('renders the All content card before the content type cards', () => {
            // Act
            renderLibraryLanding();

            // Assert
            const links = screen.getAllByRole('link');
            expect(links[0]).toHaveAttribute('href', '/library/all');
            expect(links[1]).toHaveAttribute('href', '/library/content/movies');
            expect(links[2]).toHaveAttribute('href', '/library/content/shows');
        });

        it('has no View all content link', () => {
            // Act
            renderLibraryLanding();

            // Assert
            expect(screen.queryByRole('link', { name: /view all content/i })).not.toBeInTheDocument();
        });
    });

    describe('Subject Types Section', () => {
        it('has no Subjects heading', () => {
            // Act
            renderLibraryLanding();

            // Assert
            expect(screen.queryByRole('heading', { name: /subject/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('region', { name: /subject/i })).not.toBeInTheDocument();
        });

        it('renders subject type cards', () => {
            renderLibraryLanding();

            expect(screen.getByText('Actors')).toBeInTheDocument();
            expect(screen.getByText('Studios')).toBeInTheDocument();
        });

        it('creates correct links to subject type pages', () => {
            renderLibraryLanding();

            const links = screen.getAllByRole('link');
            const actorsLink = links.find((l) => l.getAttribute('href') === '/library/subjects/actors');
            const studiosLink = links.find((l) => l.getAttribute('href') === '/library/subjects/studios');

            expect(actorsLink).toBeDefined();
            expect(studiosLink).toBeDefined();
        });

        it('has no link to all subjects', () => {
            // Act
            renderLibraryLanding();

            // Assert
            expect(screen.queryByRole('link', { name: /all subjects/i })).not.toBeInTheDocument();
            expect(document.querySelector('a[href="/library/subjects/all"]')).not.toBeInTheDocument();
        });

        it('has no visible text that names subjects', () => {
            // Act
            renderLibraryLanding();

            // Assert
            expect(document.body).not.toHaveTextContent(/subject/i);
        });
    });

    describe('Loading State', () => {
        it('shows loading skeletons when loading', () => {
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: [],
                subjectTypes: [],
                isLoading: true,
                error: null,
                refetch: vi.fn(),
            });
            mockUseLibraryCounts.mockReturnValue({ counts: {} });

            renderLibraryLanding();

            const skeletons = screen.getAllByTestId('library-landing__type-card--skeleton');
            expect(skeletons.length).toBeGreaterThan(0);
        });

        it('hides the All content card when loading', () => {
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: [],
                subjectTypes: [],
                isLoading: true,
                error: null,
                refetch: vi.fn(),
            });
            mockUseLibraryCounts.mockReturnValue({ counts: {} });

            renderLibraryLanding();

            expect(screen.queryByRole('link', { name: /all content/i })).not.toBeInTheDocument();
        });
    });

    describe('Empty State', () => {
        it('shows empty message when no content types', () => {
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: [],
                subjectTypes: mockSubjectTypes,
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            mockUseLibraryCounts.mockReturnValue({
                counts: { actors: 85, studios: 12 },
            });

            renderLibraryLanding();

            expect(screen.getByText(/no content types registered/i)).toBeInTheDocument();
            expect(screen.queryByRole('link', { name: /all content/i })).not.toBeInTheDocument();
        });

        it('shows empty message when no subject types', () => {
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: mockContentTypes,
                subjectTypes: [],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 42, shows: 150 },
            });

            renderLibraryLanding();

            expect(screen.getByText('No categories registered. Install extensions to add them.')).toBeInTheDocument();
        });
    });

    describe('Error State', () => {
        it('shows error message when API fails', () => {
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: [],
                subjectTypes: [],
                isLoading: false,
                error: new Error('Network error'),
                refetch: vi.fn(),
            });
            mockUseLibraryCounts.mockReturnValue({ counts: {} });

            renderLibraryLanding();

            expect(screen.getByRole('alert')).toBeInTheDocument();
            expect(screen.getByText(/failed to load library types/i)).toBeInTheDocument();
        });

        it('renders a retry button that calls refetch', () => {
            const mockRefetch = vi.fn();
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: [],
                subjectTypes: [],
                isLoading: false,
                error: new Error('Network error'),
                refetch: mockRefetch,
            });
            mockUseLibraryCounts.mockReturnValue({ counts: {} });

            renderLibraryLanding();

            const retryButton = screen.getByRole('button', { name: /try again/i });
            expect(retryButton).toBeInTheDocument();

            fireEvent.click(retryButton);
            expect(mockRefetch).toHaveBeenCalledOnce();
        });
    });

    describe('Type Cards', () => {
        it('renders type cards with correct data attributes', () => {
            renderLibraryLanding();

            const movieCard = document.querySelector('[data-type-id="movies"]');
            const showCard = document.querySelector('[data-type-id="shows"]');

            expect(movieCard).toBeInTheDocument();
            expect(showCard).toBeInTheDocument();
        });

        it('renders interactive cards', () => {
            renderLibraryLanding();

            const cards = document.querySelectorAll('[data-component="card"][data-interactive="true"]');

            expect(cards.length).toBe(5);
        });
    });

    describe('Item Counts', () => {
        it('displays item counts on cards when available', () => {
            renderLibraryLanding();

            expect(screen.getByText('42 items')).toBeInTheDocument();
            expect(screen.getByText('150 items')).toBeInTheDocument();
            expect(screen.getByText('85 items')).toBeInTheDocument();
            expect(screen.getByText('12 items')).toBeInTheDocument();
            expect(screen.getByText('192 items')).toBeInTheDocument();
        });

        it('displays singular "1 item" for count of 1', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 1, shows: 0, actors: 1, studios: 0 },
            });

            renderLibraryLanding();

            const items = screen.getAllByText('1 item');
            expect(items.length).toBe(3);
        });

        it('displays "0 items" for an empty type card', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 5, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding();

            const zeroItems = screen.getAllByText('0 items');
            expect(zeroItems.length).toBe(3);
        });

        it('suppresses the zero-count content cards in favor of the onboarding hero when content is empty', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 2, studios: 1 },
            });

            renderLibraryLanding();

            expect(screen.queryByText('Movies')).not.toBeInTheDocument();
            expect(screen.queryByText('TV Shows')).not.toBeInTheDocument();
            expect(screen.queryByText('All content')).not.toBeInTheDocument();
            expect(document.querySelector('[data-component="onboarding-prompt"]')).toBeInTheDocument();
            expect(screen.getByText('Actors')).toBeInTheDocument();
        });

        it('shows count skeletons while counts are loading', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: undefined, shows: undefined, actors: undefined, studios: undefined },
            });

            renderLibraryLanding();

            const countSkeletons = screen.getAllByTestId('library-landing__count-skeleton');
            expect(countSkeletons.length).toBe(5);
        });

        it('renders subject card counts from subject totalItems responses', async () => {
            const subjectTypes: LibraryTypeDefinition[] = [
                { id: 'person', name: 'Person', pluralName: 'People', slug: 'person', levels: [] },
                { id: 'studio', name: 'Studio', pluralName: 'Studios', slug: 'studio', levels: [] },
            ];

            mockUseLibraryTypes.mockReturnValue({
                contentTypes: mockContentTypes,
                subjectTypes,
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            mockUseActualLibraryCounts.mockReturnValue(true);
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url.startsWith('/content')) {
                    return Promise.resolve({ totalItems: 8 });
                }
                if (url.startsWith('/subjects') && url.includes('kind=person')) {
                    return Promise.resolve({ items: [], pageNumber: 1, pageSize: 1, totalItems: 4053 });
                }
                if (url.startsWith('/subjects') && url.includes('kind=studio')) {
                    return Promise.resolve({ items: [], pageNumber: 1, pageSize: 1, totalItems: 12 });
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            renderLibraryLanding();

            const peopleCard = screen.getByRole('link', { name: /People/ });
            const studiosCard = screen.getByRole('link', { name: /Studios/ });
            expect(within(peopleCard).getByTestId('library-landing__count-skeleton')).toBeInTheDocument();
            expect(within(studiosCard).getByTestId('library-landing__count-skeleton')).toBeInTheDocument();

            await waitFor(() => {
                expect(within(peopleCard).getByText('4,053 items')).toBeInTheDocument();
                expect(within(studiosCard).getByText('12 items')).toBeInTheDocument();
                expect(screen.queryAllByTestId('library-landing__count-skeleton')).toHaveLength(0);
            });

            expect(api.apiGet).toHaveBeenCalledWith(expect.stringContaining('/subjects?kind=person'));
            expect(api.apiGet).toHaveBeenCalledWith(expect.stringContaining('/subjects?kind=studio'));
        });

        it('passes content and subject types to useLibraryCounts', () => {
            renderLibraryLanding();

            expect(mockUseLibraryCounts).toHaveBeenCalledWith(mockContentTypes, mockSubjectTypes);
        });
    });

    describe('Onboarding Prompt', () => {
        it('shows onboarding prompt when all content counts are zero', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding(Role.Admin);

            expect(document.querySelector('[data-component="onboarding-prompt"]')).toBeInTheDocument();
            expect(screen.getByText('Your library is empty')).toBeInTheDocument();
        });

        it('does not show onboarding prompt when content exists', () => {
            renderLibraryLanding(Role.Admin);

            expect(document.querySelector('[data-component="onboarding-prompt"]')).not.toBeInTheDocument();
        });

        it('does not show onboarding prompt while counts are loading', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: undefined, shows: undefined, actors: undefined, studios: undefined },
            });

            renderLibraryLanding(Role.Admin);

            expect(document.querySelector('[data-component="onboarding-prompt"]')).not.toBeInTheDocument();
        });

        it('shows an Add content link (to the bare add page) for Requester role', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding(Role.Requester);

            const addLink = screen.getByRole('link', { name: /add content/i });
            expect(addLink).toHaveAttribute('href', '/library/add');
            expect(screen.queryByRole('button', { name: /wishlist/i })).not.toBeInTheDocument();
        });

        it('shows both the Add content link and the media-server button for Admin role', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding(Role.Admin);

            expect(screen.getByRole('link', { name: /add content/i })).toHaveAttribute('href', '/library/add');
            expect(screen.getByRole('link', { name: /connect a media server/i })).toHaveAttribute(
                'href',
                '/admin/media-servers',
            );
        });

        it('shows only the Add content link for Manager role', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding(Role.Manager);

            expect(screen.getByRole('link', { name: /add content/i })).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /connect a media server/i })).not.toBeInTheDocument();
        });

        it('shows no action controls for Viewer role', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding(Role.Viewer);

            expect(screen.queryByRole('link', { name: /add content/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /connect a media server/i })).not.toBeInTheDocument();
        });
    });

    describe('Accessibility', () => {
        it('has a labeled content section', () => {
            renderLibraryLanding();

            expect(screen.getByRole('region', { name: 'Content' })).toBeInTheDocument();
        });

        it('has icon marked as decorative', () => {
            renderLibraryLanding();

            const icon = screen.getByTestId('library-landing__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });

        it('type card icons are marked as decorative', () => {
            renderLibraryLanding();

            const typeIcons = screen.getAllByTestId('library-landing__type-icon');
            typeIcons.forEach((icon) => {
                expect(icon).toHaveAttribute('aria-hidden', 'true');
            });
        });
    });

    describe('Icons', () => {
        it('renders generic icons for type cards', () => {
            renderLibraryLanding();

            const typeIconSvgs = screen
                .getAllByTestId('library-landing__type-icon')
                .map((el) => el.querySelector('svg'));
            expect(typeIconSvgs.length).toBe(4);
        });

        it('renders fallback icon for content types', () => {
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: [
                    {
                        id: 'ct-custom',
                        name: 'Custom',
                        pluralName: 'Custom',
                        slug: 'custom',
                    },
                ],
                subjectTypes: [],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            mockUseLibraryCounts.mockReturnValue({ counts: { custom: 5 } });

            renderLibraryLanding();

            expect(screen.getByText('Custom')).toBeInTheDocument();
            const typeIcon = screen.getAllByTestId('library-landing__type-icon')[0].querySelector('svg');
            expect(typeIcon).toBeInTheDocument();
        });

        it('renders fallback icon for subject types', () => {
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: [],
                subjectTypes: [
                    {
                        id: 'st-custom',
                        name: 'Custom Subject',
                        pluralName: 'Custom Subject',
                        slug: 'custom-subject',
                    },
                ],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            });
            mockUseLibraryCounts.mockReturnValue({ counts: { 'custom-subject': 3 } });

            renderLibraryLanding();

            expect(screen.getByText('Custom Subject')).toBeInTheDocument();
            const typeIcon = screen.getAllByTestId('library-landing__type-icon')[0].querySelector('svg');
            expect(typeIcon).toBeInTheDocument();
        });
    });
});
