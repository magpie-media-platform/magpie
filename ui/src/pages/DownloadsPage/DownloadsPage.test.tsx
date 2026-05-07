import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, useSearchParams } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiDelete: vi.fn(),
    buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
        if (!params) {
            return path;
        }
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        const queryString = searchParams.toString();
        return queryString ? `${path}?${queryString}` : path;
    }),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { apiGet, apiPost, apiDelete } from '@/lib/api';
import type { ApiDownload, PagedDownloads } from '@/hooks/useDownloads';
import type { ApiDownloadClient } from '@/hooks/useDownloadClients';
import { DownloadsPage } from './DownloadsPage';

const mockApiDownloads: ApiDownload[] = [
    {
        id: 'dl-1',
        downloadClientId: 'client-1',
        externalId: 'sab_123',
        title: 'Breaking.Bad.S01E01.1080p.BluRay.x264',
        status: 2,
        createdAt: '2024-01-15T10:00:00Z',
    },
    {
        id: 'dl-2',
        downloadClientId: 'client-2',
        externalId: 'qbt_456',
        title: 'The.Office.S03E07.720p.WEB.x264',
        status: 2,
        createdAt: '2024-01-15T10:05:00Z',
    },
    {
        id: 'dl-3',
        downloadClientId: 'client-1',
        externalId: 'sab_789',
        title: 'Dune.Part.Two.2024.2160p.UHD.BluRay.x265',
        status: 1,
        createdAt: '2024-01-15T09:30:00Z',
    },
    {
        id: 'dl-4',
        downloadClientId: 'client-2',
        externalId: 'qbt_101',
        title: 'Shogun.2024.S01E05.1080p.WEB.H264',
        status: 0,
        createdAt: '2024-01-15T10:15:00Z',
    },
    {
        id: 'dl-5',
        downloadClientId: 'client-1',
        externalId: 'sab_202',
        title: 'The.Gentlemen.S01E03.1080p.WEB.x264',
        status: 3,
        createdAt: '2024-01-15T10:20:00Z',
    },
    {
        id: 'dl-6',
        downloadClientId: 'client-2',
        externalId: 'qbt_303',
        title: '3.Body.Problem.S01E01.720p.WEB.x264',
        status: 4,
        statusReason: 'Tracker returned warning: slow peer',
        createdAt: '2024-01-15T09:00:00Z',
    },
    {
        id: 'hist-1',
        downloadClientId: 'client-1',
        externalId: 'sab_hist_1',
        title: 'The.Bear.S02E10.1080p.WEB.H264',
        status: 6,
        outputPath: '/media/tv/The Bear/Season 2/The.Bear.S02E10.1080p.WEB.H264.mkv',
        dateCompleted: '2024-01-15T08:45:00Z',
        createdAt: '2024-01-14T22:00:00Z',
    },
    {
        id: 'hist-2',
        downloadClientId: 'client-2',
        externalId: 'qbt_hist_2',
        title: 'Oppenheimer.2023.2160p.UHD.BluRay.x265',
        status: 5,
        outputPath: '/media/movies/Oppenheimer (2023)/Oppenheimer.2023.2160p.UHD.BluRay.x265.mkv',
        dateCompleted: '2024-01-14T22:10:00Z',
        createdAt: '2024-01-14T20:00:00Z',
    },
    {
        id: 'hist-3',
        downloadClientId: 'client-1',
        externalId: 'sab_hist_3',
        title: 'True.Detective.S04E01.1080p.WEB.x264',
        status: 7,
        statusReason: 'Hash mismatch after download',
        dateCompleted: '2024-01-14T15:22:00Z',
        createdAt: '2024-01-14T12:00:00Z',
    },
];

const mockPagedDownloads: PagedDownloads = {
    items: mockApiDownloads,
    pageNumber: 1,
    pageSize: 25,
    totalItems: 9,
};

