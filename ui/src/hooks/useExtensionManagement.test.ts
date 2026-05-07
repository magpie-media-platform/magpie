import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
}));

import { apiGet, apiPost, apiPatch } from '@/lib/api';
import { RestartRequiredProvider } from '@/components/ui/RestartRequiredBanner';
import { useExtensionManagement, useExtensionActions, type ApiExtension } from './useExtensionManagement';

const mockExtension: ApiExtension = {
    id: 'ext-1',
    version: '1.0.0',
    name: 'TestExtension',
    filePath: '/extensions/test',
    assemblyName: 'Test.Extension',
    installed: true,
    enabled: true,
    loadOrder: 100,
    managed: true,
    autoUpgrade: false,
    createdAt: '2024-01-01T00:00:00Z',
};

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
    return ({ children }: { children: React.ReactNode }) =>
        createElement(
            QueryClientProvider,
            { client: queryClient },
            createElement(RestartRequiredProvider, null, children),
        );
}

describe('useExtensionManagement', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching extensions', () => {
        it('should fetch extensions from the API', async () => {
            mockApiGet.mockResolvedValue([mockExtension]);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/extensions');
        });

        it('should transform extensions to ExtensionItem format', async () => {
            mockApiGet.mockResolvedValue([mockExtension]);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.extensions).toHaveLength(1);
            const ext = result.current.extensions[0];
            expect(ext.id).toBe('ext-1');
            expect(ext.name).toBe('TestExtension');
            expect(ext.version).toBe('1.0.0');
            expect(ext.enabled).toBe(true);
            expect(ext.managed).toBe(true);
            // Phantom fields that used to be fabricated must not be present.
            expect('updateAvailable' in ext).toBe(false);
            expect('availableVersion' in ext).toBe(false);
            expect('hasSettings' in ext).toBe(false);
        });

        it('should surface author and description from the API extension', async () => {
            const apiExt: ApiExtension = {
                ...mockExtension,
                author: 'Author Name',
                description: 'Sample description',
            };
            mockApiGet.mockResolvedValue([apiExt]);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const ext = result.current.extensions[0];
            expect(ext.author).toBe('Author Name');
            expect(ext.description).toBe('Sample description');
        });

        it('should default author and description to null when API omits them', async () => {
            mockApiGet.mockResolvedValue([mockExtension]);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const ext = result.current.extensions[0];
            expect(ext.author).toBeNull();
            expect(ext.description).toBeNull();
        });

        it('should return empty array when API returns empty list', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.extensions).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });

        it('should return empty array and error on fetch failure', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.extensions).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });
    });

    describe('version deduplication via Map', () => {
        it('should keep only the newest extension when multiple share the same name', async () => {
            const older: ApiExtension = {
                ...mockExtension,
                id: 'ext-old',
                version: '1.0.0',
                createdAt: '2024-01-01T00:00:00Z',
            };
            const newer: ApiExtension = {
                ...mockExtension,
                id: 'ext-new',
                version: '2.0.0',
                createdAt: '2024-06-01T00:00:00Z',
            };

            mockApiGet.mockResolvedValue([older, newer]);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.extensions).toHaveLength(1);
            expect(result.current.extensions[0].id).toBe('ext-new');
            expect(result.current.extensions[0].version).toBe('2.0.0');
        });

        it('should prefer the newer entry when order is reversed in the list', async () => {
            const older: ApiExtension = {
                ...mockExtension,
                id: 'ext-old',
                version: '1.0.0',
                createdAt: '2024-01-01T00:00:00Z',
            };
            const newer: ApiExtension = {
                ...mockExtension,
                id: 'ext-new',
                version: '2.0.0',
                createdAt: '2024-06-01T00:00:00Z',
            };

            mockApiGet.mockResolvedValue([newer, older]);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.extensions).toHaveLength(1);
            expect(result.current.extensions[0].id).toBe('ext-new');
        });

        it('should keep extensions with different names as separate entries', async () => {
            const extA: ApiExtension = { ...mockExtension, id: 'ext-a', name: 'ExtensionA' };
            const extB: ApiExtension = { ...mockExtension, id: 'ext-b', name: 'ExtensionB' };

            mockApiGet.mockResolvedValue([extA, extB]);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.extensions).toHaveLength(2);
        });
    });

    describe('enable / disable', () => {
        it('should call the enable endpoint when toggling on', async () => {
            mockApiGet.mockResolvedValue([mockExtension]);
            mockApiPost.mockResolvedValue({ ...mockExtension, enabled: true });

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleEnabled('ext-1', true);
            });

            expect(mockApiPost).toHaveBeenCalledWith('/extensions/ext-1/enable');
        });

        it('should call the disable endpoint when toggling off', async () => {
            mockApiGet.mockResolvedValue([mockExtension]);
            mockApiPost.mockResolvedValue({ ...mockExtension, enabled: false });

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleEnabled('ext-1', false);
            });

            expect(mockApiPost).toHaveBeenCalledWith('/extensions/ext-1/disable');
        });

        it('should set isToggling while the mutation is pending', async () => {
            mockApiGet.mockResolvedValue([mockExtension]);

            let resolveEnable: (value: ApiExtension) => void;
            const enablePromise = new Promise<ApiExtension>((resolve) => {
                resolveEnable = resolve;
            });
            mockApiPost.mockReturnValue(enablePromise);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.toggleEnabled('ext-1', true);
            });

            await waitFor(() => {
                expect(result.current.isToggling).toBe(true);
            });

            act(() => {
                resolveEnable!({ ...mockExtension, enabled: true });
            });

            await waitFor(() => {
                expect(result.current.isToggling).toBe(false);
            });
        });
    });

    describe('patch via setAutoUpgrade', () => {
        it('should call the patch endpoint with autoUpgrade wrapped in a value object', async () => {
            mockApiGet.mockResolvedValue([mockExtension]);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.setAutoUpgrade('ext-1', true);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/extensions/ext-1', { autoUpgrade: { value: true } });
        });

        it('should set isPatching while the mutation is pending', async () => {
            mockApiGet.mockResolvedValue([mockExtension]);

            let resolvePatch: (value: void) => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useExtensionManagement(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.setAutoUpgrade('ext-1', false);
            });

            await waitFor(() => {
                expect(result.current.isPatching).toBe(true);
            });

            act(() => {
                resolvePatch!();
            });

            await waitFor(() => {
                expect(result.current.isPatching).toBe(false);
            });
        });
    });
});

