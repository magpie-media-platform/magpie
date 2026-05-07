import { act, render, renderHook, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { RestartRequiredProvider } from '@/components/ui/RestartRequiredBanner';
import { apiGet, apiPost } from '@/lib/api';
import { useExtensionManagement } from '@/hooks/useExtensionManagement';
import type { ApiExtension } from '@/hooks/useExtensionManagement';

import { ExtensionsPage } from './ExtensionsPage';

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

const mockApiExtensions: ApiExtension[] = [
    {
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
    },
    {
        id: 'ext-episodes',
        name: 'Episodes',
        version: '1.0.0',
        targetABI: '1.0',
        filePath: '/extensions/episodes.dll',
        assemblyName: 'Magpie.Extensions.Episodes',
        installed: true,
        enabled: true,
        disabledReason: null,
        loadOrder: 10,
        managed: false,
        nuGetId: null,
        packageRepository: null,
        autoUpgrade: false,
        lastChecked: null,
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'ext-movies',
        name: 'Movies',
        version: '1.0.0',
        targetABI: '1.0',
        filePath: '/extensions/movies.dll',
        assemblyName: 'Magpie.Extensions.Movies',
        installed: true,
        enabled: true,
        disabledReason: null,
        loadOrder: 11,
        managed: false,
        nuGetId: null,
        packageRepository: null,
        autoUpgrade: false,
        lastChecked: null,
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
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
        lastChecked: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
        createdAt: '2024-01-10T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'ext-plex',
        name: 'Plex',
        version: '1.5.0',
        targetABI: '1.0',
        filePath: '/extensions/plex.dll',
        assemblyName: 'Magpie.Extensions.Plex',
        installed: true,
        enabled: true,
        disabledReason: null,
        loadOrder: 101,
        managed: true,
        nuGetId: 'Magpie.Extensions.Plex',
        packageRepository: 'https://api.nuget.org/v3/index.json',
        autoUpgrade: false,
        lastChecked: new Date(Date.now() - 1 * 60 * 60 * 1000).toISOString(),
        createdAt: '2024-01-08T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
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
        lastChecked: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
        createdAt: '2024-01-05T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },

    {
        id: 'ext-qbittorrent-v2',
        name: 'qBittorrent',
        version: '2.0.0',
        targetABI: '1.0',
        filePath: '/extensions/qbittorrent-2.0.0.dll',
        assemblyName: 'Magpie.Extensions.qBittorrent, Version=2.0.0.0',
        installed: false,
        enabled: false,
        disabledReason: null,
        loadOrder: 100,
        managed: true,
        nuGetId: 'Magpie.Extensions.qBittorrent',
        packageRepository: 'https://api.nuget.org/v3/index.json',
        autoUpgrade: false,
        lastChecked: null,
        createdAt: '2024-01-05T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
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
    },
    {
        id: 'ext-plex-v1',
        name: 'Plex',
        version: '1.0.0',
        targetABI: '1.0',
        filePath: '/extensions/plex-1.0.0.dll',
        assemblyName: 'Magpie.Extensions.Plex, Version=1.0.0.0',
        installed: false,
        enabled: false,
        disabledReason: null,
        loadOrder: 101,
        managed: true,
        nuGetId: 'Magpie.Extensions.Plex',
        packageRepository: 'https://api.nuget.org/v3/index.json',
        autoUpgrade: false,
        lastChecked: null,
        createdAt: '2023-12-20T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'ext-broken',
        name: 'Legacy Extension',
        version: '0.5.0',
        targetABI: '0.9',
        filePath: '/extensions/legacy.dll',
        assemblyName: 'Legacy.Extension',
        installed: true,
        enabled: false,
        disabledReason: 'Incompatible with current ABI version (requires 0.9, current is 1.0)',
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
    },
];

const installedExtensions = (() => {
    const byName = new Map<string, ApiExtension>();
    for (const ext of mockApiExtensions) {
        const existing = byName.get(ext.name);
        if (!existing || new Date(ext.createdAt) > new Date(existing.createdAt)) {
            byName.set(ext.name, ext);
        }
    }
    return [...byName.values()];
})();

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn().mockImplementation((url: string) => {
        if (url === '/extensions') {
            return Promise.resolve(mockApiExtensions);
        }
        return Promise.resolve([]);
    }),
    apiPost: vi.fn().mockResolvedValue({}),
    apiPatch: vi.fn().mockResolvedValue(undefined),
    isApiError: vi.fn(() => false),
    buildUrl: vi.fn((path, params) => {
        if (!params) return path;
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        const queryString = searchParams.toString();
        return queryString ? `${path}?${queryString}` : path;
    }),
}));

