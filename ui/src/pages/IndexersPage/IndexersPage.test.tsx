import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiIndexer, IndexerFeedItem } from '@/hooks/useIndexers';

import { IndexersPage } from './IndexersPage';

const mockIndexers: ApiIndexer[] = [
    {
        id: 'idx-1',
        name: 'NZBgeek',
        providerType: 'newznab',
        availability: 1,
        categories: ['TV', 'Movies'],
        languages: ['en'],
        createdAt: '2024-01-01T00:00:00Z',
        modifiedAt: '2024-01-15T10:30:00Z',
    },
    {
        id: 'idx-2',
        name: 'DrunkenSlug',
        providerType: 'newznab',
        availability: 1,
        categories: ['TV'],
        languages: ['en'],
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'idx-3',
        name: 'IPTorrents',
        providerType: 'torznab',
        availability: 2,
        categories: ['TV', 'Movies'],
        languages: ['en'],
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'idx-4',
        name: 'Jackett',
        providerType: 'torznab',
        availability: 3,
        categories: [],
        languages: [],
        createdAt: '2024-01-03T00:00:00Z',
    },
];

const mockFeeds: IndexerFeedItem[] = [
    {
        id: 'feed-1',
        name: 'Prowlarr Sync',
        description: 'Automatic indexer sync from Prowlarr',
        provider: 'prowlarr',
        enabled: true,
        lastSync: '2024-01-15T09:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        settings: {},
    },
    {
        id: 'feed-2',
        name: 'Public Indexers',
        description: null,
        provider: 'indexerio',
        enabled: false,
        lastSync: null,
        createdAt: '2024-01-02T00:00:00Z',
        settings: {},
    },
];

const mockDeleteIndexer = vi.fn().mockResolvedValue(undefined);
const mockDeleteFeed = vi.fn().mockResolvedValue(undefined);

vi.mock('@/hooks/useIndexers', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useIndexers')>();
    return {
        ...actual,
        useIndexers: () => ({
            indexers: mockIndexers,
            feeds: mockFeeds,
            isLoading: false,
            isLoadingIndexers: false,
            isLoadingFeeds: false,
            error: null,
            feedsError: null,
            createIndexer: vi.fn().mockResolvedValue(undefined),
            updateIndexer: vi.fn().mockResolvedValue(undefined),
            deleteIndexer: mockDeleteIndexer,
            isCreatingIndexer: false,
            isUpdatingIndexer: false,
            isDeletingIndexer: false,
            createFeed: vi.fn().mockResolvedValue(undefined),
            updateFeed: vi.fn().mockResolvedValue(undefined),
            toggleFeedEnabled: vi.fn().mockResolvedValue(undefined),
            deleteFeed: mockDeleteFeed,
            isCreatingFeed: false,
            isUpdatingFeed: false,
            isTogglingFeed: false,
            isDeletingFeed: false,
            refetch: vi.fn(),
            refetchIndexers: vi.fn(),
            refetchFeeds: vi.fn(),
        }),
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

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = createTestQueryClient();
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <TooltipProvider>
                    <ToastProvider>
                        <ToastStateProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                        </ToastStateProvider>
                    </ToastProvider>
                </TooltipProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

describe('IndexersPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders with correct data-page attribute', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-page="indexers"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('heading', { name: 'Indexers', level: 1 })).toBeInTheDocument();
    });

    it('renders the indexer count badge in the page title', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const count = screen.getByTestId('indexers-page__count');
        expect(count).toBeInTheDocument();
        expect(count).toHaveTextContent('4');
    });

    it('renders the indexer count in the tab', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const indexersTab = document.querySelector('[data-tab="indexers"]');
        const count = indexersTab?.querySelector('[data-testid="indexers-page__tab-count"]');
        expect(count).toBeInTheDocument();
        expect(count).toHaveTextContent('4');
    });

    it('renders the page icon as decorative', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const icon = screen.getByTestId('indexers-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });

    it('renders an Add Indexer button', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const addButtons = screen.getAllByRole('button', { name: /add indexer/i });
        expect(addButtons.length).toBeGreaterThanOrEqual(1);
    });
});

