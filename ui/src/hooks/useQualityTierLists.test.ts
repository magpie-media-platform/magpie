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
import { useQualityTierLists, useQualityTierList, type ApiQualityTierList } from './useQualityTierLists';

const mockApiTierLists: ApiQualityTierList[] = [
    {
        id: 'qtl-1',
        name: 'HD Standard',
        upgradeAllowed: true,
        createdAt: '2024-01-01T00:00:00Z',
        qualityTierRanks: [
            {
                qualityTierListId: 'qtl-1',
                qualityTierId: 'qt-1',
                ordinal: 0,
                isCutoff: true,
                createdAt: '2024-01-01T00:00:00Z',
                qualityTier: {
                    id: 'qt-1',
                    name: 'Bluray-1080p',
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'Bluray' },
                                    resolution: { operator: 0, value: '1080p' },
                                },
                            },
                        ],
                    },
                    createdAt: '2024-01-01T00:00:00Z',
                },
            },
            {
                qualityTierListId: 'qtl-1',
                qualityTierId: 'qt-2',
                ordinal: 1,
                isCutoff: false,
                createdAt: '2024-01-01T00:00:00Z',
                qualityTier: {
                    id: 'qt-2',
                    name: 'HDTV-1080p',
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'HDTV' },
                                    resolution: { operator: 0, value: '1080p' },
                                },
                            },
                        ],
                    },
                    createdAt: '2024-01-01T00:00:00Z',
                },
            },
        ],
    },
    {
        id: 'qtl-2',
        name: '4K Ultra HD',
        upgradeAllowed: true,
        createdAt: '2024-01-02T00:00:00Z',
        qualityTierRanks: [
            {
                qualityTierListId: 'qtl-2',
                qualityTierId: 'qt-3',
                ordinal: 0,
                isCutoff: true,
                createdAt: '2024-01-02T00:00:00Z',
                qualityTier: {
                    id: 'qt-3',
                    name: 'Bluray-2160p',
                    criteria: {
                        and: [
                            {
                                items: {
                                    source: { operator: 0, value: 'Bluray' },
                                    resolution: { operator: 0, value: '2160p' },
                                    hdr: { operator: 0, value: 'true' },
                                },
                            },
                        ],
                    },
                    createdAt: '2024-01-02T00:00:00Z',
                },
            },
        ],
    },
    {
        id: 'qtl-3',
        name: 'Any Quality',
        upgradeAllowed: false,
        createdAt: '2024-01-03T00:00:00Z',
        qualityTierRanks: [],
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

describe('useQualityTierLists', () => {
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

    describe('fetching tier lists', () => {
        it('should fetch tier lists from the API', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/quality-tier-lists');
        });

        it('should return tier lists from API', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.tierLists).toHaveLength(3);
            expect(result.current.tierLists[0].name).toBe('HD Standard');
            expect(result.current.tierLists[1].name).toBe('4K Ultra HD');
            expect(result.current.tierLists[2].name).toBe('Any Quality');
        });

        it('should filter by tag when provided', async () => {
            mockApiGet.mockResolvedValue([mockApiTierLists[0]]);

            const { result } = renderHook(() => useQualityTierLists({ tag: 'movies' }), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/quality-tier-lists?tag=movies');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.tierLists).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns nothing', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.tierLists).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('creating tier lists', () => {
        it('should create a tier list via POST /quality-tier-lists', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);
            mockApiPost.mockResolvedValue({
                id: 'qtl-4',
                name: 'New Tier List',
                upgradeAllowed: true,
                createdAt: '2024-01-04T00:00:00Z',
                qualityTierRanks: [],
            });

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createTierList({
                    name: 'New Tier List',
                    upgradeAllowed: true,
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/quality-tier-lists', {
                name: 'New Tier List',
                upgradeAllowed: true,
            });
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);

            let resolvePost: (value: ApiQualityTierList) => void;
            const postPromise = new Promise<ApiQualityTierList>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.createTierList({
                    name: 'New Tier List',
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(true);
            });

            act(() => {
                resolvePost!({
                    id: 'qtl-4',
                    name: 'New Tier List',
                    upgradeAllowed: false,
                    createdAt: '2024-01-04T00:00:00Z',
                    qualityTierRanks: [],
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(false);
            });
        });
    });

    describe('updating tier lists', () => {
        it('should update a tier list via PATCH /quality-tier-lists/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateTierList('qtl-1', {
                    name: { value: 'Updated Name' },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/quality-tier-lists/qtl-1', {
                name: { value: 'Updated Name' },
            });
        });

        it('should update upgradeAllowed via PATCH', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateTierList('qtl-1', {
                    upgradeAllowed: { value: false },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/quality-tier-lists/qtl-1', {
                upgradeAllowed: { value: false },
            });
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.updateTierList('qtl-1', {
                    name: { value: 'Updated' },
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

        it('should send qualityTierRanks.update as an array with qualityTierId', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateTierList('qtl-1', {
                    qualityTierRanks: {
                        update: [
                            { qualityTierId: { value: 'qt-1' }, ordinal: { value: 0 }, isCutoff: { value: true } },
                            { qualityTierId: { value: 'qt-2' }, ordinal: { value: 1 } },
                        ],
                    },
                });
            });

            const sentPatch = mockApiPatch.mock.calls[0][1] as { qualityTierRanks: { update: unknown[] } };
            expect(Array.isArray(sentPatch.qualityTierRanks.update)).toBe(true);
            expect(sentPatch.qualityTierRanks.update).toHaveLength(2);
            expect(sentPatch.qualityTierRanks.update[0]).toEqual({
                qualityTierId: { value: 'qt-1' },
                ordinal: { value: 0 },
                isCutoff: { value: true },
            });
            expect(sentPatch.qualityTierRanks.update[1]).toEqual({
                qualityTierId: { value: 'qt-2' },
                ordinal: { value: 1 },
            });
        });

        it('should send qualityTierRanks with add and remove alongside update', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateTierList('qtl-1', {
                    qualityTierRanks: {
                        add: [{ qualityTierId: 'qt-new', ordinal: 2, isCutoff: false }],
                        remove: ['qt-old'],
                        update: [{ qualityTierId: { value: 'qt-1' }, isCutoff: { value: false } }],
                    },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/quality-tier-lists/qtl-1', {
                qualityTierRanks: {
                    add: [{ qualityTierId: 'qt-new', ordinal: 2, isCutoff: false }],
                    remove: ['qt-old'],
                    update: [{ qualityTierId: { value: 'qt-1' }, isCutoff: { value: false } }],
                },
            });
        });
    });

    describe('deleting tier lists', () => {
        it('should delete a tier list via DELETE /quality-tier-lists/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteTierList('qtl-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/quality-tier-lists/qtl-1');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiTierLists);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.deleteTierList('qtl-1');
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

    describe('tier list data transformation', () => {
        it('should preserve all API fields', async () => {
            mockApiGet.mockResolvedValue([mockApiTierLists[0]]);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const tierList = result.current.tierLists[0];
            expect(tierList.id).toBe('qtl-1');
            expect(tierList.name).toBe('HD Standard');
            expect(tierList.upgradeAllowed).toBe(true);
            expect(tierList.createdAt).toBe('2024-01-01T00:00:00Z');
            expect(tierList.qualityTierRanks).toHaveLength(2);
        });

        it('should preserve quality tier ranks', async () => {
            mockApiGet.mockResolvedValue([mockApiTierLists[0]]);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const tierList = result.current.tierLists[0];
            const firstRank = tierList.qualityTierRanks[0];
            expect(firstRank.qualityTierListId).toBe('qtl-1');
            expect(firstRank.qualityTierId).toBe('qt-1');
            expect(firstRank.ordinal).toBe(0);
            expect(firstRank.isCutoff).toBe(true);
            expect(firstRank.qualityTier?.name).toBe('Bluray-1080p');
        });

        it('should handle empty qualityTierRanks', async () => {
            mockApiGet.mockResolvedValue([mockApiTierLists[2]]);

            const { result } = renderHook(() => useQualityTierLists(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.tierLists[0].qualityTierRanks).toHaveLength(0);
        });
    });
});

describe('useQualityTierList', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch a single tier list by ID', async () => {
        mockApiGet.mockResolvedValue(mockApiTierLists[0]);

        const { result } = renderHook(() => useQualityTierList('qtl-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/quality-tier-lists/qtl-1');
        expect(result.current.data?.name).toBe('HD Standard');
    });

    it('should not fetch when id is undefined', async () => {
        const { result } = renderHook(() => useQualityTierList(undefined), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(false);
        expect(result.current.data).toBeUndefined();
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('should return error when tier list not found', async () => {
        mockApiGet.mockRejectedValue(new Error('Not found'));

        const { result } = renderHook(() => useQualityTierList('non-existent'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.error).toBeInstanceOf(Error);
        expect(result.current.data).toBeUndefined();
    });
});
