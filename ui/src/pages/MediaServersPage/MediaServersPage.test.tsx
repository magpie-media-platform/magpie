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

const mockTestServer = vi.fn().mockResolvedValue({ success: true, message: 'Connected.' });
const mockTestServerSettings = vi.fn().mockResolvedValue({ success: true, message: 'Connected.' });
vi.mock('@/hooks/useTestConnection', () => ({
    useTestMediaServer: () => ({ test: mockTestServer, isPending: false, error: null, cancel: vi.fn() }),
    useTestMediaServerSettings: () => ({
        test: mockTestServerSettings,
        isPending: false,
        error: null,
        cancel: vi.fn(),
    }),
}));

import { useMediaServers } from '@/hooks/useMediaServers';

const mockApiServers: ApiMediaServer[] = [
    {
        id: 'server-1',
        providerType: 'plex',
        name: 'Plex Media Server',
        enabled: true,
        status: 'Healthy',
        lastChecked: '2024-01-15T10:30:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'server-2',
        providerType: 'mediaserver-a',
        name: 'MediaServer A',
        enabled: true,
        status: 'Healthy',
        lastChecked: '2024-01-15T10:28:00Z',
        settings: {
            host: 'http://localhost:8096',
            apiKey: '********',
            libraryMappings: '[{"libraryId":"movies","rootFolderId":"root-1"}]',
        },
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'server-3',
        providerType: 'emby',
        name: 'Emby',
        enabled: true,
        status: 'Unhealthy',
        lastChecked: '2024-01-15T10:25:00Z',
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'server-4',
        providerType: 'kodi',
        name: 'Kodi',
        enabled: true,
        status: 'Healthy',
        lastChecked: '2024-01-15T10:32:00Z',
        createdAt: '2024-01-03T00:00:00Z',
    },
    {
        id: 'server-5',
        providerType: 'plex',
        name: 'Plex (Remote)',
        enabled: false,
        status: 'Unknown',
        lastChecked: null,
        createdAt: '2024-01-04T00:00:00Z',
    },
    {
        id: 'server-6',
        providerType: 'subsonic',
        name: 'Subsonic',
        enabled: true,
        status: 'Stale',
        lastChecked: '2024-01-15T10:20:00Z',
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

function createWrapper(initialEntries: string[] = ['/media-servers']) {
    return function TestWrapper({ children }: { children: ReactNode }) {
        const queryClient = new QueryClient({
            defaultOptions: { queries: { retry: false, gcTime: 0 } },
        });

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

describe('MediaServersPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('renders with correct data-page attribute', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(document.querySelector('[data-page="media-servers"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(screen.getByRole('heading', { name: 'Media Servers', level: 1 })).toBeInTheDocument();
    });

    it('keeps the selection toggle on the list toolbar line', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        const toggle = screen.getByRole('button', { name: 'Select', pressed: false });
        expect(toggle.closest('[data-component="mobile-list-toolbar"]')).toBeInTheDocument();
    });

    it('renders the page icon as decorative', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(screen.getByTestId('media-servers-page__icon')).toHaveAttribute('aria-hidden', 'true');
    });

    it('renders an Add Server button', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(screen.getAllByRole('button', { name: /add server/i }).length).toBeGreaterThanOrEqual(1);
    });

    it('does not render health-summary stat pills', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(document.querySelector('[aria-label="Health summary"]')).not.toBeInTheDocument();
    });
});

describe('MediaServersPage table', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('renders one row per server in a table', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(dataRows().length).toBe(6);
    });

    it('renders server names and provider types', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(screen.getByText('Plex Media Server')).toBeInTheDocument();
        expect(screen.getByText('mediaserver-a')).toBeInTheDocument();
    });

    it('keeps a long unbroken server name contained with the full name available', () => {
        const longName = `MediaServer${'UnbrokenNameSegment'.repeat(8)}`;
        vi.mocked(useMediaServers).mockReturnValue(
            createMockHookReturn([{ ...mockApiServers[0], id: 'server-long', name: longName }]),
        );

        render(<MediaServersPage />, { wrapper: createWrapper() });

        const name = screen.getByTitle(longName);
        expect(name).toHaveClass('media-servers-table__name');
        expect(name.closest('[data-column="name"]')).toBeInTheDocument();
    });

    it('renders a sortable Name column header', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(screen.getByRole('button', { name: /sort by name/i })).toBeInTheDocument();
    });
});

