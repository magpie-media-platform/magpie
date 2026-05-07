import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiMediaServer } from '@/hooks/useMediaServers';

import { MediaServersPage } from './MediaServersPage';

vi.mock('@/hooks/useMediaServers', async () => {
    const actual = await vi.importActual('@/hooks/useMediaServers');
    return {
        ...actual,
        useMediaServers: vi.fn(),
    };
});

import { useMediaServers } from '@/hooks/useMediaServers';

const mockApiServers: ApiMediaServer[] = [
    {
        id: 'server-1',
        providerType: 'plex',
        name: 'Plex Media Server',
        enabled: true,
        status: 1,
        lastChecked: '2024-01-15T10:30:00Z',
        settings: { host: 'localhost', port: '32400' },
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'server-2',
        providerType: 'jellyfin',
        name: 'Jellyfin',
        enabled: true,
        status: 1,
        lastChecked: '2024-01-15T10:28:00Z',
        settings: { host: 'localhost', port: '8096' },
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'server-3',
        providerType: 'emby',
        name: 'Emby',
        enabled: true,
        status: 2,
        lastChecked: '2024-01-15T10:25:00Z',
        settings: { host: 'media.example.com', port: '8920' },
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'server-4',
        providerType: 'kodi',
        name: 'Kodi',
        enabled: true,
        status: 1,
        lastChecked: '2024-01-15T10:32:00Z',
        settings: { host: '192.168.1.100', port: '8080' },
        createdAt: '2024-01-03T00:00:00Z',
    },
    {
        id: 'server-5',
        providerType: 'plex',
        name: 'Plex (Remote)',
        enabled: false,
        status: 0,
        lastChecked: null,
        settings: { host: 'remote.example.com', port: '32400' },
        createdAt: '2024-01-04T00:00:00Z',
    },
    {
        id: 'server-6',
        providerType: 'subsonic',
        name: 'Subsonic',
        enabled: true,
        status: 3,
        lastChecked: '2024-01-15T10:20:00Z',
        settings: { host: 'localhost', port: '4040' },
        createdAt: '2024-01-05T00:00:00Z',
    },
];

function createMockHookReturn(servers: ApiMediaServer[] = mockApiServers) {
    return {
        servers,
        isLoading: false,
        error: null,
        createServer: vi.fn().mockResolvedValue(undefined),
        isCreating: false,
        updateServer: vi.fn().mockResolvedValue(undefined),
        toggleServerEnabled: vi.fn().mockResolvedValue(undefined),
        isToggling: false,
        isUpdating: false,
        deleteServer: vi.fn().mockResolvedValue(undefined),
        isDeleting: false,
        refetch: vi.fn().mockResolvedValue(undefined),
    };
}

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });

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

describe('MediaServersPage', () => {
    beforeEach(() => {
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('renders with correct data-page attribute', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-page="media-servers"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('heading', { name: 'Media Servers', level: 1 })).toBeInTheDocument();
    });

    it('renders the server count', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const count = screen.getByTestId('media-servers-page__count');
        expect(count).toBeInTheDocument();
        expect(count).toHaveTextContent('6');
    });

    it('renders the page icon as decorative', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const icon = screen.getByTestId('media-servers-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });

    it('renders an Add Server button', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const addButtons = screen.getAllByRole('button', { name: /add server/i });
        expect(addButtons.length).toBeGreaterThanOrEqual(1);
    });
});

