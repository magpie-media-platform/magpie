import { LiveAnnouncerProvider } from '@/components/ui/LiveAnnouncer';
import { act, fireEvent, render, renderHook, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { RestartRequiredProvider } from '@/components/ui/RestartRequiredBanner';
import { apiGet, apiPatch, apiPost } from '@/lib/api';
import { useExtensionManagement } from '@/hooks/useExtensionManagement';
import type { ApiExtension, ExtensionUpdateInfo } from '@/hooks/useExtensionManagement';

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
        packageId: 'Magpie.Core',
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
        packageId: 'Magpie.Extensions.Episodes',
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
        packageId: 'Magpie.Extensions.Movies',
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
        lastChecked: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
        createdAt: '2024-01-10T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'ext-plex',
        packageId: 'Magpie.Extensions.Plex',
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
        lastChecked: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
        createdAt: '2024-01-05T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },

    {
        id: 'ext-downloadclient-a-v2',
        packageId: 'Magpie.Extensions.DownloadClientA',
        name: 'DownloadClient A',
        version: '2.0.0',
        targetABI: '1.0',
        filePath: '/extensions/downloadclient-a-2.0.0.dll',
        assemblyName: 'Magpie.Extensions.DownloadClientA, Version=2.0.0.0',
        installed: false,
        enabled: false,

        disabledReason: null,
        loadOrder: 100,
        managed: true,
        nuGetId: 'Magpie.Extensions.DownloadClientA',
        packageRepository: 'https://api.nuget.org/v3/index.json',
        autoUpgrade: false,
        lastChecked: null,
        createdAt: '2024-01-05T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
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
    },
    {
        id: 'ext-plex-v1',
        packageId: 'Magpie.Extensions.Plex',
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
    },
];