const mockApiClients: ApiDownloadClient[] = [
    {
        id: 'client-1',
        name: 'SABnzbd',
        providerType: 'sabnzbd',
        enabled: true,
        status: 1,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'client-2',
        name: 'qBittorrent',
        providerType: 'qbittorrent',
        enabled: true,
        status: 1,
        createdAt: '2024-01-01T00:00:00Z',
    },
];

const defaultExtensionContext = {
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

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
            mutations: {
                retry: false,
            },
        },
    });
}

function createTestWrapper(queryClient: QueryClient) {
    return function TestWrapper({ children }: { children: ReactNode }) {
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
    };
}

describe('DownloadsPage', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(mockPagedDownloads);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with correct data-page attribute', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(document.querySelector('[data-page="downloads"]')).toBeInTheDocument();
    });

    it('renders the page title', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(screen.getByRole('heading', { name: 'Downloads', level: 1 })).toBeInTheDocument();
    });

    it('renders the total active count', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const count = screen.getByTestId('downloads-page__count');
            expect(count).toBeInTheDocument();
            expect(count).toHaveTextContent('6');
        });
    });

    it('shows the page count even when zero active downloads', async () => {
        const emptyPaged: PagedDownloads = {
            items: [],
            pageNumber: 1,
            pageSize: 25,
            totalItems: 0,
        };
        const mockApiGet = vi.mocked(apiGet);
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(emptyPaged);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const count = screen.getByTestId('downloads-page__count');
            expect(count).toBeInTheDocument();
            expect(count).toHaveTextContent('0');
        });
    });

    it('renders the page icon as decorative', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const icon = screen.getByTestId('downloads-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });

    it('renders Queue and History tabs', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(screen.getByRole('tab', { name: /queue/i })).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: /history/i })).toBeInTheDocument();
    });

    it('shows Queue tab as active by default', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const queueTab = screen.getByRole('tab', { name: /queue/i });
        expect(queueTab).toHaveAttribute('data-state', 'active');
    });

    it('shows tab count badges', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const tabCounts = screen.getAllByTestId('downloads-page__tab-count');
            expect(tabCounts.length).toBeGreaterThanOrEqual(1);
        });
    });
});

describe('DownloadsPage summary badges', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(mockPagedDownloads);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders summary badges for each status', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getAllByText('Downloading').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Queued').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Paused').length).toBeGreaterThanOrEqual(1);
        });
    });

    it('applies data-status attributes to badges', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Queue summary"]');
            const badges = summary?.querySelectorAll('[data-component="badge"]') ?? [];
            const statuses = Array.from(badges).map((b) => b.getAttribute('data-status'));
            expect(statuses).toContain('downloading');
            expect(statuses).toContain('queued');
        });
    });

    it('renders badge dots as decorative', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Queue summary"]');
            const dots = summary?.querySelectorAll('[data-component="badge"] [aria-hidden="true"]') ?? [];
            expect(dots.length).toBeGreaterThan(0);
            dots.forEach((dot) => {
                expect(dot).toHaveAttribute('aria-hidden', 'true');
            });
        });
    });
});

describe('DownloadsPage queue items', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(mockPagedDownloads);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders download items in the queue list', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const queue = screen.getByRole('list', { name: /download queue/i });
            const items = within(queue).getAllByRole('listitem');
            expect(items.length).toBe(6);
        });
    });

    it('renders download titles', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
            expect(screen.getByText('The.Office.S03E07.720p.WEB.x264')).toBeInTheDocument();
            expect(screen.getByText('Dune.Part.Two.2024.2160p.UHD.BluRay.x265')).toBeInTheDocument();
        });
    });

    it('renders title attributes on download titles for tooltip on truncation', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const titleElements = screen.getAllByTestId('downloads-page__item-title');
            expect(titleElements.length).toBe(6);
            titleElements.forEach((el) => {
                expect(el).toHaveAttribute('title');
                expect(el.getAttribute('title')).toBe(el.textContent);
            });
        });
    });

    it('renders client names for items', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const clientLabels = screen.getAllByText('SABnzbd');
            expect(clientLabels.length).toBeGreaterThan(0);
            const qbtLabels = screen.getAllByText('qBittorrent');
            expect(qbtLabels.length).toBeGreaterThan(0);
        });
    });

    it('renders status reason messages when present', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Tracker returned warning: slow peer')).toBeInTheDocument();
        });
    });

    it('applies data-download-id to items', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const items = document.querySelectorAll('[data-download-id]');
            expect(items.length).toBeGreaterThan(0);
            expect(items[0]).toHaveAttribute('data-download-id', 'dl-1');
        });
    });

    it('applies data-status to items', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const downloadingItems = document.querySelectorAll('[data-status="downloading"]');
            expect(downloadingItems.length).toBeGreaterThan(0);
        });
    });
});