describe('IndexersPage summary badges', () => {
    it('renders summary badges for each availability level', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(screen.getAllByText('Public').length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText('Semi-Private').length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText('Private').length).toBeGreaterThanOrEqual(1);
    });

    it('applies data-status attributes to summary badges', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Availability summary"]');
        const badges = summary?.querySelectorAll('[data-component="badge"]') ?? [];
        const statuses = Array.from(badges).map((b) => b.getAttribute('data-status'));

        expect(statuses).toContain('healthy');
        expect(statuses).toContain('degraded');
        expect(statuses).toContain('unhealthy');
    });

    it('renders badge dots as decorative', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Availability summary"]');
        const dots = summary?.querySelectorAll('[data-component="badge"] [aria-hidden="true"]') ?? [];
        dots.forEach((dot) => {
            expect(dot).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('shows correct count for public indexers', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Availability summary"]');
        const publicBadge = summary?.querySelector('[data-component="badge"][data-status="healthy"]');
        const count = publicBadge?.querySelector('[data-component="badge"] .badge__count');

        expect(count).toHaveTextContent('2');
    });
});

describe('IndexersPage list items', () => {
    it('renders indexer items in the list', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const list = screen.getByRole('list', { name: /indexers/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(4);
    });

    it('renders indexer names', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(screen.getByText('NZBgeek')).toBeInTheDocument();
        expect(screen.getByText('DrunkenSlug')).toBeInTheDocument();
        expect(screen.getByText('IPTorrents')).toBeInTheDocument();
        expect(screen.getByText('Jackett')).toBeInTheDocument();
    });

    it('renders provider types', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(screen.getAllByText('newznab').length).toBeGreaterThanOrEqual(2);
        expect(screen.getAllByText('torznab').length).toBeGreaterThanOrEqual(2);
    });

    it('applies data-indexer-id to items', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const items = document.querySelectorAll('[data-indexer-id]');
        expect(items.length).toBe(4);
        expect(items[0]).toHaveAttribute('data-indexer-id', 'idx-1');
    });

    it('applies data-availability to items', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const publicItems = document.querySelectorAll('[data-availability="healthy"]');
        expect(publicItems.length).toBe(2);
    });
});

describe('IndexersPage availability badges', () => {
    it('renders availability badges with dot indicators', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const items = screen.getAllByTestId('indexers-page__item');
        items.forEach((item) => {
            expect(item.querySelector('[data-component="badge"]')).toBeInTheDocument();
            expect(item.querySelector('[data-component="badge"] [aria-hidden="true"]')).toBeInTheDocument();
        });
    });

    it('applies correct variant to public indexers', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const nzbgeekItem = document.querySelector('[data-indexer-id="idx-1"]');
        expect(nzbgeekItem?.querySelector('[data-component="badge"][data-variant="healthy"]')).toBeInTheDocument();
    });

    it('applies correct variant to private indexers', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const privateItem = document.querySelector('[data-indexer-id="idx-3"]');
        expect(privateItem?.querySelector('[data-component="badge"][data-variant="unhealthy"]')).toBeInTheDocument();
    });

    it('applies correct variant to semi-private indexers', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const semiPrivateItem = document.querySelector('[data-indexer-id="idx-4"]');
        expect(semiPrivateItem?.querySelector('[data-component="badge"][data-variant="degraded"]')).toBeInTheDocument();
    });
});

describe('IndexersPage actions', () => {
    it('renders edit button for each indexer', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        expect(editButtons.length).toBe(4);
    });

    it('renders delete button for each indexer', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        expect(deleteButtons.length).toBe(4);
    });

    it('edit button has correct aria-label', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const editButton = screen.getByRole('button', { name: /^edit nzbgeek$/i });
        expect(editButton).toHaveAttribute('data-action', 'edit');
    });

    it('delete button has correct aria-label', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const deleteButton = screen.getByRole('button', { name: /^delete nzbgeek$/i });
        expect(deleteButton).toHaveAttribute('data-action', 'delete');
    });

    it('opens edit form when edit button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const editButton = screen.getByRole('button', { name: /^edit nzbgeek$/i });
        await user.click(editButton);

        expect(screen.getByText('Edit Indexer')).toBeInTheDocument();
        expect(screen.getByDisplayValue('NZBgeek')).toBeInTheDocument();
    });
});

