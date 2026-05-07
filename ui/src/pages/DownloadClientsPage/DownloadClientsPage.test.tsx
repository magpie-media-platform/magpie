import { render, screen, within, waitFor } from '@testing-library/react';
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
}));

import { apiGet, apiPost, apiDelete } from '@/lib/api';
import type { ApiDownloadClient } from '@/hooks/useDownloadClients';
import { DownloadClientsPage } from './DownloadClientsPage';

const mockApiClients: ApiDownloadClient[] = [
    {
        id: 'client-1',
        name: 'SABnzbd',
        providerType: 'sabnzbd',
        enabled: true,
        status: 1,
        lastChecked: '2024-01-15T10:30:00Z',
        settings: { host: 'localhost', port: '8080', apiKey: 'abc123' },
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'client-2',
        name: 'NZBGet',
        providerType: 'nzbget',
        enabled: true,
        status: 1,
        lastChecked: '2024-01-15T10:28:00Z',
        settings: { host: 'localhost', port: '6789' },
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'client-3',
        name: 'qBittorrent',
        providerType: 'qbittorrent',
        enabled: true,
        status: 2,
        lastChecked: '2024-01-15T10:25:00Z',
        settings: { host: 'localhost', port: '8080' },
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'client-4',
        name: 'Transmission',
        providerType: 'transmission',
        enabled: true,
        status: 1,
        lastChecked: '2024-01-15T10:32:00Z',
        settings: { host: '192.168.1.100', port: '9091' },
        createdAt: '2024-01-03T00:00:00Z',
    },
    {
        id: 'client-5',
        name: 'Deluge (Seedbox)',
        providerType: 'deluge',
        enabled: false,
        status: 0,
        lastChecked: null,
        settings: { host: 'seedbox.example.com', port: '8112' },
        createdAt: '2024-01-04T00:00:00Z',
    },
    {
        id: 'client-6',
        name: 'rTorrent',
        providerType: 'rtorrent',
        enabled: true,
        status: 3,
        lastChecked: '2024-01-15T10:20:00Z',
        settings: { host: 'localhost', port: '8080' },
        createdAt: '2024-01-05T00:00:00Z',
    },
    {
        id: 'client-7',
        name: 'aria2',
        providerType: 'aria2',
        enabled: true,
        status: 0,
        lastChecked: '2024-01-15T10:15:00Z',
        settings: { host: 'localhost', port: '6800' },
        createdAt: '2024-01-06T00:00:00Z',
    },
];

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

describe('DownloadClientsPage', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with correct data-page attribute', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(document.querySelector('[data-page="download-clients"]')).toBeInTheDocument();
    });

    it('renders the page title', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(screen.getByRole('heading', { name: 'Download Clients', level: 1 })).toBeInTheDocument();
    });

    it('renders the client count after loading', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const count = screen.getByTestId('download-clients-page__count');
            expect(count).toHaveTextContent('7');
        });
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
});

describe('DownloadClientsPage summary badges', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders summary badges for each health status', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getAllByText('Healthy').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Unhealthy').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Stale').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Unknown').length).toBeGreaterThanOrEqual(1);
        });
    });

    it('applies data-status attributes to summary badges', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Health summary"]');
            const badges = summary?.querySelectorAll('[data-component="badge"]') ?? [];
            const statuses = Array.from(badges).map((b) => b.getAttribute('data-status'));

            expect(statuses).toContain('healthy');
            expect(statuses).toContain('unhealthy');
            expect(statuses).toContain('stale');
            expect(statuses).toContain('unknown');
        });
    });

    it('renders badge dots as decorative', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Health summary"]');
            const dots = summary?.querySelectorAll('[data-component="badge"] [aria-hidden="true"]') ?? [];
            expect(dots.length).toBeGreaterThan(0);
        });

        const summary = document.querySelector('[aria-label="Health summary"]');
        const dots = summary?.querySelectorAll('[data-component="badge"] [aria-hidden="true"]') ?? [];
        dots.forEach((dot) => {
            expect(dot).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('shows correct count for healthy clients', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const summary = document.querySelector('[aria-label="Health summary"]');
            const healthyBadge = summary?.querySelector('[data-component="badge"][data-status="healthy"]');
            const count = healthyBadge?.querySelector('[data-component="badge"] .badge__count');

            expect(count).toHaveTextContent('3');
        });
    });
});