describe('DownloadsPage actions', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(mockPagedDownloads);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders pause buttons for downloading items', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const pauseButtons = screen.getAllByRole('button', { name: /^pause /i });
            expect(pauseButtons.length).toBe(2);
        });
    });

    it('renders resume buttons for grabbed, queued, and paused items', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const resumeButtons = screen.getAllByRole('button', { name: /^resume /i });
            expect(resumeButtons.length).toBe(3);
        });
    });

    it('renders remove buttons for all items', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const removeButtons = screen.getAllByRole('button', { name: /^remove /i });
            expect(removeButtons.length).toBe(6);
        });
    });

    it('pause button has correct aria-label', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const pauseButton = screen.getByRole('button', { name: /^pause breaking/i });
            expect(pauseButton).toHaveAttribute('data-action', 'pause');
        });
    });

    it('remove button has correct data-action', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const removeButtons = screen.getAllByRole('button', { name: /^remove /i });
            removeButtons.forEach((btn) => {
                expect(btn).toHaveAttribute('data-action', 'remove');
            });
        });
    });

    it('calls pause endpoint when pause button is clicked', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const pauseButton = screen.getByRole('button', { name: /^pause breaking/i });
        await user.click(pauseButton);

        expect(mockApiPost).toHaveBeenCalledWith('/downloads/dl-1/pause');
    });

    it('calls resume endpoint when resume button is clicked', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Dune.Part.Two.2024.2160p.UHD.BluRay.x265')).toBeInTheDocument();
        });

        const resumeButton = screen.getByRole('button', { name: /^resume dune/i });
        await user.click(resumeButton);

        expect(mockApiPost).toHaveBeenCalledWith('/downloads/dl-3/resume');
    });

    it('shows success toast when pause succeeds', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const pauseButton = screen.getByRole('button', { name: /^pause breaking/i });
        await user.click(pauseButton);

        await waitFor(() => {
            expect(screen.getByText('Download paused')).toBeInTheDocument();
        });
    });

    it('shows error toast when pause fails', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockRejectedValue(new Error('Network error'));

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const pauseButton = screen.getByRole('button', { name: /^pause breaking/i });
        await user.click(pauseButton);

        await waitFor(() => {
            expect(screen.getByText('Failed to pause download')).toBeInTheDocument();
        });
    });

    it('shows success toast when resume succeeds', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Dune.Part.Two.2024.2160p.UHD.BluRay.x265')).toBeInTheDocument();
        });

        const resumeButton = screen.getByRole('button', { name: /^resume dune/i });
        await user.click(resumeButton);

        await waitFor(() => {
            expect(screen.getByText('Download resumed')).toBeInTheDocument();
        });
    });

    it('shows error toast when resume fails', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockRejectedValue(new Error('Network error'));

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Dune.Part.Two.2024.2160p.UHD.BluRay.x265')).toBeInTheDocument();
        });

        const resumeButton = screen.getByRole('button', { name: /^resume dune/i });
        await user.click(resumeButton);

        await waitFor(() => {
            expect(screen.getByText('Failed to resume download')).toBeInTheDocument();
        });
    });

    it('opens confirmation dialog when remove button is clicked', async () => {
        const user = userEvent.setup();

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const removeButtons = screen.getAllByRole('button', { name: /^remove /i });
        await user.click(removeButtons[0]);

        await waitFor(() => {
            const dialog = screen.getByRole('dialog');
            expect(dialog).toBeInTheDocument();
            expect(within(dialog).getByText('Delete Download')).toBeInTheDocument();
            expect(within(dialog).getByText(/Breaking\.Bad\.S01E01\.1080p\.BluRay\.x264/)).toBeInTheDocument();
        });
    });

    it('closes confirmation dialog when cancel is clicked', async () => {
        const user = userEvent.setup();

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const removeButtons = screen.getAllByRole('button', { name: /^remove /i });
        await user.click(removeButtons[0]);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });

    it('shows success toast when remove is confirmed', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const removeButtons = screen.getAllByRole('button', { name: /^remove /i });
        await user.click(removeButtons[0]);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /delete/i }));

        await waitFor(() => {
            expect(screen.getByText('Download removed')).toBeInTheDocument();
        });
    });

    it('shows error toast when remove fails after confirmation', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockRejectedValue(new Error('Network error'));

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const removeButtons = screen.getAllByRole('button', { name: /^remove /i });
        await user.click(removeButtons[0]);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /delete/i }));

        await waitFor(() => {
            expect(screen.getByText('Failed to remove download')).toBeInTheDocument();
        });
    });

    it('does not call API when remove is cancelled', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const removeButtons = screen.getAllByRole('button', { name: /^remove /i });
        await user.click(removeButtons[0]);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(mockApiDelete).not.toHaveBeenCalled();
    });
});

