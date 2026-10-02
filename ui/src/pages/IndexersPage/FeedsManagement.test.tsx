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
const mockUpdateFeed = vi.fn();
const mockSyncFeed = vi.fn();
const mockDeleteFeed = vi.fn();
const mockToggleFeedEnabled = vi.fn();

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
            createIndexer: vi.fn(),
            isCreatingIndexer: false,
            createFeed: vi.fn(),
            updateFeed: mockUpdateFeed,
            toggleFeedEnabled: mockToggleFeedEnabled,
            deleteFeed: mockDeleteFeed,
            syncFeed: mockSyncFeed,
            syncingFeedId: undefined,
            isCreatingFeed: false,
            isUpdatingFeed: false,
            refetch: vi.fn(),
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
    mockDeleteFeed.mockResolvedValue(undefined);
    mockToggleFeedEnabled.mockResolvedValue(undefined);
});

describe('FeedsManagement sync action', () => {
    it('sends an empty description when the description is cleared', async () => {
        // Arrange
        mockFeeds = [
            {
                id: 'feed-1',
                name: 'Archive',
                description: 'Old description',
                provider: 'indexer-a',
                enabled: true,
                lastSync: null,
                createdAt: '2024-01-01T00:00:00Z',
                settings: { branch: 'main' },
            },
        ];
        const user = userEvent.setup();
        render(<FeedsManagement />, { wrapper: TestWrapper });

        // Act
        await user.click(screen.getByRole('button', { name: 'Edit Archive' }));
        await user.clear(screen.getByLabelText('Description'));
        await user.click(screen.getByRole('button', { name: /save/i }));

        // Assert
        await waitFor(() =>
            expect(mockUpdateFeed).toHaveBeenCalledWith('feed-1', {
                name: { value: 'Archive' },
                description: { value: '' },
                providerType: { value: 'indexer-a' },
                enabled: { value: true },
                settings: { add: { branch: 'main' } },
            }),
        );
    });

    it('shows a friendly sync status when the server returns a generic error', async () => {
        // Arrange
        mockFeeds = [
            {
                id: 'feed-1',
                name: 'Archive',
                description: null,
                provider: 'indexer-a',
                enabled: true,
                lastSync: null,
                createdAt: '2024-01-01T00:00:00Z',
                settings: {},
            },
        ];
        mockSyncFeed.mockRejectedValue({ name: 'ApiError', status: 403, message: 'Request failed with status 403' });
        const user = userEvent.setup();
        render(<FeedsManagement />, { wrapper: TestWrapper });

        // Act
        await user.click(screen.getByRole('button', { name: 'Sync Archive now' }));

        // Assert
        expect(await screen.findByText('You do not have permission to perform this action.')).toBeInTheDocument();
    });

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

describe('FeedsManagement (mobile card collapse)', () => {
    it('marks the name cell as header role and the select column as hidden (no layout-participating checkbox)', () => {
        mockFeeds = [feed({ id: 'feed-1', name: 'Prowlarr Sync' })];

        render(
            <TestWrapper>
                <FeedsManagement />
            </TestWrapper>,
        );

        expect(document.querySelector('td[data-column="name"]')).toHaveAttribute('data-mobile-role', 'header');
        expect(document.querySelector('td[data-column="select"]')).toHaveAttribute('data-mobile-role', 'hidden');
    });

    it('bulk-deletes every selected feed', async () => {
        const user = userEvent.setup();
        mockFeeds = [feed({ id: 'feed-1', name: 'Prowlarr Sync' }), feed({ id: 'feed-2', name: 'Jackett Sync' })];

        render(
            <TestWrapper>
                <FeedsManagement />
            </TestWrapper>,
        );

        await user.click(screen.getByLabelText('Select Prowlarr Sync'));
        await user.click(screen.getByLabelText('Select Jackett Sync'));
        await user.click(screen.getByRole('button', { name: 'Delete selected feeds' }));
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLElement);

        await waitFor(() => {
            expect(mockDeleteFeed).toHaveBeenCalledWith('feed-1');
            expect(mockDeleteFeed).toHaveBeenCalledWith('feed-2');
        });
    });

    it('bulk-enables every selected feed', async () => {
        const user = userEvent.setup();
        mockFeeds = [feed({ id: 'feed-1', name: 'Prowlarr Sync', enabled: false })];

        render(
            <TestWrapper>
                <FeedsManagement />
            </TestWrapper>,
        );

        await user.click(screen.getByLabelText('Select Prowlarr Sync'));
        await user.click(screen.getByRole('button', { name: 'Enable selected feeds' }));

        await waitFor(() => {
            expect(mockToggleFeedEnabled).toHaveBeenCalledWith('feed-1', true);
        });
    });
});
