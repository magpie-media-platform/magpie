import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPatch: vi.fn(),
}));

import { apiGet, apiPatch } from '@/lib/api';
import { useBackupSettings, type BackupSettings } from './useBackupSettings';
import type { ApiSettingsGroup } from './useSettings';

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

describe('useBackupSettings', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching settings', () => {
        it('should fetch backup settings from the API', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'system-backups',
                        key: 'enabled',
                        value: 'true',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                    {
                        id: '2',
                        settingsGroupId: 'system-backups',
                        key: 'interval',
                        value: 'weekly',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                    {
                        id: '3',
                        settingsGroupId: 'system-backups',
                        key: 'retention',
                        value: '14',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/settings/groups/system/backups');

            expect(result.current.settings).toEqual({
                enabled: true,
                interval: 'weekly',
                retention: 14,
            });
        });

        it('should return default settings when API returns empty group', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.settings).toEqual({
                enabled: false,
                interval: 'daily',
                retention: 7,
            });
        });

        it('should handle error when fetching settings', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.error).toBeInstanceOf(Error);
            expect(result.current.settings).toEqual({
                enabled: false,
                interval: 'daily',
                retention: 7,
            });
        });
    });

    describe('type conversion', () => {
        it('should convert "true" to boolean true', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'system-backups',
                        key: 'enabled',
                        value: 'true',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.settings.enabled).toBe(true);
        });

        it('should convert "false" to boolean false', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'system-backups',
                        key: 'enabled',
                        value: 'false',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.settings.enabled).toBe(false);
        });

        it('should convert any non-"true" value to boolean false', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'system-backups',
                        key: 'enabled',
                        value: 'yes',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.settings.enabled).toBe(false);
        });

        it('should parse numeric retention values', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'system-backups',
                        key: 'retention',
                        value: '30',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.settings.retention).toBe(30);
        });

        it('should use default for invalid retention values', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'system-backups',
                        key: 'retention',
                        value: 'not-a-number',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.settings.retention).toBe(7);
        });

        it('should validate interval values', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'system-backups',
                        key: 'interval',
                        value: 'biweekly',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.settings.interval).toBe('biweekly');
        });

        it('should use default for invalid interval values', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'system-backups',
                        key: 'interval',
                        value: 'hourly',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.settings.interval).toBe('daily');
        });
    });

    describe('saving settings', () => {
        it('should save settings and convert to string values', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [
                    {
                        id: '1',
                        settingsGroupId: 'system-backups',
                        key: 'enabled',
                        value: 'false',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                    {
                        id: '2',
                        settingsGroupId: 'system-backups',
                        key: 'interval',
                        value: 'daily',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                    {
                        id: '3',
                        settingsGroupId: 'system-backups',
                        key: 'retention',
                        value: '7',
                        createdAt: '2025-01-01T00:00:00Z',
                    },
                ],
            };
            mockApiGet.mockResolvedValue(mockGroup);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const newSettings: BackupSettings = {
                enabled: true,
                interval: 'weekly',
                retention: 14,
            };

            await act(async () => {
                await result.current.saveSettings(newSettings);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/backups', {
                settings: {
                    update: {
                        enabled: 'true',
                        interval: 'weekly',
                        retention: '14',
                    },
                },
            });
        });

        it('should use add operation for new keys', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            };
            mockApiGet.mockResolvedValue(mockGroup);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const newSettings: BackupSettings = {
                enabled: true,
                interval: 'monthly',
                retention: 30,
            };

            await act(async () => {
                await result.current.saveSettings(newSettings);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/backups', {
                settings: {
                    add: {
                        enabled: 'true',
                        interval: 'monthly',
                        retention: '30',
                    },
                },
            });
        });

        it('should set isSaving while saving', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            };
            mockApiGet.mockResolvedValue(mockGroup);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.saveSettings({
                    enabled: true,
                    interval: 'daily',
                    retention: 7,
                });
            });

            await waitFor(() => {
                expect(result.current.isSaving).toBe(true);
            });

            act(() => {
                resolvePatch!();
            });

            await waitFor(() => {
                expect(result.current.isSaving).toBe(false);
            });
        });

        it('should handle save errors', async () => {
            const mockGroup: ApiSettingsGroup = {
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                createdAt: '2025-01-01T00:00:00Z',
                settings: [],
            };
            mockApiGet.mockResolvedValue(mockGroup);
            mockApiPatch.mockRejectedValue(new Error('Save failed'));

            const { result } = renderHook(() => useBackupSettings(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                try {
                    await result.current.saveSettings({
                        enabled: true,
                        interval: 'daily',
                        retention: 7,
                    });
                } catch {
                    /* empty */
                }
            });

            await waitFor(() => {
                expect(result.current.saveError).toBeInstanceOf(Error);
            });
        });
    });
});
