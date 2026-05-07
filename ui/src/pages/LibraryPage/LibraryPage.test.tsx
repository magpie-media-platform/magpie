import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { LibraryPage } from './LibraryPage';
import { ToastProvider, ToastStateProvider, ToastList } from '@/components/ui/Toast';
import { AuthProvider, Role } from '@/auth';
import type { ContentListResult } from '@/hooks/useContent';
import type { ContentListItem } from '@/types/content';

const mockNavigate = vi.fn();

vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual('react-router-dom');
    return {
        ...actual,
        useNavigate: () => mockNavigate,
    };
});

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockApiTags = [{ name: 'Favorites' }, { name: 'Watch Later' }, { name: 'Family' }, { name: 'Action' }];

const mockContentItems: ContentListItem[] = [
    {
        id: 'content-1',
        title: 'Breaking Bad',
        posterUrl: 'https://example.com/poster1.jpg',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2008,
        tags: ['Favorites'],
        dateAdded: '2024-01-15T10:30:00Z',
        lastSearched: '2024-01-20T14:00:00Z',
    },
    {
        id: 'content-2',
        title: 'The Office',
        posterUrl: 'https://example.com/poster2.jpg',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'upgradable',
        year: 2005,
        tags: ['Family'],
        dateAdded: '2024-01-10T08:00:00Z',
        lastSearched: '2024-01-18T09:30:00Z',
    },
    {
        id: 'content-3',
        title: 'Dune: Part Two',
        posterUrl: 'https://example.com/poster3.jpg',
        kind: 'movie',
        monitoring: 'active',
        fulfillment: 'missing',
        year: 2024,
        tags: ['Watch Later', 'Action'],
        dateAdded: '2024-01-25T16:45:00Z',
        lastSearched: '2024-01-26T12:00:00Z',
    },
    {
        id: 'content-4',
        title: 'Shogun',
        posterUrl: 'https://example.com/poster4.jpg',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2024,
        tags: ['Favorites', 'Action'],
        dateAdded: '2024-01-22T11:00:00Z',
        lastSearched: '2024-01-24T15:30:00Z',
    },
    {
        id: 'content-5',
        title: 'The Gentlemen',
        posterUrl: 'https://example.com/poster5.jpg',
        kind: 'episode',
        monitoring: 'excluded',
        fulfillment: 'missing',
        year: 2024,
        tags: ['Watch Later'],
        dateAdded: '2024-01-20T09:15:00Z',
        lastSearched: undefined,
    },
    {
        id: 'content-6',
        title: '3 Body Problem',
        posterUrl: 'https://example.com/poster6.jpg',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'upgradable',
        year: 2024,
        dateAdded: '2024-01-23T14:20:00Z',
        lastSearched: '2024-01-25T08:00:00Z',
    },
    {
        id: 'content-7',
        title: 'Oppenheimer',
        posterUrl: 'https://example.com/poster7.jpg',
        kind: 'movie',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2023,
        tags: ['Favorites'],
        dateAdded: '2024-01-05T12:00:00Z',
        lastSearched: '2024-01-15T10:00:00Z',
    },
    {
        id: 'content-8',
        title: 'Poor Things',
        posterUrl: 'https://example.com/poster8.jpg',
        kind: 'movie',
        monitoring: 'excluded',
        fulfillment: 'fulfilledExternally',
        year: 2023,
        dateAdded: '2024-01-12T17:30:00Z',
        lastSearched: '2024-01-12T17:30:00Z',
    },
    {
        id: 'content-9',
        title: 'Fallout',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2024,
        tags: ['Action'],
        dateAdded: '2024-01-24T10:00:00Z',
        lastSearched: '2024-01-26T09:00:00Z',
    },
    {
        id: 'content-10',
        title: 'The Bear',
        posterUrl: 'https://example.com/poster10.jpg',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2022,
        tags: ['Favorites', 'Family'],
        dateAdded: '2024-01-08T13:45:00Z',
        lastSearched: '2024-01-22T11:15:00Z',
    },
];

const mockUseContentList = vi.fn();
const mockUseTags = vi.fn();

