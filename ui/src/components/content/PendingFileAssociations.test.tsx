import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ToastProvider, ToastStateProvider } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

const apiGet = vi.fn();
const apiPost = vi.fn();

vi.mock('@/lib/api', () => ({
    apiGet: (url: string) => apiGet(url),
    apiPost: (url: string, body: unknown) => apiPost(url, body),
}));

vi.mock('@/hooks/useInvalidateQueriesOnEvents', () => ({
    useInvalidateQueriesOnEvents: () => {},
}));

import { PendingFileAssociations, PendingFilesIndicator } from './PendingFileAssociations';

describe('PendingFileAssociations', () => {
    beforeEach(() => {
        apiPost.mockResolvedValue({ queued: [], skipped: [], count: 1 });
    });

    afterEach(() => {
        vi.clearAllMocks();
    });

    it('shows the pending file count and opens the files on click', async () => {
        const user = userEvent.setup();
        const onOpen = vi.fn();
        apiGet.mockResolvedValue([
            {
                id: 'a',
                rootFolderId: 'root-1',
                scanRunId: 'scan-1',
                filePath: '/media/tv/Bluey/a.mkv',
                contentType: 'tv',
                status: 'Pending',
                matches: [
                    {
                        confidence: 1,
                        providerType: 'tvmaze',
                        externalIdKey: 'tvmaze',
                        externalId: '44796',
                        title: 'Bluey',
                    },
                ],
                matchCount: 1,
                contentHierarchyItemId: 'episode-1',
                createdAt: '2026-09-24T00:00:00Z',
            },
            {
                id: 'b',
                rootFolderId: 'root-1',
                scanRunId: 'scan-1',
                filePath: '/media/tv/Bluey/b.mkv',
                contentType: 'tv',
                status: 'Pending',
                matches: [
                    {
                        confidence: 1,
                        providerType: 'tvmaze',
                        externalIdKey: 'tvmaze',
                        externalId: '44796',
                        title: 'Bluey',
                    },
                ],
                matchCount: 1,
                contentHierarchyItemId: 'episode-1',
                createdAt: '2026-09-24T00:00:00Z',
            },
            {
                id: 'c',
                rootFolderId: 'root-1',
                scanRunId: 'scan-1',
                filePath: '/media/tv/Bluey/c.mkv',
                contentType: 'tv',
                status: 'Queued',
                matches: [
                    {
                        confidence: 1,
                        providerType: 'tvmaze',
                        externalIdKey: 'tvmaze',
                        externalId: '44796',
                        title: 'Bluey',
                    },
                ],
                matchCount: 1,
                contentHierarchyItemId: 'episode-1',
                createdAt: '2026-09-24T00:00:00Z',
            },
        ]);

        render(
            <QueryClientProvider
                client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
            >
                <TooltipProvider>
                    <ToastStateProvider>
                        <ToastProvider>
                            <PendingFilesIndicator contentHierarchyItemId="item-1" onOpen={onOpen} />
                        </ToastProvider>
                    </ToastStateProvider>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        await user.click(await screen.findByRole('button', { name: '2 pending files' }));
        expect(apiGet).toHaveBeenCalledWith('/import/content/item-1/candidates');
        expect(onOpen).toHaveBeenCalledTimes(1);
    });

    it('shows no indicator when no file waits for the item', async () => {
        apiGet.mockResolvedValue([]);

        const { container } = render(
            <QueryClientProvider
                client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
            >
                <TooltipProvider>
                    <ToastStateProvider>
                        <ToastProvider>
                            <PendingFilesIndicator contentHierarchyItemId="item-1" onOpen={vi.fn()} />
                        </ToastProvider>
                    </ToastStateProvider>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        await waitFor(() => expect(apiGet).toHaveBeenCalledTimes(1));
        expect(container).toBeEmptyDOMElement();
    });

    it('accepts, rejects, and accepts all only the files that no association owns', async () => {
        const user = userEvent.setup();
        apiGet.mockResolvedValue([
            {
                id: 'a',
                rootFolderId: 'root-1',
                scanRunId: 'scan-1',
                filePath: '/media/tv/Bluey/a.mkv',
                contentType: 'tv',
                status: 'Pending',
                matches: [
                    {
                        confidence: 1,
                        providerType: 'tvmaze',
                        externalIdKey: 'tvmaze',
                        externalId: '44796',
                        title: 'Bluey',
                    },
                ],
                matchCount: 1,
                contentHierarchyItemId: 'episode-1',
                createdAt: '2026-09-24T00:00:00Z',
            },
            {
                id: 'b',
                rootFolderId: 'root-1',
                scanRunId: 'scan-1',
                filePath: '/media/tv/Bluey/b.mkv',
                contentType: 'tv',
                status: 'Pending',
                matches: [
                    {
                        confidence: 1,
                        providerType: 'tvmaze',
                        externalIdKey: 'tvmaze',
                        externalId: '44796',
                        title: 'Bluey',
                    },
                ],
                matchCount: 1,
                contentHierarchyItemId: 'episode-1',
                createdAt: '2026-09-24T00:00:00Z',
            },
            {
                id: 'c',
                rootFolderId: 'root-1',
                scanRunId: 'scan-1',
                filePath: '/media/tv/Bluey/c.mkv',
                contentType: 'tv',
                status: 'Importing',
                matches: [
                    {
                        confidence: 1,
                        providerType: 'tvmaze',
                        externalIdKey: 'tvmaze',
                        externalId: '44796',
                        title: 'Bluey',
                    },
                ],
                matchCount: 1,
                contentHierarchyItemId: 'episode-1',
                createdAt: '2026-09-24T00:00:00Z',
            },
        ]);

        render(
            <QueryClientProvider
                client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
            >
                <TooltipProvider>
                    <ToastStateProvider>
                        <ToastProvider>
                            <PendingFileAssociations contentHierarchyItemId="item-1" canManage />
                        </ToastProvider>
                    </ToastStateProvider>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        await screen.findByText('/media/tv/Bluey/a.mkv');
        const first = screen.getByTitle('/media/tv/Bluey/a.mkv').closest('tr') as HTMLElement;
        const inFlight = screen.getByTitle('/media/tv/Bluey/c.mkv').closest('tr') as HTMLElement;
        expect(within(inFlight).getByRole('button', { name: 'Associating' })).toBeDisabled();
        expect(within(inFlight).queryByRole('button', { name: 'Accept' })).toBeNull();

        await user.click(within(first).getByRole('button', { name: 'Accept' }));
        await user.click(
            within(screen.getByTitle('/media/tv/Bluey/b.mkv').closest('tr') as HTMLElement).getByRole('button', {
                name: 'Reject',
            }),
        );
        await user.click(screen.getByRole('button', { name: 'Accept all (2)' }));

        expect(apiPost.mock.calls).toEqual([
            ['/import/content/item-1/associate', { ids: ['a'] }],
            ['/import/candidates/reject', { ids: ['b'] }],
            ['/import/content/item-1/associate', { ids: ['a', 'b'] }],
        ]);
    });

    it('keeps the row actions mounted when the panel re-renders', async () => {
        apiGet.mockResolvedValue([
            {
                id: 'a',
                rootFolderId: 'root-1',
                scanRunId: 'scan-1',
                filePath: '/media/tv/Bluey/a.mkv',
                contentType: 'tv',
                status: 'Pending',
                matches: [
                    {
                        confidence: 1,
                        providerType: 'tvmaze',
                        externalIdKey: 'tvmaze',
                        externalId: '44796',
                        title: 'Bluey',
                    },
                ],
                matchCount: 1,
                contentHierarchyItemId: 'episode-1',
                createdAt: '2026-09-24T00:00:00Z',
            },
        ]);
        const queryClient = new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
        });
        const { rerender } = render(<PendingFileAssociations contentHierarchyItemId="item-1" canManage />, {
            wrapper: ({ children }: { children: ReactNode }) => (
                <QueryClientProvider client={queryClient}>
                    <TooltipProvider>
                        <ToastStateProvider>
                            <ToastProvider>{children}</ToastProvider>
                        </ToastStateProvider>
                    </TooltipProvider>
                </QueryClientProvider>
            ),
        });

        await screen.findByText('/media/tv/Bluey/a.mkv');
        const accept = within(screen.getByTitle('/media/tv/Bluey/a.mkv').closest('tr') as HTMLElement).getByRole(
            'button',
            { name: 'Accept' },
        );
        rerender(<PendingFileAssociations contentHierarchyItemId="item-1" canManage />);

        expect(accept).toBeInTheDocument();
    });

    it('PendingFileAssociations_RendersNothing_WhenNothingIsPending', async () => {
        // Arrange
        apiGet.mockResolvedValue([]);

        // Act
        const { container } = render(
            <QueryClientProvider
                client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
            >
                <TooltipProvider>
                    <ToastStateProvider>
                        <ToastProvider>
                            <PendingFileAssociations contentHierarchyItemId="item-1" canManage />
                        </ToastProvider>
                    </ToastStateProvider>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        await waitFor(() => expect(apiGet).toHaveBeenCalled());
        await waitFor(() => expect(container).toBeEmptyDOMElement());
    });

    it('PendingFileAssociations_ShowsALoadingState_WhileTheQueryLoads', async () => {
        // Arrange
        apiGet.mockReturnValue(new Promise(() => {}));

        // Act
        render(
            <QueryClientProvider
                client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
            >
                <TooltipProvider>
                    <ToastStateProvider>
                        <ToastProvider>
                            <PendingFileAssociations contentHierarchyItemId="item-1" canManage />
                        </ToastProvider>
                    </ToastStateProvider>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        const section = await screen.findByRole('region', { name: 'Pending files' });
        await waitFor(() => expect(apiGet).toHaveBeenCalled());
        expect(within(section).getByRole('status', { name: 'Loading' })).toBeInTheDocument();
    });

    it('PendingFileAssociations_ShowsTheFailedRequestStateAndRetries_WhenTheQueryFails', async () => {
        // Arrange
        const user = userEvent.setup();
        apiGet.mockRejectedValue(new Error('offline'));
        render(
            <QueryClientProvider
                client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
            >
                <TooltipProvider>
                    <ToastStateProvider>
                        <ToastProvider>
                            <PendingFileAssociations contentHierarchyItemId="item-1" canManage />
                        </ToastProvider>
                    </ToastStateProvider>
                </TooltipProvider>
            </QueryClientProvider>,
        );
        const section = await screen.findByRole('region', { name: 'Pending files' });

        // Act
        await user.click(await within(section).findByRole('button', { name: 'Try again' }));

        // Assert
        expect(section).toHaveTextContent('Failed to load');
        await waitFor(() => expect(apiGet).toHaveBeenCalledTimes(2));
    });

    it('explains pending files and lists them when some wait', async () => {
        apiGet.mockResolvedValue([
            {
                id: 'a',
                rootFolderId: 'root-1',
                scanRunId: 'scan-1',
                filePath: '/media/tv/Bluey/a.mkv',
                contentType: 'tv',
                status: 'Pending',
                matches: [
                    {
                        confidence: 1,
                        providerType: 'tvmaze',
                        externalIdKey: 'tvmaze',
                        externalId: '44796',
                        title: 'Bluey',
                    },
                ],
                matchCount: 1,
                contentHierarchyItemId: 'episode-1',
                createdAt: '2026-09-24T00:00:00Z',
            },
        ]);

        render(
            <QueryClientProvider
                client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
            >
                <TooltipProvider>
                    <ToastStateProvider>
                        <ToastProvider>
                            <PendingFileAssociations contentHierarchyItemId="item-1" canManage />
                        </ToastProvider>
                    </ToastStateProvider>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        const section = await screen.findByRole('region', { name: 'Pending files' });
        expect(await within(section).findByText('/media/tv/Bluey/a.mkv')).toBeInTheDocument();
        expect(section).toHaveTextContent('Accepting links a file');
    });

    it('offers no actions to a user who cannot manage content', async () => {
        apiGet.mockResolvedValue([
            {
                id: 'a',
                rootFolderId: 'root-1',
                scanRunId: 'scan-1',
                filePath: '/media/tv/Bluey/a.mkv',
                contentType: 'tv',
                status: 'Pending',
                matches: [
                    {
                        confidence: 1,
                        providerType: 'tvmaze',
                        externalIdKey: 'tvmaze',
                        externalId: '44796',
                        title: 'Bluey',
                    },
                ],
                matchCount: 1,
                contentHierarchyItemId: 'episode-1',
                createdAt: '2026-09-24T00:00:00Z',
            },
        ]);

        render(
            <QueryClientProvider
                client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}
            >
                <TooltipProvider>
                    <ToastStateProvider>
                        <ToastProvider>
                            <PendingFileAssociations contentHierarchyItemId="item-1" canManage={false} />
                        </ToastProvider>
                    </ToastStateProvider>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        await screen.findByText('/media/tv/Bluey/a.mkv');
        expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Reject' })).toBeNull();
    });
});
