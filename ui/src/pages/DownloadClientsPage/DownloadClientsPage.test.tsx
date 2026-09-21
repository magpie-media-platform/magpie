import { act, fireEvent, render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
    isAbortError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && (value as { name?: unknown }).name === 'AbortError';
    }),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import type { ApiDownloadClient } from '@/hooks/useDownloadClients';
import { DownloadClientsPage } from './DownloadClientsPage';

const mockApiClients: ApiDownloadClient[] = [
    {
        id: 'client-1',
        name: 'SABnzbd',
        providerType: 'sabnzbd',
        enabled: true,
        status: 'Healthy',
        lastChecked: '2024-01-15T10:30:00Z',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        settings: { host: 'http://localhost:8080', apiKey: '********' },
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'client-2',
        name: 'NZBGet',
        providerType: 'nzbget',
        enabled: true,
        status: 'Healthy',
        lastChecked: '2024-01-15T10:28:00Z',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'client-3',
        name: 'DownloadClient A',
        providerType: 'downloadclient-a',
        enabled: true,
        status: 'Unhealthy',
        lastChecked: '2024-01-15T10:25:00Z',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'client-4',
        name: 'Transmission',
        providerType: 'transmission',
        enabled: true,
        status: 'Healthy',
        lastChecked: '2024-01-15T10:32:00Z',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        createdAt: '2024-01-03T00:00:00Z',
    },
    {
        id: 'client-5',
        name: 'Deluge (Seedbox)',
        providerType: 'deluge',
        enabled: false,
        status: 'Unknown',
        lastChecked: null,
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        createdAt: '2024-01-04T00:00:00Z',
    },
    {
        id: 'client-6',
        name: 'rTorrent',
        providerType: 'rtorrent',
        enabled: true,
        status: 'Stale',
        lastChecked: '2024-01-15T10:20:00Z',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        createdAt: '2024-01-05T00:00:00Z',
    },
    {
        id: 'client-7',
        name: 'aria2',
        providerType: 'aria2',
        enabled: true,
        status: 'Unknown',
        lastChecked: '2024-01-15T10:15:00Z',
        cleanupDeleteFiles: true,
        keepFailedOnClient: false,
        createdAt: '2024-01-06T00:00:00Z',
    },
];

function mockClientList() {
    vi.mocked(apiGet).mockResolvedValue(mockApiClients as Awaited<ReturnType<typeof apiGet>>);
}

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
}

function createTestWrapper(queryClient: QueryClient, initialEntries: string[] = ['/download-clients']) {
    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter initialEntries={initialEntries}>
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

function dataRows() {
    const table = screen.getByRole('grid');
    return within(table)
        .getAllByRole('row')
        .filter((r) => within(r).queryAllByRole('gridcell').length > 0);
}

describe('DownloadClientsPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockClientList();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with correct data-page attribute', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(document.querySelector('[data-page="download-clients"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(screen.getByRole('heading', { name: 'Download Clients', level: 1 })).toBeInTheDocument();
    });

    it('renders the page subtitle as a capitalised, full-stopped sentence', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(screen.getByTestId('page-header__description')).toHaveTextContent(
            'Download clients Magpie uses to send releases.',
        );
    });

    it('renders the page icon as decorative', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        const icon = screen.getByTestId('download-clients-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });

    it('renders an Add Client button', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        const addButtons = screen.getAllByRole('button', { name: /add client/i });
        expect(addButtons.length).toBeGreaterThanOrEqual(1);
    });

    it('does not render health-summary stat pills', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });

        expect(document.querySelector('[aria-label="Health summary"]')).not.toBeInTheDocument();
    });

    it('no longer renders a global download-behaviors section (cleanup is now per-client)', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });

        expect(document.querySelector('[data-area="download-behaviors"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-component="download-cleanup-settings"]')).not.toBeInTheDocument();
    });
});