describe('MediaServersPage summary badges', () => {
    beforeEach(() => {
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('renders summary badges for each health status', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        expect(screen.getAllByText('Healthy').length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText('Unhealthy').length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText('Stale').length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText('Unknown').length).toBeGreaterThanOrEqual(1);
    });

    it('applies data-status attributes to summary badges', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Health summary"]');
        const badges = summary?.querySelectorAll('[data-component="badge"]') ?? [];
        const statuses = Array.from(badges).map((b) => b.getAttribute('data-status'));

        expect(statuses).toContain('healthy');
        expect(statuses).toContain('unhealthy');
        expect(statuses).toContain('stale');
        expect(statuses).toContain('unknown');
    });

    it('renders badge dots as decorative', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Health summary"]');
        const dots = summary?.querySelectorAll('[data-component="badge"] [aria-hidden="true"]') ?? [];
        dots.forEach((dot) => {
            expect(dot).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('shows correct count for healthy servers', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Health summary"]');
        const healthyBadge = summary?.querySelector('[data-component="badge"][data-status="healthy"]');
        const count = healthyBadge?.querySelector('[data-component="badge"] .badge__count');

        expect(count).toHaveTextContent('3');
    });
});

describe('MediaServersPage list items', () => {
    beforeEach(() => {
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('renders server items in the list', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const list = screen.getByRole('list', { name: /media servers/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(6);
    });

    it('renders server names', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        expect(screen.getByText('Plex Media Server')).toBeInTheDocument();
        expect(screen.getByText('Jellyfin')).toBeInTheDocument();
        expect(screen.getByText('Emby')).toBeInTheDocument();
        expect(screen.getByText('Kodi')).toBeInTheDocument();
    });

    it('renders provider names', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        expect(screen.getAllByText('plex').length).toBeGreaterThanOrEqual(1);
        expect(screen.getByText('jellyfin')).toBeInTheDocument();
        expect(screen.getByText('emby')).toBeInTheDocument();
        expect(screen.getByText('kodi')).toBeInTheDocument();
    });

    it('renders last checked time with label', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        expect(screen.getByText('Checked Never')).toBeInTheDocument();
    });

    it('applies data-server-id to items', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const items = document.querySelectorAll('[data-server-id]');
        expect(items.length).toBe(6);
        expect(items[0]).toHaveAttribute('data-server-id', 'server-1');
    });

    it('applies data-status to items', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const healthyItems = document.querySelectorAll('[data-status="healthy"]');

        expect(healthyItems.length).toBeGreaterThanOrEqual(3);
    });

    it('applies data-enabled attribute to items', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const enabledItems = document.querySelectorAll('[data-testid="media-servers-page__item"][data-enabled="true"]');
        const disabledItems = document.querySelectorAll(
            '[data-testid="media-servers-page__item"][data-enabled="false"]',
        );
        expect(enabledItems.length).toBe(5);
        expect(disabledItems.length).toBe(1);
    });

    it('applies disabled styling to disabled servers', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const disabledItems = document.querySelectorAll(
            '[data-testid="media-servers-page__item"][data-enabled="false"]',
        );
        expect(disabledItems.length).toBe(1);
    });
});

describe('MediaServersPage health badges', () => {
    beforeEach(() => {
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('renders health badges with dot indicators', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const items = screen.getAllByTestId('media-servers-page__item');
        items.forEach((item) => {
            expect(item.querySelector('[data-component="badge"]')).toBeInTheDocument();
            expect(item.querySelector('[data-component="badge"] [aria-hidden="true"]')).toBeInTheDocument();
        });
    });

    it('applies correct variant to healthy servers', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const plexItem = document.querySelector('[data-server-id="server-1"]');
        expect(plexItem?.querySelector('[data-component="badge"][data-variant="healthy"]')).toBeInTheDocument();
    });

    it('applies correct variant to unhealthy servers', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const unhealthyItem = document.querySelector('[data-server-id="server-3"]');
        expect(unhealthyItem?.querySelector('[data-component="badge"][data-variant="unhealthy"]')).toBeInTheDocument();
    });

    it('applies correct variant to stale servers', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const staleItem = document.querySelector('[data-server-id="server-6"]');
        expect(staleItem?.querySelector('[data-component="badge"][data-variant="stale"]')).toBeInTheDocument();
    });
});

