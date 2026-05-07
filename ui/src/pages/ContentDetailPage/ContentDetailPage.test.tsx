import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ContentDetailPage } from './ContentDetailPage';
import { createApiError } from '@/lib/errors';
import { ToastProvider, ToastStateProvider, ToastList } from '@/components/ui/Toast';
import type { ContentDetailItem } from '@/types/content';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockContentData: Record<string, ContentDetailItem> = {
    'content-1': {
        id: 'content-1',
        title: 'Breaking Bad',
        posterUrl: 'https://example.com/poster1.jpg',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2008,
        overview: 'A chemistry teacher diagnosed with inoperable lung cancer turns to manufacturing.',
        subjectId: 'subject-1',
        subjectName: 'Breaking Bad',
    },
    'content-3': {
        id: 'content-3',
        title: 'Dune: Part Two',
        posterUrl: 'https://example.com/poster3.jpg',
        kind: 'movie',
        monitoring: 'active',
        fulfillment: 'missing',
        year: 2024,
        overview: 'Paul Atreides unites with Chani and the Fremen.',
    },
    'content-5': {
        id: 'content-5',
        title: 'The Gentlemen',
        posterUrl: 'https://example.com/poster5.jpg',
        kind: 'episode',
        monitoring: 'excluded',
        fulfillment: 'missing',
        year: 2024,
        overview: 'A nobleman inherits a weed empire.',
    },
    'content-8': {
        id: 'content-8',
        title: 'Poor Things',
        posterUrl: 'https://example.com/poster8.jpg',
        kind: 'movie',
        monitoring: 'excluded',
        fulfillment: 'fulfilledExternally',
        year: 2023,
        overview: 'The story of Bella Baxter.',
    },
    'content-9': {
        id: 'content-9',
        title: 'Fallout',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2024,
        overview: 'Post-apocalyptic survival.',
    },
    'season-1': {
        id: 'season-1',
        title: 'Season 1',
        posterUrl: 'https://example.com/poster1.jpg',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2008,
        overview: 'The first season of Breaking Bad.',
    },
    'episode-1': {
        id: 'episode-1',
        title: 'Pilot',
        posterUrl: 'https://example.com/poster1.jpg',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2008,
        overview: 'Walter White decides to get into the meth-making business.',
    },
};

const mockUseContentHierarchy = vi.fn();
const mockUseContentChildren = vi.fn();
const mockUseRefreshContent = vi.fn();
const mockUseSearchContentReleases = vi.fn();

