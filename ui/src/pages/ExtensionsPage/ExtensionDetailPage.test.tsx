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

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn().mockResolvedValue({}),
    apiPatch: vi.fn().mockResolvedValue(undefined),
    apiFetchResponse: vi.fn().mockResolvedValue(new Response(null, { status: 204 })),
    isApiError: vi.fn(() => false),
    buildUrl: vi.fn((path: string) => path),
}));

import { apiFetchResponse, apiGet, apiPost, apiPatch } from '@/lib/api';

const mockManagedExtension: ApiExtension = {
    id: 'ext-downloadclient-a',
    packageId: 'Magpie.Extensions.DownloadClientA',
    name: 'DownloadClient A',
    version: '2.1.0',
    targetABI: '1.0',
    filePath: '/extensions/downloadclient-a.dll',
    assemblyName: 'Magpie.Extensions.DownloadClientA',
    installed: true,
    enabled: true,
    disabledReason: null,
    loadOrder: 100,
    managed: true,
    nuGetId: 'Magpie.Extensions.DownloadClientA',
    packageRepository: 'https://api.nuget.org/v3/index.json',
    autoUpgrade: false,
    lastChecked: '2024-01-15T10:00:00Z',
    createdAt: '2024-01-10T00:00:00Z',
    createdByUserId: null,
    modifiedAt: null,
    modifiedByUserId: null,
};

const mockManagedExtensionOldVersion: ApiExtension = {
    id: 'ext-downloadclient-a-v1',
    packageId: 'Magpie.Extensions.DownloadClientA',
    name: 'DownloadClient A',
    version: '1.0.0',
    targetABI: '1.0',
    filePath: '/extensions/downloadclient-a-1.0.0.dll',
    assemblyName: 'Magpie.Extensions.DownloadClientA, Version=1.0.0.0',
    installed: false,
    enabled: false,
    disabledReason: null,
    loadOrder: 100,
    managed: true,
    nuGetId: 'Magpie.Extensions.DownloadClientA',
    packageRepository: 'https://api.nuget.org/v3/index.json',
    autoUpgrade: false,
    lastChecked: null,
    createdAt: '2023-11-15T00:00:00Z',
    createdByUserId: null,
    modifiedAt: null,
    modifiedByUserId: null,
};

const mockLocalExtension: ApiExtension = {
    id: 'ext-local',
    packageId: 'Magpie.Extensions.Local',
    name: 'Local Extension',
    version: '1.0.0',
    targetABI: '1.0',
    filePath: '/extensions/local.dll',
    assemblyName: 'Magpie.Extensions.Local',
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
    packageId: 'Legacy.Extension',
    name: 'Legacy Extension',
    version: '0.5.0',
    targetABI: '0.9',
    filePath: '/extensions/legacy.dll',
    assemblyName: 'Legacy.Extension',
    installed: true,
    enabled: false,
    disabledReason: 'Activation failed: targets ABI [2.0.0,), host ABI is 1.0.0.',
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
    packageId: 'Magpie.Extensions.Prowlarr',
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
    mockLocalExtension,
    mockDisabledExtension,
    mockDisabledManagedExtension,
];

function toExtensionListItems(extensions: ApiExtension[]) {
    return extensions.map((extension) => ({ extension, updateInfo: null }));
}