describe('DownloadsPage filters', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(mockPagedDownloads);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders download filters component', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(document.querySelector('[data-component="download-filters"]')).toBeInTheDocument();
    });

    it('renders client and state filter dropdowns', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(document.querySelector('[data-filter="client"]')).toBeInTheDocument();
        expect(document.querySelector('[data-filter="state"]')).toBeInTheDocument();
    });

    it('client filter displays "All Clients" by default', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const clientTrigger = document.querySelector('[data-filter="client"]');
        expect(clientTrigger).toHaveTextContent('All Clients');
    });

    it('filters items by client when selection changes', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const clientTrigger = document.querySelector('[data-filter="client"]') as HTMLElement;
        await user.click(clientTrigger);

        const sabnzbdOption = await screen.findByRole('option', { name: 'SABnzbd' });
        await user.click(sabnzbdOption);

        const queue = screen.getByRole('list', { name: /download queue/i });
        const items = within(queue).getAllByRole('listitem');

        items.forEach((item) => {
            expect(within(item).getByText('SABnzbd')).toBeInTheDocument();
        });
    });
});

describe('DownloadsPage accessibility', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(mockPagedDownloads);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('queue list has accessible label', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(screen.getByRole('list', { name: /download queue/i })).toBeInTheDocument();
    });

    it('action buttons have aria-labels', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const actionButtons = screen.getAllByTestId('downloads-page__action-btn');
            expect(actionButtons.length).toBeGreaterThan(0);
            actionButtons.forEach((btn) => {
                expect(btn).toHaveAttribute('aria-label');
            });
        });
    });

    it('state indicator dots are hidden from screen readers', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const queue = screen.getByRole('list', { name: /download queue/i });
            const dots = queue.querySelectorAll('[data-component="badge"] [aria-hidden="true"]');
            expect(dots.length).toBeGreaterThan(0);
            dots.forEach((dot) => {
                expect(dot).toHaveAttribute('aria-hidden', 'true');
            });
        });
    });

    it('filter triggers have aria-labels', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const clientFilter = document.querySelector('[data-filter="client"]');
        expect(clientFilter).toHaveAttribute('aria-label', 'Filter by client');

        const stateFilter = document.querySelector('[data-filter="state"]');
        expect(stateFilter).toHaveAttribute('aria-label', 'Filter by state');
    });

    it('tabs have proper ARIA roles', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(screen.getByRole('tablist')).toBeInTheDocument();
        expect(screen.getAllByRole('tab').length).toBe(2);
        expect(screen.getByRole('tabpanel')).toBeInTheDocument();
    });
});

