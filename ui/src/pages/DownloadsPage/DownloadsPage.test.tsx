import { fireEvent, render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, useSearchParams, Routes, Route } from 'react-router-dom';
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
            if (value === undefined || value === null) {
                continue;
            }
            if (Array.isArray(value)) {
                for (const element of value) {
                    if (element !== undefined && element !== null) {
                        searchParams.append(key, String(element));
                    }
                }
            } else {
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
const mockHasPermission = vi.fn((_permission: string) => true as boolean);

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

vi.mock('@/auth', async () => {
    const actual = await vi.importActual<typeof import('@/auth')>('@/auth');
    return {
        ...actual,
        useAuth: () => ({
            hasPermission: (permission: string) => mockHasPermission(permission),
            hasRole: () => true,
        }),
    };
});

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { apiGet, apiPost, apiDelete } from '@/lib/api';
import { formatDateTime } from '@/lib/dateFormatting';
import type { ApiDownload, DownloadStatusValue, PagedDownloads } from '@/hooks/useDownloads';
import type { ApiDownloadClient } from '@/hooks/useDownloadClients';
import { Role, type Permission } from '@/auth/types';
import { testHasPermission } from '@/test/permissions';
import { DownloadsPage } from './DownloadsPage';

const mockApiDownloads: ApiDownload[] = [
    {
        id: 'dl-1',
        downloadClientId: 'client-1',
        downloadClientName: 'SABnzbd',
        externalId: 'sab_123',
        title: 'Breaking.Bad.S01E01.1080p.BluRay.x264',
        contentHierarchyName: 'Breaking Bad',
        contentHierarchyItemId: 'chi-1',
        posterUrl: 'https://images.example/breaking-bad.jpg',
        releaseInfoUrl: 'https://indexer.example/release/bb-101',
        status: 'Downloading',
        progress: 42,
        canPause: true,
        canResume: true,
        createdAt: '2024-01-15T10:00:00Z',
        modifiedAt: '2024-01-15T10:01:00Z',
    },
    {
        id: 'dl-2',
        downloadClientId: 'client-2',
        downloadClientName: 'DownloadClient A',
        externalId: 'qbt_456',
        title: 'The.Office.S03E07.720p.WEB.x264',
        status: 'Downloading',
        canPause: true,
        canResume: true,
        createdAt: '2024-01-15T10:05:00Z',
    },
    {
        id: 'dl-3',
        downloadClientId: 'client-1',
        downloadClientName: 'SABnzbd',
        externalId: 'sab_789',
        title: 'Dune.Part.Two.2024.2160p.UHD.BluRay.x265',
        contentHierarchyName: 'Dune: Part Two',
        contentHierarchyItemId: 'chi-3',
        status: 'Queued',
        canPause: true,
        canResume: true,
        createdAt: '2024-01-15T09:30:00Z',
    },
    {
        id: 'dl-4',
        downloadClientId: 'client-2',
        downloadClientName: 'DownloadClient A',
        externalId: 'qbt_101',
        title: 'Shogun.2024.S01E05.1080p.WEB.H264',
        status: 'Grabbed',
        canPause: true,
        canResume: true,
        createdAt: '2024-01-15T10:15:00Z',
    },
    {
        id: 'dl-5',
        downloadClientId: 'client-1',
        downloadClientName: 'SABnzbd',
        externalId: 'sab_202',
        title: 'The.Gentlemen.S01E03.1080p.WEB.x264',
        status: 'Paused',
        canPause: true,
        canResume: true,
        createdAt: '2024-01-15T10:20:00Z',
    },
    {
        id: 'dl-6',
        downloadClientId: 'client-2',
        downloadClientName: 'DownloadClient A',
        externalId: 'qbt_303',
        title: '3.Body.Problem.S01E01.720p.WEB.x264',
        status: 'Stalled',
        statusReason: 'Tracker returned warning: slow peer',
        canPause: true,
        canResume: true,
        canForceStart: true,
        createdAt: '2024-01-15T09:00:00Z',
    },
    {
        id: 'hist-1',
        downloadClientId: 'client-1',
        downloadClientName: 'SABnzbd',
        externalId: 'sab_hist_1',
        title: 'The.Bear.S02E10.1080p.WEB.H264',
        contentHierarchyName: 'The Bear',
        contentHierarchyItemId: 'chi-bear',
        status: 'Imported',
        outputPath: '/media/tv/The Bear/Season 2/The.Bear.S02E10.1080p.WEB.H264.mkv',
        dateCompleted: '2024-01-15T08:45:00Z',
        createdAt: '2024-01-14T22:00:00Z',
    },
    {
        id: 'hist-2',
        downloadClientId: 'client-2',
        downloadClientName: 'DownloadClient A',
        externalId: 'qbt_hist_2',
        title: 'Oppenheimer.2023.2160p.UHD.BluRay.x265',
        status: 'Downloaded',
        outputPath: '/media/movies/Oppenheimer (2023)/Oppenheimer.2023.2160p.UHD.BluRay.x265.mkv',
        dateCompleted: '2024-01-14T22:10:00Z',
        createdAt: '2024-01-14T20:00:00Z',
    },
    {
        id: 'hist-3',
        downloadClientId: 'client-1',
        downloadClientName: 'SABnzbd',
        externalId: 'sab_hist_3',
        title: 'True.Detective.S04E01.1080p.WEB.x264',
        status: 'Failed',
        statusReason: 'Hash mismatch after download',
        failureReason: 'DownloadErrored',
        failedAt: '2024-01-14T15:22:00Z',
        dateCompleted: '2024-01-14T15:22:00Z',
        createdAt: '2024-01-14T12:00:00Z',
    },
];

const terminalTimestampDownloads: ApiDownload[] = [
    {
        id: 'term-failed',
        downloadClientId: 'client-1',
        downloadClientName: 'SABnzbd',
        externalId: 'sab_failed_terminal',
        title: 'Terminal.Timestamp.Failed.1080p.WEB.x264',
        status: 'Failed',
        statusReason: 'Hash mismatch after download',
        failureReason: 'DownloadErrored',
        failedAt: '2024-01-16T03:00:00Z',
        dateCompleted: '2024-01-12T03:00:00Z',
        createdAt: '2024-01-10T03:00:00Z',
    },
    {
        id: 'term-cancelled',
        downloadClientId: 'client-2',
        downloadClientName: 'DownloadClient A',
        externalId: 'qbt_cancelled_terminal',
        title: 'Terminal.Timestamp.Cancelled.2160p.WEB.x265',
        status: 'Cancelled',
        modifiedAt: '2024-01-15T03:00:00Z',
        dateCompleted: '2024-01-11T03:00:00Z',
        createdAt: '2024-01-09T03:00:00Z',
    },
    {
        id: 'term-completed',
        downloadClientId: 'client-1',
        downloadClientName: 'SABnzbd',
        externalId: 'sab_completed_terminal',
        title: 'Terminal.Timestamp.Completed.720p.WEB.x264',
        status: 'Imported',
        outputPath: '/media/tv/Terminal Timestamp/Terminal.Timestamp.Completed.720p.WEB.x264.mkv',
        dateCompleted: '2024-01-14T03:00:00Z',
        createdAt: '2024-01-08T03:00:00Z',
    },
];

function getDownloadEndedAt(download: ApiDownload): string {
    if (download.status === 'Failed') {
        return download.failedAt ?? download.dateCompleted ?? download.modifiedAt ?? download.createdAt;
    }
    if (download.status === 'Cancelled') {
        return download.modifiedAt ?? download.dateCompleted ?? download.createdAt;
    }
    return download.dateCompleted ?? download.modifiedAt ?? download.createdAt;
}

function respondDownloads(url: string, source: ApiDownload[] = mockApiDownloads): PagedDownloads {
    const query = url.includes('?') ? url.slice(url.indexOf('?') + 1) : '';
    const params = new URLSearchParams(query);
    const single = params.get('status');
    const set = params.getAll('statuses');
    const client = params.get('downloadClientId');
    const pageNumber = Number.parseInt(params.get('pageNumber') ?? '1', 10) || 1;
    const pageSize = Number.parseInt(params.get('pageSize') ?? '25', 10) || 25;

    let items = source;
    if (client) {
        items = items.filter((d) => d.downloadClientId === client);
    }
    if (single) {
        items = items.filter((d) => d.status === single);
    } else if (set.length > 0) {
        items = items.filter((d) => set.includes(d.status));
    }

    const sortBy = params.get('sortBy');
    const desc = (params.get('sortDirection') ?? 'Descending') !== 'Ascending';
    const dir = desc ? -1 : 1;
    const cmp = (a: string | number, b: string | number) => (a < b ? -1 : a > b ? 1 : 0);
    if (sortBy === 'DateCompleted') {
        items = [...items].sort(
            (a, b) => dir * cmp(Date.parse(getDownloadEndedAt(a)), Date.parse(getDownloadEndedAt(b))),
        );
    } else if (sortBy === 'CreatedAt') {
        items = [...items].sort((a, b) => dir * cmp(Date.parse(a.createdAt), Date.parse(b.createdAt)));
    } else if (sortBy === 'ModifiedAt') {
        items = [...items].sort(
            (a, b) => dir * cmp(Date.parse(a.modifiedAt ?? a.createdAt), Date.parse(b.modifiedAt ?? b.createdAt)),
        );
    } else if (sortBy === 'ContentHierarchyName') {
        items = [...items].sort(
            (a, b) =>
                dir *
                cmp(
                    (a.contentHierarchyName ?? a.title).toLowerCase(),
                    (b.contentHierarchyName ?? b.title).toLowerCase(),
                ),
        );
    } else if (sortBy === 'DownloadClientName') {
        items = [...items].sort(
            (a, b) => dir * cmp((a.downloadClientName ?? '').toLowerCase(), (b.downloadClientName ?? '').toLowerCase()),
        );
    } else if (sortBy === 'Status') {
        items = [...items].sort((a, b) => dir * cmp(a.status, b.status));
    } else if (sortBy === 'Progress') {
        items = [...items].sort((a, b) => dir * cmp(a.progress ?? -1, b.progress ?? -1));
    }

    const totalItems = items.length;
    const start = (pageNumber - 1) * pageSize;
    return {
        items: items.slice(start, start + pageSize),
        pageNumber,
        pageSize,
        totalItems,
    };
}

const mockApiClients: ApiDownloadClient[] = [
    {
        id: 'client-1',
        name: 'SABnzbd',
        providerType: 'sabnzbd',
        enabled: true,
        status: 'Healthy',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'client-2',
        name: 'DownloadClient A',
        providerType: 'downloadclient-a',
        enabled: true,
        status: 'Healthy',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
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
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
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
                                <Routes>
                                    <Route path="/" element={<>{children}</>} />
                                    <Route path="/library/:contentId" element={<div>Library Detail</div>} />
                                </Routes>
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

function setupApiGet(source: ApiDownload[] = mockApiDownloads) {
    const mockApiGet = vi.mocked(apiGet);
    mockApiGet.mockImplementation((url: string) => {
        if (url.startsWith('/downloads')) {
            return Promise.resolve(respondDownloads(url, source));
        }
        if (url === '/download-clients') {
            return Promise.resolve(mockApiClients);
        }
        return Promise.resolve([]);
    });
    return mockApiGet;
}

function getTable(): HTMLElement {
    return document.querySelector('[data-component="data-table"]') as HTMLElement;
}

function getTableRow(id: string): HTMLElement {
    const row = getTable().querySelector<HTMLElement>(`[data-row-id="${id}"]`);
    expect(row).toBeInTheDocument();
    return row as HTMLElement;
}

function getEndedCell(id: string): HTMLElement {
    const cell = getTableRow(id).querySelector<HTMLElement>('[data-column="completed"] .downloads-page__date');
    expect(cell).toBeInTheDocument();
    return cell as HTMLElement;
}

function getTableRowIds(): string[] {
    return Array.from(getTable().querySelectorAll<HTMLElement>('.data-table__row')).map(
        (row) => row.dataset.rowId ?? '',
    );
}

function createMatchMedia(width: number) {
    return vi.fn((query: string) => {
        const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
        const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);
        return {
            matches:
                (minWidth === null || width >= Number(minWidth[1])) &&
                (maxWidth === null || width <= Number(maxWidth[1])),
            media: query,
            onchange: null,
            addListener: () => {},
            removeListener: () => {},
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => false,
        };
    });
}

beforeEach(() => {
    vi.stubGlobal('matchMedia', createMatchMedia(1024));
});

afterEach(() => {
    vi.unstubAllGlobals();
});

async function openRowMenu(user: ReturnType<typeof userEvent.setup>, titleFragment: RegExp) {
    const trigger = within(getTable()).getByRole('button', { name: titleFragment });
    await user.click(trigger);
}

describe('DownloadsPage shell', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders selectable mobile cards only at a 390px viewport', async () => {
        const matchMedia = createMatchMedia(390);
        vi.stubGlobal('matchMedia', matchMedia);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const queueCards = await screen.findByRole('list', { name: 'Download queue' });
        expect(await within(queueCards).findByText('Breaking Bad')).toBeInTheDocument();
        expect(matchMedia).toHaveBeenCalledWith('(max-width: 1023px)');
        expect(document.querySelector('[data-component="data-table"]')).not.toBeInTheDocument();
        expect(screen.queryByRole('columnheader', { name: 'Title' })).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /^select$/i }));
        expect(within(queueCards).queryAllByRole('checkbox')).toHaveLength(0);
        await user.click(within(queueCards).getByRole('button', { name: 'Select Breaking Bad' }));

        const queueBar = document.querySelector(
            '[data-component="downloads-mobile-selection-bar-queue"]',
        ) as HTMLElement;
        expect(within(queueBar).getByText('1 selected')).toBeInTheDocument();
    });

    it('hands queue progress directly to the purpose-built card at a 768px viewport', async () => {
        const matchMedia = createMatchMedia(768);
        vi.stubGlobal('matchMedia', matchMedia);
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const queueCards = await screen.findByRole('list', { name: 'Download queue' });
        expect(await within(queueCards).findByText('Breaking Bad')).toBeInTheDocument();

        const progress = within(queueCards).getByTestId('downloads-page__progress');
        expect(matchMedia).toHaveBeenCalledWith('(max-width: 1023px)');
        expect(document.querySelector('[data-component="data-table"]')).not.toBeInTheDocument();
        expect(progress.querySelector('.downloads-page__progress-fill')).toHaveStyle({ width: '42%' });
    });

    it('renders with the data-page attribute and title', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(document.querySelector('[data-page="downloads"]')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Downloads', level: 1 })).toBeInTheDocument();
    });

    it('renders the active count as plain text in the Active tab label', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getAllByTestId('downloads-page__tab-count')[0]).toHaveTextContent('6');
        });
    });

    it('keeps terminal downloads out of the empty Active panel and shows them in History', async () => {
        const user = userEvent.setup();
        setupApiGet(terminalTimestampDownloads);
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getAllByText('No active downloads').length).toBeGreaterThan(0);
        });

        expect(within(getTable()).queryByText('Terminal.Timestamp.Failed.1080p.WEB.x264')).not.toBeInTheDocument();

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            expect(screen.getByTestId('downloads-page__tab-count')).toHaveTextContent('3');
            expect(within(getTable()).getByText('Terminal.Timestamp.Failed.1080p.WEB.x264')).toBeInTheDocument();
            expect(within(getTable()).getByText('Terminal.Timestamp.Cancelled.2160p.WEB.x265')).toBeInTheDocument();
            expect(within(getTable()).getByText('Terminal.Timestamp.Completed.720p.WEB.x264')).toBeInTheDocument();
        });
    });

    it('renders Active and History tabs with Active active by default', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const activeTab = screen.getByRole('tab', { name: /active/i });
        expect(activeTab).toHaveAttribute('data-state', 'active');
        expect(screen.getByRole('tab', { name: /history/i })).toBeInTheDocument();
    });
});

