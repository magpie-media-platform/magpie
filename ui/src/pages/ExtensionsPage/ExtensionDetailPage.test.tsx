import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { RestartRequiredProvider } from '@/components/ui/RestartRequiredBanner';
import type { ApiExtension } from '@/hooks/useExtensionManagement';

import { ExtensionDetailPage } from './ExtensionDetailPage';

vi.mock('@/components/ui/Tooltip', () => ({
    Tooltip: ({ children }: { children: React.ReactNode }) => <>{children}</>,
    TooltipProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
    TooltipRoot: ({ children }: { children: React.ReactNode }) => <>{children}</>,
    TooltipTrigger: ({ children }: { children: React.ReactNode }) => <>{children}</>,
    TooltipPortal: ({ children }: { children: React.ReactNode }) => <>{children}</>,
    TooltipContent: ({ children }: { children: React.ReactNode }) => <>{children}</>,
    TooltipArrow: () => null,
    default: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

vi.mock('@/components/ui/Switch', () => ({
    Switch: (props: {
        checked?: boolean;
        'aria-label'?: string;
        disabled?: boolean;
        onCheckedChange?: (v: boolean) => void;
    }) => (
        <button
            role="switch"
            aria-checked={props.checked}
            aria-label={props['aria-label']}
            disabled={props.disabled}
            onClick={() => props.onCheckedChange?.(!props.checked)}
        />
    ),
    default: (props: {
        checked?: boolean;
        'aria-label'?: string;
        disabled?: boolean;
        onCheckedChange?: (v: boolean) => void;
    }) => (
        <button
            role="switch"
            aria-checked={props.checked}
            aria-label={props['aria-label']}
            disabled={props.disabled}
            onClick={() => props.onCheckedChange?.(!props.checked)}
        />
    ),
}));

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => ({ apiBaseUrl: '', userId: null, username: null, role: null }),
    useSlotExtensions: () => ({ extensions: [], isReady: true, isLoading: false }),
}));

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn().mockResolvedValue({}),
    apiPatch: vi.fn().mockResolvedValue(undefined),
    isApiError: vi.fn(() => false),
    buildUrl: vi.fn((path: string) => path),
}));

import { apiGet, apiPost } from '@/lib/api';

const mockManagedExtension: ApiExtension = {
    id: 'ext-qbittorrent',
    name: 'qBittorrent',
    version: '2.1.0',
    targetABI: '1.0',
    filePath: '/extensions/qbittorrent.dll',
    assemblyName: 'Magpie.Extensions.qBittorrent',
    installed: true,
    enabled: true,
    disabledReason: null,
    loadOrder: 100,
    managed: true,
    nuGetId: 'Magpie.Extensions.qBittorrent',
    packageRepository: 'https://api.nuget.org/v3/index.json',
    autoUpgrade: false,
    lastChecked: '2024-01-15T10:00:00Z',
    createdAt: '2024-01-10T00:00:00Z',
    createdByUserId: null,
    modifiedAt: null,
    modifiedByUserId: null,
};

const mockManagedExtensionOldVersion: ApiExtension = {
    id: 'ext-qbittorrent-v1',
    name: 'qBittorrent',
    version: '1.0.0',
    targetABI: '1.0',
    filePath: '/extensions/qbittorrent-1.0.0.dll',
    assemblyName: 'Magpie.Extensions.qBittorrent, Version=1.0.0.0',
    installed: false,
    enabled: false,
    disabledReason: null,
    loadOrder: 100,
    managed: true,
    nuGetId: 'Magpie.Extensions.qBittorrent',
    packageRepository: 'https://api.nuget.org/v3/index.json',
    autoUpgrade: false,
    lastChecked: null,
    createdAt: '2023-11-15T00:00:00Z',
    createdByUserId: null,
    modifiedAt: null,
    modifiedByUserId: null,
};

const mockBuiltInExtension: ApiExtension = {
    id: 'ext-core',
    name: 'Magpie Core',
    version: '1.0.0',
    targetABI: '1.0',
    filePath: '/extensions/core.dll',
    assemblyName: 'Magpie.Core',
    installed: true,
    enabled: true,
    disabledReason: null,
    loadOrder: 1,
    managed: false,
    nuGetId: null,
    packageRepository: null,
    autoUpgrade: false,
    lastChecked: null,
    createdAt: '2024-01-01T00:00:00Z',
    createdByUserId: null,
    modifiedAt: null,
    modifiedByUserId: null,
};

const mockDisabledExtension: ApiExtension = {
    id: 'ext-broken',
    name: 'Legacy Extension',
    version: '0.5.0',
    targetABI: '0.9',
    filePath: '/extensions/legacy.dll',
    assemblyName: 'Legacy.Extension',
    installed: true,
    enabled: false,
    disabledReason: 'Incompatible with current ABI version',
    loadOrder: 200,
    managed: false,
    nuGetId: null,
    packageRepository: null,
    autoUpgrade: false,
    lastChecked: null,
    createdAt: '2023-12-01T00:00:00Z',
    createdByUserId: null,
    modifiedAt: null,
    modifiedByUserId: null,
};