describe('DownloadClientsPage table', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockClientList();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders one row per client in a table', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });
    });

    it('renders client names and provider types', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('SABnzbd')).toBeInTheDocument();
        });
        expect(screen.getByText('DownloadClient A')).toBeInTheDocument();
        expect(screen.getByText('sabnzbd')).toBeInTheDocument();
        expect(screen.getByText('downloadclient-a')).toBeInTheDocument();
    });

    it('keeps a long unbroken client name contained with the full name available', async () => {
        const longName = `DownloadClient${'UnbrokenNameSegment'.repeat(8)}`;
        vi.mocked(apiGet).mockResolvedValue([
            {
                ...mockApiClients[0],
                id: 'client-long',
                name: longName,
            },
        ] as Awaited<ReturnType<typeof apiGet>>);

        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByTitle(longName)).toHaveClass('download-clients-table__name');
        });
        expect(screen.getByTitle(longName).closest('[data-column="name"]')).toBeInTheDocument();
    });

    it('renders a sortable Name column header', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });

        expect(screen.getByRole('button', { name: /sort by name/i })).toBeInTheDocument();
    });
});

describe('DownloadClientsPage row actions', () => {
    const mockApiDelete = vi.mocked(apiDelete);
    const mockApiPatch = vi.mocked(apiPatch);
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockClientList();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('exposes test, enable toggle, edit and delete per row', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-action="test"]').length).toBe(7);
        });
        expect(document.querySelectorAll('[data-action="toggle-enabled"]').length).toBe(7);
        expect(document.querySelectorAll('[data-action="edit"]').length).toBe(7);
        expect(document.querySelectorAll('[data-action="delete"]').length).toBe(7);
    });

    it('tests the targeted client by id', async () => {
        mockApiPost.mockResolvedValue({ success: true, message: 'Connected.' });
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^test sabnzbd$/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^test sabnzbd$/i }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/download-clients/client-1/test');
        });
    });

    it('opens edit form when edit button is clicked', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^edit sabnzbd$/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^edit sabnzbd$/i }));

        await waitFor(() => {
            expect(screen.getByText('Edit Download Client')).toBeInTheDocument();
            expect(screen.getAllByDisplayValue('SABnzbd').length).toBeGreaterThan(0);
        });
    });

    it('cancels a pending test when the client form closes after save', async () => {
        // Arrange
        const user = userEvent.setup();
        let resolveTest!: (result: { success: boolean; message: string }) => void;
        const pending = new Promise<{ success: boolean; message: string }>((resolve) => {
            resolveTest = resolve;
        });
        vi.mocked(apiPost).mockReturnValue(pending);
        vi.mocked(apiPatch).mockResolvedValue(undefined);
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(createTestQueryClient()) });
        await user.click(await screen.findByRole('button', { name: /^edit sabnzbd$/i }));
        const dialog = await screen.findByRole('dialog');
        await user.click(within(dialog).getByRole('button', { name: 'Test' }));
        await waitFor(() =>
            expect(apiPost).toHaveBeenCalledWith('/download-clients/test', expect.anything(), expect.anything()),
        );
        const signal = vi.mocked(apiPost).mock.calls.at(-1)?.[2]?.signal;
        // Act
        fireEvent.submit(dialog.querySelector('form')!);
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        await act(async () => {
            resolveTest({ success: true, message: 'Stale test result' });
            await pending;
        });
        // Assert
        expect(signal?.aborted).toBe(true);
        expect(screen.getByText('Download client updated')).toBeInTheDocument();
        expect(screen.queryByText('Stale test result')).not.toBeInTheDocument();
    });

    it('keeps saved settings in the update bucket when an edited client is saved without changes', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^edit sabnzbd$/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^edit sabnzbd$/i }));

        await waitFor(() => {
            expect(screen.getByText('Edit Download Client')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/download-clients/client-1', {
                name: { value: 'SABnzbd' },
                enabled: { value: true },
                cleanupDeleteFiles: { value: true },
                keepFailedOnClient: { value: false },
                settings: {
                    update: {
                        host: 'http://localhost:8080',
                        apiKey: '********',
                    },
                },
            });
        });
    });

    it('opens confirmation dialog when delete is clicked, then deletes after confirming', async () => {
        mockApiDelete.mockResolvedValue(undefined);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete sabnzbd$/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^delete sabnzbd$/i }));

        await waitFor(() => {
            expect(screen.getByText('Delete Download Client')).toBeInTheDocument();
            expect(screen.getByText(/Are you sure you want to delete "SABnzbd"\?/)).toBeInTheDocument();
        });
        expect(mockApiDelete).not.toHaveBeenCalled();

        await user.click(screen.getByRole('button', { name: /^delete$/i }));

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/download-clients/client-1');
        });
    });

    it('patches disabled state when toggle is clicked on enabled client', async () => {
        mockApiPatch.mockResolvedValue(undefined);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /disable sabnzbd/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('switch', { name: /disable sabnzbd/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/download-clients/client-1', {
                enabled: { value: false },
            });
        });
    });

    it('patches enabled state when toggle is clicked on disabled client', async () => {
        mockApiPatch.mockResolvedValue(undefined);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /enable deluge \(seedbox\)/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('switch', { name: /enable deluge \(seedbox\)/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/download-clients/client-5', {
                enabled: { value: true },
            });
        });
    });

    it('keeps the row enabled and surfaces the server error detail when the toggle patch fails', async () => {
        mockApiPatch.mockRejectedValue({ status: 409, message: 'Client is in use by an active download.' });
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /disable sabnzbd/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('switch', { name: /disable sabnzbd/i }));

        await waitFor(() => {
            expect(screen.getByText('Client is in use by an active download.')).toBeInTheDocument();
        });
        expect(screen.getByRole('switch', { name: /disable sabnzbd/i })).toBeChecked();
    });
});