describe('DownloadsPage table columns', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('renders a sortable table with the expected column headers', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });

        const table = getTable();
        expect(table).toBeInTheDocument();
        for (const header of ['Title', 'Client', 'Status', 'Added', 'Last status']) {
            expect(within(table).getByText(header)).toBeInTheDocument();
        }
    });

    it('reserves enough Status column width for the Downloading badge', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });

        expect(getTable().querySelector('[data-column="status"]')).toHaveStyle({ width: '156px' });
        expect(within(getTable()).getAllByText('Downloading')).not.toHaveLength(0);
    });

    it('shows the content title (not the release title) linking to the library item', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });

        const link = within(getTable()).getByRole('link', { name: 'Breaking Bad' });
        expect(link).toHaveAttribute('href', '/library/chi-1');
        expect(within(getTable()).queryByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).not.toBeInTheDocument();
    });

    it('falls back to the release title (no link) when there is no content link', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(within(getTable()).getByText('The.Office.S03E07.720p.WEB.x264')).toBeInTheDocument();
        });

        expect(
            within(getTable()).queryByRole('link', { name: 'The.Office.S03E07.720p.WEB.x264' }),
        ).not.toBeInTheDocument();
    });

    it('renders client names per row', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getAllByText('SABnzbd').length).toBeGreaterThan(0);
            expect(screen.getAllByText('DownloadClient A').length).toBeGreaterThan(0);
        });
    });

    it('navigates to the library item when the title link is clicked (SPA, no reload)', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });

        await user.click(within(getTable()).getByRole('link', { name: 'Breaking Bad' }));

        await waitFor(() => {
            expect(screen.getByText('Library Detail')).toBeInTheDocument();
        });
    });
});

