import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiIndexer, ApiIndexerConnection, IndexerFeedItem, ConfiguredConnectionRow } from '@/hooks/useIndexers';

import { IndexersPage } from './IndexersPage';

const mockIndexers: ApiIndexer[] = [
    {
        id: 'idx-1',
        name: 'NZBgeek',
        providerType: 'newznab',
        availability: 'Public',
        categories: ['Movies', 'TV'],
        languages: ['en'],
        connectionCount: 2,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'idx-2',
        name: 'IPTorrents',
        providerType: 'torznab',
        availability: 'Private',
        categories: ['Anime'],
        languages: ['ja'],
        connectionCount: 1,
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'idx-3',
        name: 'Jackett',
        providerType: 'torznab',
        availability: 'SemiPrivate',
        categories: [],
        languages: [],
        connectionCount: 0,
        createdAt: '2024-01-03T00:00:00Z',
    },
];

function conn(
    id: string,
    indexerId: string,
    name: string,
    status: ApiIndexerConnection['status'],
): ApiIndexerConnection {
    return {
        id,
        indexerId,
        name,
        enabled: true,
        status,
        settings: { url: 'https://x.test' },
        createdAt: '2024-01-01T00:00:00Z',
    };
}

const mockRows: ConfiguredConnectionRow[] = [
    { connection: conn('c-1', 'idx-1', 'NZBgeek US', 'Healthy'), indexer: mockIndexers[0] },
    { connection: conn('c-2', 'idx-1', 'NZBgeek EU', 'Healthy'), indexer: mockIndexers[0] },
    { connection: conn('c-3', 'idx-2', 'IPTorrents', 'Unhealthy'), indexer: mockIndexers[1] },
];

const mockFeeds: IndexerFeedItem[] = [
    {
        id: 'feed-1',
        name: 'Prowlarr Sync',
        description: 'Sync from Prowlarr',
        provider: 'prowlarr',
        enabled: true,
        lastSync: '2024-01-15T09:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        settings: {},
    },
];

const mockTestSettings = vi.fn().mockResolvedValue({ success: true, message: 'Connected.' });
const mockTestConnection = vi.fn().mockResolvedValue({ success: true, message: 'Connected.' });
const mockDeleteConnection = vi.fn().mockResolvedValue(undefined);
const mockUpdateConnection = vi.fn().mockResolvedValue(undefined);
const mockCreateConnection = vi.fn().mockResolvedValue(undefined);

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
            createIndexer: vi.fn(),
            updateIndexer: vi.fn(),
            deleteIndexer: vi.fn(),
            isCreatingIndexer: false,
            isUpdatingIndexer: false,
            isDeletingIndexer: false,
            createFeed: vi.fn(),
            updateFeed: vi.fn(),
            toggleFeedEnabled: vi.fn(),
            deleteFeed: vi.fn(),
            isCreatingFeed: false,
            isUpdatingFeed: false,
            isTogglingFeed: false,
            isDeletingFeed: false,
            refetch: vi.fn(),
            refetchIndexers: vi.fn(),
            refetchFeeds: vi.fn(),
        }),
        useAllIndexerConnections: () => ({ rows: mockRows, isLoading: false }),
        useIndexerConnectionMutations: () => ({
            createConnection: mockCreateConnection,
            updateConnection: mockUpdateConnection,
            deleteConnection: mockDeleteConnection,
            testConnection: mockTestConnection,
            testSettings: mockTestSettings,
            cancelTestSettings: vi.fn(),
            isUpdating: false,
            isDeleting: false,
            isTesting: false,
            isTestingSettings: false,
        }),
    };
});

vi.mock('@/hooks/useProviderSchema', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useProviderSchema')>();
    return {
        ...actual,
        useIndexerInstanceSchema: () => ({ data: [], isLoading: false }),
    };
});

const mockApiPost = vi.fn().mockResolvedValue({});
vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiPost: (...args: unknown[]) => mockApiPost(...args),
    };
});

function createTestQueryClient() {
    return new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
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

function dataRows() {
    const table = screen.getByRole('grid');
    return within(table)
        .getAllByRole('row')
        .filter((r) => within(r).queryAllByRole('cell').length > 0);
}

beforeEach(() => {
    vi.clearAllMocks();
});

describe('IndexersPage structure', () => {
    it('renders the three subtabs: Configured, Available, Feeds', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-tab="configured"]')).toBeInTheDocument();
        expect(document.querySelector('[data-tab="available"]')).toBeInTheDocument();
        expect(document.querySelector('[data-tab="feeds"]')).toBeInTheDocument();
    });

    it('uses the page-level description', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(screen.getByText('Connections to release indexers and feeds.')).toBeInTheDocument();
    });

    it('shows the configured-instance count (connections) in the Configured tab label', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(screen.getAllByTestId('indexers-page__tab-count')[0]).toHaveTextContent('2');
    });

    it('the Available tab counts every definition in the catalog', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const availableTab = document.querySelector('[data-tab="available"]');
        expect(availableTab?.querySelector('[data-testid="indexers-page__tab-count"]')).toHaveTextContent('3');
    });
});