describe('DownloadClientsPage column-header filters', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockClientList();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('exposes provider, status and enabled column-header filters', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });

        expect(screen.getByRole('button', { name: /filter by provider/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by status/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by enabled/i })).toBeInTheDocument();
    });

    it('renders the full "Enabled" column header label', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });

        const enabledHeader = screen
            .getAllByRole('columnheader')
            .find((cell) => cell.getAttribute('data-column') === 'enabled');
        expect(enabledHeader).toBeDefined();
        expect(enabledHeader).toHaveTextContent('Enabled');
    });

    it('filters rows by status', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });

        await user.click(screen.getByRole('button', { name: /filter by status/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Unhealthy' }));

        await waitFor(() => {
            expect(dataRows().length).toBe(1);
        });
        expect(screen.getByText('DownloadClient A')).toBeInTheDocument();
    });

    it('filters rows by enabled state', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });

        await user.click(screen.getByRole('button', { name: /filter by enabled/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Disabled' }));

        await waitFor(() => {
            expect(dataRows().length).toBe(1);
        });
        expect(screen.getByText('Deluge (Seedbox)')).toBeInTheDocument();
    });

    it('shows a clear-filters control once a filter is active and clears it', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /filter by status/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Healthy' }));

        const clear = document.querySelector('[data-action="clear-filters"]') as HTMLElement;
        expect(clear).toBeInTheDocument();

        await user.click(clear);

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });
    });

    it('shows an empty state with a clear-filters action when filters match nothing', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(dataRows().length).toBe(7);
        });

        await user.click(screen.getByRole('button', { name: /filter by enabled/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Disabled' }));
        await user.click(screen.getByRole('button', { name: /filter by status/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Healthy' }));

        await waitFor(() => {
            expect(screen.getByText('No download clients match the active filters')).toBeInTheDocument();
        });
    });
});

describe('DownloadClientsPage URL state', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockClientList();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('initializes the status filter from a legacy lowercase URL param', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, {
            wrapper: createTestWrapper(queryClient, ['/download-clients?status=healthy']),
        });

        await waitFor(() => {
            expect(dataRows().length).toBe(3);
        });
    });
});

describe('DownloadClientsPage empty state', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockResolvedValue([] as Awaited<ReturnType<typeof apiGet>>);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows a single add affordance with no duplicate in the empty state', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'No download clients configured' })).toBeInTheDocument();
        });
        expect(screen.getByText('Add a download client to start downloading content')).toBeInTheDocument();

        expect(document.querySelector('[data-action="add-client"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="add-client-empty"]')).not.toBeInTheDocument();
    });
});