describe('DownloadsPage sorting', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
    });

    afterEach(() => vi.restoreAllMocks());

    it('issues the default sort (CreatedAt desc) for the active tab', async () => {
        const mockApiGet = setupApiGet();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const listCalls = mockApiGet.mock.calls
                .map((c) => String(c[0]))
                .filter((u) => u.startsWith('/downloads') && u.includes('statuses='));
            expect(
                listCalls.some((u) => u.includes('sortBy=CreatedAt') && u.includes('sortDirection=Descending')),
            ).toBe(true);
        });
    });

    it('requests a new server sort when a column header is clicked', async () => {
        const user = userEvent.setup();
        const mockApiGet = setupApiGet();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });

        await user.click(within(getTable()).getByRole('button', { name: 'Sort by Client' }));

        await waitFor(() => {
            const sorted = mockApiGet.mock.calls
                .map((c) => String(c[0]))
                .some((u) => u.startsWith('/downloads') && u.includes('sortBy=DownloadClientName'));
            expect(sorted).toBe(true);
        });
    });

    it('requests a progress server sort from the active tab Progress header', async () => {
        const user = userEvent.setup();
        const mockApiGet = setupApiGet();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });

        await user.click(within(getTable()).getByRole('button', { name: 'Sort by Progress' }));

        await waitFor(() => {
            const sorted = mockApiGet.mock.calls
                .map((c) => String(c[0]))
                .some(
                    (u) => u.startsWith('/downloads') && u.includes('sortBy=Progress') && u.includes('sortDirection='),
                );
            expect(sorted).toBe(true);
        });
    });

    it('requests both directions of the Ended server sort for history', async () => {
        const user = userEvent.setup();
        const mockApiGet = setupApiGet();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        await user.click(screen.getByRole('tab', { name: /history/i }));
        await waitFor(() => expect(within(getTable()).getByText('The Bear')).toBeInTheDocument());
        expect(getTableRowIds()).toEqual(['hist-1', 'hist-2', 'hist-3']);

        const endedHeader = within(getTable()).getByRole('button', { name: 'Sort by Ended' });
        await user.click(endedHeader);

        await waitFor(() => {
            const requestedAscending = mockApiGet.mock.calls
                .map((call) => String(call[0]))
                .some(
                    (url) =>
                        url.startsWith('/downloads') &&
                        url.includes('sortBy=DateCompleted') &&
                        url.includes('sortDirection=Ascending'),
                );
            expect(requestedAscending).toBe(true);
        });
        await waitFor(() => expect(getTableRowIds()).toEqual(['hist-3', 'hist-2', 'hist-1']));

        await user.click(within(getTable()).getByRole('button', { name: 'Sort by Ended' }));

        await waitFor(() => {
            const requestedDescending = mockApiGet.mock.calls
                .map((call) => String(call[0]))
                .some(
                    (url) =>
                        url.startsWith('/downloads') &&
                        url.includes('sortBy=DateCompleted') &&
                        url.includes('sortDirection=Descending'),
                );
            expect(requestedDescending).toBe(true);
        });
        await waitFor(() => expect(getTableRowIds()).toEqual(['hist-1', 'hist-2', 'hist-3']));
    });
});

describe('DownloadsPage row actions', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('keeps an open row menu mounted when refetched download data arrives', async () => {
        const user = userEvent.setup();
        let source = mockApiDownloads;
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(respondDownloads(url, source));
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        await openRowMenu(user, /actions for breaking bad/i);
        expect(await screen.findByRole('menuitem', { name: /view details/i })).toBeInTheDocument();

        source = mockApiDownloads.map((download) =>
            download.id === 'dl-1' ? { ...download, progress: 43 } : download,
        );
        await queryClient.invalidateQueries({ queryKey: ['downloads', 'list'] });

        await waitFor(() => expect(within(getTable()).getByText('43%')).toBeInTheDocument());
        expect(screen.getByRole('menuitem', { name: /view details/i })).toBeInTheDocument();
    });

    it('offers Pause for a downloading row and calls the pause endpoint', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        await user.click(await screen.findByRole('menuitem', { name: /^pause$/i }));

        await waitFor(() => expect(mockApiPost).toHaveBeenCalledWith('/downloads/dl-1/pause'));
    });

    it('offers Resume for a queued row and calls the resume endpoint', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Dune: Part Two')).toBeInTheDocument());

        await openRowMenu(user, /actions for dune: part two/i);
        await user.click(await screen.findByRole('menuitem', { name: /^resume$/i }));

        await waitFor(() => expect(mockApiPost).toHaveBeenCalledWith('/downloads/dl-3/resume'));
    });

    it('offers Force start only for a force-start-capable row and calls the force-start endpoint', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        expect(screen.queryByRole('menuitem', { name: /force start/i })).not.toBeInTheDocument();
        await user.keyboard('{Escape}');

        await openRowMenu(user, /actions for 3\.body\.problem/i);
        await user.click(await screen.findByRole('menuitem', { name: /force start/i }));

        await waitFor(() => expect(mockApiPost).toHaveBeenCalledWith('/downloads/dl-6/force-start'));
        await waitFor(() => expect(screen.getByText(/Kicked/)).toBeInTheDocument());
    });

    it('offers Stop for an in-flight row and calls the cancel endpoint', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiPost.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        await user.click(await screen.findByRole('menuitem', { name: /^stop$/i }));

        await waitFor(() => expect(mockApiPost).toHaveBeenCalledWith('/downloads/dl-1/cancel'));
        expect(mockApiDelete).not.toHaveBeenCalled();
    });

    it('Forget confirms then issues DELETE /downloads/{id} and makes no files call', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        await user.click(await screen.findByRole('menuitem', { name: /^forget$/i }));

        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).queryByRole('switch', { name: /also delete files from disk/i })).not.toBeInTheDocument();
        await user.click(within(dialog).getByRole('button', { name: /^forget$/i }));

        await waitFor(() => expect(mockApiDelete).toHaveBeenCalledWith('/downloads/dl-1'));
        expect(mockApiDelete).not.toHaveBeenCalledWith('/downloads/dl-1/files');
    });

    it('Remove confirms then issues DELETE /downloads/{id}/files', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        await user.click(await screen.findByRole('menuitem', { name: /^remove$/i }));

        const dialog = await screen.findByRole('dialog');
        await user.click(within(dialog).getByRole('button', { name: /^remove$/i }));

        await waitFor(() => expect(mockApiDelete).toHaveBeenCalledWith('/downloads/dl-1/files'));
    });

    it('shows a success toast after a successful pause', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        await user.click(await screen.findByRole('menuitem', { name: /^pause$/i }));

        await waitFor(() => expect(screen.getByText('Download paused')).toBeInTheDocument());
    });
});

describe('DownloadsPage details modal', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('opens a modal showing the file name, transfer id, and release title', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        await user.click(await screen.findByRole('menuitem', { name: /view details/i }));

        const dialog = await screen.findByTestId('download-details-dialog');
        expect(within(dialog).getByText('Breaking.Bad.S01E01.1080p.BluRay.x264')).toBeInTheDocument();
        expect(within(dialog).getByText('sab_123')).toBeInTheDocument();
    });

    it('renders the release page as an external indexer link when a URL is known', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        await user.click(await screen.findByRole('menuitem', { name: /view details/i }));

        const dialog = await screen.findByTestId('download-details-dialog');
        const link = dialog.querySelector('[data-detail="Release page"] a') as HTMLElement;
        expect(link).toHaveAttribute('href', 'https://indexer.example/release/bb-101');
        expect(link).toHaveAttribute('target', '_blank');
        expect(link).toHaveAttribute('rel', 'noopener noreferrer');
        expect(link).toHaveTextContent('https://indexer.example/release/bb-101');
    });

    it('omits the release page row when no indexer URL is known', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() =>
            expect(within(getTable()).getByText('The.Office.S03E07.720p.WEB.x264')).toBeInTheDocument(),
        );

        await openRowMenu(user, /actions for the\.office/i);
        await user.click(await screen.findByRole('menuitem', { name: /view details/i }));

        const dialog = await screen.findByTestId('download-details-dialog');
        expect(dialog.querySelector('[data-detail="Release page"]')).toBeNull();
        expect(within(dialog).queryByRole('link')).not.toBeInTheDocument();
        expect(dialog.querySelector('[data-detail="Title"]')).toHaveTextContent('The.Office.S03E07.720p.WEB.x264');
    });

    it('shows the output path and failure reason for a failed history row', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() =>
            expect(within(getTable()).getByText('True.Detective.S04E01.1080p.WEB.x264')).toBeInTheDocument(),
        );

        await openRowMenu(user, /actions for true\.detective/i);
        await user.click(await screen.findByRole('menuitem', { name: /view details/i }));

        const dialog = await screen.findByTestId('download-details-dialog');
        expect(within(dialog).getByText('Hash mismatch after download')).toBeInTheDocument();
        expect(within(dialog).getByText('Download errored')).toBeInTheDocument();
    });
});

describe('DownloadsPage poster column', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('renders a poster thumbnail for a row that resolves one', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        const artwork = within(getTable()).getByRole('img', { name: 'Breaking Bad artwork' });
        expect(artwork).toHaveAttribute('src', 'https://images.example/breaking-bad.jpg');
        fireEvent.error(artwork);
        expect(within(getTable()).queryByRole('img', { name: 'Breaking Bad artwork' })).not.toBeInTheDocument();
        expect(getTable().querySelector('.downloads-page__poster--placeholder')).toBeInTheDocument();
    });
});

describe('DownloadsPage progress', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('renders a Progress column with a bar and percent for a reporting active row', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        const table = getTable();
        expect(within(table).getByText('Progress')).toBeInTheDocument();
        expect(within(table).getByText('42%')).toBeInTheDocument();
        const bar = within(table).getAllByRole('progressbar')[0];
        expect(bar).toHaveAttribute('aria-valuenow', '42');
    });

    it('renders a muted dash for an active row with no reported percent', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() =>
            expect(within(getTable()).getByText('The.Office.S03E07.720p.WEB.x264')).toBeInTheDocument(),
        );

        const table = getTable();
        expect(within(table).queryByText('0%')).not.toBeInTheDocument();
    });

    it('omits the Progress column on the history tab', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => expect(within(getTable()).getByText('The Bear')).toBeInTheDocument());

        expect(within(getTable()).queryByText('Progress')).not.toBeInTheDocument();
    });

    it('renders the progress bar in the mobile card for an active row', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(768));
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const queueCards = await screen.findByRole('list', { name: 'Download queue' });
        await within(queueCards).findByText('Breaking Bad');

        const card = document.querySelector('.downloads-page__mobile-list [data-download-id="dl-1"]') as HTMLElement;
        expect(card).toBeInTheDocument();
        expect(within(card).getByRole('progressbar')).toHaveAttribute('aria-valuenow', '42');
        expect(within(card).getByText('42%')).toBeInTheDocument();
    });
});