function createWrapper(initialEntries?: string[]) {
    const queryClient = new QueryClient({
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

    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter initialEntries={initialEntries}>
                    <RestartRequiredProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </RestartRequiredProvider>
                </MemoryRouter>
            </QueryClientProvider>
        );
    };
}

describe('ExtensionsPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders with correct data-page attribute', () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        expect(document.querySelector('[data-page="extensions"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        expect(screen.getByRole('heading', { name: 'Extensions', level: 1 })).toBeInTheDocument();
    });

    it('renders the page icon as decorative', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const icon = screen.getByTestId('extensions-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('renders extension list items from API data', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /installed extensions/i });
            expect(list).toBeInTheDocument();

            const items = screen.getAllByRole('listitem');
            expect(items.length).toBe(installedExtensions.length);
        });

        for (const ext of installedExtensions) {
            expect(screen.getByText(ext.name)).toBeInTheDocument();
        }
    });

    it('excludes prior versions from the list', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const items = screen.getAllByRole('listitem');
            expect(items.length).toBe(installedExtensions.length);
        });

        const currentIds = new Set(installedExtensions.map((e) => e.id));
        const priorVersions = mockApiExtensions.filter((e) => !currentIds.has(e.id));
        for (const ext of priorVersions) {
            expect(document.querySelector(`[data-magpie-extension-id="${ext.id}"]`)).not.toBeInTheDocument();
        }
    });

    it('renders summary badges with correct counts', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(installedExtensions.length);
        });

        const summary = screen.getByLabelText('Extension status summary');
        expect(summary).toBeInTheDocument();

        const enabled = installedExtensions.filter((e) => e.installed && e.enabled && !e.disabledReason);
        const disabled = installedExtensions.filter((e) => e.installed && !e.enabled && !e.disabledReason);
        const errored = installedExtensions.filter((e) => e.installed && !!e.disabledReason);

        expect(summary).toHaveTextContent(`${enabled.length}`);
        expect(summary).toHaveTextContent('Enabled');
        expect(summary).toHaveTextContent(`${disabled.length}`);
        expect(summary).toHaveTextContent('Disabled');
        expect(summary).toHaveTextContent(`${errored.length}`);
        expect(summary).toHaveTextContent('Error');
    });

    it('each card links to the extension detail page', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(installedExtensions.length);
        });

        for (const ext of installedExtensions) {
            const link = screen.getByRole('link', { name: new RegExp(ext.name) });
            expect(link).toHaveAttribute('href', `/admin/extensions/${ext.id}`);
        }
    });
});

describe('ExtensionsPage full-page error state', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('shows error state when API fails', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.resolve([]);
        });

        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Failed to load extensions')).toBeInTheDocument();
        });
    });

    it('shows error message from API', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.reject(new Error('Server unavailable'));
            }
            return Promise.resolve([]);
        });

        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Server unavailable')).toBeInTheDocument();
        });
    });

    it('shows retry button in error state', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.resolve([]);
        });

        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
        });
    });

    it('still shows page title in error state', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.resolve([]);
        });

        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Extensions', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders error state inside the admin slot wrapper', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.resolve([]);
        });

        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Failed to load extensions')).toBeInTheDocument();
        });

        const slotWrapper = document.querySelector('[data-slot="admin.extensions"]');
        expect(slotWrapper).toBeInTheDocument();
        expect(slotWrapper!.querySelector('[data-component="empty-state"]')).toBeInTheDocument();
    });
});

describe('ExtensionsPage enable/disable toggle', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(mockApiExtensions);
            }
            return Promise.resolve([]);
        });
        vi.mocked(apiPost).mockResolvedValue({});
    });

    it('calls POST /extensions/{id}/enable when enabling', async () => {
        const wrapper = createWrapper();
        const { result } = renderHook(() => useExtensionManagement(), { wrapper });

        await waitFor(() => {
            expect(result.current.extensions.length).toBeGreaterThan(0);
        });

        await act(async () => {
            await result.current.toggleEnabled('ext-prowlarr', true);
        });

        expect(vi.mocked(apiPost)).toHaveBeenCalledWith('/extensions/ext-prowlarr/enable');
    });

    it('calls POST /extensions/{id}/disable when disabling', async () => {
        const wrapper = createWrapper();
        const { result } = renderHook(() => useExtensionManagement(), { wrapper });

        await waitFor(() => {
            expect(result.current.extensions.length).toBeGreaterThan(0);
        });

        await act(async () => {
            await result.current.toggleEnabled('ext-qbittorrent', false);
        });

        expect(vi.mocked(apiPost)).toHaveBeenCalledWith('/extensions/ext-qbittorrent/disable');
    });
});