describe('DownloadClientsPage list items', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders client items in the list', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /download clients/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(7);
        });
    });

    it('renders client names', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('SABnzbd')).toBeInTheDocument();
            expect(screen.getByText('NZBGet')).toBeInTheDocument();
            expect(screen.getByText('qBittorrent')).toBeInTheDocument();
            expect(screen.getByText('Transmission')).toBeInTheDocument();
        });
    });

    it('renders provider names', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('sabnzbd')).toBeInTheDocument();
            expect(screen.getByText('nzbget')).toBeInTheDocument();
            expect(screen.getByText('qbittorrent')).toBeInTheDocument();
            expect(screen.getByText('transmission')).toBeInTheDocument();
        });
    });

    it('renders last checked time with label', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByText('Checked Never')).toBeInTheDocument();
        });
    });

    it('applies data-client-id to items', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const items = document.querySelectorAll('[data-client-id]');
            expect(items.length).toBe(7);
            expect(items[0]).toHaveAttribute('data-client-id', 'client-1');
        });
    });

    it('applies data-status to items', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const healthyItems = document.querySelectorAll(
                '[data-testid="download-clients-page__item"][data-status="healthy"]',
            );

            expect(healthyItems.length).toBe(3);
        });
    });

    it('applies data-enabled attribute to items', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const enabledItems = document.querySelectorAll(
                '[data-testid="download-clients-page__item"][data-enabled="true"]',
            );
            const disabledItems = document.querySelectorAll(
                '[data-testid="download-clients-page__item"][data-enabled="false"]',
            );
            expect(enabledItems.length).toBe(6);
            expect(disabledItems.length).toBe(1);
        });
    });

    it('applies disabled styling to disabled clients', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const disabledItems = document.querySelectorAll(
                '[data-testid="download-clients-page__item"][data-enabled="false"]',
            );
            expect(disabledItems.length).toBe(1);
        });
    });
});

describe('DownloadClientsPage health badges', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders health badges with dot indicators', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const items = screen.getAllByTestId('download-clients-page__item');
            expect(items.length).toBe(7);
        });

        const items = screen.getAllByTestId('download-clients-page__item');
        items.forEach((item) => {
            expect(item.querySelector('[data-component="badge"]')).toBeInTheDocument();
            expect(item.querySelector('[data-component="badge"] [aria-hidden="true"]')).toBeInTheDocument();
        });
    });

    it('applies correct variant to healthy clients', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const sabnzbdItem = document.querySelector('[data-client-id="client-1"]');
            expect(sabnzbdItem?.querySelector('[data-component="badge"][data-variant="healthy"]')).toBeInTheDocument();
        });
    });

    it('applies correct variant to unhealthy clients', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const unhealthyItem = document.querySelector('[data-client-id="client-3"]');
            expect(
                unhealthyItem?.querySelector('[data-component="badge"][data-variant="unhealthy"]'),
            ).toBeInTheDocument();
        });
    });

    it('applies correct variant to stale clients', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const staleItem = document.querySelector('[data-client-id="client-6"]');
            expect(staleItem?.querySelector('[data-component="badge"][data-variant="stale"]')).toBeInTheDocument();
        });
    });

    it('applies correct variant to unknown clients', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const unknownItem = document.querySelector('[data-client-id="client-7"]');
            expect(unknownItem?.querySelector('[data-component="badge"][data-variant="unknown"]')).toBeInTheDocument();
        });
    });
});

