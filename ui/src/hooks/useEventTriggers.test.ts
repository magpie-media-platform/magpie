import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
        if (!params) {
            return path;
        }
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        const queryString = searchParams.toString();
        return queryString ? `${path}?${queryString}` : path;
    }),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { useEventTriggers, useEventTrigger, type ApiEventTrigger } from './useEventTriggers';

const mockApiTriggers: ApiEventTrigger[] = [
    {
        id: 'event-1',
        jobType: 'RefreshMetadata',
        scope: 2,
        name: 'Refresh on Download Complete',
        eventFilter: {
            eventName: { value: 'download.completed' },
        },
        executionMode: 0,
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'event-2',
        jobType: 'SearchMissing',
        scope: 2,
        name: 'Search on Content Added',
        eventFilter: {
            eventName: { value: 'content.added' },
            eventContext: {
                contentType: { value: 'episode' },
            },
        },
        executionMode: 0,
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'event-3',
        jobType: 'SyncPlex',
        scope: 1,
        name: 'Sync Plex on Import',
        eventFilter: {
            eventName: { value: 'file.imported' },
        },
        executionMode: 1,
        enabled: false,
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

describe('useEventTriggers', () => {
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

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/event-triggers');
        });

        it('should return triggers from API', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.triggers).toHaveLength(3);
            expect(result.current.triggers[0].name).toBe('Refresh on Download Complete');
            expect(result.current.triggers[1].name).toBe('Search on Content Added');
            expect(result.current.triggers[2].name).toBe('Sync Plex on Import');
        });

        it('should pass query parameters to the API', async () => {
            mockApiGet.mockResolvedValue([mockApiTriggers[0]]);

            const { result } = renderHook(() => useEventTriggers({ enabled: true }), { wrapper: createWrapper() });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/event-triggers?enabled=true');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useEventTriggers(), {
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

            const { result } = renderHook(() => useEventTriggers(), {
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
        it('should create a trigger via POST /event-triggers', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);
            mockApiPost.mockResolvedValue(mockApiTriggers[0]);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const newTrigger = {
                jobType: 'NotifyUser',
                scope: 3 as const,
                name: 'Notify on Download',
                eventFilter: {
                    eventName: { value: 'download.completed' },
                },
            };

            await act(async () => {
                await result.current.createTrigger(newTrigger);
            });

            expect(mockApiPost).toHaveBeenCalledWith('/event-triggers', newTrigger);
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);

            let resolvePost: (value: ApiEventTrigger) => void;
            const postPromise = new Promise<ApiEventTrigger>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.createTrigger({
                    jobType: 'NotifyUser',
                    scope: 3,
                    name: 'Notify on Download',
                    eventFilter: { eventName: { value: 'download.completed' } },
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
        it('should update a trigger via PATCH /event-triggers/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const patch = { enabled: { value: false } };

            await act(async () => {
                await result.current.updateTrigger({ id: 'event-1', patch });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/event-triggers/event-1', patch);
        });

        it('should send parameters as DictionaryPatch with add/remove/update', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const patch = {
                parameters: {
                    add: { newParam: 'value1' },
                    remove: ['oldParam'],
                    update: { existingParam: 'value2' },
                },
            };

            await act(async () => {
                await result.current.updateTrigger({ id: 'event-1', patch });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/event-triggers/event-1', patch);
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.updateTrigger({
                    id: 'event-1',
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
        it('should delete a trigger via DELETE /event-triggers/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteTrigger('event-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/event-triggers/event-1');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiTriggers);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.deleteTrigger('event-1');
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
            mockApiGet.mockResolvedValue([mockApiTriggers[1]]);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const trigger = result.current.triggers[0];
            expect(trigger.id).toBe('event-2');
            expect(trigger.jobType).toBe('SearchMissing');
            expect(trigger.scope).toBe(2);
            expect(trigger.name).toBe('Search on Content Added');
            expect(trigger.eventFilter.eventName?.value).toBe('content.added');
            expect(trigger.eventFilter.eventContext?.contentType?.value).toBe('episode');
            expect(trigger.executionMode).toBe(0);
            expect(trigger.enabled).toBe(true);
            expect(trigger.createdAt).toBe('2024-01-01T00:00:00Z');
        });

        it('should handle trigger with simple event filter', async () => {
            mockApiGet.mockResolvedValue([mockApiTriggers[0]]);

            const { result } = renderHook(() => useEventTriggers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.triggers[0].eventFilter.eventName?.value).toBe('download.completed');
            expect(result.current.triggers[0].eventFilter.eventContext).toBeUndefined();
        });
    });
});

describe('useEventTrigger', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch a single trigger by ID', async () => {
        mockApiGet.mockResolvedValue(mockApiTriggers[0]);

        const { result } = renderHook(() => useEventTrigger('event-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/event-triggers/event-1');
        expect(result.current.trigger?.id).toBe('event-1');
        expect(result.current.trigger?.name).toBe('Refresh on Download Complete');
    });

    it('should not fetch when disabled', async () => {
        const { result } = renderHook(() => useEventTrigger('event-1', false), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).not.toHaveBeenCalled();
        });

        expect(result.current.trigger).toBeNull();
    });

    it('should not fetch when ID is empty', async () => {
        const { result } = renderHook(() => useEventTrigger(''), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).not.toHaveBeenCalled();
        });

        expect(result.current.trigger).toBeNull();
    });

    it('should return error on failure', async () => {
        mockApiGet.mockRejectedValue(new Error('Not found'));

        const { result } = renderHook(() => useEventTrigger('event-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.trigger).toBeNull();
        expect(result.current.error).toBeInstanceOf(Error);
    });
});