describe('Configured tab (table, row per connection)', () => {
    it('describes the area as a configured instance of an indexer', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(screen.getByText('Configured instance of an indexer.')).toBeInTheDocument();
    });

    it('opens the available catalog from the configured quick action without mutation', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const addAction = document.querySelector('.quick-actions-button [data-action="add-indexer"]');
        expect(addAction).toHaveAccessibleName('Add Indexer');

        await user.click(addAction as HTMLElement);

        await waitFor(() => {
            expect(document.querySelector('[data-tab="available"]')).toHaveAttribute('data-state', 'active');
        });
        expect(mockApiPost).not.toHaveBeenCalled();
    });

    it('renders one row per connection, not per indexer', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(dataRows()).toHaveLength(3);
        expect(screen.getByText('NZBgeek US')).toBeInTheDocument();
        expect(screen.getByText('NZBgeek EU')).toBeInTheDocument();
    });

    it('keeps a long unbroken connection name contained with the full name available', () => {
        const longName = `IndexerConnection${'UnbrokenNameSegment'.repeat(8)}`;
        mockRows.push({ connection: conn('c-long', 'idx-1', longName, 'Healthy'), indexer: mockIndexers[0] });
        try {
            render(
                <TestWrapper>
                    <IndexersPage />
                </TestWrapper>,
            );

            const name = screen.getByTitle(longName).querySelector('.indexers-table__name-text');
            expect(name).toHaveTextContent(longName);
            expect(name?.closest('[data-column="name"]')).toBeInTheDocument();
        } finally {
            mockRows.pop();
        }
    });

    it('renders the full "Enabled" column header label', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const enabledHeader = screen
            .getAllByRole('columnheader')
            .find((cell) => cell.getAttribute('data-column') === 'enabled');
        expect(enabledHeader).toBeDefined();
        expect(enabledHeader).toHaveTextContent('Enabled');
    });

    it('exposes test, enable toggle, edit and delete per connection row', () => {
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        expect(document.querySelectorAll('[data-action="test"]').length).toBe(3);
        expect(document.querySelectorAll('[data-action="toggle-enabled"]').length).toBe(3);
        expect(document.querySelectorAll('[data-action="edit"]').length).toBe(3);
        expect(document.querySelectorAll('[data-action="delete"]').length).toBe(3);
    });

    it('toggles a connection through updateConnection with only an enabled patch', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const toggle = screen.getByRole('switch', { name: /disable nzbgeek eu/i });
        await user.click(toggle);

        await waitFor(() => {
            expect(mockUpdateConnection).toHaveBeenCalledWith('idx-1', 'c-2', {
                enabled: { value: false },
            });
        });
    });

    it('keeps the instance enabled and surfaces the server error detail when the toggle fails', async () => {
        mockUpdateConnection.mockRejectedValueOnce(new Error('Indexer connection is locked.'));
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('switch', { name: /disable nzbgeek eu/i }));

        await waitFor(() => {
            expect(screen.getByText('Indexer connection is locked.')).toBeInTheDocument();
        });
        expect(screen.getByRole('switch', { name: /disable nzbgeek eu/i })).toBeChecked();
    });

    it('tests the targeted connection by id', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        const testButton = screen.getByRole('button', { name: /^test nzbgeek eu$/i });
        await user.click(testButton);

        await waitFor(() => {
            expect(mockTestConnection).toHaveBeenCalledWith('idx-1', 'c-2');
        });
    });

    it('deletes the targeted connection after confirming', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /^delete nzbgeek eu$/i }));
        await waitFor(() => {
            expect(screen.getByText('Delete Indexer Instance')).toBeInTheDocument();
        });
        await user.click(screen.getByRole('button', { name: /^delete$/i }));

        await waitFor(() => {
            expect(mockDeleteConnection).toHaveBeenCalledWith('idx-1', 'c-2');
        });
    });
});