describe('DownloadClientsPage actions', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders enable/disable toggle for each client', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const toggles = document.querySelectorAll('[data-action="toggle-enabled"]');
            expect(toggles.length).toBe(7);
        });
    });

    it('toggle shows checked state for enabled clients', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const sabnzbdItem = document.querySelector('[data-client-id="client-1"]');
            const toggle = sabnzbdItem?.querySelector('[data-action="toggle-enabled"]');
            expect(toggle).toHaveAttribute('aria-checked', 'true');
        });
    });

    it('toggle shows unchecked state for disabled clients', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const delugeItem = document.querySelector('[data-client-id="client-5"]');
            const toggle = delugeItem?.querySelector('[data-action="toggle-enabled"]');
            expect(toggle).toHaveAttribute('aria-checked', 'false');
        });
    });

    it('renders edit button for each client', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const editButtons = document.querySelectorAll('[data-action="edit"]');
            expect(editButtons.length).toBe(7);
        });
    });

    it('renders delete button for each client', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const deleteButtons = document.querySelectorAll('[data-action="delete"]');
            expect(deleteButtons.length).toBe(7);
        });
    });

    it('edit button has correct aria-label', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const editButton = screen.getByRole('button', { name: /^edit sabnzbd$/i });
            expect(editButton).toHaveAttribute('data-action', 'edit');
        });
    });

    it('delete button has correct aria-label', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const deleteButton = screen.getByRole('button', { name: /^delete sabnzbd$/i });
            expect(deleteButton).toHaveAttribute('data-action', 'delete');
        });
    });

    it('opens edit form when edit button is clicked', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^edit sabnzbd$/i })).toBeInTheDocument();
        });

        const editButton = screen.getByRole('button', { name: /^edit sabnzbd$/i });
        await user.click(editButton);

        await waitFor(() => {
            expect(screen.getByText('Edit Download Client')).toBeInTheDocument();

            expect(screen.getAllByDisplayValue('SABnzbd').length).toBeGreaterThan(0);
        });
    });

    it('opens confirmation dialog when delete button is clicked', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete sabnzbd$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete sabnzbd$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Download Client')).toBeInTheDocument();
            expect(screen.getByText(/Are you sure you want to delete "SABnzbd"\?/)).toBeInTheDocument();
        });

        expect(mockApiDelete).not.toHaveBeenCalled();
    });

    it('calls delete API after confirming in the dialog', async () => {
        mockApiDelete.mockResolvedValue(undefined);
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete sabnzbd$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete sabnzbd$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Download Client')).toBeInTheDocument();
        });

        const confirmButton = screen.getByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/download-clients/client-1');
        });
    });

    it('does not call delete API when cancel is clicked in the dialog', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete sabnzbd$/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /^delete sabnzbd$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Download Client')).toBeInTheDocument();
        });

        const cancelButton = screen.getByRole('button', { name: /^cancel$/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByText('Delete Download Client')).not.toBeInTheDocument();
        });

        expect(mockApiDelete).not.toHaveBeenCalled();
    });
});

describe('DownloadClientsPage filters', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders status and enabled filter dropdowns', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        expect(document.querySelector('[data-filter="status"]')).toBeInTheDocument();
        expect(document.querySelector('[data-filter="enabled"]')).toBeInTheDocument();
    });

    it('status filter displays "Status" by default', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        const statusTrigger = document.querySelector('[data-filter="status"]');
        expect(statusTrigger).toHaveTextContent('Status');
    });

    it('enabled filter displays "Enabled" by default', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        const enabledTrigger = document.querySelector('[data-filter="enabled"]');
        expect(enabledTrigger).toHaveTextContent('Enabled');
    });

    it('filters items by status when selection changes', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-client-id]').length).toBe(7);
        });

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const unhealthyOption = await screen.findByRole('option', { name: 'Unhealthy' });
        await user.click(unhealthyOption);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /download clients/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
        expect(screen.getByText('qBittorrent')).toBeInTheDocument();
    });

    it('filters items by enabled state when selection changes', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-client-id]').length).toBe(7);
        });

        const enabledTrigger = document.querySelector('[data-filter="enabled"]') as HTMLElement;
        await user.click(enabledTrigger);

        const disabledOption = await screen.findByRole('option', { name: 'Disabled' });
        await user.click(disabledOption);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /download clients/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
        expect(screen.getByText('Deluge (Seedbox)')).toBeInTheDocument();
    });

    it('shows clear button when filters are active', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-client-id]').length).toBe(7);
        });

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const healthyOption = await screen.findByRole('option', { name: 'Healthy' });
        await user.click(healthyOption);

        expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
    });

    it('clears filters when clear button is clicked', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-client-id]').length).toBe(7);
        });

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const healthyOption = await screen.findByRole('option', { name: 'Healthy' });
        await user.click(healthyOption);

        const clearButton = document.querySelector('[data-action="clear-filters"]') as HTMLElement;
        await user.click(clearButton);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /download clients/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(7);
        });
    });

    it('shows empty state when filters match nothing', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-client-id]').length).toBe(7);
        });

        const enabledTrigger = document.querySelector('[data-filter="enabled"]') as HTMLElement;
        await user.click(enabledTrigger);

        const disabledOption = await screen.findByRole('option', { name: 'Disabled' });
        await user.click(disabledOption);

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const healthyOption = await screen.findByRole('option', { name: 'Healthy' });
        await user.click(healthyOption);

        await waitFor(() => {
            expect(screen.getByText('No download clients found')).toBeInTheDocument();
        });
        expect(screen.getByText('Try adjusting your filters')).toBeInTheDocument();
    });
});