vi.mock('@/hooks/useContent', () => ({
    useContentHierarchy: (activeId: string | undefined, parentId: string | undefined, rootId: string | undefined) =>
        mockUseContentHierarchy(activeId, parentId, rootId),
    useContentChildren: (id: string | undefined) => mockUseContentChildren(id),
    useRefreshContent: () => mockUseRefreshContent(),
    useSearchContentReleases: () => mockUseSearchContentReleases(),
    contentQueryKeys: {
        all: ['content'],
        list: () => ['content', 'list'],
        detail: (id: string) => ['content', 'detail', id],
        children: (id: string) => ['content', 'children', id],
    },
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

function renderWithRouter(contentId: string, childId?: string, grandchildId?: string, search?: string) {
    const queryClient = createTestQueryClient();
    const basePath = grandchildId
        ? `/library/${contentId}/${childId}/${grandchildId}`
        : childId
          ? `/library/${contentId}/${childId}`
          : `/library/${contentId}`;
    const path = search ? `${basePath}${search}` : basePath;

    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[path]}>
                <ToastProvider>
                    <ToastStateProvider>
                        <Routes>
                            <Route path="/library/:contentId" element={<ContentDetailPage />} />
                            <Route path="/library/:contentId/:childId" element={<ContentDetailPage />} />
                            <Route path="/library/:contentId/:childId/:grandchildId" element={<ContentDetailPage />} />
                        </Routes>
                        <ToastList />
                    </ToastStateProvider>
                </ToastProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('ContentDetailPage', () => {
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

    const mockMutationResult = {
        mutate: vi.fn(),
        isPending: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);

        mockUseContentHierarchy.mockImplementation(
            (activeId: string | undefined, parentId: string | undefined, rootId: string | undefined) => {
                const makeResult = (id: string | undefined) => {
                    const data = id ? mockContentData[id] : undefined;
                    return {
                        data,
                        isLoading: false,
                        error: data ? null : createApiError(404, 'Not found'),
                        refetch: vi.fn(),
                    };
                };
                return {
                    content: makeResult(activeId),
                    parent: makeResult(parentId),
                    root: makeResult(rootId),
                };
            },
        );

        mockUseContentChildren.mockReturnValue({
            data: { items: [], pageNumber: 1, pageSize: 25, totalItems: 0 },
            isLoading: false,
            error: null,
        });

        mockUseRefreshContent.mockReturnValue(mockMutationResult);
        mockUseSearchContentReleases.mockReturnValue(mockMutationResult);
    });

    describe('page structure', () => {
        it('renders the page with correct data-page attribute', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(document.querySelector('[data-page="content-detail"]')).toBeInTheDocument();
            });
        });

        it('renders library link in breadcrumbs', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                const libraryLinks = screen.getAllByRole('link', { name: /library/i });
                expect(libraryLinks.length).toBeGreaterThan(0);
                expect(libraryLinks[0]).toHaveAttribute('href', '/library');
            });
        });
    });

    describe('content display', () => {
        it('renders the content title', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Breaking Bad/i, level: 1 })).toBeInTheDocument();
            });
        });

        it('renders the content year', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByText('(2008)')).toBeInTheDocument();
            });
        });

        it('renders the poster image', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByAltText('Breaking Bad poster')).toBeInTheDocument();
            });
        });

        it('renders the monitoring status badge', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByText('Monitored')).toBeInTheDocument();
            });
        });

        it('renders the fulfillment status badge', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByText('Fulfilled')).toBeInTheDocument();
            });
        });

        it('renders the kind badge', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByText('Episode')).toBeInTheDocument();
            });
        });
    });

    describe('loading state', () => {
        it('shows loading skeleton while fetching', () => {
            mockUseContentHierarchy.mockReturnValue({
                content: { data: undefined, isLoading: true, error: null },
                parent: { data: undefined, isLoading: false, error: null },
                root: { data: undefined, isLoading: false, error: null },
            });

            renderWithRouter('content-1');
            expect(screen.getByTestId('content-detail-page__loading')).toBeInTheDocument();
        });
    });

    describe('action buttons', () => {
        it('renders the refresh button', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('button', { name: /refresh/i })).toBeInTheDocument();
            });
        });

        it('renders the search now button', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('button', { name: /search now/i })).toBeInTheDocument();
            });
        });
    });

    describe('tabs', () => {
        it('renders the Overview tab', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /overview/i })).toBeInTheDocument();
            });
        });

        it('renders the Files tab', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /files/i })).toBeInTheDocument();
            });
        });

        it('renders the History tab', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /history/i })).toBeInTheDocument();
            });
        });

        it('shows Overview tab content by default', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                const overviewTab = screen.getByRole('tabpanel');
                expect(within(overviewTab).getByText(/chemistry teacher/i)).toBeInTheDocument();
            });
        });

        it('switches to Files tab when clicked', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /files/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /files/i }));

            const tabPanel = screen.getByRole('tabpanel');
            expect(within(tabPanel).getAllByText(/no files attached/i).length).toBeGreaterThan(0);
        });

        it('switches to History tab when clicked', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /history/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /history/i }));

            const tabPanel = screen.getByRole('tabpanel');
            expect(within(tabPanel).getAllByText(/no history/i).length).toBeGreaterThan(0);
        });

        it('restores active tab from ?tab= search param', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=files');

            await waitFor(() => {
                const filesTab = screen.getByRole('tab', { name: /files/i });
                expect(filesTab).toHaveAttribute('aria-selected', 'true');
            });

            const tabPanel = screen.getByRole('tabpanel');
            expect(within(tabPanel).getAllByText(/no files attached/i).length).toBeGreaterThan(0);
        });

        it('defaults to overview when ?tab= param is absent', async () => {
            renderWithRouter('content-1');

            await waitFor(() => {
                const overviewTab = screen.getByRole('tab', { name: /overview/i });
                expect(overviewTab).toHaveAttribute('aria-selected', 'true');
            });
        });
    });

    describe('children tab visibility', () => {
        it('shows Seasons tab when content has children', async () => {
            mockUseContentChildren.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            kind: 'episode',
                            monitoring: 'active',
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });
        });

        it('hides children tab when content has no children', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /overview/i })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /seasons/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('tab', { name: /episodes/i })).not.toBeInTheDocument();
        });

        it('hides children tab for movies even if children data is empty', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /overview/i })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /seasons/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('tab', { name: /episodes/i })).not.toBeInTheDocument();
        });

        it('shows Episodes tab when viewing a season with children', async () => {
            mockUseContentChildren.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'episode-1',
                            title: 'Pilot',
                            kind: 'episode',
                            monitoring: 'active',
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1', 'season-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /episodes/i })).toBeInTheDocument();
            });
        });
    });

    describe('Overview tab', () => {
        it('displays overview text', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getAllByText(/chemistry teacher/i).length).toBeGreaterThan(0);
            });
        });
    });

    describe('different content items', () => {
        it('renders movie content correctly', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Dune: Part Two/i })).toBeInTheDocument();
                expect(screen.getByText('Movie')).toBeInTheDocument();
                expect(screen.getByText('Missing')).toBeInTheDocument();
            });
        });

        it('renders unmonitored content correctly', async () => {
            renderWithRouter('content-5');
            await waitFor(() => {
                expect(screen.getByText('Not Monitored')).toBeInTheDocument();
            });
        });

        it('renders external fulfillment status correctly', async () => {
            renderWithRouter('content-8');
            await waitFor(() => {
                expect(screen.getByText('External')).toBeInTheDocument();
            });
        });

        it('renders content without poster', async () => {
            renderWithRouter('content-9');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Fallout/i })).toBeInTheDocument();
                expect(screen.getByTestId('content-details-header__poster--placeholder')).toBeInTheDocument();
            });
        });
    });

    describe('not found state', () => {
        it('shows not found message for unknown content ID', async () => {
            renderWithRouter('nonexistent-id');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Content Not Found/i })).toBeInTheDocument();
                expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
            });
        });

        it('shows back to library link on not found page', async () => {
            renderWithRouter('nonexistent-id');
            await waitFor(() => {
                const backLink = screen.getByRole('link', { name: /library/i });
                expect(backLink).toBeInTheDocument();
                expect(backLink).toHaveAttribute('href', '/library');
            });
        });
    });

    describe('error state (non-404)', () => {
        const errorHierarchy = (contentOverride: Record<string, unknown>) => ({
            content: contentOverride,
            parent: { data: undefined, isLoading: false, error: null, refetch: vi.fn() },
            root: { data: undefined, isLoading: false, error: null, refetch: vi.fn() },
        });

        it('shows failed to load message for server errors', async () => {
            mockUseContentHierarchy.mockReturnValue(
                errorHierarchy({
                    data: undefined,
                    isLoading: false,
                    error: createApiError(500, 'Internal Server Error'),
                    refetch: vi.fn(),
                }),
            );

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Failed to Load Content/i })).toBeInTheDocument();
                expect(screen.getByText(/error occurred while loading/i)).toBeInTheDocument();
            });
        });

        it('shows retry button instead of back link for non-404 errors', async () => {
            mockUseContentHierarchy.mockReturnValue(
                errorHierarchy({
                    data: undefined,
                    isLoading: false,
                    error: createApiError(500, 'Internal Server Error'),
                    refetch: vi.fn(),
                }),
            );

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
                expect(screen.queryByRole('link', { name: /library/i })).not.toBeInTheDocument();
            });
        });

        it('calls refetch when retry button is clicked', async () => {
            const mockRefetch = vi.fn();
            mockUseContentHierarchy.mockReturnValue(
                errorHierarchy({
                    data: undefined,
                    isLoading: false,
                    error: createApiError(502, 'Bad Gateway'),
                    refetch: mockRefetch,
                }),
            );

            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('button', { name: /retry/i }));
            expect(mockRefetch).toHaveBeenCalled();
        });
    });

    describe('extension slot', () => {
        it('renders the content details header slot', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                const slotWrapper = document.querySelector('[data-slot="content.details.header"]');
                expect(slotWrapper).toBeInTheDocument();
            });
        });
    });

    describe('accessibility', () => {
        it('has navigation regions for breadcrumbs', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                const navRegions = screen.getAllByRole('navigation');
                expect(navRegions.length).toBeGreaterThan(0);
                expect(screen.getByRole('navigation', { name: /breadcrumb/i })).toBeInTheDocument();
            });
        });

        it('has header element for content header', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('banner')).toBeInTheDocument();
            });
        });

        it('includes data attribute for content ID', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                const header = document.querySelector('[data-content-id="content-1"]');
                expect(header).toBeInTheDocument();
            });
        });

        it('has accessible tab list', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tablist')).toBeInTheDocument();
            });
        });

        it('tabs are keyboard navigable', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /overview/i })).toBeInTheDocument();
            });

            await user.tab();
            expect(document.activeElement).toBeInTheDocument();
        });
    });

    describe('nested routes', () => {
        it('renders season content when childId is provided', async () => {
            renderWithRouter('content-1', 'season-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Season 1/i })).toBeInTheDocument();
            });
        });

        it('renders breadcrumbs showing full hierarchy', async () => {
            renderWithRouter('content-1', 'season-1');
            await waitFor(() => {
                const breadcrumbNav = screen.getByRole('navigation', { name: /breadcrumb/i });
                expect(breadcrumbNav).toBeInTheDocument();
            });
        });

        it('renders episode content when grandchildId is provided', async () => {
            renderWithRouter('content-1', 'season-1', 'episode-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Pilot/i })).toBeInTheDocument();
            });
        });

        it('parent link shows parent name', async () => {
            renderWithRouter('content-1', 'season-1');
            await waitFor(() => {
                const parentLink = screen.getByTestId('content-details-header__parent-link');
                expect(parentLink).toBeInTheDocument();
                expect(parentLink?.textContent).toContain('Breaking Bad');
            });
        });
    });
});