describe('Available tab (table + Configure button)', () => {
    async function gotoAvailable(user: ReturnType<typeof userEvent.setup>) {
        await user.click(document.querySelector('[data-tab="available"]') as HTMLElement);
    }

    it('lists ALL definitions including already-configured ones', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );
        await gotoAvailable(user);

        expect(dataRows()).toHaveLength(3);
    });

    it('keeps a long unbroken definition name contained with the full name available', async () => {
        const longName = `IndexerDefinition${'UnbrokenNameSegment'.repeat(8)}`;
        mockIndexers.push({
            id: 'idx-long',
            name: longName,
            providerType: 'newznab',
            availability: 'Public',
            categories: ['Movies'],
            languages: ['en'],
            connectionCount: 0,
            createdAt: '2024-01-04T00:00:00Z',
        });
        try {
            const user = userEvent.setup();
            render(
                <TestWrapper>
                    <IndexersPage />
                </TestWrapper>,
            );
            await gotoAvailable(user);

            const name = screen.getByTitle(longName);
            expect(name).toHaveClass('available-catalog__name');
            expect(name.closest('[data-column="name"]')).toBeInTheDocument();
        } finally {
            mockIndexers.pop();
        }
    });

    it('shows a configured-count badge on definitions that already have connections', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );
        await gotoAvailable(user);

        const badges = screen.getAllByTestId('catalog-configured-badge');
        expect(badges.length).toBe(2);
    });

    it('exposes provider, language, category and availability column filters', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );
        await gotoAvailable(user);

        expect(screen.getByRole('button', { name: /filter by provider/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by languages/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by categories/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by availability/i })).toBeInTheDocument();
    });

    it('opens the configure dialog via the per-row Configure button', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );
        await gotoAvailable(user);

        const configure = screen.getAllByRole('button', { name: /^configure$/i });
        expect(configure.length).toBe(3);
        await user.click(configure[0]);

        await waitFor(() => {
            expect(document.querySelector('[data-component="configure-instance"]')).toBeInTheDocument();
        });
    });
});

describe('Configure dialog (test + close-on-add)', () => {
    async function openAddDialog(user: ReturnType<typeof userEvent.setup>) {
        await user.click(document.querySelector('[data-tab="available"]') as HTMLElement);
        const jackettRow = screen.getByRole('grid').querySelector('[data-row-id="idx-3"]') as HTMLElement;
        await user.click(jackettRow.querySelector('[data-action="configure"]') as HTMLElement);
        await waitFor(() => {
            expect(document.querySelector('[data-component="configure-instance"]')).toBeInTheDocument();
        });
    }

    it('runs a pre-save test with the configured instance settings', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );
        await openAddDialog(user);

        await user.click(document.querySelector('[data-action="test-instance"]') as HTMLElement);

        await waitFor(() => {
            expect(mockTestSettings).toHaveBeenCalled();
        });
        const arg = mockTestSettings.mock.calls[0][0];
        expect(arg).toEqual({ id: 'idx-3', providerType: 'torznab', settings: {} });
    });

    it('closes after a successful add', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );
        await openAddDialog(user);

        await user.click(document.querySelector('[data-action="add-instance"]') as HTMLElement);

        await waitFor(() => {
            expect(mockCreateConnection).toHaveBeenCalledWith('idx-3', expect.objectContaining({ enabled: true }));
        });

        await waitFor(() => {
            expect(document.querySelector('[data-component="configure-instance"]')).not.toBeInTheDocument();
        });
    });
});

describe('Feeds tab (table)', () => {
    it('renders feed rows when the Feeds tab is selected', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <IndexersPage />
            </TestWrapper>,
        );

        await user.click(document.querySelector('[data-tab="feeds"]') as HTMLElement);

        expect(screen.getByRole('grid')).toBeInTheDocument();
        expect(screen.getByText('Prowlarr Sync')).toBeInTheDocument();
    });

    it('keeps a long unbroken feed name contained with the full name available', async () => {
        const longName = `IndexerFeed${'UnbrokenNameSegment'.repeat(8)}`;
        mockFeeds.push({
            ...mockFeeds[0],
            id: 'feed-long',
            name: longName,
            description: `Description${'UnbrokenDescriptionSegment'.repeat(6)}`,
        });
        try {
            const user = userEvent.setup();
            render(
                <TestWrapper>
                    <IndexersPage />
                </TestWrapper>,
            );

            await user.click(document.querySelector('[data-tab="feeds"]') as HTMLElement);

            const name = screen.getByTitle(longName).querySelector('.indexers-table__name-text');
            expect(name).toHaveTextContent(longName);
            expect(name?.closest('[data-column="name"]')).toBeInTheDocument();
        } finally {
            mockFeeds.pop();
        }
    });
});
