import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { IndexerFeedItem } from '@/hooks/useIndexers';

import { FeedsManagement } from './FeedsManagement';

function feed(overrides: Partial<IndexerFeedItem>): IndexerFeedItem {
    return {
        id: 'feed-1',
        name: 'Feed',
        description: null,
        provider: 'indexer-a',
        enabled: true,
        lastSync: null,
        createdAt: '2024-01-01T00:00:00Z',
        settings: {},
        ...overrides,
    };
}

let mockFeeds: IndexerFeedItem[] = [];
const mockSyncFeed = vi.fn();

vi.mock('@/hooks/useIndexers', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useIndexers')>();
    return {
        ...actual,
        useIndexers: () => ({
            indexers: [],
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
            syncFeed: mockSyncFeed,
            syncingFeedId: undefined,
            isCreatingFeed: false,
            isUpdatingFeed: false,
            isTogglingFeed: false,
            isDeletingFeed: false,
            isSyncingFeed: false,
            refetch: vi.fn(),
            refetchIndexers: vi.fn(),
            refetchFeeds: vi.fn(),
        }),
    };
});

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
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

function row(id: string) {
    return screen.getByRole('grid').querySelector(`[data-row-id="${id}"]`) as HTMLElement;
}

beforeEach(() => {
    vi.clearAllMocks();
    mockFeeds = [];
    mockSyncFeed.mockResolvedValue(undefined);
});

describe('FeedsManagement sync action', () => {
    it('syncs the feed and toasts when "Sync now" is clicked', async () => {
        const user = userEvent.setup();
        mockFeeds = [feed({ id: 'feed-1', name: 'Prowlarr Sync' })];

        render(
            <TestWrapper>
                <FeedsManagement />
            </TestWrapper>,
        );

        await user.click(within(row('feed-1')).getByRole('button', { name: 'Sync Prowlarr Sync now' }));

        expect(mockSyncFeed).toHaveBeenCalledWith('feed-1');

        await waitFor(() => {
            expect(screen.getByText('Sync started')).toBeInTheDocument();
        });
    });
});