describe('DownloadClientsPage empty state', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shows empty state icon as decorative when no results', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-client-id]').length).toBe(7);
        });

        const enabledTrigger = document.querySelector('[data-filter="enabled"]') as HTMLElement;
        await user.click(enabledTrigger);

        const disabledOption = await screen.findByRole('option', { name: 'Disabled' });
        await user.click(disabledOption);

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const healthyOption = await screen.findByRole('option', { name: 'Healthy' });
        await user.click(healthyOption);

        await waitFor(() => {
            expect(screen.getByTestId('empty-state__icon')).toHaveAttribute('aria-hidden', 'true');
        });
    });
});

describe('DownloadClientsPage accessibility', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('list has accessible label', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByRole('list', { name: /download clients/i })).toBeInTheDocument();
        });
    });

    it('summary section has accessible label', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        const summary = document.querySelector('[aria-label="Health summary"]');
        expect(summary).toBeInTheDocument();
    });

    it('toggle switches have aria-labels', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const toggles = document.querySelectorAll('[data-action="toggle-enabled"]');
            expect(toggles.length).toBe(7);
        });

        const toggles = document.querySelectorAll('[data-action="toggle-enabled"]');
        toggles.forEach((toggle) => {
            expect(toggle).toHaveAttribute('aria-label');
        });
    });

    it('action buttons have aria-labels', async () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            const actionButtons = screen.getAllByTestId('download-clients-page__action-btn');
            expect(actionButtons.length).toBeGreaterThan(0);
        });

        const actionButtons = screen.getAllByTestId('download-clients-page__action-btn');
        actionButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
    });

    it('filter triggers have aria-labels', () => {
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        const statusFilter = document.querySelector('[data-filter="status"]');
        expect(statusFilter).toHaveAttribute('aria-label', 'Filter by status');

        const enabledFilter = document.querySelector('[data-filter="enabled"]');
        expect(enabledFilter).toHaveAttribute('aria-label', 'Filter by enabled state');
    });
});

describe('DownloadClientsPage URL param initialization', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('initializes status filter from URL search params', async () => {
        const queryClient = createTestQueryClient();

        function TestWrapperWithUrl({ children }: { children: ReactNode }) {
            return (
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={['/download-clients?status=healthy']}>
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

        render(<DownloadClientsPage />, { wrapper: TestWrapperWithUrl });

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /download clients/i });
            const items = within(list).getAllByRole('listitem');

            expect(items.length).toBe(3);
        });

        const statusTrigger = document.querySelector('[data-filter="status"]');
        expect(statusTrigger).toHaveTextContent('Healthy');
    });
});

describe('DownloadClientsPage enable/disable toggle', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiClients);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('calls disable API when toggle is clicked on enabled client', async () => {
        mockApiPost.mockResolvedValue({ ...mockApiClients[0], enabled: false });
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(document.querySelector('[data-client-id="client-1"]')).toBeInTheDocument();
        });

        const sabnzbdItem = document.querySelector('[data-client-id="client-1"]');
        const toggle = sabnzbdItem?.querySelector('[data-action="toggle-enabled"]') as HTMLElement;
        expect(toggle).toHaveAttribute('aria-checked', 'true');

        await user.click(toggle);

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/download-clients/client-1/disable');
        });
    });

    it('calls enable API when toggle is clicked on disabled client', async () => {
        mockApiPost.mockResolvedValue({ ...mockApiClients[4], enabled: true });
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<DownloadClientsPage />, { wrapper: createTestWrapper(queryClient) });

        await waitFor(() => {
            expect(document.querySelector('[data-client-id="client-5"]')).toBeInTheDocument();
        });

        const delugeItem = document.querySelector('[data-client-id="client-5"]');
        const toggle = delugeItem?.querySelector('[data-action="toggle-enabled"]') as HTMLElement;
        expect(toggle).toHaveAttribute('aria-checked', 'false');

        await user.click(toggle);

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/download-clients/client-5/enable');
        });
    });
});