describe('MediaServersPage actions', () => {
    beforeEach(() => {
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('renders enable/disable toggle for each server', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const toggles = document.querySelectorAll('[data-action="toggle-enabled"]');
        expect(toggles.length).toBe(6);
    });

    it('toggle shows checked state for enabled servers', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const plexItem = document.querySelector('[data-server-id="server-1"]');
        const toggle = plexItem?.querySelector('[data-action="toggle-enabled"]');
        expect(toggle).toHaveAttribute('aria-checked', 'true');
    });

    it('toggle shows unchecked state for disabled servers', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const remoteItem = document.querySelector('[data-server-id="server-5"]');
        const toggle = remoteItem?.querySelector('[data-action="toggle-enabled"]');
        expect(toggle).toHaveAttribute('aria-checked', 'false');
    });

    it('renders edit button for each server', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        expect(editButtons.length).toBe(6);
    });

    it('renders delete button for each server', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const deleteButtons = document.querySelectorAll('[data-action="delete"]');
        expect(deleteButtons.length).toBe(6);
    });

    it('edit button has correct aria-label', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const editButton = screen.getByRole('button', { name: /^edit plex media server$/i });
        expect(editButton).toHaveAttribute('data-action', 'edit');
    });

    it('delete button has correct aria-label', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const deleteButton = screen.getByRole('button', { name: /^delete plex media server$/i });
        expect(deleteButton).toHaveAttribute('data-action', 'delete');
    });

    it('opens edit form when edit button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const editButton = screen.getByRole('button', { name: /^edit plex media server$/i });
        await user.click(editButton);

        await waitFor(() => {
            expect(screen.getByText('Edit Media Server')).toBeInTheDocument();
            expect(screen.getByDisplayValue('Plex Media Server')).toBeInTheDocument();
        });
    });

    it('opens confirmation dialog when delete button is clicked', async () => {
        const mockHookReturn = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(mockHookReturn);

        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const deleteButton = screen.getByRole('button', { name: /^delete plex media server$/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByText('Delete Media Server')).toBeInTheDocument();
        });
        const dialog = document.querySelector('[data-component="delete-confirm-dialog"]')!;
        expect(within(dialog).getByText(/Plex Media Server/)).toBeInTheDocument();
        expect(mockHookReturn.deleteServer).not.toHaveBeenCalled();
    });

    it('calls deleteServer after confirming deletion', async () => {
        const mockHookReturn = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(mockHookReturn);

        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const deleteButton = screen.getByRole('button', { name: /^delete plex media server$/i });
        await user.click(deleteButton);

        const confirmButton = await screen.findByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        expect(mockHookReturn.deleteServer).toHaveBeenCalledWith('server-1');
    });

    it('does not call deleteServer when cancel is clicked', async () => {
        const mockHookReturn = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(mockHookReturn);

        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const deleteButton = screen.getByRole('button', { name: /^delete plex media server$/i });
        await user.click(deleteButton);

        const cancelButton = await screen.findByRole('button', { name: /^cancel$/i });
        await user.click(cancelButton);

        expect(mockHookReturn.deleteServer).not.toHaveBeenCalled();
    });
});

describe('MediaServersPage filters', () => {
    beforeEach(() => {
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('renders status and enabled filter dropdowns', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-filter="status"]')).toBeInTheDocument();
        expect(document.querySelector('[data-filter="enabled"]')).toBeInTheDocument();
    });

    it('status filter displays "All Status" by default', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const statusTrigger = document.querySelector('[data-filter="status"]');
        expect(statusTrigger).toHaveTextContent('All Status');
    });

    it('enabled filter displays "All Enabled" by default', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const enabledTrigger = document.querySelector('[data-filter="enabled"]');
        expect(enabledTrigger).toHaveTextContent('All Enabled');
    });

    it('filters items by status when selection changes', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const unhealthyOption = await screen.findByRole('option', { name: 'Unhealthy' });
        await user.click(unhealthyOption);

        const list = screen.getByRole('list', { name: /media servers/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(1);
        expect(screen.getByText('Emby')).toBeInTheDocument();
    });

    it('filters items by enabled state when selection changes', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const enabledTrigger = document.querySelector('[data-filter="enabled"]') as HTMLElement;
        await user.click(enabledTrigger);

        const disabledOption = await screen.findByRole('option', { name: 'Disabled' });
        await user.click(disabledOption);

        const list = screen.getByRole('list', { name: /media servers/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(1);
        expect(screen.getByText('Plex (Remote)')).toBeInTheDocument();
    });

    it('shows clear button when filters are active', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const healthyOption = await screen.findByRole('option', { name: 'Healthy' });
        await user.click(healthyOption);

        expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
    });

    it('clears filters when clear button is clicked', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const healthyOption = await screen.findByRole('option', { name: 'Healthy' });
        await user.click(healthyOption);

        const clearButton = document.querySelector('[data-action="clear-filters"]') as HTMLElement;
        await user.click(clearButton);

        const list = screen.getByRole('list', { name: /media servers/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(6);
    });

    it('shows empty state when filters match nothing', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const enabledTrigger = document.querySelector('[data-filter="enabled"]') as HTMLElement;
        await user.click(enabledTrigger);

        const disabledOption = await screen.findByRole('option', { name: 'Disabled' });
        await user.click(disabledOption);

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const healthyOption = await screen.findByRole('option', { name: 'Healthy' });
        await user.click(healthyOption);

        expect(screen.getByText('No media servers found')).toBeInTheDocument();
        expect(screen.getByText('Try adjusting your filters')).toBeInTheDocument();
    });
});