const listedExtensions = mockApiExtensions;
const listedExtensionItems = mockApiExtensions.map((extension) => ({ extension, updateInfo: null }));

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn().mockImplementation((url: string) => {
        if (url === '/extensions') {
            return Promise.resolve(listedExtensionItems);
        }
        return Promise.resolve([]);
    }),
    apiPost: vi.fn().mockResolvedValue({}),
    apiPatch: vi.fn().mockResolvedValue(undefined),
    apiDelete: vi.fn().mockResolvedValue(undefined),
    isApiError: vi.fn(
        (value: unknown) => typeof value === 'object' && value !== null && 'status' in value && 'message' in value,
    ),
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
            <LiveAnnouncerProvider>
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={initialEntries}>
                        <RestartRequiredProvider>
                            <ToastProvider>
                                <ToastStateProvider>
                                    {children}
                                    <ToastList />
                                    <ToastViewport />
                                </ToastStateProvider>
                            </ToastProvider>
                        </RestartRequiredProvider>
                    </MemoryRouter>
                </QueryClientProvider>
            </LiveAnnouncerProvider>
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

    it.each([
        ['Installed', ['Add extension', 'Check for updates']],
        ['NuGet Feeds', ['Add extension', 'Add feed']],
    ])('lists the %s tab actions in the Extension Actions sheet', async (tab, expected) => {
        const user = userEvent.setup();
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await user.click(await screen.findByRole('tab', { name: tab }));
        await user.click(screen.getByRole('button', { name: 'Extension Actions' }));

        const sheet = await screen.findByRole('dialog', { name: 'Extension Actions' });
        expect(
            within(within(sheet).getByRole('group', { name: 'Quick actions' }))
                .getAllByRole('button')
                .map((action) => action.textContent),
        ).toEqual(expected);
    });

    it('opens the Add Feed form from the Extension Actions sheet on the NuGet Feeds tab', async () => {
        const user = userEvent.setup();
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await user.click(await screen.findByRole('tab', { name: 'NuGet Feeds' }));
        await user.click(screen.getByRole('button', { name: 'Extension Actions' }));
        await user.click(
            within(await screen.findByRole('dialog', { name: 'Extension Actions' })).getByRole('button', {
                name: 'Add feed',
            }),
        );

        expect(await screen.findByRole('dialog', { name: /add.*feed/i })).toBeInTheDocument();
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
            expect(list.children).toHaveLength(10);
            expect(list.querySelector(':scope > :not([role="listitem"])')).toBeNull();

            const items = screen.getAllByRole('listitem');
            expect(items.length).toBe(listedExtensions.length);
        });

        for (const ext of listedExtensions) {
            expect(document.querySelector(`[data-magpie-extension-id="${ext.id}"]`)).toBeInTheDocument();
        }
    });

    it('renders duplicate extension names as separate rows', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const items = screen.getAllByRole('listitem');
            expect(items.length).toBe(listedExtensions.length);
        });

        const duplicateNamedExtensions = listedExtensions.filter((e) => e.name === 'DownloadClient A');
        expect(screen.getAllByText('DownloadClient A')).toHaveLength(duplicateNamedExtensions.length);
        for (const ext of duplicateNamedExtensions) {
            expect(document.querySelector(`[data-magpie-extension-id="${ext.id}"]`)).toBeInTheDocument();
        }
    });

    it('each card links to the extension detail page', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(listedExtensions.length);
        });

        for (const ext of listedExtensions) {
            const card = document.querySelector(`[data-magpie-extension-id="${ext.id}"]`);
            const link = card?.querySelector('a');
            expect(link).toHaveAttribute('href', `/admin/extensions/${ext.id}`);
        }
    });

    it('shows failed activation status and reason from API data', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(listedExtensions.length);
        });

        const card = document.querySelector('[data-magpie-extension-id="ext-broken"]');
        expect(card).toHaveTextContent('Failed');
        expect(card).toHaveTextContent('Activation failed: targets ABI [2.0.0,), host ABI is 1.0.0.');
        expect(card?.querySelector('[data-action="toggle-enabled"]')).not.toBeInTheDocument();
    });

    it('checks for updates by refreshing the installed list and shows refreshed availability', async () => {
        let extensionFetches = 0;
        const update: ExtensionUpdateInfo = {
            extensionId: 'ext-downloadclient-a',
            packageId: 'Magpie.Extensions.DownloadClientA',
            currentVersion: '2.1.0',
            latestVersion: '2.2.0',
            hasUpdate: true,
        };
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url !== '/extensions') {
                return Promise.resolve([]);
            }
            extensionFetches += 1;
            return Promise.resolve(
                mockApiExtensions.map((extension) => ({
                    extension,
                    updateInfo: extension.id === update.extensionId && extensionFetches > 1 ? update : null,
                })),
            );
        });

        render(<ExtensionsPage />, { wrapper: createWrapper() });
        await screen.findByRole('button', { name: 'Check for Updates' });

        fireEvent.click(screen.getByRole('button', { name: 'Check for Updates' }));

        await waitFor(() => expect(extensionFetches).toBeGreaterThanOrEqual(2));
        expect(await screen.findByTestId('extensions-page__card-update-badge')).toHaveTextContent('Update 2.2.0');
        expect(vi.mocked(apiPost)).not.toHaveBeenCalledWith('/extensions/check-updates');
        expect(await screen.findByText('1 extension has updates available.')).toBeInTheDocument();
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
                return Promise.resolve(listedExtensionItems);
            }
            return Promise.resolve([]);
        });
        vi.mocked(apiPost).mockResolvedValue({});
        vi.mocked(apiPatch).mockResolvedValue(undefined);
    });

    it('shows a readable error when the toggle request fails generically', async () => {
        // Arrange
        const user = userEvent.setup();
        vi.mocked(apiPost).mockRejectedValue({ status: 500, message: 'Request failed with status 500' });
        render(<ExtensionsPage />, { wrapper: createWrapper() });
        await screen.findByRole('list', { name: 'Installed extensions' });
        const card = document.querySelector('[data-magpie-extension-id="ext-downloadclient-a"]') as HTMLElement;
        // Act
        await user.click(within(card).getByRole('switch'));
        // Assert
        expect(await screen.findByText('The server request failed. Try again later.')).toBeInTheDocument();
        expect(within(card).getByRole('switch')).toBeEnabled();
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
            await result.current.toggleEnabled('ext-downloadclient-a', false);
        });

        expect(vi.mocked(apiPost)).toHaveBeenCalledWith('/extensions/ext-downloadclient-a/disable');
    });
});

describe('ExtensionsPage card anatomy (mobile collapse)', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('marks the drag handle and enable toggle for the mobile card layout', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(listedExtensions.length);
        });

        const enabledCard = document.querySelector('[data-magpie-extension-id="ext-prowlarr"]');
        expect(enabledCard?.querySelector('.extensions-page__drag-handle')).toBeInTheDocument();

        const toggle = enabledCard?.querySelector('.extensions-page__card-toggle');
        expect(toggle?.querySelector('.extensions-page__toggle-label')).toHaveTextContent('Enabled');
        expect(toggle?.querySelector('[role="switch"]')).toBeInTheDocument();
    });

    it('keeps the reorder buttons out of the enable toggle so they collapse into the bottom bar separately', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(listedExtensions.length);
        });

        const card = document.querySelector('[data-magpie-extension-id="ext-prowlarr"]');
        const orderControls = card?.querySelector('.extensions-page__order-controls');
        expect(orderControls).toBeInTheDocument();
        expect(orderControls?.querySelector('.extensions-page__card-toggle')).not.toBeInTheDocument();
        expect(
            card?.querySelector('.extensions-page__card-toggle .extensions-page__order-controls'),
        ).not.toBeInTheDocument();
    });
});