describe('DownloadsPage history tab', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('switches to history and renders the Ended column with terminal rows', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            expect(within(getTable()).getByText('The Bear')).toBeInTheDocument();
            expect(within(getTable()).getByText('Oppenheimer.2023.2160p.UHD.BluRay.x265')).toBeInTheDocument();
        });

        const table = document.querySelector('[data-component="data-table"]') as HTMLElement;
        expect(within(table).getByText('Ended')).toBeInTheDocument();
        expect(within(table).queryByText('Added')).not.toBeInTheDocument();
    });

    it('issues the default DateCompleted sort for history', async () => {
        const user = userEvent.setup();
        const mockApiGet = setupApiGet();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            const sorted = mockApiGet.mock.calls
                .map((c) => String(c[0]))
                .some(
                    (u) =>
                        u.startsWith('/downloads') &&
                        u.includes('statuses=Imported') &&
                        u.includes('sortBy=DateCompleted'),
                );
            expect(sorted).toBe(true);
        });
    });

    it('renders Failed history Ended from failedAt on a Failed filter URL', async () => {
        setupApiGet(terminalTimestampDownloads);
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?tab=history&hstate=Failed') });

        await waitFor(() =>
            expect(within(getTable()).getByText('Terminal.Timestamp.Failed.1080p.WEB.x264')).toBeInTheDocument(),
        );

        expect(getEndedCell('term-failed')).toHaveAttribute('title', formatDateTime('2024-01-16T03:00:00Z'));
        expect(getEndedCell('term-failed')).not.toHaveAttribute('title', formatDateTime('2024-01-10T03:00:00Z'));
    });

    it('shows cancelled downloads when the cancelled filter is selected', async () => {
        // Arrange
        const user = userEvent.setup();
        setupApiGet(terminalTimestampDownloads);
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?tab=history') });
        await screen.findByText('Terminal.Timestamp.Cancelled.2160p.WEB.x265');
        // Act
        await user.click(within(getTable()).getByRole('button', { name: 'Filter by Status' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Cancelled' }));
        // Assert
        await waitFor(() => expect(apiGet).toHaveBeenCalledWith(expect.stringContaining('status=Cancelled')));
        expect(within(getTable()).getByText('Terminal.Timestamp.Cancelled.2160p.WEB.x265')).toBeInTheDocument();
    });

    it('renders Cancelled and completed history Ended from their terminal timestamps', async () => {
        setupApiGet(terminalTimestampDownloads);
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?tab=history') });

        await waitFor(() =>
            expect(within(getTable()).getByText('Terminal.Timestamp.Cancelled.2160p.WEB.x265')).toBeInTheDocument(),
        );

        expect(getEndedCell('term-cancelled')).toHaveAttribute('title', formatDateTime('2024-01-15T03:00:00Z'));
        expect(getEndedCell('term-completed')).toHaveAttribute('title', formatDateTime('2024-01-14T03:00:00Z'));
    });

    it('sorts history rows by terminal timestamp by default', async () => {
        setupApiGet(terminalTimestampDownloads);
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?tab=history') });

        await waitFor(() => expect(getTableRowIds()).toEqual(['term-failed', 'term-cancelled', 'term-completed']));
    });
});

describe('DownloadsPage filters and summary', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('drops the redundant filter chrome (toolbar dropdowns and the status-count pill row)', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        expect(document.querySelector('[data-component="download-filters"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-filter="client"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-filter="state"]')).not.toBeInTheDocument();

        expect(document.querySelector('[aria-label="Queue summary"]')).not.toBeInTheDocument();
        expect(document.querySelector('[aria-label="History summary"]')).not.toBeInTheDocument();
    });

    it('exposes a funnel filter on the Client and Status column headers', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        const table = getTable();
        expect(within(table).getByRole('button', { name: 'Filter by Client' })).toBeInTheDocument();
        expect(within(table).getByRole('button', { name: 'Filter by Status' })).toBeInTheDocument();
    });

    it('filters by client via the Client header funnel', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('button', { name: 'Filter by Client' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'SABnzbd' }));

        await waitFor(() => {
            expect(screen.queryByText('The.Office.S03E07.720p.WEB.x264')).not.toBeInTheDocument();
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });
    });

    it('filters by status via the Status header funnel and offers a Clear filters reset', async () => {
        const user = userEvent.setup();
        const mockApiGet = setupApiGet();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('button', { name: 'Filter by Status' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Paused' }));

        await waitFor(() => {
            const filtered = mockApiGet.mock.calls
                .map((c) => String(c[0]))
                .some((u) => u.startsWith('/downloads') && u.includes('status=Paused') && !u.includes('statuses='));
            expect(filtered).toBe(true);
        });

        const filterBar = getTable().querySelector('[data-component="data-table-filter-bar"]') as HTMLElement;
        const clearFilters = within(filterBar).getByRole('button', { name: 'Clear filters' });
        expect(screen.getAllByRole('button', { name: 'Clear filters' })).toEqual([clearFilters]);

        await user.click(clearFilters);
        await waitFor(() => {
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });
    });

    it('clears an active queue filter from the table filter bar', async () => {
        const user = userEvent.setup();
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?qstate=Paused') });

        await waitFor(() =>
            expect(within(getTable()).getByText('The.Gentlemen.S01E03.1080p.WEB.x264')).toBeInTheDocument(),
        );

        const filterBar = getTable().querySelector('[data-component="data-table-filter-bar"]') as HTMLElement;
        const clearFilters = within(filterBar).getByRole('button', { name: 'Clear filters' });
        expect(screen.getAllByRole('button', { name: 'Clear filters' })).toEqual([clearFilters]);

        await user.click(clearFilters);

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        expect(getTable().querySelector('[data-component="data-table-filter-bar"]')).not.toBeInTheDocument();
    });

    it('clears an active history filter from the table filter bar', async () => {
        const user = userEvent.setup();
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?tab=history&hclient=client-1') });

        await waitFor(() => expect(within(getTable()).getByText('The Bear')).toBeInTheDocument());

        const filterBar = getTable().querySelector('[data-component="data-table-filter-bar"]') as HTMLElement;
        const clearFilters = within(filterBar).getByRole('button', { name: 'Clear filters' });
        expect(screen.getAllByRole('button', { name: 'Clear filters' })).toEqual([clearFilters]);

        await user.click(clearFilters);

        await waitFor(() =>
            expect(within(getTable()).getByText('Oppenheimer.2023.2160p.UHD.BluRay.x265')).toBeInTheDocument(),
        );
        expect(getTable().querySelector('[data-component="data-table-filter-bar"]')).not.toBeInTheDocument();
    });

    it('clears an active queue filter from the card-layout list toolbar at a 390px viewport', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?qstate=Paused') });

        const queueCards = await screen.findByRole('list', { name: 'Download queue' });
        expect(await within(queueCards).findByText('The.Gentlemen.S01E03.1080p.WEB.x264')).toBeInTheDocument();

        const toolbar = document.querySelector('[data-component="mobile-list-toolbar"]') as HTMLElement;
        expect(toolbar).toBeInTheDocument();

        const clearFilters = within(toolbar).getByRole('button', { name: 'Clear filters' });
        expect(screen.getAllByRole('button', { name: 'Clear filters' })).toEqual([clearFilters]);

        await user.click(clearFilters);

        await waitFor(() => expect(within(queueCards).getByText('Breaking Bad')).toBeInTheDocument());
        expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
    });

    it('keeps the card-layout list toolbar reachable when a queue filter matches no rows', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?qclient=client-2&qstate=Paused') });

        expect(await screen.findByText('No active downloads')).toBeInTheDocument();
        expect(screen.queryByRole('list', { name: 'Download queue' })).not.toBeInTheDocument();
        const queueCards = document.querySelector('.downloads-page__mobile-list') as HTMLElement;

        const toolbar = document.querySelector('[data-component="mobile-list-toolbar"]') as HTMLElement;
        expect(toolbar).toBeInTheDocument();

        const clearFilters = within(toolbar).getByRole('button', { name: 'Clear filters' });
        expect(screen.getAllByRole('button', { name: 'Clear filters' })).toEqual([clearFilters]);

        await user.click(clearFilters);

        await waitFor(() => expect(within(queueCards).getByText('Breaking Bad')).toBeInTheDocument());
        expect(within(queueCards).queryByText('No active downloads')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
    });

    it('keeps the card-layout list toolbar reachable when a history filter matches no rows', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();
        render(<DownloadsPage />, {
            wrapper: createUrlWrapper('/downloads?tab=history&hclient=client-2&hstate=Failed'),
        });

        expect(await screen.findByText('No download history')).toBeInTheDocument();
        expect(screen.queryByRole('list', { name: 'Download history' })).not.toBeInTheDocument();
        const historyCards = document.querySelector('.downloads-page__mobile-list') as HTMLElement;

        const toolbar = document.querySelector('[data-component="mobile-list-toolbar"]') as HTMLElement;
        expect(toolbar).toBeInTheDocument();

        const clearFilters = within(toolbar).getByRole('button', { name: 'Clear filters' });
        expect(screen.getAllByRole('button', { name: 'Clear filters' })).toEqual([clearFilters]);

        await user.click(clearFilters);

        await waitFor(() => expect(within(historyCards).getByText('The Bear')).toBeInTheDocument());
        expect(within(historyCards).queryByText('No download history')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
    });
});