describe('MediaServersPage empty state', () => {
    beforeEach(() => {
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('shows empty state icon as decorative when no results', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const enabledTrigger = document.querySelector('[data-filter="enabled"]') as HTMLElement;
        await user.click(enabledTrigger);

        const disabledOption = await screen.findByRole('option', { name: 'Disabled' });
        await user.click(disabledOption);

        const statusTrigger = document.querySelector('[data-filter="status"]') as HTMLElement;
        await user.click(statusTrigger);

        const healthyOption = await screen.findByRole('option', { name: 'Healthy' });
        await user.click(healthyOption);

        expect(screen.getByTestId('empty-state__icon')).toHaveAttribute('aria-hidden', 'true');
    });
});

describe('MediaServersPage accessibility', () => {
    beforeEach(() => {
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('list has accessible label', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /media servers/i })).toBeInTheDocument();
    });

    it('summary section has accessible label', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Health summary"]');
        expect(summary).toBeInTheDocument();
    });

    it('toggle switches have aria-labels', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const toggles = document.querySelectorAll('[data-action="toggle-enabled"]');
        toggles.forEach((toggle) => {
            expect(toggle).toHaveAttribute('aria-label');
        });
    });

    it('action buttons have aria-labels', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const actionButtons = screen.getAllByTestId('media-servers-page__action-btn');
        actionButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
    });

    it('filter triggers have aria-labels', () => {
        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const statusFilter = document.querySelector('[data-filter="status"]');
        expect(statusFilter).toHaveAttribute('aria-label', 'Filter by status');

        const enabledFilter = document.querySelector('[data-filter="enabled"]');
        expect(enabledFilter).toHaveAttribute('aria-label', 'Filter by enabled state');
    });
});

describe('MediaServersPage URL param initialization', () => {
    beforeEach(() => {
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('initializes status filter from URL search params', () => {
        const queryClient = new QueryClient({
            defaultOptions: {
                queries: {
                    retry: false,
                    gcTime: 0,
                },
            },
        });

        function TestWrapperWithUrl({ children }: { children: ReactNode }) {
            return (
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={['/media-servers?status=healthy']}>
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

        render(
            <TestWrapperWithUrl>
                <MediaServersPage />
            </TestWrapperWithUrl>,
        );

        const list = screen.getByRole('list', { name: /media servers/i });
        const items = within(list).getAllByRole('listitem');
        expect(items.length).toBe(3);

        const statusTrigger = document.querySelector('[data-filter="status"]');
        expect(statusTrigger).toHaveTextContent('Healthy');
    });
});

describe('MediaServersPage enable/disable toggle', () => {
    it('calls toggleServerEnabled when toggle is clicked', async () => {
        const mockHookReturn = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(mockHookReturn);

        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const plexItem = document.querySelector('[data-server-id="server-1"]');
        const toggle = plexItem?.querySelector('[data-action="toggle-enabled"]') as HTMLElement;

        await user.click(toggle);

        expect(mockHookReturn.toggleServerEnabled).toHaveBeenCalledWith('server-1', false);
    });

    it('enables a disabled server when toggle is clicked', async () => {
        const mockHookReturn = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(mockHookReturn);

        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MediaServersPage />
            </TestWrapper>,
        );

        const remoteItem = document.querySelector('[data-server-id="server-5"]');
        const toggle = remoteItem?.querySelector('[data-action="toggle-enabled"]') as HTMLElement;

        await user.click(toggle);

        expect(mockHookReturn.toggleServerEnabled).toHaveBeenCalledWith('server-5', true);
    });
});