vi.mock('@/hooks/useContent', () => ({
    useContentList: (params: unknown) => mockUseContentList(params),
    useContentTags: () => mockUseTags(),
    contentQueryKeys: {
        all: ['content'],
        list: () => ['content', 'list'],
    },
    useBulkUpdateContent: () => ({
        mutateAsync: vi.fn(),
        isPending: false,
        error: null,
    }),
}));

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

function renderLibraryPage(role: Role = Role.Admin) {
    const queryClient = createTestQueryClient();
    const mockUser = { id: 'user-1', username: 'testuser', role };

    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <AuthProvider initialUser={mockUser}>
                    <ToastProvider>
                        <ToastStateProvider>
                            <LibraryPage />
                            <ToastList />
                        </ToastStateProvider>
                    </ToastProvider>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('LibraryPage', () => {
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

    const defaultContentListResult: ContentListResult = {
        items: mockContentItems,
        pageNumber: 1,
        pageSize: 100,
        totalItems: mockContentItems.length,
    };

    beforeEach(() => {
        mockNavigate.mockClear();
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);

        mockUseContentList.mockReturnValue({
            data: defaultContentListResult,
            isLoading: false,
            error: null,
        });

        mockUseTags.mockReturnValue({
            data: mockApiTags,
            isLoading: false,
            error: null,
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the library page with correct data-page attribute', () => {
        renderLibraryPage();
        expect(document.querySelector('[data-page="library"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        renderLibraryPage();
        expect(screen.getByRole('heading', { name: 'Library', level: 1 })).toBeInTheDocument();
    });

    it('renders the content list component', () => {
        renderLibraryPage();
        expect(document.querySelector('[data-component="content-list"]')).toBeInTheDocument();
    });

    it('shows loading state while fetching data', () => {
        mockUseContentList.mockReturnValue({
            data: undefined,
            isLoading: true,
            error: null,
        });

        renderLibraryPage();

        expect(screen.getByTestId('library-page__loading')).toBeInTheDocument();
    });

    it('shows error state when fetch fails', () => {
        mockUseContentList.mockReturnValue({
            data: undefined,
            isLoading: false,
            error: new Error('Failed to fetch'),
        });

        renderLibraryPage();
        expect(screen.getByText('Failed to load content')).toBeInTheDocument();
    });

    describe('content items', () => {
        it('renders content items', () => {
            renderLibraryPage();

            expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
            expect(screen.getByText('The Office')).toBeInTheDocument();
            expect(screen.getByText('Dune: Part Two')).toBeInTheDocument();
            expect(screen.getByText('Shogun')).toBeInTheDocument();
        });

        it('renders content years', () => {
            renderLibraryPage();

            expect(screen.getByText('2008')).toBeInTheDocument();
            expect(screen.getByText('2005')).toBeInTheDocument();
            expect(screen.getAllByText('2024').length).toBeGreaterThan(0);
        });

        it('renders fulfillment status badges', () => {
            renderLibraryPage();

            expect(screen.getAllByText('Fulfilled').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Missing').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Upgradable').length).toBeGreaterThan(0);
            expect(screen.getAllByText('External').length).toBeGreaterThan(0);
        });

        it('renders kind badges', () => {
            renderLibraryPage();

            expect(screen.getAllByText('Episode').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Movie').length).toBeGreaterThan(0);
        });
    });

    describe('navigation', () => {
        it('navigates to content details when row is clicked', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const row = screen.getByText('Breaking Bad').closest('tr');
            expect(row).toBeInTheDocument();

            await user.click(row!);

            expect(mockNavigate).toHaveBeenCalledWith('/library/content-1');
        });
    });

    describe('accessibility', () => {
        it('has icon marked as decorative', () => {
            renderLibraryPage();

            const icon = screen.getByTestId('library-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });

        it('renders a table with grid role', () => {
            renderLibraryPage();

            expect(screen.getByRole('grid')).toBeInTheDocument();
        });
    });

    describe('extension slot', () => {
        it('renders the content list slot', () => {
            renderLibraryPage();

            const slotWrapper = document.querySelector('[data-slot="content.list"]');
            expect(slotWrapper).toBeInTheDocument();
        });
    });

    describe('content sorting', () => {
        it('renders the content sorting component', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-component="content-sorting"]')).toBeInTheDocument();
        });

        it('passes title ascending sort params to the server by default', () => {
            renderLibraryPage();

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ sortBy: 'Name', sortDirection: 'Ascending' }),
            );
        });

        it('passes title descending sort params when direction is toggled', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            await user.click(directionButton);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ sortBy: 'Name', sortDirection: 'Descending' }),
                );
            });
        });

        it('passes date added sort params to the server', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            await user.click(fieldTrigger);

            const dateAddedOption = await screen.findByRole('option', { name: 'Date Added' });
            await user.click(dateAddedOption);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ sortBy: 'CreatedAt', sortDirection: 'Ascending' }),
                );
            });
        });

        it('sorts content by last searched', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            await user.click(fieldTrigger);

            const lastSearchedOption = await screen.findByRole('option', { name: 'Last Searched' });
            await user.click(lastSearchedOption);

            const rows = screen.getAllByRole('row');

            expect(rows[1]).toHaveTextContent('The Gentlemen');
        });
    });

    describe('content filters', () => {
        it('renders the filter bar component', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-component="filter-bar"]')).toBeInTheDocument();
        });

        it('renders filter toggle button', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-action="toggle-filter"]')).toBeInTheDocument();
        });
    });

    describe('content search', () => {
        it('renders the content search component', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-component="content-search"]')).toBeInTheDocument();
        });

        it('renders search input', () => {
            renderLibraryPage();

            expect(screen.getByRole('searchbox', { name: 'Search content by title' })).toBeInTheDocument();
        });
    });

    describe('multi-select', () => {
        it('renders checkboxes for row selection', () => {
            renderLibraryPage();

            const checkboxes = screen.getAllByRole('checkbox');
            expect(checkboxes.length).toBeGreaterThan(1);
        });

        it('does not show bulk actions toolbar initially', () => {
            renderLibraryPage();

            expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).not.toBeInTheDocument();
        });

        it('shows bulk actions toolbar when items are selected', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            await waitFor(() => {
                expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).toBeInTheDocument();
            });
        });

        it('shows selected count in bulk actions toolbar', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox1 = screen.getByLabelText('Select Breaking Bad');
            const checkbox2 = screen.getByLabelText('Select The Office');
            await user.click(checkbox1);
            await user.click(checkbox2);

            await waitFor(() => {
                expect(screen.getByText('2 selected')).toBeInTheDocument();
            });
        });

        it('clears selection when clear button is clicked', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            await waitFor(() => {
                expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).toBeInTheDocument();
            });

            const clearButton = document.querySelector('[data-action="clear-selection"]') as HTMLElement;
            await user.click(clearButton);

            await waitFor(() => {
                expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).not.toBeInTheDocument();
            });
        });

        it('has Assign Tags button in bulk actions', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            await waitFor(() => {
                const tagButton = document.querySelector('[data-action="bulk-tags"]');
                expect(tagButton).toBeInTheDocument();
            });
        });

        it('opens bulk tag assignment sheet when Assign Tags is clicked', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            const tagButton = await waitFor(() => {
                return document.querySelector('[data-action="bulk-tags"]') as HTMLElement;
            });
            await user.click(tagButton);

            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Assign Tags/i })).toBeInTheDocument();
            });
        });

        it('selects all items when select-all checkbox is clicked', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const selectAllCheckbox = screen.getByLabelText('Select all');
            await user.click(selectAllCheckbox);

            await waitFor(() => {
                expect(screen.getByText('10 selected')).toBeInTheDocument();
            });
        });

        it('selecting row does not navigate to content details', async () => {
            const user = userEvent.setup();
            renderLibraryPage();

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            expect(mockNavigate).not.toHaveBeenCalled();
        });
    });

    describe('URL search params persistence', () => {
        function renderWithParams(params: string) {
            const queryClient = createTestQueryClient();

            return render(
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={[`/library/all${params}`]}>
                        <ToastProvider>
                            <ToastStateProvider>
                                <LibraryPage />
                                <ToastList />
                            </ToastStateProvider>
                        </ToastProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
            );
        }

        it('pre-populates filter from URL param', () => {
            renderWithParams(`?filter=${encodeURIComponent('genre has "Action"')}`);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });

        it('auto-expands filter bar when URL param is present', () => {
            renderWithParams(`?filter=${encodeURIComponent('year >= 2020')}`);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });

        it('filter bar is collapsed when no URL param', () => {
            renderLibraryPage();

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'false');
        });

        it('restores sort field from URL param', () => {
            renderWithParams('?sort=dateAdded');

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            expect(fieldTrigger).toHaveTextContent('Date Added');
        });

        it('restores sort direction from URL param', () => {
            renderWithParams('?sortDir=desc');

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            expect(directionButton).toHaveAttribute('data-direction', 'desc');
        });

        it('restores search from URL param', () => {
            renderWithParams('?search=Breaking');

            const searchInput = screen.getByRole('searchbox', { name: 'Search content by title' });
            expect(searchInput).toHaveValue('Breaking');
        });

        it('uses default sort when URL params are absent', () => {
            renderLibraryPage();

            expect(mockUseContentList).toHaveBeenLastCalledWith(
                expect.objectContaining({ sortBy: 'Name', sortDirection: 'Ascending' }),
            );
        });

        it('ignores invalid sort field in URL', () => {
            renderWithParams('?sort=invalid');

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            expect(fieldTrigger).toHaveTextContent('Title');
        });

        it('ignores invalid sort direction in URL', () => {
            renderWithParams('?sortDir=invalid');

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            expect(directionButton).toHaveAttribute('data-direction', 'asc');
        });

        it('persists sort change to URL', async () => {
            const user = userEvent.setup();
            renderWithParams('');

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            await user.click(fieldTrigger);
            const dateAddedOption = await screen.findByRole('option', { name: 'Date Added' });
            await user.click(dateAddedOption);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ sortBy: 'CreatedAt', sortDirection: 'Ascending' }),
                );
            });
        });

        it('persists sort direction toggle to URL', async () => {
            const user = userEvent.setup();
            renderWithParams('');

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            await user.click(directionButton);

            await waitFor(() => {
                expect(mockUseContentList).toHaveBeenLastCalledWith(
                    expect.objectContaining({ sortBy: 'Name', sortDirection: 'Descending' }),
                );
            });
        });
    });

    describe('empty states', () => {
        it('shows onboarding empty state when library is empty and no filters are active', () => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderLibraryPage();

            expect(screen.getByText('Your library is empty')).toBeInTheDocument();
            expect(
                screen.getByText(
                    'Content will appear here once it has been added. Create a wishlist to start monitoring and tracking movies or TV shows.',
                ),
            ).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /clear search and filters/i })).not.toBeInTheDocument();
        });

        it('shows no-results empty state with clear button when search returns no results', async () => {
            const user = userEvent.setup();
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderLibraryPage();

            const searchInput = screen.getByRole('searchbox', { name: 'Search content by title' });
            await user.type(searchInput, 'nonexistent');

            await waitFor(() => {
                expect(screen.getByText('No results found')).toBeInTheDocument();
            });
            expect(screen.getByText(/No content matches your current search or filters/)).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /clear search and filters/i })).toBeInTheDocument();
        });

        it('clears search and filters when clear button is clicked', async () => {
            const user = userEvent.setup();
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            renderLibraryPage();

            const searchInput = screen.getByRole('searchbox', { name: 'Search content by title' });
            await user.type(searchInput, 'nonexistent');

            await waitFor(() => {
                expect(screen.getByText('No results found')).toBeInTheDocument();
            });

            const clearButton = screen.getByRole('button', { name: /clear search and filters/i });
            await user.click(clearButton);

            await waitFor(() => {
                expect(screen.getByText('Your library is empty')).toBeInTheDocument();
            });
            expect(searchInput).toHaveValue('');
        });

        it('shows no-results empty state when filter returns no results', () => {
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 },
                isLoading: false,
                error: null,
            });

            const queryClient = createTestQueryClient();
            render(
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={['/library/all?filter=genre%20has%20%22Action%22']}>
                        <ToastProvider>
                            <ToastStateProvider>
                                <LibraryPage />
                                <ToastList />
                            </ToastStateProvider>
                        </ToastProvider>
                    </MemoryRouter>
                </QueryClientProvider>,
            );

            expect(screen.getByText('No results found')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /clear search and filters/i })).toBeInTheDocument();
        });
    });
});