describe('DownloadsPage request fan-out', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('loads each tab only when it becomes active with no per-status count request', async () => {
        const user = userEvent.setup();
        const mockApiGet = setupApiGet();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        expect(
            mockApiGet.mock.calls.map((call) => String(call[0])).filter((url) => url.startsWith('/downloads')),
        ).toHaveLength(1);

        await user.click(screen.getByRole('tab', { name: /history/i }));
        await waitFor(() => expect(screen.getAllByTestId('downloads-page__tab-count')[1]).toHaveTextContent('3'));

        const listCalls = mockApiGet.mock.calls
            .map((call) => String(call[0]))
            .filter((url) => url.startsWith('/downloads'));
        expect(listCalls.filter((url) => /[?&]status=/.test(url))).toEqual([]);
        expect(listCalls.filter((url) => /[?&]pageSize=1(?:&|$)/.test(url))).toEqual([]);
        expect(new Set(listCalls).size).toBe(2);
    });

    it('keeps the full Active total via one grouped count request when a status filter is applied', async () => {
        const user = userEvent.setup();
        const mockApiGet = setupApiGet();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('button', { name: 'Filter by Status' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Paused' }));

        await waitFor(() => expect(getTableRowIds()).toEqual(['dl-5']));
        await waitFor(() => expect(screen.getAllByTestId('downloads-page__tab-count')[0]).toHaveTextContent('6'));

        const listCalls = mockApiGet.mock.calls
            .map((call) => String(call[0]))
            .filter((url) => url.startsWith('/downloads'));
        const countCalls = listCalls.filter((url) => /[?&]pageSize=1(?:&|$)/.test(url));
        expect(countCalls).toHaveLength(1);
        expect(countCalls[0]).toContain('statuses=');
        expect(countCalls.filter((url) => /[?&]status=/.test(url))).toEqual([]);
    });
});

describe('DownloadsPage URL persistence', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('restores the history tab from ?tab=history', () => {
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?tab=history') });
        expect(screen.getByRole('tab', { name: /history/i })).toHaveAttribute('data-state', 'active');
    });

    it('falls back to Active and normalizes the URL when ?tab= is invalid', async () => {
        function UrlObserver() {
            const [params] = useSearchParams();
            return <span data-testid="url-params">{params.toString()}</span>;
        }

        render(
            <>
                <DownloadsPage />
                <UrlObserver />
            </>,
            { wrapper: createUrlWrapper('/downloads?tab=bogus&qstate=Paused') },
        );

        expect(screen.getByRole('tab', { name: /active/i })).toHaveAttribute('data-state', 'active');
        await waitFor(() => {
            expect(screen.getByTestId('url-params').textContent).toBe('qstate=Paused');
        });
        await waitFor(() => {
            expect(within(getTable()).getByText('The.Gentlemen.S01E03.1080p.WEB.x264')).toBeInTheDocument();
        });
    });

    it('normalizes invalid download table URL params by replacement', async () => {
        function UrlObserver() {
            const [params] = useSearchParams();
            return <span data-testid="url-params">{params.toString()}</span>;
        }

        render(
            <>
                <DownloadsPage />
                <UrlObserver />
            </>,
            {
                wrapper: createUrlWrapper(
                    '/downloads?qstate=Finished&hstate=Paused&qsort=missing:desc&hsort=title:sideways&qpage=0&hpage=nope',
                ),
            },
        );

        await waitFor(() => {
            expect(screen.getByTestId('url-params').textContent).toBe('');
        });
        await waitFor(() => {
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });
    });

    it('clamps an out-of-range queue page after totals load', async () => {
        function UrlObserver() {
            const [params] = useSearchParams();
            return <span data-testid="url-params">{params.toString()}</span>;
        }

        render(
            <>
                <DownloadsPage />
                <UrlObserver />
            </>,
            { wrapper: createUrlWrapper('/downloads?qpage=99') },
        );

        await waitFor(() => {
            expect(screen.getByTestId('url-params').textContent).toBe('');
        });
        await waitFor(() => {
            expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument();
        });
    });

    it('restores a queue state filter from ?qstate=Paused (single row)', async () => {
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?qstate=Paused') });

        await waitFor(() => {
            const rows = document.querySelectorAll('[data-component="data-table"] .data-table__row');
            expect(rows.length).toBe(1);
        });
    });

    it('restores the queue and history filters independently from their namespaced params', async () => {
        render(<DownloadsPage />, { wrapper: createUrlWrapper('/downloads?qstate=Paused&hstate=Imported') });

        await waitFor(() => {
            const rows = document.querySelectorAll('[data-component="data-table"] .data-table__row');
            expect(rows.length).toBe(1);
            expect(within(getTable()).getByText('The.Gentlemen.S01E03.1080p.WEB.x264')).toBeInTheDocument();
        });
    });

    it('keeps Active and History filter state separate when switching tabs', async () => {
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

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('button', { name: 'Filter by Status' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Paused' }));

        await waitFor(() => {
            expect(screen.getByTestId('url-params').textContent).toContain('qstate=Paused');
        });

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => expect(within(getTable()).getByText('The Bear')).toBeInTheDocument());

        const historyParams = screen.getByTestId('url-params').textContent ?? '';
        expect(historyParams).not.toContain('hstate=Paused');
        expect(within(getTable()).getByRole('button', { name: 'Filter by Status' })).toBeInTheDocument();
        expect(
            within(getTable()).queryByRole('button', { name: /Status filtered, edit filter/i }),
        ).not.toBeInTheDocument();

        expect(historyParams).toContain('qstate=Paused');

        await user.click(screen.getByRole('tab', { name: /active/i }));
        await waitFor(() => {
            expect(
                within(getTable()).getByRole('button', { name: /Status filtered, edit filter/i }),
            ).toBeInTheDocument();
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
        expect(screen.getByTestId('url-params').textContent).toContain('tab=history');
    });
});

describe('DownloadsPage paging', () => {
    const manyActive: ApiDownload[] = Array.from({ length: 30 }, (_, i) => ({
        id: `q-${i}`,
        downloadClientId: 'client-1',
        downloadClientName: 'SABnzbd',
        externalId: `ext-${i}`,
        title: `Active Item ${i}`,
        status: 'Queued' as DownloadStatusValue,
        canPause: true,
        canResume: true,
        createdAt: new Date(Date.UTC(2024, 0, 1, 0, 0, 30 - i)).toISOString(),
    }));

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return Promise.resolve(respondDownloads(url, manyActive));
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });
    });

    afterEach(() => vi.restoreAllMocks());

    it('renders a pager and limits the first page to the page size', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const rows = document.querySelectorAll('[data-component="data-table"] .data-table__row');
            expect(rows.length).toBe(25);
        });

        expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
    });

    it('requests the next page server-side', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(screen.getByText('Page 1 of 2')).toBeInTheDocument());

        await user.click(screen.getByRole('button', { name: /go to next page/i }));

        await waitFor(() => {
            const advanced = vi
                .mocked(apiGet)
                .mock.calls.map((c) => String(c[0]))
                .some((u) => u.startsWith('/downloads') && u.includes('statuses=') && u.includes('pageNumber=2'));
            expect(advanced).toBe(true);
        });
    });
});