describe('DownloadClientsPage (mobile card collapse)', () => {
    const mockApiDelete = vi.mocked(apiDelete);
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
        mockClientList();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('marks the name cell as header role and the select column as hidden (no layout-participating checkbox)', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(dataRows().length).toBe(7));
        expect(document.querySelector('td[data-column="name"]')).toHaveAttribute('data-mobile-role', 'header');
        expect(document.querySelector('td[data-column="select"]')).toHaveAttribute('data-mobile-role', 'hidden');
    });

    it('bulk-deletes every selected client', async () => {
        mockApiDelete.mockResolvedValue(undefined);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(dataRows().length).toBe(7));

        await user.click(screen.getByLabelText('Select SABnzbd'));
        await user.click(screen.getByLabelText('Select NZBGet'));
        await user.click(screen.getByRole('button', { name: 'Delete selected download clients' }));
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLElement);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/download-clients/client-1');
            expect(mockApiDelete).toHaveBeenCalledWith('/download-clients/client-2');
        });
    });

    it('bulk-enables every selected client', async () => {
        mockApiPatch.mockResolvedValue(undefined);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(dataRows().length).toBe(7));

        await user.click(screen.getByLabelText('Select Deluge (Seedbox)'));
        await user.click(screen.getByRole('button', { name: 'Enable selected download clients' }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/download-clients/client-5', {
                enabled: { value: true },
            });
        });
    });

    it('reports a partial bulk failure once and leaves the failed clients selected', async () => {
        mockApiDelete.mockImplementation((path: string) =>
            path === '/download-clients/client-2' ? Promise.reject(new Error('busy')) : Promise.resolve(undefined),
        );
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(dataRows().length).toBe(7));

        await user.click(screen.getByLabelText('Select SABnzbd'));
        await user.click(screen.getByLabelText('Select NZBGet'));
        await user.click(screen.getByRole('button', { name: 'Delete selected download clients' }));
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLElement);

        expect(
            await screen.findByText('1 succeeded and 1 failed. Failed download clients remain selected.'),
        ).toBeInTheDocument();
        expect(screen.getAllByRole('alert')).toHaveLength(1);
        expect(screen.getByLabelText('Select NZBGet')).toBeChecked();
        expect(screen.getByLabelText('Select SABnzbd')).not.toBeChecked();
    });
});

describe('DownloadClientsPage selection vs filters', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockClientList();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('clears the selection when a column filter changes the row set', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(dataRows().length).toBe(7));

        await user.click(screen.getByLabelText('Select Deluge (Seedbox)'));
        expect(screen.getByText('1 selected')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /filter by enabled/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Enabled' }));

        await waitFor(() => {
            expect(dataRows().length).toBe(6);
        });
        expect(screen.queryByText('1 selected')).not.toBeInTheDocument();
    });
});

describe('DownloadClientsPage select-all scope', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('takes only the current page and offers the across-pages escalation when more rows match', async () => {
        vi.mocked(apiGet).mockResolvedValue(
            Array.from({ length: 30 }, (_, index) => ({
                ...mockApiClients[0],
                id: `client-${index + 1}`,
                name: `Client ${String(index + 1).padStart(2, '0')}`,
            })) as Awaited<ReturnType<typeof apiGet>>,
        );
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(dataRows().length).toBe(25));

        await user.click(screen.getByLabelText('Select all download clients'));

        expect(screen.getByText('25 selected')).toBeInTheDocument();
        expect(screen.getByText('All 25 on this page are selected.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Select all 30 across all pages' })).toBeInTheDocument();
    });

    it('reaches the off-page rows only through the banner', async () => {
        vi.mocked(apiGet).mockResolvedValue(
            Array.from({ length: 30 }, (_, index) => ({
                ...mockApiClients[0],
                id: `client-${index + 1}`,
                name: `Client ${String(index + 1).padStart(2, '0')}`,
            })) as Awaited<ReturnType<typeof apiGet>>,
        );
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(dataRows().length).toBe(25));

        await user.click(screen.getByLabelText('Select all download clients'));
        await user.click(screen.getByRole('button', { name: 'Select all 30 across all pages' }));

        expect(screen.getByText('30 selected')).toBeInTheDocument();
        expect(screen.getByText('All 30 matching download clients across all pages are selected.')).toBeInTheDocument();
    });

    it('offers no escalation when one page already holds every client', async () => {
        mockClientList();
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => expect(dataRows().length).toBe(7));

        await user.click(screen.getByLabelText('Select all download clients'));

        expect(screen.getByText('7 selected')).toBeInTheDocument();
        expect(screen.queryByText(/across all pages/)).not.toBeInTheDocument();
    });
});