function createWrapper(initialPath: string = '/admin/extensions/ext-downloadclient-a', queryClient?: QueryClient) {
    const client =
        queryClient ??
        new QueryClient({
            defaultOptions: {
                queries: { retry: false, gcTime: 0 },
                mutations: { retry: false },
            },
        });

    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={client}>
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
                return Promise.resolve(toExtensionListItems(mockAllExtensions));
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
            expect(screen.getByRole('heading', { name: 'DownloadClient A', level: 1 })).toBeInTheDocument();
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
            expect(nav).toHaveTextContent('DownloadClient A');
        });
    });

    it('renders Enabled status badge for enabled extension', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Enabled')).toBeInTheDocument();
        });
    });

    it('renders Failed status badge for activation failures', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-broken') });

        await waitFor(() => {
            expect(screen.getByText('Failed')).toBeInTheDocument();
        });
    });

    it('renders Managed badge for managed extensions', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Managed')).toBeInTheDocument();
        });
    });

    it('renders enable/disable toggle for an installed extension', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const toggle = screen.getByRole('switch', { name: /disable downloadclient a/i });
            expect(toggle).toBeInTheDocument();
        });
    });

    it('renders overview section with file path', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('/extensions/downloadclient-a.dll')).toBeInTheDocument();
        });
    });

    it('renders overview section with NuGet package ID', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const overviewSection = document.querySelector('[data-section="overview"]');
            expect(overviewSection).toHaveTextContent('Magpie.Extensions.DownloadClientA');
        });
    });

    it('renders Package Management section for managed extensions', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Package Management')).toBeInTheDocument();
        });
    });

    it('renders only the uninstall management control for unmanaged extensions', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-local') });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Local Extension', level: 1 })).toBeInTheDocument();
        });

        expect(screen.getByText('Package Management')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Uninstall Extension' })).toBeInTheDocument();
        expect(screen.queryByRole('switch', { name: /toggle auto-upgrade/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /update now/i })).not.toBeInTheDocument();
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
            expect(screen.getByText('Activation failed: targets ABI [2.0.0,), host ABI is 1.0.0.')).toBeInTheDocument();
        });
    });

    it('shows Retry button in error banner', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-broken') });

        await waitFor(() => {
            expect(screen.getByText('Activation failed: targets ABI [2.0.0,), host ABI is 1.0.0.')).toBeInTheDocument();
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

    it('shows version history empty state when no older versions exist', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-local') });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Local Extension', level: 1 })).toBeInTheDocument();
        });

        expect(screen.getByText('Version History')).toBeInTheDocument();
        expect(screen.getByText('This is the only installed version.')).toBeInTheDocument();
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
        vi.mocked(apiGet).mockResolvedValue(toExtensionListItems([mockManagedExtension]));

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
                return Promise.resolve(toExtensionListItems(mockAllExtensions));
            }
            return Promise.resolve([]);
        });
    });

    it('calls enable API when toggle is clicked to enable', async () => {
        const user = userEvent.setup();

        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(toExtensionListItems([mockDisabledManagedExtension]));
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
            expect(screen.getByRole('switch', { name: /disable downloadclient a/i })).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch', { name: /disable downloadclient a/i });
        await user.click(toggle);

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/extensions/ext-downloadclient-a/disable');
        });
    });

    it('shows success toast when toggle succeeds', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPost).mockResolvedValue({ ...mockManagedExtension, enabled: false });

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /disable downloadclient a/i })).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch', { name: /disable downloadclient a/i });
        await user.click(toggle);

        await waitFor(() => {
            expect(screen.getByText('Extension Disabled')).toBeInTheDocument();
        });
        expect(screen.getByText('DownloadClient A will be disabled after the next restart.')).toBeInTheDocument();
    });

    it('shows a persistent restart hint next to the enable/disable toggle', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByTestId('extension-toggle-hint')).toBeInTheDocument();
        });
        expect(screen.getByTestId('extension-toggle-hint')).toHaveTextContent(/takes effect after restart/i);
    });

    it('shows error message when toggle fails', async () => {
        const user = userEvent.setup();

        vi.mocked(apiPost).mockRejectedValue(new Error('Server error'));

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: /disable downloadclient a/i })).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch', { name: /disable downloadclient a/i });
        await user.click(toggle);

        await waitFor(() => {
            expect(screen.getByText('Server error')).toBeInTheDocument();
        });
    });

    it('does not uninstall a managed extension until the confirmation is accepted', async () => {
        const user = userEvent.setup();

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await user.click(await screen.findByRole('button', { name: 'Uninstall Extension' }));

        expect(apiFetchResponse).not.toHaveBeenCalled();
        expect(screen.getByRole('dialog', { name: 'Uninstall Extension' })).toHaveTextContent(
            'Uninstall "DownloadClient A"? Removal takes effect after Magpie restarts.',
        );

        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(apiFetchResponse).not.toHaveBeenCalled();
        expect(screen.queryByRole('dialog', { name: 'Uninstall Extension' })).not.toBeInTheDocument();
    });

    it('uninstalls an unmanaged extension through the same confirmation', async () => {
        const user = userEvent.setup();

        render(<ExtensionDetailPage />, { wrapper: createWrapper('/admin/extensions/ext-local') });

        await user.click(await screen.findByRole('button', { name: 'Uninstall Extension' }));

        expect(apiFetchResponse).not.toHaveBeenCalled();
        expect(screen.getByRole('dialog', { name: 'Uninstall Extension' })).toHaveTextContent(
            'Uninstall "Local Extension"? Removal takes effect after Magpie restarts.',
        );

        await user.click(screen.getByRole('button', { name: 'Uninstall' }));

        await waitFor(() => {
            expect(apiFetchResponse).toHaveBeenCalledWith('/extensions/ext-local', { method: 'DELETE' });
        });
    });
});