describe('DownloadsPage bulk selection', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => vi.restoreAllMocks());

    it('does not render a Clear all control', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        expect(screen.queryByRole('button', { name: /clear all/i })).not.toBeInTheDocument();
    });

    it('keeps the bulk-action bar collapsed and inert when nothing is selected', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        expect(bar).toBeInTheDocument();
        expect(bar).toHaveClass('bulk-actions-toolbar-presence');
        expect(bar).toHaveAttribute('data-visible', 'false');
        expect(bar).toHaveAttribute('aria-hidden', 'true');
        expect(within(bar).queryByRole('button', { name: /forget/i })).not.toBeInTheDocument();
        expect(within(bar).queryByRole('button', { name: /remove/i })).not.toBeInTheDocument();
    });

    it('enables the actions once a workable row is selected', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        const checkbox = within(getTable()).getByRole('checkbox', { name: /select breaking bad/i });
        await user.click(checkbox);

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        expect(within(bar).getByText('1 selected')).toBeInTheDocument();
        expect(within(bar).getByRole('button', { name: /forget/i })).toBeEnabled();
        expect(within(bar).getByRole('button', { name: /remove/i })).toBeEnabled();
        expect(within(bar).getByRole('button', { name: /^pause$/i })).toBeEnabled();
        expect(within(bar).getByRole('button', { name: /^stop$/i })).toBeEnabled();
        expect(within(bar).getByRole('button', { name: /^start$/i })).toBeEnabled();
    });

    it('lets a mobile History card select a download for bulk actions', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() =>
            expect(
                within(screen.getByRole('list', { name: 'Download queue' })).getByText('Breaking Bad'),
            ).toBeInTheDocument(),
        );
        await user.click(screen.getByRole('tab', { name: /history/i }));
        await waitFor(() =>
            expect(
                within(screen.getByRole('list', { name: 'Download history' })).getByText('The Bear'),
            ).toBeInTheDocument(),
        );

        const historyCards = screen.getByRole('list', { name: 'Download history' });
        await user.click(screen.getByRole('button', { name: /^select$/i }));
        const toggle = within(historyCards).getByRole('button', { name: /select the bear/i });
        await user.click(toggle);

        const bar = document.querySelector('[data-component="downloads-mobile-selection-bar-history"]') as HTMLElement;
        expect(within(bar).getByText('1 selected')).toBeInTheDocument();
        expect(within(bar).getByRole('button', { name: /forget/i })).toBeEnabled();
        expect(within(bar).getByRole('button', { name: /remove/i })).toBeEnabled();
    });

    it('selects and clears the visible mobile page on each tab', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() =>
            expect(
                within(screen.getByRole('list', { name: 'Download queue' })).getByText('Breaking Bad'),
            ).toBeInTheDocument(),
        );

        await user.click(screen.getByRole('button', { name: /^select$/i }));
        const queueBar = document.querySelector(
            '[data-component="downloads-mobile-selection-bar-queue"]',
        ) as HTMLElement;
        const queueSelectAll = within(queueBar).getByRole('button', { name: /^select all$/i });
        await user.click(queueSelectAll);

        expect(within(queueBar).getByText('6 selected')).toBeInTheDocument();
        await user.click(within(queueBar).getByRole('button', { name: /clear selection/i }));
        expect(within(queueBar).getByText('0 selected')).toBeInTheDocument();
        expect(queueSelectAll).toHaveAttribute('aria-pressed', 'false');

        await user.click(screen.getByRole('tab', { name: /history/i }));
        await waitFor(() =>
            expect(
                within(screen.getByRole('list', { name: 'Download history' })).getByText('The Bear'),
            ).toBeInTheDocument(),
        );

        await user.click(screen.getByRole('button', { name: /^select$/i }));
        const historyBar = document.querySelector(
            '[data-component="downloads-mobile-selection-bar-history"]',
        ) as HTMLElement;
        const historySelectAll = within(historyBar).getByRole('button', { name: /^select all$/i });
        await user.click(historySelectAll);

        expect(within(historyBar).getByText('3 selected')).toBeInTheDocument();
        await user.click(within(historyBar).getByRole('button', { name: /clear selection/i }));
        expect(within(historyBar).getByText('0 selected')).toBeInTheDocument();
        expect(historySelectAll).toHaveAttribute('aria-pressed', 'false');
    });

    it('preserves independent mobile selections when switching tabs', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        const queueCards = await screen.findByRole('list', { name: 'Download queue' });
        await waitFor(() => expect(within(queueCards).getByText('Breaking Bad')).toBeInTheDocument());
        await user.click(screen.getByRole('button', { name: /^select$/i }));
        await user.click(screen.getByRole('button', { name: 'Select Breaking Bad' }));

        await user.click(screen.getByRole('tab', { name: /history/i }));
        const historyCards = await screen.findByRole('list', { name: 'Download history' });
        await waitFor(() => expect(within(historyCards).getByText('The Bear')).toBeInTheDocument());
        await user.click(screen.getByRole('button', { name: /^select$/i }));
        await user.click(screen.getByRole('button', { name: 'Select The Bear' }));

        await user.click(screen.getByRole('tab', { name: /active/i }));
        expect(screen.getByRole('button', { name: /stop selecting/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Select Breaking Bad' })).toHaveAttribute('aria-pressed', 'true');
        expect(
            within(
                document.querySelector('[data-component="downloads-mobile-selection-bar-queue"]') as HTMLElement,
            ).getByText('1 selected'),
        ).toBeInTheDocument();

        await user.click(screen.getByRole('tab', { name: /history/i }));
        expect(screen.getByRole('button', { name: /stop selecting/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Select The Bear' })).toHaveAttribute('aria-pressed', 'true');

        await user.click(screen.getByRole('button', { name: /stop selecting/i }));
        expect(screen.queryByRole('button', { name: 'Select The Bear' })).not.toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: /^select$/i }));
        expect(screen.getByRole('button', { name: 'Select The Bear' })).toHaveAttribute('aria-pressed', 'false');

        await user.click(screen.getByRole('tab', { name: /active/i }));
        expect(screen.getByRole('button', { name: 'Select Breaking Bad' })).toHaveAttribute('aria-pressed', 'true');
        await user.click(screen.getByRole('button', { name: /stop selecting/i }));
        expect(screen.queryByRole('button', { name: 'Select Breaking Bad' })).not.toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: /^select$/i }));
        expect(screen.getByRole('button', { name: 'Select Breaking Bad' })).toHaveAttribute('aria-pressed', 'false');
    });

    it('bulk Forget removes each selected row via DELETE /downloads/{id}', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));
        await user.click(within(getTable()).getByRole('checkbox', { name: /select dune: part two/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: /forget/i }));

        const dialog = await screen.findByRole('dialog');
        await user.click(within(dialog).getByRole('button', { name: /^forget$/i }));

        await waitFor(() => expect(mockApiDelete).toHaveBeenCalledWith('/downloads/dl-1'));
        expect(mockApiDelete).toHaveBeenCalledWith('/downloads/dl-3');
        expect(mockApiDelete).not.toHaveBeenCalledWith('/downloads/dl-1/files');
    });

    it('bulk Forget offers no file deletion and leaves files in the client', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));
        await user.click(
            within(document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement).getByRole(
                'button',
                { name: /forget/i },
            ),
        );

        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).queryByRole('switch', { name: /also delete files from disk/i })).not.toBeInTheDocument();
        expect(within(dialog).getByText(/will remain in the download client/i)).toBeInTheDocument();
        await user.click(within(dialog).getByRole('button', { name: /^forget$/i }));

        await waitFor(() => expect(mockApiDelete).toHaveBeenCalledWith('/downloads/dl-1'));
        expect(mockApiDelete).not.toHaveBeenCalledWith('/downloads/dl-1/files');
    });

    it('keeps only failed bulk Forget rows selected for retry', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        let duneAttempts = 0;
        mockApiDelete.mockImplementation((url: string) => {
            if (url === '/downloads/dl-3' && duneAttempts++ === 0) {
                return Promise.reject(new Error('delete failed'));
            }
            return Promise.resolve(undefined);
        });

        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));
        await user.click(within(getTable()).getByRole('checkbox', { name: /select dune: part two/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: /forget/i }));
        await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: /^forget$/i }));

        await waitFor(() => expect(screen.getByText('Failed to forget some downloads')).toBeInTheDocument());
        expect(screen.getByText('1 succeeded and 1 failed. Failed downloads remain selected.')).toBeInTheDocument();
        expect(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i })).toHaveAttribute(
            'data-state',
            'unchecked',
        );
        expect(within(getTable()).getByRole('checkbox', { name: /select dune: part two/i })).toHaveAttribute(
            'data-state',
            'checked',
        );
        expect(within(bar).getByText('1 selected')).toBeInTheDocument();

        await user.click(within(bar).getByRole('button', { name: /forget/i }));
        await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: /^forget$/i }));

        await waitFor(() => expect(screen.getByText('Download forgotten')).toBeInTheDocument());
        expect(mockApiDelete.mock.calls.filter(([url]) => url === '/downloads/dl-1')).toHaveLength(1);
        expect(mockApiDelete.mock.calls.filter(([url]) => url === '/downloads/dl-3')).toHaveLength(2);
        await waitFor(() => expect(bar).toHaveAttribute('data-visible', 'false'));
    });

    it('bulk Remove removes each selected row via DELETE /downloads/{id}/files', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: /remove/i }));

        const dialog = await screen.findByRole('dialog');
        await user.click(within(dialog).getByRole('button', { name: /^remove$/i }));

        await waitFor(() => expect(mockApiDelete).toHaveBeenCalledWith('/downloads/dl-1/files'));
    });

    it('reports partial bulk Remove results and leaves only failures selected', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockImplementation((url: string) =>
            url === '/downloads/dl-3/files' ? Promise.reject(new Error('delete failed')) : Promise.resolve(undefined),
        );

        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));
        await user.click(within(getTable()).getByRole('checkbox', { name: /select dune: part two/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: /remove/i }));
        await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: /^remove$/i }));

        await waitFor(() => expect(screen.getByText('Failed to remove some downloads')).toBeInTheDocument());
        expect(screen.getByText('1 succeeded and 1 failed. Failed downloads remain selected.')).toBeInTheDocument();
        expect(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i })).toHaveAttribute(
            'data-state',
            'unchecked',
        );
        expect(within(getTable()).getByRole('checkbox', { name: /select dune: part two/i })).toHaveAttribute(
            'data-state',
            'checked',
        );
        expect(within(bar).getByText('1 selected')).toBeInTheDocument();
    });

    it('reports partial bulk state changes and retries only failed rows', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        let officeAttempts = 0;
        mockApiPost.mockImplementation((url: string) => {
            if (url === '/downloads/dl-2/pause' && officeAttempts++ === 0) {
                return Promise.reject(new Error('pause failed'));
            }
            return Promise.resolve(undefined);
        });

        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));
        await user.click(within(getTable()).getByRole('checkbox', { name: /select the\.office/i }));
        await user.click(within(getTable()).getByRole('checkbox', { name: /select dune: part two/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: /^pause$/i }));

        await waitFor(() => expect(screen.getByText('Failed to pause some downloads')).toBeInTheDocument());
        expect(screen.getByText('2 succeeded and 1 failed. Failed downloads remain selected.')).toBeInTheDocument();
        expect(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i })).toHaveAttribute(
            'data-state',
            'unchecked',
        );
        expect(within(getTable()).getByRole('checkbox', { name: /select the\.office/i })).toHaveAttribute(
            'data-state',
            'checked',
        );
        expect(within(getTable()).getByRole('checkbox', { name: /select dune: part two/i })).toHaveAttribute(
            'data-state',
            'unchecked',
        );
        expect(within(bar).getByText('1 selected')).toBeInTheDocument();

        await user.click(within(bar).getByRole('button', { name: /^pause$/i }));

        await waitFor(() => expect(screen.getByText('Downloads paused')).toBeInTheDocument());
        expect(mockApiPost.mock.calls.filter(([url]) => url === '/downloads/dl-1/pause')).toHaveLength(1);
        expect(mockApiPost.mock.calls.filter(([url]) => url === '/downloads/dl-2/pause')).toHaveLength(2);
        expect(mockApiPost.mock.calls.filter(([url]) => url === '/downloads/dl-3/pause')).toHaveLength(1);
        await waitFor(() => expect(bar).toHaveAttribute('data-visible', 'false'));
    });
});