describe('DownloadsPage history tab', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(mockPagedDownloads);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('switches to history tab when clicked', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const historyTab = screen.getByRole('tab', { name: /history/i });
        await user.click(historyTab);

        expect(historyTab).toHaveAttribute('data-state', 'active');
    });

    it('renders history items when history tab is active', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            const historyList = screen.getByRole('list', { name: /download history/i });
            const items = within(historyList).getAllByRole('listitem');
            expect(items.length).toBe(3);
        });
    });

    it('renders history item titles', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            expect(screen.getByText('The.Bear.S02E10.1080p.WEB.H264')).toBeInTheDocument();
            expect(screen.getByText('Oppenheimer.2023.2160p.UHD.BluRay.x265')).toBeInTheDocument();
        });
    });

    it('renders title attributes on history item titles for tooltip on truncation', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            const titleElements = screen.getAllByTestId('downloads-page__history-item-title');
            expect(titleElements.length).toBe(3);
            titleElements.forEach((el) => {
                expect(el).toHaveAttribute('title');
                expect(el.getAttribute('title')).toBe(el.textContent);
            });
        });
    });

    it('renders Imported badges for imported downloads', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            expect(screen.getByText('Imported')).toBeInTheDocument();
        });
    });

    it('renders Failed badges for failed downloads', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            const failedElements = screen.getAllByText('Failed');
            expect(failedElements.length).toBeGreaterThanOrEqual(1);
        });
    });

    it('renders output paths for completed downloads', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            expect(screen.getByText(/\/media\/tv\/The Bear\/Season 2\//)).toBeInTheDocument();
        });
    });

    it('renders error messages for failed downloads', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            expect(screen.getByText('Hash mismatch after download')).toBeInTheDocument();
        });
    });

    it('applies data-download-id to history items', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            const historyItems = document.querySelectorAll(
                '[data-testid="downloads-page__history-item"][data-download-id]',
            );
            expect(historyItems.length).toBe(3);
            expect(historyItems[0]).toHaveAttribute('data-download-id', 'hist-1');
        });
    });

    it('renders status icons as decorative', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            const statusIcons = screen.getAllByTestId('downloads-page__history-status-icon');
            expect(statusIcons.length).toBe(3);
            statusIcons.forEach((icon) => {
                expect(icon).toHaveAttribute('aria-hidden', 'true');
            });
        });
    });
});

