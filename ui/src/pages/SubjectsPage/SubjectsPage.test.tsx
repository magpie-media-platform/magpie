import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { SubjectsPage } from './SubjectsPage';
import type { ApiSubject, ApiMetadataContainer } from '@/hooks/useSubjects';

const mockNavigate = vi.fn();

vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual('react-router-dom');
    return {
        ...actual,
        useNavigate: () => mockNavigate,
    };
});

const mockRefetch = vi.fn();
vi.mock('@/hooks/useSubjects', () => ({
    useSubjects: vi.fn(),
    useSubject: vi.fn(),
}));

vi.mock('@/hooks/useTags', () => ({
    useTags: vi.fn(),
    useTagsByPrefix: vi.fn(),
    useTagsByEntity: vi.fn(),
}));

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

import { useSubjects } from '@/hooks/useSubjects';
import { useTags } from '@/hooks/useTags';

function createMockMetadataContainer(title: string, imageUrl?: string): ApiMetadataContainer {
    return {
        id: `mc-${title.toLowerCase().replace(/\s+/g, '-')}`,
        createdAt: '2024-01-01T00:00:00Z',
        contributions: [
            {
                id: `contrib-${title.toLowerCase().replace(/\s+/g, '-')}`,
                ordinal: 0,
                locked: false,
                providerType: 'tmdb',
                title,
                images: imageUrl ? [{ type: 'poster', url: imageUrl }] : [],
            },
        ],
    };
}

const mockApiSubjects: ApiSubject[] = [
    {
        id: 'subject-1',
        metadataContainerId: 'mc-1',
        kind: 'show',
        dateLastRefreshed: '2024-01-20T14:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Breaking Bad', 'https://example.com/bb.jpg'),
    },
    {
        id: 'subject-2',
        metadataContainerId: 'mc-2',
        kind: 'show',
        dateLastRefreshed: '2024-01-18T09:30:00Z',
        createdAt: '2024-01-02T00:00:00Z',
        metadataContainer: createMockMetadataContainer('The Office', 'https://example.com/office.jpg'),
    },
    {
        id: 'subject-3',
        metadataContainerId: 'mc-3',
        kind: 'movie',
        dateLastRefreshed: '2024-01-26T12:00:00Z',
        createdAt: '2024-01-03T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Dune: Part Two', 'https://example.com/dune.jpg'),
    },
    {
        id: 'subject-4',
        metadataContainerId: 'mc-4',
        kind: 'person',
        dateLastRefreshed: '2024-01-15T10:00:00Z',
        createdAt: '2024-01-04T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Bryan Cranston'),
    },
    {
        id: 'subject-5',
        metadataContainerId: 'mc-5',
        kind: 'movie',
        dateLastRefreshed: '2024-01-15T10:00:00Z',
        createdAt: '2024-01-05T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Oppenheimer', 'https://example.com/opp.jpg'),
    },
    {
        id: 'subject-6',
        metadataContainerId: 'mc-6',
        kind: 'show',
        dateLastRefreshed: '2024-01-24T15:30:00Z',
        createdAt: '2024-01-06T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Shogun', 'https://example.com/shogun.jpg'),
    },
    {
        id: 'subject-7',
        metadataContainerId: 'mc-7',
        kind: 'movie',
        dateLastRefreshed: '2024-01-12T17:30:00Z',
        createdAt: '2024-01-07T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Poor Things', 'https://example.com/poor.jpg'),
    },
    {
        id: 'subject-8',
        metadataContainerId: 'mc-8',
        kind: 'show',
        dateLastRefreshed: '2024-01-26T09:00:00Z',
        createdAt: '2024-01-08T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Fallout'),
    },
    {
        id: 'subject-9',
        metadataContainerId: 'mc-9',
        kind: 'show',
        dateLastRefreshed: '2024-01-22T11:15:00Z',
        createdAt: '2024-01-09T00:00:00Z',
        metadataContainer: createMockMetadataContainer('The Bear', 'https://example.com/bear.jpg'),
    },
    {
        id: 'subject-10',
        metadataContainerId: 'mc-10',
        kind: 'person',
        dateLastRefreshed: '2024-01-20T14:00:00Z',
        createdAt: '2024-01-10T00:00:00Z',
        metadataContainer: createMockMetadataContainer('Denis Villeneuve'),
    },
];

