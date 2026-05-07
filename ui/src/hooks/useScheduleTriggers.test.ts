import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { useScheduleTriggers, useScheduleTrigger, type ApiScheduleTrigger } from './useScheduleTriggers';

const mockApiTriggers: ApiScheduleTrigger[] = [
    {
        id: 'trigger-1',
        jobType: 'RssSync',
        scope: 1,
        name: 'Hourly RSS Sync',
        schedule: '0 * * * *',
        executionMode: 0,
        enabled: true,
        lastRunAt: '2024-01-15T11:00:00Z',
        nextRunAt: '2024-01-15T12:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-2',
        jobType: 'RefreshMetadata',
        scope: 2,
        name: 'Daily Metadata Refresh',
        schedule: '0 6 * * *',
        executionMode: 0,
        enabled: true,
        lastRunAt: '2024-01-15T06:00:00Z',
        nextRunAt: '2024-01-16T06:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-3',
        jobType: 'SearchMissing',
        scope: 2,
        name: 'Nightly Missing Search',
        schedule: '0 2 * * *',
        executionMode: 0,
        enabled: false,
        lastRunAt: '2024-01-14T02:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
];

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

describe('useScheduleTriggers', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);
    const mockApiPatch = vi.mocked(apiPatch);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching triggers', () => {
        it('should fetch triggers from the API', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/schedule-triggers');
        });

        it('should return triggers from API', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.triggers).toHaveLength(3);
            expect(result.current.triggers[0].name).toBe('Hourly RSS Sync');
            expect(result.current.triggers[1].name).toBe('Daily Metadata Refresh');
            expect(result.current.triggers[2].name).toBe('Nightly Missing Search');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.triggers).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns empty list', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.triggers).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('creating triggers', () => {
        it('should create a trigger via POST /schedule-triggers', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);
            mockApiPost.mockResolvedValue(mockApiTriggers[0]);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const newTrigger = {
                jobType: 'BackupDatabase',
                scope: 1 as const,
                name: 'Weekly Backup',
                schedule: '0 0 * * 0',
            };

            await act(async () => {
                await result.current.createTrigger(newTrigger);
            });

            expect(mockApiPost).toHaveBeenCalledWith('/schedule-triggers', newTrigger);
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);

            let resolvePost: (value: ApiScheduleTrigger) => void;
            const postPromise = new Promise<ApiScheduleTrigger>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.createTrigger({
                    jobType: 'BackupDatabase',
                    scope: 1,
                    name: 'Weekly Backup',
                    schedule: '0 0 * * 0',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(true);
            });

            act(() => {
                resolvePost!(mockApiTriggers[0]);
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(false);
            });
        });
    });

    describe('updating triggers', () => {
        it('should update a trigger via PATCH /schedule-triggers/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const patch = { enabled: { value: false } };

            await act(async () => {
                await result.current.updateTrigger({ id: 'trigger-1', patch });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/schedule-triggers/trigger-1', patch);
        });

        it('should send parameters as DictionaryPatch with add/remove/update', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const patch = {
                parameters: {
                    add: { newKey: 'newValue' },
                    remove: ['oldKey'],
                    update: { existingKey: 'updatedValue' },
                },
            };

            await act(async () => {
                await result.current.updateTrigger({ id: 'trigger-1', patch });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/schedule-triggers/trigger-1', patch);
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.updateTrigger({
                    id: 'trigger-1',
                    patch: { enabled: { value: false } },
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

    describe('deleting triggers', () => {
        it('should delete a trigger via DELETE /schedule-triggers/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteTrigger('trigger-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/schedule-triggers/trigger-1');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.deleteTrigger('trigger-1');
            });

            await waitFor(() => {
                expect(result.current.isDeleting).toBe(true);
            });

            act(() => {
                resolveDelete!();
            });

            await waitFor(() => {
                expect(result.current.isDeleting).toBe(false);
            });
        });
    });

    describe('trigger data transformation', () => {
        it('should preserve all API fields', async () => {
            mockApiGet.mockResolvedValue([mockApiTriggers[0]]);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const trigger = result.current.triggers[0];
            expect(trigger.id).toBe('trigger-1');
            expect(trigger.jobType).toBe('RssSync');
            expect(trigger.scope).toBe(1);
            expect(trigger.name).toBe('Hourly RSS Sync');
            expect(trigger.schedule).toBe('0 * * * *');
            expect(trigger.executionMode).toBe(0);
            expect(trigger.enabled).toBe(true);
            expect(trigger.lastRunAt).toBe('2024-01-15T11:00:00Z');
            expect(trigger.nextRunAt).toBe('2024-01-15T12:00:00Z');
            expect(trigger.createdAt).toBe('2024-01-01T00:00:00Z');
        });

        it('should handle null optional fields', async () => {
            mockApiGet.mockResolvedValue([mockApiTriggers[2]]);

            const { result } = renderHook(() => useScheduleTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.triggers[0].nextRunAt).toBeUndefined();
        });
    });
});

describe('useScheduleTrigger', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch a single trigger by ID', async () => {
        mockApiGet.mockResolvedValue(mockApiTriggers[0]);

        const { result } = renderHook(() => useScheduleTrigger('trigger-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/schedule-triggers/trigger-1');
        expect(result.current.trigger?.id).toBe('trigger-1');
        expect(result.current.trigger?.name).toBe('Hourly RSS Sync');
    });

    it('should not fetch when disabled', async () => {
        const { result } = renderHook(() => useScheduleTrigger('trigger-1', false), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).not.toHaveBeenCalled();
        });

        expect(result.current.trigger).toBeNull();
    });

    it('should not fetch when ID is empty', async () => {
        const { result } = renderHook(() => useScheduleTrigger(''), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).not.toHaveBeenCalled();
        });

        expect(result.current.trigger).toBeNull();
    });

    it('should return error on failure', async () => {
        mockApiGet.mockRejectedValue(new Error('Not found'));

        const { result } = renderHook(() => useScheduleTrigger('trigger-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.trigger).toBeNull();
        expect(result.current.error).toBeInstanceOf(Error);
    });
});