describe('DownloadsPage URL persistence', () => {
    const mockApiGet = vi.mocked(apiGet);

    function createUrlWrapper(url: string) {
        const queryClient = createTestQueryClient();
        return function TestWrapperWithUrl({ children }: { children: ReactNode }) {
            return (
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={[url]}>
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
        };
    }

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(mockPagedDownloads);
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('initializes state filter from URL search params', async () => {
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?state=downloading') });

        await waitFor(() => {
            const queue = screen.getByRole('list', { name: /download queue/i });
            const items = within(queue).getAllByRole('listitem');
            expect(items.length).toBe(2);
        });
    });

    it('restores history tab from ?tab=history search param', () => {
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?tab=history') });

        const historyTab = screen.getByRole('tab', { name: /history/i });
        expect(historyTab).toHaveAttribute('data-state', 'active');
    });

    it('defaults to queue tab when no tab param is present', () => {
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads') });

        const queueTab = screen.getByRole('tab', { name: /queue/i });
        expect(queueTab).toHaveAttribute('data-state', 'active');
    });

    it('restores queue state filter from ?state=paused', async () => {
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?state=paused') });

        await waitFor(() => {
            const queue = screen.getByRole('list', { name: /download queue/i });
            const items = within(queue).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
    });

    it('restores history state filter from ?tab=history&hstate=failed', async () => {
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?tab=history&hstate=failed') });

        await waitFor(() => {
            const historyList = screen.getByRole('list', { name: /download history/i });
            const items = within(historyList).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
    });

    it('writes tab change to URL when switching to history', async () => {
        const user = userEvent.setup();

        function UrlObserver() {
            const [params] = useSearchParams();
            return <span data-testid="url-params">{params.toString()}</span>;
        }

        const queryClient = createTestQueryClient();

        render(
            <QueryClientProvider client={queryClient}>
                <MemoryRouter initialEntries={['/downloads']}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <DownloadsPage />
                                <UrlObserver />
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>,
        );

        await user.click(screen.getByRole('tab', { name: /history/i }));

        const urlParams = screen.getByTestId('url-params');
        expect(urlParams.textContent).toContain('tab=history');
    });

    it('removes tab param when switching back to queue', async () => {
        const user = userEvent.setup();

        function UrlObserver() {
            const [params] = useSearchParams();
            return <span data-testid="url-params">{params.toString()}</span>;
        }

        const queryClient = createTestQueryClient();

        render(
            <QueryClientProvider client={queryClient}>
                <MemoryRouter initialEntries={['/downloads?tab=history']}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <DownloadsPage />
                                <UrlObserver />
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>,
        );

        await user.click(screen.getByRole('tab', { name: /queue/i }));

        const urlParams = screen.getByTestId('url-params');
        expect(urlParams.textContent).not.toContain('tab=');
    });

    it('writes queue state filter to URL when filter changes', async () => {
        const user = userEvent.setup();

        function UrlObserver() {
            const [params] = useSearchParams();
            return <span data-testid="url-params">{params.toString()}</span>;
        }

        const queryClient = createTestQueryClient();

        render(
            <QueryClientProvider client={queryClient}>
                <MemoryRouter initialEntries={['/downloads']}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <DownloadsPage />
                                <UrlObserver />
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>,
        );

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const stateFilter = document.querySelector('[data-filter="state"]') as HTMLElement;
        await user.click(stateFilter);
        const downloadingOption = await screen.findByRole('option', { name: 'Downloading' });
        await user.click(downloadingOption);

        const urlParams = screen.getByTestId('url-params');
        expect(urlParams.textContent).toContain('state=downloading');
    });

    it('writes queue client filter to URL when filter changes', async () => {
        const user = userEvent.setup();

        function UrlObserver() {
            const [params] = useSearchParams();
            return <span data-testid="url-params">{params.toString()}</span>;
        }

        const queryClient = createTestQueryClient();

        render(
            <QueryClientProvider client={queryClient}>
                <MemoryRouter initialEntries={['/downloads']}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <DownloadsPage />
                                <UrlObserver />
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>,
        );

        await waitFor(() => {
            expect(screen.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        });

        const clientFilter = document.querySelector('[data-filter="client"]') as HTMLElement;
        await user.click(clientFilter);
        const sabnzbdOption = await screen.findByRole('option', { name: 'SABnzbd' });
        await user.click(sabnzbdOption);

        const urlParams = screen.getByTestId('url-params');
        expect(urlParams.textContent).toContain('client=client-1');
    });

    it('restores queue client filter from ?client= search param', async () => {
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?client=client-1') });

        await waitFor(() => {
            const queue = screen.getByRole('list', { name: /download queue/i });
            const items = within(queue).getAllByRole('listitem');
            expect(items.length).toBe(3);
            items.forEach((item) => {
                expect(within(item).getByText('SABnzbd')).toBeInTheDocument();
            });
        });
    });
});

describe('DownloadsPage empty state', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows empty state when no downloads', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 25, totalItems: 0 });
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('No active downloads')).toBeInTheDocument();
            expect(screen.getByText('Downloads will appear here when items are grabbed')).toBeInTheDocument();
        });
    });

    it('shows empty state icon as decorative', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 25, totalItems: 0 });
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('No active downloads')).toBeInTheDocument();
        });

        const emptyIcon = screen.getByTestId('empty-state__icon');
        expect(emptyIcon).toHaveAttribute('aria-hidden', 'true');
    });
});
