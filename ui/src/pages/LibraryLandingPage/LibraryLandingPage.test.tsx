import { render, screen, within, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { LibraryLandingPage } from './LibraryLandingPage';
import { AuthProvider, Role } from '@/auth';
import type { LibraryTypeDefinition } from '@/hooks/useLibraryTypes';

const mockContentTypes: LibraryTypeDefinition[] = [
    { id: 'movies', name: 'Movies', slug: 'movies' },
    { id: 'shows', name: 'TV Shows', slug: 'shows' },
];

const mockSubjectTypes: LibraryTypeDefinition[] = [
    { id: 'actors', name: 'Actors', slug: 'actors' },
    { id: 'studios', name: 'Studios', slug: 'studios' },
];

const mockUseLibraryTypes = vi.fn();
const mockUseLibraryCounts = vi.fn();

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();
    return {
        ...original,
        useLibraryTypes: () => mockUseLibraryTypes(),
        useLibraryCounts: (...args: unknown[]) => mockUseLibraryCounts(...args),
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
                <AuthProvider initialUser={mockUser}>
                    <LibraryLandingPage />
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('LibraryLandingPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
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

        it('has link to all content page', () => {
            renderLibraryLanding();

            const viewAllLink = screen.getByRole('link', { name: /view all content/i });

            expect(viewAllLink).toHaveAttribute('href', '/library/all');
        });
    });

    describe('Subject Types Section', () => {
        it('renders subject section heading', () => {
            renderLibraryLanding();

            expect(screen.getByRole('heading', { name: 'Subjects', level: 2 })).toBeInTheDocument();
        });

        it('renders subject type cards', () => {
            renderLibraryLanding();

            const subjectSection = screen.getByRole('region', { name: 'Subjects' });

            expect(within(subjectSection).getByText('Actors')).toBeInTheDocument();
            expect(within(subjectSection).getByText('Studios')).toBeInTheDocument();
        });

        it('creates correct links to subject type pages', () => {
            renderLibraryLanding();

            const actorsLink = screen.getByRole('link', { name: /actors/i });
            const studiosLink = screen.getByRole('link', { name: /studios/i });

            expect(actorsLink).toHaveAttribute('href', '/library/subjects/actors');
            expect(studiosLink).toHaveAttribute('href', '/library/subjects/studios');
        });

        it('has link to all subjects page', () => {
            renderLibraryLanding();

            const viewAllLink = screen.getByRole('link', { name: /view all subjects/i });

            expect(viewAllLink).toHaveAttribute('href', '/library/subjects/all');
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

        it('hides view all links when loading', () => {
            mockUseLibraryTypes.mockReturnValue({
                contentTypes: [],
                subjectTypes: [],
                isLoading: true,
                error: null,
                refetch: vi.fn(),
            });
            mockUseLibraryCounts.mockReturnValue({ counts: {} });

            renderLibraryLanding();

            expect(screen.queryByRole('link', { name: /view all content/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('link', { name: /view all subjects/i })).not.toBeInTheDocument();
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

            expect(screen.getByText(/no subject types registered/i)).toBeInTheDocument();
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

            expect(cards.length).toBe(4);
        });
    });

    describe('Item Counts', () => {
        it('displays item counts on cards when available', () => {
            renderLibraryLanding();

            expect(screen.getByText('42 items')).toBeInTheDocument();
            expect(screen.getByText('150 items')).toBeInTheDocument();
            expect(screen.getByText('85 items')).toBeInTheDocument();
            expect(screen.getByText('12 items')).toBeInTheDocument();
        });

        it('displays singular "1 item" for count of 1', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 1, shows: 0, actors: 1, studios: 0 },
            });

            renderLibraryLanding();

            const items = screen.getAllByText('1 item');
            expect(items.length).toBe(2);
        });

        it('displays "0 items" for empty types', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding();

            const zeroItems = screen.getAllByText('0 items');
            expect(zeroItems.length).toBe(4);
        });

        it('shows count skeletons while counts are loading', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: undefined, shows: undefined, actors: undefined, studios: undefined },
            });

            renderLibraryLanding();

            const countSkeletons = screen.getAllByTestId('library-landing__count-skeleton');
            expect(countSkeletons.length).toBe(4);
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

        it('shows Go to Wishlists button for Requester role', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding(Role.Requester);

            expect(screen.getByRole('button', { name: /go to wishlists/i })).toBeInTheDocument();
        });

        it('shows both buttons for Admin role', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding(Role.Admin);

            expect(screen.getByRole('button', { name: /go to wishlists/i })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /connect a media server/i })).toBeInTheDocument();
        });

        it('shows only Go to Wishlists button for Manager role', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding(Role.Manager);

            expect(screen.getByRole('button', { name: /go to wishlists/i })).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /connect a media server/i })).not.toBeInTheDocument();
        });

        it('shows no action buttons for Viewer role', () => {
            mockUseLibraryCounts.mockReturnValue({
                counts: { movies: 0, shows: 0, actors: 0, studios: 0 },
            });

            renderLibraryLanding(Role.Viewer);

            expect(screen.queryByRole('button', { name: /go to wishlists/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /connect a media server/i })).not.toBeInTheDocument();
        });
    });

    describe('Accessibility', () => {
        it('has properly labeled sections', () => {
            renderLibraryLanding();

            expect(screen.getByRole('region', { name: 'Content' })).toBeInTheDocument();
            expect(screen.getByRole('region', { name: 'Subjects' })).toBeInTheDocument();
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