describe('DownloadsPage permission gating', () => {
    function grantRole(role: Role) {
        mockHasPermission.mockImplementation((permission: string) => testHasPermission(role, permission as Permission));
    }

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGet();
    });

    afterEach(() => {
        vi.restoreAllMocks();
        mockHasPermission.mockImplementation(() => true);
    });

    it('lets a Requester pause/resume a row but hides the destructive actions', async () => {
        grantRole(Role.Requester);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        expect(await screen.findByRole('menuitem', { name: /^pause$/i })).toBeInTheDocument();
        expect(screen.queryByRole('menuitem', { name: /^stop$/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('menuitem', { name: /force start/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('menuitem', { name: /^forget$/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('menuitem', { name: /^remove$/i })).not.toBeInTheDocument();
        await user.keyboard('{Escape}');

        await openRowMenu(user, /actions for dune: part two/i);
        expect(await screen.findByRole('menuitem', { name: /^resume$/i })).toBeInTheDocument();
    });

    it('lets a Requester select queue rows and bulk pause/resume but not stop or delete', async () => {
        grantRole(Role.Requester);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        expect(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i })).toBeInTheDocument();
        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        expect(within(bar).getByRole('button', { name: /^start$/i })).toBeInTheDocument();
        expect(within(bar).getByRole('button', { name: /^pause$/i })).toBeInTheDocument();
        expect(within(bar).queryByRole('button', { name: /^stop$/i })).not.toBeInTheDocument();
        expect(within(bar).queryByRole('button', { name: /forget/i })).not.toBeInTheDocument();
        expect(within(bar).queryByRole('button', { name: /remove/i })).not.toBeInTheDocument();
    });

    it('hides the History selection checkbox from a Requester (only manager-tier bulk actions there)', async () => {
        grantRole(Role.Requester);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() =>
            expect(within(getTable()).queryByRole('checkbox', { name: /select all/i })).not.toBeInTheDocument(),
        );
    });

    it('lets a Manager use every row action including stop, forget, and remove', async () => {
        grantRole(Role.Manager);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await openRowMenu(user, /actions for breaking bad/i);
        expect(await screen.findByRole('menuitem', { name: /^pause$/i })).toBeInTheDocument();
        expect(screen.getByRole('menuitem', { name: /^stop$/i })).toBeInTheDocument();
        expect(screen.getByRole('menuitem', { name: /^forget$/i })).toBeInTheDocument();
        expect(screen.getByRole('menuitem', { name: /^remove$/i })).toBeInTheDocument();
    });

    it('shows a Manager the full bulk-action set and the selection checkboxes', async () => {
        grantRole(Role.Manager);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        expect(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i })).toBeInTheDocument();
        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        expect(within(bar).getByRole('button', { name: /^start$/i })).toBeInTheDocument();
        expect(within(bar).getByRole('button', { name: /^pause$/i })).toBeInTheDocument();
        expect(within(bar).getByRole('button', { name: /^stop$/i })).toBeInTheDocument();
        expect(within(bar).getByRole('button', { name: /forget/i })).toBeInTheDocument();
        expect(within(bar).getByRole('button', { name: /remove/i })).toBeInTheDocument();
    });

    it('gives every bulk-action button an aria-label so it keeps an accessible name when its text label is hidden on mobile', async () => {
        grantRole(Role.Manager);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        const start = within(bar).getByRole('button', { name: /^start$/i });
        const pause = within(bar).getByRole('button', { name: /^pause$/i });
        const stop = within(bar).getByRole('button', { name: /^stop$/i });
        const forget = within(bar).getByRole('button', { name: /^forget$/i });
        const remove = within(bar).getByRole('button', { name: /^remove$/i });

        expect(start).toHaveAttribute('aria-label', 'Start');
        expect(pause).toHaveAttribute('aria-label', 'Pause');
        expect(stop).toHaveAttribute('aria-label', 'Stop');
        expect(forget).toHaveAttribute('aria-label', 'Forget');
        expect(remove).toHaveAttribute('aria-label', 'Remove');
    });

    it('renders no selection toolbar on either tab for a Viewer (no download bulk actions)', async () => {
        grantRole(Role.Viewer);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        expect(within(getTable()).queryByRole('checkbox', { name: /select breaking bad/i })).not.toBeInTheDocument();
        expect(document.querySelector('[data-component="downloads-bulk-bar-queue"]')).not.toBeInTheDocument();

        await user.click(screen.getByRole('tab', { name: /history/i }));
        await waitFor(() => expect(within(getTable()).getByText('The Bear')).toBeInTheDocument());
        expect(document.querySelector('[data-component="downloads-bulk-bar-history"]')).not.toBeInTheDocument();
    });

    it('keeps the queue selection toolbar for a Requester but hides it on History', async () => {
        grantRole(Role.Requester);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        expect(document.querySelector('[data-component="downloads-bulk-bar-queue"]')).toBeInTheDocument();

        await user.click(screen.getByRole('tab', { name: /history/i }));
        await waitFor(() => expect(within(getTable()).getByText('The Bear')).toBeInTheDocument());
        expect(document.querySelector('[data-component="downloads-bulk-bar-history"]')).not.toBeInTheDocument();
    });
});