describe('useExtensionActions', () => {
    const mockApiPost = vi.mocked(apiPost);
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should patch extension settings', async () => {
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useExtensionActions(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.patchExtension('ext-1', { autoUpgrade: { value: true } });
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/extensions/ext-1', { autoUpgrade: { value: true } });
    });

    it('installs an extension and surfaces the restart-required hint', async () => {
        mockApiPost.mockResolvedValue({ extension: mockExtension, restartRequired: true });

        const { result } = renderHook(() => useExtensionActions(), {
            wrapper: createWrapper(),
        });

        let response: { extension: ApiExtension; restartRequired: boolean } | undefined;
        await act(async () => {
            response = await result.current.installExtension({ packageId: 'Magpie.Foo', version: '1.2.3' });
        });

        expect(mockApiPost).toHaveBeenCalledWith('/extensions/install', {
            packageId: 'Magpie.Foo',
            version: '1.2.3',
        });
        expect(response).toEqual({ extension: mockExtension, restartRequired: true });
    });

    it('installs an extension from an uploaded file via FormData', async () => {
        mockApiPost.mockResolvedValue({ extension: mockExtension, restartRequired: true });

        const { result } = renderHook(() => useExtensionActions(), {
            wrapper: createWrapper(),
        });

        const file = new File(['fake'], 'extension.nupkg', { type: 'application/octet-stream' });

        await act(async () => {
            await result.current.installExtensionFromFile(file);
        });

        expect(mockApiPost).toHaveBeenCalledTimes(1);
        const [url, body] = mockApiPost.mock.calls[0];
        expect(url).toBe('/extensions/install-file');
        expect(body).toBeInstanceOf(FormData);
        expect((body as FormData).get('file')).toBe(file);
    });

    it('uninstalls an extension via the uninstall endpoint', async () => {
        mockApiPost.mockResolvedValue(undefined);

        const { result } = renderHook(() => useExtensionActions(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.uninstallExtension('ext-1');
        });

        expect(mockApiPost).toHaveBeenCalledWith('/extensions/ext-1/uninstall');
    });

    it('updates an extension and forwards the version', async () => {
        mockApiPost.mockResolvedValue({ extension: mockExtension, restartRequired: true });

        const { result } = renderHook(() => useExtensionActions(), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.updateExtension('ext-1', '2.0.0');
        });

        expect(mockApiPost).toHaveBeenCalledWith('/extensions/ext-1/update', { version: '2.0.0' });
    });

    it('checks for updates and returns the per-extension info list', async () => {
        const updates = [
            {
                extensionId: 'ext-1',
                packageId: 'Magpie.Foo',
                currentVersion: '1.0.0',
                latestVersion: '1.1.0',
                hasUpdate: true,
            },
        ];
        mockApiPost.mockResolvedValue(updates);

        const { result } = renderHook(() => useExtensionActions(), {
            wrapper: createWrapper(),
        });

        let returned: typeof updates | undefined;
        await act(async () => {
            returned = await result.current.checkUpdates();
        });

        expect(mockApiPost).toHaveBeenCalledWith('/extensions/check-updates');
        expect(returned).toEqual(updates);
    });

    it('tracks isInstalling while the install request is in flight', async () => {
        let resolveInstall: (value: { extension: ApiExtension; restartRequired: boolean }) => void;
        const installPromise = new Promise<{ extension: ApiExtension; restartRequired: boolean }>((resolve) => {
            resolveInstall = resolve;
        });
        mockApiPost.mockReturnValue(installPromise);

        const { result } = renderHook(() => useExtensionActions(), {
            wrapper: createWrapper(),
        });

        act(() => {
            void result.current.installExtension({ packageId: 'Magpie.Foo' });
        });

        await waitFor(() => {
            expect(result.current.isInstalling).toBe(true);
        });

        act(() => {
            resolveInstall!({ extension: mockExtension, restartRequired: true });
        });

        await waitFor(() => {
            expect(result.current.isInstalling).toBe(false);
        });
    });
});