describe('MediaServersPage row actions', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('exposes test, enable toggle, edit and delete per row', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(document.querySelectorAll('[data-action="test"]').length).toBe(6);
        expect(document.querySelectorAll('[data-action="toggle-enabled"]').length).toBe(6);
        expect(document.querySelectorAll('[data-action="edit"]').length).toBe(6);
        expect(document.querySelectorAll('[data-action="delete"]').length).toBe(6);
    });

    it('tests the targeted server by id', async () => {
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByRole('button', { name: /^test plex media server$/i }));

        await waitFor(() => {
            expect(mockTestServer).toHaveBeenCalledWith('server-1');
        });
    });

    it('toggles a server through toggleServerEnabled', async () => {
        const hook = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(hook);
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByRole('switch', { name: /disable plex media server/i }));

        await waitFor(() => {
            expect(hook.toggleServerEnabled).toHaveBeenCalledWith('server-1', false);
        });
    });

    it('keeps the row enabled and surfaces the server error detail when the toggle fails', async () => {
        const hook = createMockHookReturn();
        hook.toggleServerEnabled = vi.fn().mockRejectedValue(new Error('Media server is unreachable.'));
        vi.mocked(useMediaServers).mockReturnValue(hook);
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByRole('switch', { name: /disable plex media server/i }));

        await waitFor(() => {
            expect(screen.getByText('Media server is unreachable.')).toBeInTheDocument();
        });
        expect(screen.getByRole('switch', { name: /disable plex media server/i })).toBeChecked();
    });

    it('opens the edit form when edit is clicked', async () => {
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByRole('button', { name: /^edit plex media server$/i }));

        await waitFor(() => {
            expect(screen.getByText('Edit Media Server')).toBeInTheDocument();
        });
    });

    it('keeps saved settings when an edited server is saved without changes', async () => {
        const hook = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(hook);
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByRole('button', { name: /^edit mediaserver a$/i }));

        await waitFor(() => {
            expect(screen.getByText('Edit Media Server')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(hook.updateServer).toHaveBeenCalledWith('server-2', {
                name: { value: 'MediaServer A' },
                enabled: { value: true },
                settings: {
                    update: {
                        host: 'http://localhost:8096',
                        apiKey: '********',
                        libraryMappings: '[{"libraryId":"movies","rootFolderId":"root-1"}]',
                    },
                },
            });
        });
    });

    it('deletes the targeted server after confirming', async () => {
        const hook = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(hook);
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByRole('button', { name: /^delete plex media server$/i }));

        await waitFor(() => {
            expect(screen.getByText('Delete Media Server')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^delete$/i }));

        await waitFor(() => {
            expect(hook.deleteServer).toHaveBeenCalledWith('server-1');
        });
    });
});

describe('MediaServersPage column-header filters', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('exposes provider, status and enabled column-header filters', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(screen.getByRole('button', { name: /filter by provider/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by status/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by enabled/i })).toBeInTheDocument();
    });

    it('renders the full "Enabled" column header label', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        const enabledHeader = screen
            .getAllByRole('columnheader')
            .find((cell) => cell.getAttribute('data-column') === 'enabled');
        expect(enabledHeader).toBeDefined();
        expect(enabledHeader).toHaveTextContent('Enabled');
    });

    it('filters rows by status', async () => {
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByRole('button', { name: /filter by status/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Unhealthy' }));

        await waitFor(() => {
            expect(dataRows().length).toBe(1);
        });
        expect(screen.getByText('Emby')).toBeInTheDocument();
    });

    it('filters rows by enabled state', async () => {
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByRole('button', { name: /filter by enabled/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Disabled' }));

        await waitFor(() => {
            expect(dataRows().length).toBe(1);
        });
        expect(screen.getByText('Plex (Remote)')).toBeInTheDocument();
    });

    it('shows a clear-filters control once a filter is active', async () => {
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /filter by status/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Healthy' }));

        expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
    });
});

describe('MediaServersPage URL state', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('initializes the status filter from a legacy lowercase URL param', () => {
        render(<MediaServersPage />, { wrapper: createWrapper(['/media-servers?status=healthy']) });

        expect(dataRows().length).toBe(3);
    });
});

describe('MediaServersPage (mobile card collapse)', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(useMediaServers).mockReturnValue(createMockHookReturn());
    });

    it('marks the name cell as header role and the select column as hidden (no layout-participating checkbox)', () => {
        render(<MediaServersPage />, { wrapper: createWrapper() });

        expect(document.querySelector('td[data-column="name"]')).toHaveAttribute('data-mobile-role', 'header');
        expect(document.querySelector('td[data-column="select"]')).toHaveAttribute('data-mobile-role', 'hidden');
    });

    it('bulk-deletes every selected server', async () => {
        const hookReturn = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(hookReturn);
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByLabelText('Select Plex Media Server'));
        await user.click(screen.getByLabelText('Select MediaServer A'));
        await user.click(screen.getByRole('button', { name: 'Delete selected media servers' }));
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLElement);

        await waitFor(() => {
            expect(hookReturn.deleteServer).toHaveBeenCalledWith('server-1');
            expect(hookReturn.deleteServer).toHaveBeenCalledWith('server-2');
        });
    });

    it('bulk-enables every selected server', async () => {
        const hookReturn = createMockHookReturn();
        vi.mocked(useMediaServers).mockReturnValue(hookReturn);
        const user = userEvent.setup();
        render(<MediaServersPage />, { wrapper: createWrapper() });

        await user.click(screen.getByLabelText('Select Plex Media Server'));
        await user.click(screen.getByRole('button', { name: 'Enable selected media servers' }));

        await waitFor(() => {
            expect(hookReturn.toggleServerEnabled).toHaveBeenCalledWith('server-1', true);
        });
    });
});