describe('DownloadsPage select all across pages', () => {
    function setupApiGetWithLargeTotal(source: ApiDownload[] = mockApiDownloads) {
        const mockApiGet = vi.mocked(apiGet);
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            if (url.startsWith('/downloads')) {
                if (url.includes('pageSize=25')) {
                    const page = respondDownloads(url, source);
                    return Promise.resolve({ ...page, totalItems: 40 });
                }
                return Promise.resolve(respondDownloads(url, source));
            }
            return Promise.resolve([]);
        });
        return mockApiGet;
    }

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
        setupApiGetWithLargeTotal();
    });

    afterEach(() => vi.restoreAllMocks());

    async function selectWholePage(user: ReturnType<typeof userEvent.setup>, title: string | RegExp = 'Breaking Bad') {
        await waitFor(() => expect(within(getTable()).getByText(title)).toBeInTheDocument());
        await user.click(within(getTable()).getByRole('checkbox', { name: /^select all$/i }));
    }

    it('offers to select all matching rows once the whole page is checked', async () => {
        const user = userEvent.setup();
        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await selectWholePage(user);

        const banner = document.querySelector('[data-component="select-all-banner"]') as HTMLElement;
        expect(banner).toBeInTheDocument();
        expect(within(banner).getByRole('button', { name: /select all 40 across all pages/i })).toBeInTheDocument();
    });

    it.each([
        [
            'Forget',
            '/downloads/bulk/delete',
            'Magpie stopped tracking them. The transfers are left in the download client.',
        ],
        ['Remove', '/downloads/bulk/remove', 'Removed the download and its files from the download client.'],
    ])('bulk %s across pages sends one filtered request', async (action, route, description) => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue({ count: 40, error: null });
        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await selectWholePage(user);

        const banner = document.querySelector('[data-component="select-all-banner"]') as HTMLElement;
        await user.click(within(banner).getByRole('button', { name: /select all 40 across all pages/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: action }));

        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).queryByRole('switch', { name: 'Also delete files from disk' })).not.toBeInTheDocument();
        await user.click(within(dialog).getByRole('button', { name: action }));

        await waitFor(() => expect(mockApiPost).toHaveBeenCalledTimes(1));
        expect(mockApiPost).toHaveBeenCalledWith(route, {
            filter: {
                downloadClientId: undefined,
                statuses: ['Grabbed', 'Queued', 'Downloading', 'Paused', 'Stalled'],
                excludeIds: [],
            },
        });
        expect(apiDelete).not.toHaveBeenCalled();
        expect(await screen.findByText(description)).toBeInTheDocument();
    });

    it.each([
        {
            action: 'Forget',
            route: '/downloads/bulk/delete',
            successTitle: '17 downloads forgotten',
        },
        {
            action: 'Remove',
            route: '/downloads/bulk/remove',
            successTitle: '17 downloads removed',
        },
    ])('bulk $action across History pages uses the filtered result count', async (config) => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue({ count: 17, error: null });
        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await user.click(screen.getByRole('tab', { name: /history/i }));
        await selectWholePage(user, 'The Bear');

        const banner = document.querySelector('[data-component="select-all-banner"]') as HTMLElement;
        await user.click(within(banner).getByRole('button', { name: /select all 40 across all pages/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-history"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: new RegExp(`^${config.action}$`, 'i') }));
        await user.click(
            within(await screen.findByRole('dialog')).getByRole('button', {
                name: new RegExp(`^${config.action}$`, 'i'),
            }),
        );

        await waitFor(() => expect(mockApiPost).toHaveBeenCalledTimes(1));
        expect(mockApiPost).toHaveBeenCalledWith(config.route, {
            filter: {
                downloadClientId: undefined,
                statuses: ['Downloaded', 'Importing', 'Imported', 'Failed', 'Cancelled'],
                excludeIds: [],
            },
        });
        await waitFor(() => expect(screen.getByText(config.successTitle)).toBeInTheDocument());
        expect(apiDelete).not.toHaveBeenCalled();
    });

    it.each([
        { action: 'Forget', emptyTitle: 'No downloads forgotten', successTitle: '40 downloads forgotten' },
        { action: 'Remove', emptyTitle: 'No downloads removed', successTitle: '40 downloads removed' },
    ])('does not report History all-pages $action as successful when no targets resolve', async (config) => {
        const user = userEvent.setup();
        vi.mocked(apiPost).mockResolvedValue({ count: 0, error: null });
        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await user.click(screen.getByRole('tab', { name: /history/i }));
        await selectWholePage(user, 'The Bear');

        const banner = document.querySelector('[data-component="select-all-banner"]') as HTMLElement;
        await user.click(within(banner).getByRole('button', { name: /select all 40 across all pages/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-history"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: new RegExp(`^${config.action}$`, 'i') }));
        await user.click(
            within(await screen.findByRole('dialog')).getByRole('button', {
                name: new RegExp(`^${config.action}$`, 'i'),
            }),
        );

        await waitFor(() => expect(screen.getByText(config.emptyTitle)).toBeInTheDocument());
        expect(screen.queryByText(config.successTitle)).not.toBeInTheDocument();
    });

    it.each([
        { action: 'Start', button: /^start$/i, confirm: false },
        { action: 'Pause', button: /^pause$/i, confirm: false },
        { action: 'Stop', button: /^stop$/i, confirm: false },
        { action: 'Forget', button: /^forget$/i, confirm: true },
        { action: 'Remove', button: /^remove$/i, confirm: true },
    ])('surfaces the backend error for all-pages $action', async (config) => {
        const user = userEvent.setup();
        vi.mocked(apiPost).mockResolvedValue({ count: 0, error: `${config.action} bulk failure` });
        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await selectWholePage(user);

        const banner = document.querySelector('[data-component="select-all-banner"]') as HTMLElement;
        await user.click(within(banner).getByRole('button', { name: /select all 40 across all pages/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: config.button }));
        if (config.confirm) {
            await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: config.button }));
        }

        await waitFor(() => expect(screen.getByText(`${config.action} bulk failure`)).toBeInTheDocument());
    });

    it('keeps all-pages Start and Pause available when the current page does not support them', async () => {
        const user = userEvent.setup();
        setupApiGetWithLargeTotal([
            {
                id: 'unsupported-downloading',
                downloadClientId: 'client-basic',
                downloadClientName: 'Basic Client',
                externalId: 'basic-downloading',
                title: 'Unsupported Downloading',
                status: 'Downloading',
                canPause: false,
                canResume: false,
                createdAt: '2024-01-15T10:00:00Z',
            },
            {
                id: 'unsupported-paused',
                downloadClientId: 'client-basic',
                downloadClientName: 'Basic Client',
                externalId: 'basic-paused',
                title: 'Unsupported Paused',
                status: 'Paused',
                canPause: false,
                canResume: false,
                createdAt: '2024-01-15T10:01:00Z',
            },
        ]);
        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await selectWholePage(user, 'Unsupported Downloading');

        const banner = document.querySelector('[data-component="select-all-banner"]') as HTMLElement;
        await user.click(within(banner).getByRole('button', { name: /select all 40 across all pages/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        expect(within(bar).getByRole('button', { name: /^start$/i })).toBeEnabled();
        expect(within(bar).getByRole('button', { name: /^pause$/i })).toBeEnabled();
    });

    it.each([
        { action: 'Start', button: /^start$/i, emptyTitle: 'No downloads started', successTitle: 'Downloads resumed' },
        { action: 'Pause', button: /^pause$/i, emptyTitle: 'No downloads paused', successTitle: 'Downloads paused' },
    ])('does not report all-pages $action as successful when the backend affects no rows', async (config) => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue({ count: 0, error: null });
        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await selectWholePage(user);

        const banner = document.querySelector('[data-component="select-all-banner"]') as HTMLElement;
        await user.click(within(banner).getByRole('button', { name: /select all 40 across all pages/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: config.button }));

        await waitFor(() => expect(mockApiPost).toHaveBeenCalledTimes(1));
        await waitFor(() => expect(screen.getByText(config.emptyTitle)).toBeInTheDocument());
        expect(screen.queryByText(config.successTitle)).not.toBeInTheDocument();
    });

    it('excludes a hand-deselected row from the across-pages request', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue({ count: 39, error: null });
        render(<DownloadsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });

        await selectWholePage(user);

        const banner = document.querySelector('[data-component="select-all-banner"]') as HTMLElement;
        await user.click(within(banner).getByRole('button', { name: /select all 40 across all pages/i }));

        await user.click(within(getTable()).getByRole('checkbox', { name: /select breaking bad/i }));

        const bar = document.querySelector('[data-component="downloads-bulk-bar-queue"]') as HTMLElement;
        await user.click(within(bar).getByRole('button', { name: /^stop$/i }));

        await waitFor(() => expect(mockApiPost).toHaveBeenCalledTimes(1));
        expect(mockApiPost).toHaveBeenCalledWith('/downloads/bulk/stop', {
            filter: {
                downloadClientId: undefined,
                statuses: ['Grabbed', 'Queued', 'Downloading', 'Paused', 'Stalled'],
                excludeIds: ['dl-1'],
            },
        });
    });
});

describe('DownloadsPage empty state', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultExtensionContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
    });

    afterEach(() => vi.restoreAllMocks());

    it('shows a loading skeleton (not the empty state) while the first load is in flight', async () => {
        const resolvers: ((value: PagedDownloads) => void)[] = [];
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                return new Promise<PagedDownloads>((resolve) => {
                    resolvers.push(resolve);
                });
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(await screen.findAllByTestId('downloads-page__loading')).not.toHaveLength(0);
        expect(screen.queryByText('No active downloads')).not.toBeInTheDocument();

        resolvers.forEach((resolve) => resolve({ items: [], pageNumber: 1, pageSize: 25, totalItems: 0 }));

        await waitFor(() => {
            expect(within(getTable()).getByText('No active downloads')).toBeInTheDocument();
        });
    });

    it('shows the active empty state when there are no downloads', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
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
            expect(within(getTable()).getByText('No active downloads')).toBeInTheDocument();
        });
    });

    it('hides the column headers on the cold-start empty (no filters active)', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
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
            expect(within(getTable()).getByText('No active downloads')).toBeInTheDocument();
        });
        expect(within(getTable()).queryByRole('button', { name: 'Filter by Status' })).not.toBeInTheDocument();
    });

    it('keeps the Status funnel live above the empty message when a status filter matches nothing', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url.startsWith('/downloads')) {
                if (url.includes('status=Paused')) {
                    return Promise.resolve({ items: [], pageNumber: 1, pageSize: 25, totalItems: 0 });
                }
                return Promise.resolve(respondDownloads(url));
            }
            if (url === '/download-clients') {
                return Promise.resolve(mockApiClients);
            }
            return Promise.resolve([]);
        });

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());

        await user.click(within(getTable()).getByRole('button', { name: 'Filter by Status' }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Paused' }));

        await waitFor(() => {
            expect(within(getTable()).getByText('No active downloads')).toBeInTheDocument();
        });
        expect(within(getTable()).getByRole('button', { name: /Status filtered, edit filter/i })).toBeInTheDocument();
    });

    it('does not render the bulk toolbar when there are no active downloads', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
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
            expect(within(getTable()).getByText('No active downloads')).toBeInTheDocument();
        });
        expect(document.querySelector('[data-component="downloads-bulk-bar-queue"]')).not.toBeInTheDocument();
    });

    it('does not render the bulk toolbar when there is no download history', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url: string) => {
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

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => {
            expect(within(getTable()).getByText('No download history')).toBeInTheDocument();
        });
        expect(document.querySelector('[data-component="downloads-bulk-bar-history"]')).not.toBeInTheDocument();
    });

    it('renders the bulk toolbar for each tab once rows are present', async () => {
        const user = userEvent.setup();
        setupApiGet();

        const queryClient = createTestQueryClient();
        render(<DownloadsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(within(getTable()).getByText('Breaking Bad')).toBeInTheDocument());
        expect(document.querySelector('[data-component="downloads-bulk-bar-queue"]')).toBeInTheDocument();

        await user.click(screen.getByRole('tab', { name: /history/i }));

        await waitFor(() => expect(within(getTable()).getByText('The Bear')).toBeInTheDocument());
        expect(document.querySelector('[data-component="downloads-bulk-bar-history"]')).toBeInTheDocument();
    });
});