const mockApiTags = [{ name: 'Favorites' }, { name: 'Watch Later' }, { name: 'Family' }, { name: 'Action' }];

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

function renderSubjectsPage(initialSearchParams?: string) {
    const queryClient = createTestQueryClient();
    const initialEntries = initialSearchParams ? [`/?${initialSearchParams}`] : ['/'];

    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={initialEntries}>
                <SubjectsPage />
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('SubjectsPage', () => {
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
        mockNavigate.mockClear();
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);

        vi.mocked(useSubjects).mockReturnValue({
            subjects: mockApiSubjects,
            isLoading: false,
            error: null,
            updateSubjectTags: vi.fn(),
            isUpdating: false,
            deleteSubject: vi.fn(),
            isDeleting: false,
            refetch: mockRefetch,
        });

        vi.mocked(useTags).mockReturnValue({
            tags: mockApiTags,
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the subjects page with correct data-page attribute', () => {
        renderSubjectsPage();

        expect(document.querySelector('[data-page="subjects"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        renderSubjectsPage();

        expect(screen.getByRole('heading', { name: 'Subjects', level: 1 })).toBeInTheDocument();
    });

    it('renders the subject list component', () => {
        renderSubjectsPage();

        expect(document.querySelector('[data-component="subject-list"]')).toBeInTheDocument();
    });

    it('renders the subject filters component', () => {
        renderSubjectsPage();

        expect(document.querySelector('[data-component="subject-filters"]')).toBeInTheDocument();
    });

    it('renders the subject sorting component', () => {
        renderSubjectsPage();

        expect(document.querySelector('[data-component="subject-sorting"]')).toBeInTheDocument();
    });

    describe('subject items', () => {
        it('renders subjects from API', () => {
            renderSubjectsPage();

            expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
            expect(screen.getByText('The Office')).toBeInTheDocument();
            expect(screen.getByText('Dune: Part Two')).toBeInTheDocument();
            expect(screen.getByText('Bryan Cranston')).toBeInTheDocument();
        });

        it('renders subject kinds', () => {
            renderSubjectsPage();

            expect(screen.getAllByText('Show').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Movie').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Person').length).toBeGreaterThan(0);
        });

        it('shows loading state', () => {
            vi.mocked(useSubjects).mockReturnValue({
                subjects: [],
                isLoading: true,
                error: null,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            });

            renderSubjectsPage();

            expect(document.querySelector('[data-component="subject-list"]')).toBeInTheDocument();
        });
    });

    describe('navigation', () => {
        it('navigates to subject details when row is clicked', async () => {
            const user = userEvent.setup();
            renderSubjectsPage();

            const row = screen.getByText('Breaking Bad').closest('tr');
            expect(row).toBeInTheDocument();

            await user.click(row!);

            expect(mockNavigate).toHaveBeenCalledWith('/subjects/subject-1');
        });
    });

    describe('accessibility', () => {
        it('has icon marked as decorative', () => {
            renderSubjectsPage();

            const icon = screen.getByTestId('subjects-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });

        it('renders a table with grid role', () => {
            renderSubjectsPage();

            expect(screen.getByRole('grid')).toBeInTheDocument();
        });
    });

    describe('error state', () => {
        it('shows error with retry button when API fails and no data', () => {
            vi.mocked(useSubjects).mockReturnValue({
                subjects: [],
                isLoading: false,
                error: new Error('Network Error'),
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            });

            renderSubjectsPage();

            expect(screen.getByText('Failed to load subjects')).toBeInTheDocument();
            expect(screen.getByText('Network Error')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
        });

        it('calls refetch when retry button is clicked', async () => {
            const user = userEvent.setup();

            vi.mocked(useSubjects).mockReturnValue({
                subjects: [],
                isLoading: false,
                error: new Error('Network Error'),
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            });

            renderSubjectsPage();

            await user.click(screen.getByRole('button', { name: 'Retry' }));

            expect(mockRefetch).toHaveBeenCalledTimes(1);
        });

        it('shows subject list when error exists but data is available', () => {
            vi.mocked(useSubjects).mockReturnValue({
                subjects: mockApiSubjects,
                isLoading: false,
                error: new Error('Stale data warning'),
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            });

            renderSubjectsPage();

            expect(screen.queryByText('Failed to load subjects')).not.toBeInTheDocument();
            expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        });
    });

    describe('extension slot', () => {
        it('renders the subject list slot', () => {
            renderSubjectsPage();

            const slotWrapper = document.querySelector('[data-slot="subject.list"]');
            expect(slotWrapper).toBeInTheDocument();
        });
    });

    describe('subject sorting', () => {
        it('sorts subjects by title ascending by default', () => {
            renderSubjectsPage();

            const rows = screen.getAllByRole('row');
            expect(rows[1]).toHaveTextContent('Breaking Bad');
        });

        it('sorts subjects by title descending when direction is toggled', async () => {
            const user = userEvent.setup();
            renderSubjectsPage();

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            await user.click(directionButton);

            const rows = screen.getAllByRole('row');
            expect(rows[1]).toHaveTextContent('The Office');
        });

        it('sorts subjects by date added', async () => {
            const user = userEvent.setup();
            renderSubjectsPage();

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            await user.click(fieldTrigger);

            const dateAddedOption = await screen.findByRole('option', { name: 'Date Added' });
            await user.click(dateAddedOption);

            const rows = screen.getAllByRole('row');
            expect(rows[1]).toHaveTextContent('Breaking Bad');
        });
    });

    describe('subject filters', () => {
        it('renders the kind filter', () => {
            renderSubjectsPage();

            expect(document.querySelector('[data-filter="kind"]')).toBeInTheDocument();
        });

        it('renders the tags filter', () => {
            renderSubjectsPage();

            expect(document.querySelector('[data-filter="tags"]')).toBeInTheDocument();
        });

        it('filters subjects by movie kind via API', async () => {
            const user = userEvent.setup();

            const movieSubjects = mockApiSubjects.filter((s) => s.kind === 'movie');
            vi.mocked(useSubjects).mockImplementation((params) => ({
                subjects: params?.kind === 'movie' ? movieSubjects : mockApiSubjects,
                isLoading: false,
                error: null,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            }));

            renderSubjectsPage();

            const kindTrigger = document.querySelector('[data-filter="kind"]') as HTMLElement;
            await user.click(kindTrigger);

            const movieOption = await screen.findByRole('option', { name: 'Movies' });
            await user.click(movieOption);

            await waitFor(() => {
                expect(screen.getByText('Dune: Part Two')).toBeInTheDocument();
                expect(screen.getByText('Oppenheimer')).toBeInTheDocument();
                expect(screen.getByText('Poor Things')).toBeInTheDocument();
            });
        });

        it('filters subjects by person kind via API', async () => {
            const user = userEvent.setup();

            const personSubjects = mockApiSubjects.filter((s) => s.kind === 'person');
            vi.mocked(useSubjects).mockImplementation((params) => ({
                subjects: params?.kind === 'person' ? personSubjects : mockApiSubjects,
                isLoading: false,
                error: null,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            }));

            renderSubjectsPage();

            const kindTrigger = document.querySelector('[data-filter="kind"]') as HTMLElement;
            await user.click(kindTrigger);

            const personOption = await screen.findByRole('option', { name: 'People' });
            await user.click(personOption);

            await waitFor(() => {
                expect(screen.getByText('Bryan Cranston')).toBeInTheDocument();
                expect(screen.getByText('Denis Villeneuve')).toBeInTheDocument();
            });
        });

        it('shows clear all button when filters are active', async () => {
            const user = userEvent.setup();
            renderSubjectsPage();

            const kindTrigger = document.querySelector('[data-filter="kind"]') as HTMLElement;
            await user.click(kindTrigger);

            const movieOption = await screen.findByRole('option', { name: 'Movies' });
            await user.click(movieOption);

            expect(screen.getByText('Clear all')).toBeInTheDocument();
        });
    });

    describe('subject search', () => {
        it('renders the search input', () => {
            renderSubjectsPage();

            expect(screen.getByRole('searchbox', { name: 'Search content by title' })).toBeInTheDocument();
        });

        it('filters subjects by search query', async () => {
            const user = userEvent.setup();
            renderSubjectsPage();

            const searchInput = screen.getByRole('searchbox');
            await user.type(searchInput, 'Breaking');

            await waitFor(() => {
                expect(screen.queryByText('The Office')).not.toBeInTheDocument();
            });

            expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
            expect(screen.queryByText('Dune: Part Two')).not.toBeInTheDocument();
        });

        it('performs case-insensitive search', async () => {
            const user = userEvent.setup();
            renderSubjectsPage();

            const searchInput = screen.getByRole('searchbox');
            await user.type(searchInput, 'breaking');

            await waitFor(() => {
                expect(screen.queryByText('The Office')).not.toBeInTheDocument();
            });

            expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        });

        it('combines search with kind filter', async () => {
            const user = userEvent.setup();

            const movieSubjects = mockApiSubjects.filter((s) => s.kind === 'movie');
            vi.mocked(useSubjects).mockImplementation((params) => ({
                subjects: params?.kind === 'movie' ? movieSubjects : mockApiSubjects,
                isLoading: false,
                error: null,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            }));

            renderSubjectsPage();

            const kindTrigger = document.querySelector('[data-filter="kind"]') as HTMLElement;
            await user.click(kindTrigger);
            const movieOption = await screen.findByRole('option', { name: 'Movies' });
            await user.click(movieOption);

            const searchInput = screen.getByRole('searchbox');
            await user.type(searchInput, 'opp');

            await waitFor(() => {
                expect(screen.queryByText('Dune: Part Two')).not.toBeInTheDocument();
            });

            expect(screen.getByText('Oppenheimer')).toBeInTheDocument();
        });

        it('shows no subjects found when search has no matches', async () => {
            const user = userEvent.setup();
            renderSubjectsPage();

            const searchInput = screen.getByRole('searchbox');
            await user.type(searchInput, 'xyznonexistent');

            await waitFor(() => {
                expect(screen.getByText('No subjects found')).toBeInTheDocument();
            });
        });
    });

    describe('URL search params persistence', () => {
        it('restores search from URL params', () => {
            renderSubjectsPage('search=Breaking');

            const searchInput = screen.getByRole('searchbox');
            expect(searchInput).toHaveValue('Breaking');

            expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
            expect(screen.queryByText('The Office')).not.toBeInTheDocument();
        });

        it('restores sort from URL params', () => {
            renderSubjectsPage('sort=dateAdded&sortDir=desc');

            const rows = screen.getAllByRole('row');
            expect(rows[1]).toHaveTextContent('Denis Villeneuve');
        });

        it('restores kind filter from URL params', () => {
            const movieSubjects = mockApiSubjects.filter((s) => s.kind === 'movie');
            vi.mocked(useSubjects).mockImplementation((params) => ({
                subjects: params?.kind === 'movie' ? movieSubjects : mockApiSubjects,
                isLoading: false,
                error: null,
                updateSubjectTags: vi.fn(),
                isUpdating: false,
                deleteSubject: vi.fn(),
                isDeleting: false,
                refetch: mockRefetch,
            }));

            renderSubjectsPage('kind=movie');

            expect(screen.getByText('Dune: Part Two')).toBeInTheDocument();
            expect(screen.getByText('Oppenheimer')).toBeInTheDocument();
            expect(screen.getByText('Poor Things')).toBeInTheDocument();
            expect(screen.queryByText('Breaking Bad')).not.toBeInTheDocument();
        });

        it('ignores invalid sort field in URL params', () => {
            renderSubjectsPage('sort=invalid&sortDir=asc');

            const rows = screen.getAllByRole('row');
            expect(rows[1]).toHaveTextContent('Breaking Bad');
        });

        it('ignores invalid sort direction in URL params', () => {
            renderSubjectsPage('sort=title&sortDir=invalid');

            const rows = screen.getAllByRole('row');
            expect(rows[1]).toHaveTextContent('Breaking Bad');
        });
    });
});