describe('IndexersPage filters', () => {
    it('renders availability and provider filter dropdowns', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-filter="availability"]')).toBeInTheDocument();
        expect(document.querySelector('[data-filter="provider"]')).toBeInTheDocument();
    });

    it('availability filter displays "All Availability" by default', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const availabilityTrigger = document.querySelector('[data-filter="availability"]');
        expect(availabilityTrigger).toHaveTextContent('All Availability');
    });

    it('provider filter displays "All Providers" by default', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const providerTrigger = document.querySelector('[data-filter="provider"]');
        expect(providerTrigger).toHaveTextContent('All Providers');
    });

    it('filters items by availability when selection changes', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const availabilityTrigger = document.querySelector('[data-filter="availability"]') as HTMLElement;
        await user.click(availabilityTrigger);

        const privateOption = await screen.findByRole('option', { name: 'Private' });
        await user.click(privateOption);

        const list = screen.getByRole('list', { name: /indexers/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(1);
        expect(screen.getByText('IPTorrents')).toBeInTheDocument();
    });

    it('filters items by provider when selection changes', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const providerTrigger = document.querySelector('[data-filter="provider"]') as HTMLElement;
        await user.click(providerTrigger);

        const torznabOption = await screen.findByRole('option', { name: 'torznab' });
        await user.click(torznabOption);

        const list = screen.getByRole('list', { name: /indexers/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(2);
        expect(screen.getByText('IPTorrents')).toBeInTheDocument();
        expect(screen.getByText('Jackett')).toBeInTheDocument();
    });

    it('shows clear button when filters are active', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        const availabilityTrigger = document.querySelector('[data-filter="availability"]') as HTMLElement;
        await user.click(availabilityTrigger);

        const publicOption = await screen.findByRole('option', { name: 'Public' });
        await user.click(publicOption);

        expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
    });

    it('clears filters when clear button is clicked', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const availabilityTrigger = document.querySelector('[data-filter="availability"]') as HTMLElement;
        await user.click(availabilityTrigger);

        const publicOption = await screen.findByRole('option', { name: 'Public' });
        await user.click(publicOption);

        const clearButton = document.querySelector('[data-action="clear-filters"]') as HTMLElement;
        await user.click(clearButton);

        const list = screen.getByRole('list', { name: /indexers/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(4);
    });

    it('shows empty state when filters match nothing', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const availabilityTrigger = document.querySelector('[data-filter="availability"]') as HTMLElement;
        await user.click(availabilityTrigger);

        const privateOption = await screen.findByRole('option', { name: 'Private' });
        await user.click(privateOption);

        const providerTrigger = document.querySelector('[data-filter="provider"]') as HTMLElement;
        await user.click(providerTrigger);

        const newznabOption = await screen.findByRole('option', { name: 'newznab' });
        await user.click(newznabOption);

        expect(screen.getByText('No indexers found')).toBeInTheDocument();
        expect(screen.getByText('Try adjusting your filters')).toBeInTheDocument();
    });
});

describe('IndexersPage empty state', () => {
    it('shows empty state icon as decorative when no results', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const availabilityTrigger = document.querySelector('[data-filter="availability"]') as HTMLElement;
        await user.click(availabilityTrigger);

        const privateOption = await screen.findByRole('option', { name: 'Private' });
        await user.click(privateOption);

        const providerTrigger = document.querySelector('[data-filter="provider"]') as HTMLElement;
        await user.click(providerTrigger);

        const newznabOption = await screen.findByRole('option', { name: 'newznab' });
        await user.click(newznabOption);

        expect(screen.getByTestId('empty-state__icon')).toHaveAttribute('aria-hidden', 'true');
    });
});

