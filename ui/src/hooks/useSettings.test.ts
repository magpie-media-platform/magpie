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

describe('settingsQueryKeys', () => {
    it('should produce correct all key', () => {
        expect(settingsQueryKeys.all).toEqual(['settings']);
    });

    it('should produce correct systemGroup key', () => {
        expect(settingsQueryKeys.systemGroup('backups')).toEqual(['settings', 'system', 'backups']);
    });

    it('should produce correct extensionGroups key', () => {
        expect(settingsQueryKeys.extensionGroups('ext-123')).toEqual(['settings', 'extension', 'ext-123']);
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

    it('should call GET /settings/groups/system/{name} and return group data', async () => {
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
        mockApiGet.mockResolvedValue(mockGroup);

        const { result } = renderHook(() => useSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);
        expect(result.current.group).toBeNull();

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/settings/groups/system/general');
        expect(result.current.group).toEqual(mockGroup);
        expect(result.current.error).toBeNull();
    });

    it('should return null group while loading', async () => {
        let resolveGet: (value: ApiSettingsGroup) => void;
        const getPromise = new Promise<ApiSettingsGroup>((resolve) => {
            resolveGet = resolve;
        });
        mockApiGet.mockReturnValue(getPromise);

        const { result } = renderHook(() => useSystemSettingsGroup('general'), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);
        expect(result.current.group).toBeNull();

        act(() => {
            resolveGet!({
                id: 'system-general',
                name: 'general',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            });
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
            result.current.updateSettings({
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
            result.current.updateSettings({
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