describe('ExtensionsPage load-order reordering', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(listedExtensionItems);
            }
            return Promise.resolve([]);
        });
        vi.mocked(apiPost).mockResolvedValue({});
    });

    function makeDataTransfer(payload = '') {
        const store: Record<string, string> = { 'text/plain': payload };
        return {
            effectAllowed: '',
            dropEffect: '',
            setDragImage: () => {},
            setData: (type: string, value: string) => {
                store[type] = value;
            },
            getData: (type: string) => store[type] ?? '',
        } as unknown as DataTransfer;
    }

    function cardFor(id: string): HTMLElement {
        const card = document.querySelector(`[data-magpie-extension-id="${id}"]`);
        if (!card) {
            throw new Error(`card ${id} not found`);
        }
        return card as HTMLElement;
    }

    it('renders cards sorted by load order descending (top is highest priority)', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(listedExtensions.length);
        });

        const cards = screen.getAllByRole('listitem');
        const firstId = cards[0].getAttribute('data-magpie-extension-id');
        const lastId = cards[cards.length - 1].getAttribute('data-magpie-extension-id');

        expect(firstId).toBe('ext-broken');
        expect(lastId).toBe('ext-core');
    });

    it('renders a drag handle on each card', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(listedExtensions.length);
        });

        const handles = screen.getAllByTestId('extensions-page__drag-handle');
        expect(handles.length).toBe(listedExtensions.length);
        for (const handle of handles) {
            expect(handle).toHaveAttribute('draggable', 'true');
        }
        for (const card of screen.getAllByRole('listitem')) {
            expect(card).not.toHaveAttribute('draggable');
        }
    });

    it('moves a card up with the keyboard, announces its new position, and retains focus', async () => {
        let persistedItems = listedExtensionItems.map((item) => ({
            ...item,
            extension: { ...item.extension },
        }));
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(persistedItems);
            }
            return Promise.resolve([]);
        });
        vi.mocked(apiPatch).mockImplementation((url: string, body?: unknown) => {
            const id = url.split('/').at(-1);
            const loadOrder = (body as { loadOrder: { value: number } }).loadOrder.value;
            persistedItems = persistedItems.map((item) =>
                item.extension.id === id ? { ...item, extension: { ...item.extension, loadOrder } } : item,
            );
            return Promise.resolve(undefined);
        });

        const user = userEvent.setup();
        const firstRender = render(<ExtensionsPage />, { wrapper: createWrapper() });

        const moveUp = await screen.findByRole('button', { name: 'Move Magpie Core up' });
        moveUp.focus();
        await user.keyboard('{Enter}');

        await waitFor(() => {
            expect(vi.mocked(apiPatch)).toHaveBeenCalledWith('/extensions/ext-core', {
                loadOrder: { value: 10 },
            });
            expect(vi.mocked(apiPatch)).toHaveBeenCalledWith('/extensions/ext-episodes', {
                loadOrder: { value: 1 },
            });
        });
        expect(document.querySelector('[data-component="live-announcer"][aria-live="polite"]')).toHaveTextContent(
            `Magpie Core moved to position ${listedExtensions.length - 1} of ${listedExtensions.length}.`,
        );
        await waitFor(() => {
            expect(moveUp).toHaveFocus();
        });

        firstRender.unmount();
        render(<ExtensionsPage />, { wrapper: createWrapper() });
        await waitFor(() => {
            const reloadedIds = screen
                .getAllByRole('listitem')
                .map((card) => card.getAttribute('data-magpie-extension-id'));
            expect(reloadedIds.indexOf('ext-core')).toBe(reloadedIds.indexOf('ext-episodes') - 1);
        });
    });

    it('moves focus to the sibling control when a keyboard move reaches a boundary', async () => {
        let moveCompleted = false;
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url !== '/extensions') {
                return Promise.resolve([]);
            }

            return Promise.resolve(
                listedExtensionItems.map((item) =>
                    item.extension.id === 'ext-prowlarr' && moveCompleted
                        ? { ...item, extension: { ...item.extension, loadOrder: 201 } }
                        : item,
                ),
            );
        });
        vi.mocked(apiPatch).mockImplementation(() => {
            moveCompleted = true;
            return Promise.resolve(undefined);
        });

        const user = userEvent.setup();
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        const moveUp = await screen.findByRole('button', { name: 'Move Prowlarr up' });
        const moveDown = screen.getByRole('button', { name: 'Move Prowlarr down' });
        moveUp.focus();
        await user.keyboard('{Enter}');

        await waitFor(() => {
            expect(moveUp).toBeDisabled();
            expect(moveDown).toHaveFocus();
        });
    });

    it('disables keyboard moves beyond the first and last positions', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem')).toHaveLength(listedExtensions.length);
        });

        expect(screen.getByRole('button', { name: 'Move Legacy Extension up' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Move Magpie Core down' })).toBeDisabled();
    });

    it('moves a card up via drag-and-drop and persists the affected load orders', async () => {
        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(listedExtensions.length);
        });

        const source = within(cardFor('ext-core')).getByTestId('extensions-page__drag-handle');
        const target = cardFor('ext-movies');
        const dataTransfer = makeDataTransfer();

        fireEvent.dragStart(source, { dataTransfer });
        fireEvent.dragOver(target, { dataTransfer });
        fireEvent.drop(target, { dataTransfer });

        await waitFor(() => {
            expect(vi.mocked(apiPatch)).toHaveBeenCalledWith('/extensions/ext-core', {
                loadOrder: { value: 10 },
            });
            expect(vi.mocked(apiPatch)).toHaveBeenCalledWith('/extensions/ext-episodes', {
                loadOrder: { value: 1 },
            });
        });
        expect(vi.mocked(apiPatch)).toHaveBeenCalledTimes(2);
    });

    it('moves a card down via drag-and-drop and persists the affected load orders', async () => {
        let persistedItems = listedExtensionItems.map((item) => ({
            ...item,
            extension: { ...item.extension },
        }));
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(persistedItems);
            }
            return Promise.resolve([]);
        });
        vi.mocked(apiPatch).mockImplementation((url: string, body?: unknown) => {
            const id = url.split('/').at(-1);
            const loadOrder = (body as { loadOrder: { value: number } }).loadOrder.value;
            persistedItems = persistedItems.map((item) =>
                item.extension.id === id ? { ...item, extension: { ...item.extension, loadOrder } } : item,
            );
            return Promise.resolve(undefined);
        });

        const firstRender = render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(listedExtensions.length);
        });

        const source = within(cardFor('ext-movies')).getByTestId('extensions-page__drag-handle');
        const target = cardFor('ext-episodes');
        const dataTransfer = makeDataTransfer();

        fireEvent.dragStart(source, { dataTransfer });
        fireEvent.dragOver(target, { dataTransfer });
        fireEvent.drop(target, { dataTransfer });

        await waitFor(() => {
            expect(vi.mocked(apiPatch)).toHaveBeenCalledWith('/extensions/ext-movies', {
                loadOrder: { value: 10 },
            });
            expect(vi.mocked(apiPatch)).toHaveBeenCalledWith('/extensions/ext-episodes', {
                loadOrder: { value: 11 },
            });
        });
        expect(vi.mocked(apiPatch)).toHaveBeenCalledTimes(2);

        firstRender.unmount();
        render(<ExtensionsPage />, { wrapper: createWrapper() });
        await waitFor(() => {
            const reloadedIds = screen
                .getAllByRole('listitem')
                .map((card) => card.getAttribute('data-magpie-extension-id'));
            expect(reloadedIds.indexOf('ext-movies')).toBe(reloadedIds.indexOf('ext-episodes') + 1);
        });
    });
});

