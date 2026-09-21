import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPatch: vi.fn(),
}));

import { apiGet, apiPatch } from '@/lib/api';
import {
    hasConfiguredExtensionSettings,
    settingsToRecord,
    settingsQueryKeys,
    useSystemSettingsGroup,
    useUpdateSystemSettingsGroup,
    useExtensionSettingsGroups,
    useUpdateExtensionSettingsGroup,
} from './useSettings';
import type { ApiSetting, ApiSettingsGroup } from './useSettings';

function createWrapper() {
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

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('settingsToRecord', () => {
    it('should return empty record for empty array', () => {
        expect(settingsToRecord([])).toEqual({});
    });

    it('should convert settings array to record', () => {
        const settings: ApiSetting[] = [
            { id: '1', settingsGroupId: 'g1', key: 'foo', value: 'bar', createdAt: '2025-01-01T00:00:00Z' },
        ];
        expect(settingsToRecord(settings)).toEqual({ foo: 'bar' });
    });

    it('should handle multiple settings', () => {
        const settings: ApiSetting[] = [
            { id: '1', settingsGroupId: 'g1', key: 'a', value: '1', createdAt: '2025-01-01T00:00:00Z' },
            { id: '2', settingsGroupId: 'g1', key: 'b', value: '2', createdAt: '2025-01-01T00:00:00Z' },
            { id: '3', settingsGroupId: 'g1', key: 'c', value: '3', createdAt: '2025-01-01T00:00:00Z' },
        ];
        expect(settingsToRecord(settings)).toEqual({ a: '1', b: '2', c: '3' });
    });

    it('should let last value win for duplicate keys', () => {
        const settings: ApiSetting[] = [
            { id: '1', settingsGroupId: 'g1', key: 'dup', value: 'first', createdAt: '2025-01-01T00:00:00Z' },
            { id: '2', settingsGroupId: 'g1', key: 'dup', value: 'second', createdAt: '2025-01-01T00:00:00Z' },
        ];
        expect(settingsToRecord(settings)).toEqual({ dup: 'second' });
    });
});

describe('hasConfiguredExtensionSettings', () => {
    const baseGroup: ApiSettingsGroup = {
        id: 'group-1',
        name: 'metadata',
        extensionId: 'ext-1',
        createdAt: '2025-01-01T00:00:00Z',
        settings: [],
    };

    it('returns false for an available extension group with no configured values', () => {
        expect(
            hasConfiguredExtensionSettings({
                ...baseGroup,
                settings: [
                    {
                        id: 'setting-1',
                        settingsGroupId: 'group-1',
                        key: 'apiKey',
                        value: '',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
                secretSettings: {
                    apiKey: { isSet: false },
                },
            }),
        ).toBe(false);
    });

    it('returns true when an extension group has a visible configured value', () => {
        expect(
            hasConfiguredExtensionSettings({
                ...baseGroup,
                settings: [
                    {
                        id: 'setting-1',
                        settingsGroupId: 'group-1',
                        key: 'apiKey',
                        value: 'configured',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            }),
        ).toBe(true);
    });

    it('returns true when an extension group has a configured secret value', () => {
        expect(
            hasConfiguredExtensionSettings({
                ...baseGroup,
                secretSettings: {
                    apiKey: { isSet: true },
                },
            }),
        ).toBe(true);
    });

    it('returns false for configured system settings', () => {
        expect(
            hasConfiguredExtensionSettings({
                ...baseGroup,
                extensionId: null,
                settings: [
                    {
                        id: 'setting-1',
                        settingsGroupId: 'group-1',
                        key: 'theme',
                        value: 'dark',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            }),
        ).toBe(false);
    });
});

describe('settingsQueryKeys', () => {
    it('should produce correct all key', () => {
        expect(settingsQueryKeys.all).toEqual(['settings']);
    });
});

describe('useSystemSettingsGroup', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('shares the group list across system names and extensions', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([
            { id: 'general', name: 'general', extensionId: null, settings: [], createdAt: '' },
            { id: 'extension', name: 'general', extensionId: 'extension-a', settings: [], createdAt: '' },
        ]);
        // Act
        const { result } = renderHook(
            () => ({
                system: useSystemSettingsGroup('general'),
                missing: useSystemSettingsGroup('missing'),
                extension: useExtensionSettingsGroups('extension-a'),
            }),
            { wrapper: createWrapper() },
        );
        await waitFor(() => expect(result.current.system.isLoading).toBe(false));
        // Assert
        expect(mockApiGet).toHaveBeenCalledTimes(1);
        expect(result.current.system.group?.id).toBe('general');
        expect(result.current.missing.group).toBeNull();
        expect(result.current.extension.groups.map((group) => group.id)).toEqual(['extension']);
    });

    it('should read the named system group from the settings-group list', async () => {
        const mockGroup: ApiSettingsGroup = {
            id: 'system-general',
            name: 'general',
            extensionId: null,
            createdAt: '2025-01-01T00:00:00Z',
            settings: [
                {
                    id: '1',
                    settingsGroupId: 'system-general',
                    key: 'theme',
                    value: 'dark',
                    createdAt: '2025-01-01T00:00:00Z',
                },
            ],
        };
        mockApiGet.mockResolvedValue([mockGroup]);

        const { result } = renderHook(() => useSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);
        expect(result.current.group).toBeNull();

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/settings/groups');
        expect(result.current.group).toEqual(mockGroup);
        expect(result.current.error).toBeNull();
    });

    it('should return null when a dynamic system group has not been created yet', async () => {
        mockApiGet.mockResolvedValue([
            {
                id: 'system-setup',
                name: 'setup',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            },
        ]);

        const { result } = renderHook(() => useSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(result.current.group).toBeNull();
        expect(result.current.error).toBeNull();
    });

    it('should return null group while loading', async () => {
        let resolveGet: (value: ApiSettingsGroup[]) => void;
        const getPromise = new Promise<ApiSettingsGroup[]>((resolve) => {
            resolveGet = resolve;
        });
        mockApiGet.mockReturnValue(getPromise);

        const { result } = renderHook(() => useSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);
        expect(result.current.group).toBeNull();

        act(() => {
            resolveGet!([
                {
                    id: 'system-general',
                    name: 'general',
                    extensionId: null,
                    createdAt: '2025-01-01T00:00:00Z',
                    settings: [],
                },
            ]);
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });
    });

    it('should return error on failure', async () => {
        mockApiGet.mockRejectedValue(new Error('Network error'));

        const { result } = renderHook(() => useSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.error).toBeInstanceOf(Error);
        expect(result.current.group).toBeNull();
    });
});

describe('useUpdateSystemSettingsGroup', () => {
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should call PATCH /settings/groups/system/{name} with add for new keys', async () => {
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useUpdateSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.updateSettings({
                values: { theme: 'dark', language: 'en' },
                existingKeys: new Set<string>(),
            });
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/general', {
            settings: {
                add: {
                    theme: 'dark',
                    language: 'en',
                },
            },
        });
    });

    it('should build correct patch with update for existing keys', async () => {
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useUpdateSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.updateSettings({
                values: { theme: 'light', language: 'fr' },
                existingKeys: new Set(['theme', 'language']),
            });
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/general', {
            settings: {
                update: {
                    theme: 'light',
                    language: 'fr',
                },
            },
        });
    });

    it('should build mixed patch with both add and update', async () => {
        mockApiPatch.mockResolvedValue(undefined);

        const { result } = renderHook(() => useUpdateSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.updateSettings({
                values: { theme: 'dark', newKey: 'newValue' },
                existingKeys: new Set(['theme']),
            });
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/general', {
            settings: {
                add: {
                    newKey: 'newValue',
                },
                update: {
                    theme: 'dark',
                },
            },
        });
    });

    it('should set isUpdating during mutation', async () => {
        let resolvePatch: () => void;
        const patchPromise = new Promise<void>((resolve) => {
            resolvePatch = resolve;
        });
        mockApiPatch.mockReturnValue(patchPromise);

        const { result } = renderHook(() => useUpdateSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        act(() => {
            void result.current.updateSettings({
                values: { theme: 'dark' },
                existingKeys: new Set<string>(),
            });
        });

        await waitFor(() => {
            expect(result.current.isUpdating).toBe(true);
        });

        act(() => {
            resolvePatch!();
        });

        await waitFor(() => {
            expect(result.current.isUpdating).toBe(false);
        });
    });
});