const mockDisabledManagedExtension: ApiExtension = {
    id: 'ext-prowlarr',
    name: 'Prowlarr',
    version: '1.0.2',
    targetABI: '1.0',
    filePath: '/extensions/prowlarr.dll',
    assemblyName: 'Magpie.Extensions.Prowlarr',
    installed: true,
    enabled: false,
    disabledReason: null,
    loadOrder: 102,
    managed: true,
    nuGetId: 'Magpie.Extensions.Prowlarr',
    packageRepository: 'https://api.nuget.org/v3/index.json',
    autoUpgrade: false,
    lastChecked: null,
    createdAt: '2024-01-05T00:00:00Z',
    createdByUserId: null,
    modifiedAt: null,
    modifiedByUserId: null,
};

const mockAllExtensions = [
    mockManagedExtension,
    mockManagedExtensionOldVersion,
    mockBuiltInExtension,
    mockDisabledExtension,
    mockDisabledManagedExtension,
];

function createWrapper(initialPath: string = '/admin/extensions/ext-qbittorrent') {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter initialEntries={[initialPath]}>
                    <RestartRequiredProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <Routes>
                                    <Route path="/admin/extensions/:extensionId" element={children} />
                                </Routes>
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </RestartRequiredProvider>
                </MemoryRouter>
            </QueryClientProvider>
        );
    };
}

describe('ExtensionDetailPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(mockAllExtensions);
            }
            if (url === '/settings/groups') {
                return Promise.resolve([]);
            }
            return Promise.resolve([]);
        });
    });

    it('renders with correct data-page attribute', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-page="extension-detail"]')).toBeInTheDocument();
        });
    });

    it('shows loading state initially', () => {
        vi.mocked(apiGet).mockReturnValue(new Promise(() => {}));

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        expect(screen.getByText('Loading extension...')).toBeInTheDocument();
    });

    it('renders extension name as heading', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'qBittorrent', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders extension version in header', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const header = screen.getByTestId('extension-detail__header');
            expect(header).toHaveTextContent('v2.1.0');
        });
    });

    it('shows breadcrumb link back to extensions list', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const nav = screen.getByRole('navigation', { name: 'Breadcrumb' });
            expect(nav).toBeInTheDocument();
        });

        const backLink = screen.getByRole('link', { name: /extensions/i });
        expect(backLink).toHaveAttribute('href', '/admin/extensions');
    });

    it('shows current extension name in breadcrumb', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const nav = screen.getByRole('navigation', { name: 'Breadcrumb' });
            expect(nav).toHaveTextContent('qBittorrent');
        });
    });

    it('renders Enabled status badge for enabled extension', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Enabled')).toBeInTheDocument();
        });
    });

    it('renders Managed badge for managed extensions', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Managed')).toBeInTheDocument();
        });
    });

    it('renders enable/disable toggle for non-built-in extension', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const toggle = screen.getByRole('switch', { name: /disable qbittorrent/i });
            expect(toggle).toBeInTheDocument();
        });
    });

    it('does not render enable/disable toggle for built-in extension', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-core') });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Magpie Core', level: 1 })).toBeInTheDocument();
        });

        expect(screen.queryByRole('switch')).not.toBeInTheDocument();
    });

    it('shows Built-in badge for built-in extensions', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-core') });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Magpie Core', level: 1 })).toBeInTheDocument();
        });

        expect(screen.getAllByText('Built-in').length).toBeGreaterThanOrEqual(1);
    });

    it('renders overview section with file path', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('/extensions/qbittorrent.dll')).toBeInTheDocument();
        });
    });

    it('renders overview section with NuGet package ID', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const overviewSection = document.querySelector('[data-section="overview"]');
            expect(overviewSection).toHaveTextContent('Magpie.Extensions.qBittorrent');
        });
    });

    it('renders Package Management section for managed extensions', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Package Management')).toBeInTheDocument();
        });
    });

    it('does not render Package Management section for built-in extensions', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-core') });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Magpie Core', level: 1 })).toBeInTheDocument();
        });

        expect(screen.queryByText('Package Management')).not.toBeInTheDocument();
    });

    it('renders auto-upgrade toggle in management section', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /toggle auto-upgrade/i })).toBeInTheDocument();
        });
    });

    it('shows error banner when extension has disabledReason', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-broken') });

        await waitFor(() => {
            expect(screen.getByText('Incompatible with current ABI version')).toBeInTheDocument();
        });
    });

    it('shows Retry button in error banner', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-broken') });

        await waitFor(() => {
            expect(screen.getByText('Incompatible with current ABI version')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
    });

    it('shows version history section when older versions exist', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Version History')).toBeInTheDocument();
        });
    });

    it('shows current version badge in version history', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Version History')).toBeInTheDocument();
        });

        expect(screen.getByText('Current')).toBeInTheDocument();
    });

    it('does not show version history section when no older versions exist', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-core') });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Magpie Core', level: 1 })).toBeInTheDocument();
        });

        expect(screen.queryByText('Version History')).not.toBeInTheDocument();
    });
});