describe('ExtensionsPage tie-break mirrors the startup gate', () => {
    function buildTiedRow(id: string, packageId: string): ApiExtension {
        return {
            id,
            packageId,
            name: id,
            version: '1.0.0',
            targetABI: '1.0',
            filePath: `/extensions/${id}.dll`,
            assemblyName: packageId,
            installed: true,
            enabled: true,

            disabledReason: null,
            loadOrder: 1000,
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
    }

    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiPost).mockResolvedValue({});
    });

    it('renders ties in the exact reverse of the gate order (package id descending)', async () => {
        const tied = [
            buildTiedRow('ext-b', 'Magpie.B'),
            buildTiedRow('ext-a', 'Magpie.A'),
            buildTiedRow('ext-c', 'Magpie.C'),
        ];
        const gateAscending = [...tied].sort((a, b) =>
            a.packageId.toUpperCase() < b.packageId.toUpperCase()
                ? -1
                : a.packageId.toUpperCase() > b.packageId.toUpperCase()
                  ? 1
                  : 0,
        );

        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/extensions') {
                return Promise.resolve(tied.map((extension) => ({ extension, updateInfo: null })));
            }
            return Promise.resolve([]);
        });

        render(<ExtensionsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('listitem').length).toBe(tied.length);
        });

        const renderedIds = screen
            .getAllByRole('listitem')
            .map((card) => card.getAttribute('data-magpie-extension-id'));
        const expectedTopDown = [...gateAscending].reverse().map((e) => e.id);

        expect(renderedIds).toEqual(expectedTopDown);
        expect(renderedIds).toEqual(['ext-c', 'ext-b', 'ext-a']);
    });
});