describe('ExtensionDetailPage extension settings UI', () => {
    const flareGroup = {
        id: 'group-flaresolverr',
        name: 'flaresolverr',
        extensionId: mockManagedExtension.id,
        createdAt: '2024-01-10T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
        settings: [],
        secretSettings: {},
        schema: [
            {
                key: 'base-url',
                type: 'String',
                displayName: 'FlareSolverr URL',
                description: 'Base URL of the FlareSolverr instance.',
                placeholder: 'http://localhost:8191',
            },
            {
                key: 'timeout-ms',
                type: 'Number',
                displayName: 'Solve timeout (ms)',
                defaultValue: '60000',
            },
        ],
        canTest: true,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(toExtensionListItems([mockManagedExtension]));
            }
            if (url === '/settings/groups') {
                return Promise.resolve([flareGroup]);
            }
            return Promise.resolve([]);
        });
    });

    it('renders a schema-driven settings form for the extension group', async () => {
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Settings' })).toBeInTheDocument();
        });

        expect(screen.getByText('FlareSolverr URL')).toBeInTheDocument();
        expect(screen.getByText('Solve timeout (ms)')).toBeInTheDocument();
        expect(document.querySelector('[data-field="base-url"]')).toBeInTheDocument();
    });

    it('saves settings via the extension settings group endpoint', async () => {
        const user = userEvent.setup();
        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Settings' })).toBeInTheDocument();
        });

        const urlField = document.querySelector('[data-field="base-url"]') as HTMLInputElement;
        await user.clear(urlField);
        await user.type(urlField, 'http://flare.local:8191');
        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(apiPatch).toHaveBeenCalledWith(
                `/settings/groups/${mockManagedExtension.id}/flaresolverr`,
                expect.objectContaining({
                    settings: expect.objectContaining({
                        add: expect.objectContaining({ 'base-url': 'http://flare.local:8191' }),
                    }),
                }),
            );
        });
    });

    it('runs the connection test via the test endpoint and surfaces the result', async () => {
        const user = userEvent.setup();
        vi.mocked(apiPost).mockResolvedValue({ success: true, message: 'Connected to FlareSolverr.' });

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Settings' })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /test/i }));

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith(
                `/settings/groups/${mockManagedExtension.id}/flaresolverr/test`,
                expect.objectContaining({ settings: expect.any(Object) }),
            );
        });
        expect(await screen.findByText('Connection Succeeded')).toBeInTheDocument();
    });

    it('hides the Test action when the settings group cannot be tested', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(toExtensionListItems([mockManagedExtension]));
            }
            if (url === '/settings/groups') {
                return Promise.resolve([{ ...flareGroup, canTest: false }]);
            }
            return Promise.resolve([]);
        });

        render(<ExtensionDetailPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Settings' })).toBeInTheDocument();
        });

        expect(screen.queryByRole('button', { name: /test/i })).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
    });
});