describe('ExtensionDetailPage error state', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('shows error state when extension fetch fails', async () => {
        vi.mocked(apiGet).mockRejectedValue(new Error('Network error'));

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Failed to load extension')).toBeInTheDocument();
        });
    });

    it('shows retry button in error state', async () => {
        vi.mocked(apiGet).mockRejectedValue(new Error('Network error'));

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
        });
    });

    it('shows back link in error state', async () => {
        vi.mocked(apiGet).mockRejectedValue(new Error('Network error'));

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('link', { name: /extensions/i })).toBeInTheDocument();
        });
    });

    it('shows not found message when extensionId does not match any extension', async () => {
        vi.mocked(apiGet).mockResolvedValue([mockManagedExtension]);

        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/non-existent') });

        await waitFor(() => {
            expect(screen.getByText('Extension not found')).toBeInTheDocument();
        });
    });
});

describe('ExtensionDetailPage interactions', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(mockAllExtensions);
            }
            return Promise.resolve([]);
        });
    });

    it('calls enable API when toggle is clicked to enable', async () => {
        const user = userEvent.setup();

        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve([mockDisabledManagedExtension]);
            }
            return Promise.resolve([]);
        });

        vi.mocked(apiPost).mockResolvedValue({ ...mockDisabledManagedExtension, enabled: true });

        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-prowlarr') });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /enable prowlarr/i })).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch', { name: /enable prowlarr/i });
        await user.click(toggle);

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/extensions/ext-prowlarr/enable');
        });
    });

    it('calls disable API when toggle is clicked to disable', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPost).mockResolvedValue({ ...mockManagedExtension, enabled: false });

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /disable qbittorrent/i })).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch', { name: /disable qbittorrent/i });
        await user.click(toggle);

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/extensions/ext-qbittorrent/disable');
        });
    });

    it('shows success toast when toggle succeeds', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPost).mockResolvedValue({ ...mockManagedExtension, enabled: false });

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /disable qbittorrent/i })).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch', { name: /disable qbittorrent/i });
        await user.click(toggle);

        await waitFor(() => {
            expect(screen.getByText('Extension Disabled')).toBeInTheDocument();
        });
    });

    it('shows error message when toggle fails', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPost).mockRejectedValue(new Error('Server error'));

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /disable qbittorrent/i })).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch', { name: /disable qbittorrent/i });
        await user.click(toggle);

        await waitFor(() => {
            expect(screen.getByText('Server error')).toBeInTheDocument();
        });
    });
});

describe('ExtensionDetailPage settings section', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('shows settings section when extension has settings groups', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve([mockManagedExtension]);
            }
            if (url === '/settings/groups') {
                return Promise.resolve([
                    {
                        id: 'sg-1',
                        name: 'Connection',
                        extensionId: 'ext-qbittorrent',
                        createdAt: '2024-01-01T00:00:00Z',
                        settings: [
                            {
                                id: 's-1',
                                settingsGroupId: 'sg-1',
                                key: 'Host',
                                value: 'localhost',
                                createdAt: '2024-01-01T00:00:00Z',
                            },
                        ],
                    },
                ]);
            }
            return Promise.resolve([]);
        });

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Settings')).toBeInTheDocument();
        });
    });

    it('renders setting input with value', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve([mockManagedExtension]);
            }
            if (url === '/settings/groups') {
                return Promise.resolve([
                    {
                        id: 'sg-1',
                        name: 'Connection',
                        extensionId: 'ext-qbittorrent',
                        createdAt: '2024-01-01T00:00:00Z',
                        settings: [
                            {
                                id: 's-1',
                                settingsGroupId: 'sg-1',
                                key: 'Host',
                                value: 'localhost',
                                createdAt: '2024-01-01T00:00:00Z',
                            },
                        ],
                    },
                ]);
            }
            return Promise.resolve([]);
        });

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByDisplayValue('localhost')).toBeInTheDocument();
        });

        const settingsSection = document.querySelector('[data-section="settings"]');
        expect(settingsSection).toHaveTextContent('Host');
    });

    it('does not show settings section when extension is disabled', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve([mockDisabledManagedExtension]);
            }
            return Promise.resolve([]);
        });

        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-prowlarr') });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Prowlarr', level: 1 })).toBeInTheDocument();
        });

        expect(screen.queryByText('Settings')).not.toBeInTheDocument();
    });

    it('renders Save Settings button when settings are present', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve([mockManagedExtension]);
            }
            if (url === '/settings/groups') {
                return Promise.resolve([
                    {
                        id: 'sg-1',
                        name: 'Connection',
                        extensionId: 'ext-qbittorrent',
                        createdAt: '2024-01-01T00:00:00Z',
                        settings: [
                            {
                                id: 's-1',
                                settingsGroupId: 'sg-1',
                                key: 'Host',
                                value: 'localhost',
                                createdAt: '2024-01-01T00:00:00Z',
                            },
                        ],
                    },
                ]);
            }
            return Promise.resolve([]);
        });

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const settingsSection = document.querySelector('[data-section="settings"]');
            expect(settingsSection).toHaveTextContent('Save Settings');
        });
    });
});