describe('IndexersPage feeds tab', () => {
    it('switches to feeds tab when clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const feedsTab = document.querySelector('[data-tab="feeds"]') as HTMLElement;
        await user.click(feedsTab);

        expect(screen.getByRole('list', { name: /indexer feeds/i })).toBeInTheDocument();
    });

    it('renders feed items with enable/disable toggles', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const feedsTab = document.querySelector('[data-tab="feeds"]') as HTMLElement;
        await user.click(feedsTab);

        const list = screen.getByRole('list', { name: /indexer feeds/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(2);

        const toggles = document.querySelectorAll('[data-action="toggle-enabled"]');
        expect(toggles.length).toBe(2);
    });

    it('shows enabled badge for enabled feeds', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const feedsTab = document.querySelector('[data-tab="feeds"]') as HTMLElement;
        await user.click(feedsTab);

        const prowlarrFeed = document.querySelector('[data-feed-id="feed-1"]');
        const badge = prowlarrFeed?.querySelector('[data-component="badge"]');
        expect(badge).toHaveTextContent('Enabled');
    });

    it('shows disabled badge for disabled feeds', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const feedsTab = document.querySelector('[data-tab="feeds"]') as HTMLElement;
        await user.click(feedsTab);

        const publicFeed = document.querySelector('[data-feed-id="feed-2"]');
        const badge = publicFeed?.querySelector('[data-component="badge"]');
        expect(badge).toHaveTextContent('Disabled');
    });
});

describe('IndexersPage accessibility', () => {
    it('list has accessible label', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /indexers/i })).toBeInTheDocument();
    });

    it('summary section has accessible label', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Availability summary"]');
        expect(summary).toBeInTheDocument();
    });

    it('action buttons have aria-labels', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const actionButtons = screen.getAllByTestId('indexers-page__action-btn');
        actionButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
    });

    it('filter triggers have aria-labels', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const availabilityFilter = document.querySelector('[data-filter="availability"]');
        expect(availabilityFilter).toHaveAttribute('aria-label', 'Filter by availability');

        const providerFilter = document.querySelector('[data-filter="provider"]');
        expect(providerFilter).toHaveAttribute('aria-label', 'Filter by provider type');
    });
});

describe('IndexersPage delete confirmations', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('opens confirmation dialog when indexer delete button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const deleteButton = screen.getByRole('button', { name: /^delete nzbgeek$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Indexer')).toBeInTheDocument();
            expect(screen.getByText(/Are you sure you want to delete "NZBgeek"\?/)).toBeInTheDocument();
        });

        expect(mockDeleteIndexer).not.toHaveBeenCalled();
    });

    it('calls deleteIndexer after confirming in the dialog', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const deleteButton = screen.getByRole('button', { name: /^delete nzbgeek$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Indexer')).toBeInTheDocument();
        });

        const confirmButton = screen.getByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(mockDeleteIndexer).toHaveBeenCalledWith('idx-1');
        });
    });

    it('does not call deleteIndexer when cancel is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const deleteButton = screen.getByRole('button', { name: /^delete nzbgeek$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Indexer')).toBeInTheDocument();
        });

        const cancelButton = screen.getByRole('button', { name: /^cancel$/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByText('Delete Indexer')).not.toBeInTheDocument();
        });

        expect(mockDeleteIndexer).not.toHaveBeenCalled();
    });

    it('opens confirmation dialog when feed delete button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const feedsTab = document.querySelector('[data-tab="feeds"]') as HTMLElement;
        await user.click(feedsTab);

        const deleteButton = screen.getByRole('button', { name: /^delete prowlarr sync$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Indexer Feed')).toBeInTheDocument();
            expect(screen.getByText(/Are you sure you want to delete "Prowlarr Sync"\?/)).toBeInTheDocument();
        });

        expect(mockDeleteFeed).not.toHaveBeenCalled();
    });

    it('calls deleteFeed after confirming in the dialog', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const feedsTab = document.querySelector('[data-tab="feeds"]') as HTMLElement;
        await user.click(feedsTab);

        const deleteButton = screen.getByRole('button', { name: /^delete prowlarr sync$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Indexer Feed')).toBeInTheDocument();
        });

        const confirmButton = screen.getByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(mockDeleteFeed).toHaveBeenCalledWith('feed-1');
        });
    });

    it('does not call deleteFeed when cancel is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const feedsTab = document.querySelector('[data-tab="feeds"]') as HTMLElement;
        await user.click(feedsTab);

        const deleteButton = screen.getByRole('button', { name: /^delete prowlarr sync$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Indexer Feed')).toBeInTheDocument();
        });

        const cancelButton = screen.getByRole('button', { name: /^cancel$/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByText('Delete Indexer Feed')).not.toBeInTheDocument();
        });

        expect(mockDeleteFeed).not.toHaveBeenCalled();
    });
});