describe('useExtensionSettingsGroups', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should call GET /settings/groups and filter by extensionId', async () => {
        const allGroups: ApiSettingsGroup[] = [
            {
                id: 'ext-abc-config',
                name: 'config',
                extensionId: 'ext-abc',
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'ext-abc-config',
                        key: 'apiKey',
                        value: 'secret',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            },
            {
                id: 'ext-xyz-config',
                name: 'config',
                extensionId: 'ext-xyz',
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            },
            {
                id: 'system-general',
                name: 'general',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            },
        ];
        mockApiGet.mockResolvedValue(allGroups);

        const { result } = renderHook(() => useExtensionSettingsGroups('ext-abc'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/settings/groups');
        expect(result.current.groups).toHaveLength(1);
        expect(result.current.groups[0].extensionId).toBe('ext-abc');
        expect(result.current.error).toBeNull();
    });

    it('should return empty array when extensionId is null', () => {
        const { result } = renderHook(() => useExtensionSettingsGroups(null), {
            wrapper: createWrapper(),
        });

        expect(result.current.groups).toEqual([]);
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it.each([null, ''])('returns no groups for extensionId %s with a populated cache', (extensionId) => {
        // Arrange
        const queryClient = new QueryClient();
        queryClient.setQueryData(settingsQueryKeys.groups, [
            { id: 'system', name: 'general', extensionId: null, settings: [], createdAt: '' },
            { id: 'extension', name: 'general', extensionId: 'ext-abc', settings: [], createdAt: '' },
        ]);

        // Act
        const { result, rerender } = renderHook(({ id }) => useExtensionSettingsGroups(id), {
            initialProps: { id: extensionId as string | null },
            wrapper: ({ children }) => createElement(QueryClientProvider, { client: queryClient }, children),
        });

        // Assert
        expect(result.current.groups).toEqual([]);
        rerender({ id: 'ext-abc' });
        expect(result.current.groups.map((group) => group.id)).toEqual(['extension']);
        rerender({ id: extensionId });
        expect(result.current.groups).toEqual([]);
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('should return only matching extension groups', async () => {
        const allGroups: ApiSettingsGroup[] = [
            {
                id: 'ext-abc-config',
                name: 'config',
                extensionId: 'ext-abc',
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            },
            {
                id: 'ext-abc-advanced',
                name: 'advanced',
                extensionId: 'ext-abc',
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            },
            {
                id: 'ext-xyz-config',
                name: 'config',
                extensionId: 'ext-xyz',
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            },
        ];
        mockApiGet.mockResolvedValue(allGroups);

        const { result } = renderHook(() => useExtensionSettingsGroups('ext-abc'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.groups).toHaveLength(2);
        expect(result.current.groups.every((g) => g.extensionId === 'ext-abc')).toBe(true);
    });
});

describe('useUpdateExtensionSettingsGroup', () => {
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should call PATCH /settings/groups/{extensionId}/{name}', async () => {
        mockApiPatch.mockResolvedValue(undefined);
        const invalidateQueries = vi.spyOn(QueryClient.prototype, 'invalidateQueries');

        const { result } = renderHook(() => useUpdateExtensionSettingsGroup('ext-abc', 'config'), {
            wrapper: createWrapper(),
        });

        await act(async () => {
            await result.current.updateSettings({
                values: { apiKey: 'new-secret' },
                existingKeys: new Set(['apiKey']),
            });
        });

        expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/ext-abc/config', {
            settings: {
                update: {
                    apiKey: 'new-secret',
                },
            },
        });
        expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: settingsQueryKeys.all });
    });

    it('should set isUpdating during mutation', async () => {
        let resolvePatch: () => void;
        const patchPromise = new Promise<void>((resolve) => {
            resolvePatch = resolve;
        });
        mockApiPatch.mockReturnValue(patchPromise);

        const { result } = renderHook(() => useUpdateExtensionSettingsGroup('ext-abc', 'config'), {
            wrapper: createWrapper(),
        });

        act(() => {
            void result.current.updateSettings({
                values: { apiKey: 'new-secret' },
                existingKeys: new Set<string>(),
            });
        });

        await waitFor(() => {
            expect(result.current.isUpdating).toBe(true);
        });

        act(() => {
            resolvePatch!();
        });

        await waitFor(() => {
            expect(result.current.isUpdating).toBe(false);
        });
    });
});
