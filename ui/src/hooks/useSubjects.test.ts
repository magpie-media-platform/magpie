import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    buildUrl: vi.fn((path: string, params?: Record<string, string | undefined>) => {
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
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { extractImageUrl, extractTitle } from '@/lib/metadata';
import { useSubjects, useSubject, subjectQueryKeys, type ApiSubject, type ApiMetadataContainer } from './useSubjects';

const mockMetadataContainer: ApiMetadataContainer = {
    id: 'mc-1',
    createdAt: '2024-01-01T00:00:00Z',
    contributions: [
        {
            id: 'contrib-1',
            ordinal: 0,
            locked: false,
            providerType: 'tmdb',
            title: 'Bryan Cranston',
            overview: 'American actor and director',
            images: [{ type: 'profile', url: 'https://example.com/profile.jpg', width: 300, height: 450 }],
        },
        {
            id: 'contrib-2',
            ordinal: 1,
            locked: false,
            providerType: 'imdb',
            title: 'Bryan Lee Cranston',
            images: [{ type: 'poster', url: 'https://example.com/poster.jpg' }],
        },
    ],
};

const mockApiSubjects: ApiSubject[] = [
    {
        id: 'subject-1',
        metadataContainerId: 'mc-1',
        kind: 'person',
        dateLastRefreshed: '2024-01-20T14:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        metadataContainer: mockMetadataContainer,
    },
    {
        id: 'subject-2',
        metadataContainerId: 'mc-2',
        kind: 'studio',
        dateLastRefreshed: '2024-01-18T09:30:00Z',
        createdAt: '2024-01-02T00:00:00Z',
        metadataContainer: {
            id: 'mc-2',
            createdAt: '2024-01-02T00:00:00Z',
            contributions: [
                {
                    id: 'contrib-3',
                    ordinal: 0,
                    locked: false,
                    providerType: 'tmdb',
                    title: 'Warner Bros.',
                    images: [{ type: 'logo', url: 'https://example.com/logo.png' }],
                },
            ],
        },
    },
    {
        id: 'subject-3',
        metadataContainerId: 'mc-3',
        kind: 'person',
        createdAt: '2024-01-03T00:00:00Z',
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

describe('useSubjects', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPatch = vi.mocked(apiPatch);
    const mockApiDelete = vi.mocked(apiDelete);
    const mockBuildUrl = vi.mocked(buildUrl);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching subjects', () => {
        it('should fetch subjects from the API', async () => {
            mockApiGet.mockResolvedValue(mockApiSubjects);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/subjects');
        });

        it('should return subjects from API', async () => {
            mockApiGet.mockResolvedValue(mockApiSubjects);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.subjects).toHaveLength(3);
            expect(result.current.subjects[0].id).toBe('subject-1');
            expect(result.current.subjects[0].kind).toBe('person');
            expect(result.current.subjects[1].id).toBe('subject-2');
            expect(result.current.subjects[1].kind).toBe('studio');
        });

        it('should filter by kind', async () => {
            mockApiGet.mockResolvedValue([mockApiSubjects[0], mockApiSubjects[2]]);

            const { result } = renderHook(() => useSubjects({ kind: 'person' }), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockBuildUrl).toHaveBeenCalledWith('/subjects', { kind: 'person' });
        });

        it('should filter by tag', async () => {
            mockApiGet.mockResolvedValue([mockApiSubjects[0]]);

            const { result } = renderHook(() => useSubjects({ tag: 'favorites' }), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockBuildUrl).toHaveBeenCalledWith('/subjects', { tag: 'favorites' });
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.subjects).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns nothing', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.subjects).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('metadata extraction', () => {
        it('should extract title from metadata contributions', async () => {
            mockApiGet.mockResolvedValue(mockApiSubjects);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const title = extractTitle(result.current.subjects[0].metadataContainer);
            expect(title).toBe('Bryan Cranston');
        });

        it('should extract image URL from metadata contributions', async () => {
            mockApiGet.mockResolvedValue(mockApiSubjects);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const imageUrl = extractImageUrl(result.current.subjects[0].metadataContainer);
            expect(imageUrl).toBe('https://example.com/profile.jpg');
        });

        it('should return undefined for missing metadata', async () => {
            mockApiGet.mockResolvedValue(mockApiSubjects);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const title = extractTitle(result.current.subjects[2].metadataContainer);
            const imageUrl = extractImageUrl(result.current.subjects[2].metadataContainer);

            expect(title).toBeUndefined();
            expect(imageUrl).toBeUndefined();
        });

        it('should fall back to any image if no primary type found', async () => {
            mockApiGet.mockResolvedValue([mockApiSubjects[1]]);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const imageUrl = extractImageUrl(result.current.subjects[0].metadataContainer);
            expect(imageUrl).toBe('https://example.com/logo.png');
        });
    });

    describe('updating subject tags', () => {
        it('should update subject tags via PATCH /subjects/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiSubjects);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateSubjectTags('subject-1', {
                    tags: { add: ['favorites', 'action'] },
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/subjects/subject-1', {
                tags: { add: ['favorites', 'action'] },
            });
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockApiSubjects);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.updateSubjectTags('subject-1', { tags: { add: ['test'] } });
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

    describe('deleting subjects', () => {
        it('should delete a subject via DELETE /subjects/{id}', async () => {
            mockApiGet.mockResolvedValue(mockApiSubjects);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteSubject('subject-1');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/subjects/subject-1');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiSubjects);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useSubjects(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.deleteSubject('subject-1');
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

    describe('query keys', () => {
        it('should export query keys for external use', async () => {
            expect(subjectQueryKeys.all).toEqual(['subjects']);
            expect(subjectQueryKeys.list()).toEqual(['subjects', 'list', {}]);
            expect(subjectQueryKeys.list({ kind: 'person' })).toEqual(['subjects', 'list', { kind: 'person' }]);
            expect(subjectQueryKeys.detail('subject-1')).toEqual(['subjects', 'detail', 'subject-1']);
        });
    });
});

describe('useSubject', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch a single subject by ID', async () => {
        mockApiGet.mockResolvedValue(mockApiSubjects[0]);

        const { result } = renderHook(() => useSubject('subject-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/subjects/subject-1');
        expect(result.current.subject?.id).toBe('subject-1');
    });

    it('should not fetch when disabled', async () => {
        const { result } = renderHook(() => useSubject('subject-1', false), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(false);
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('should not fetch with empty ID', async () => {
        const { result } = renderHook(() => useSubject(''), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(false);
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('should provide metadata extraction utilities', async () => {
        mockApiGet.mockResolvedValue(mockApiSubjects[0]);

        const { result } = renderHook(() => useSubject('subject-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        const subject = result.current.subject;
        expect(extractTitle(subject?.metadataContainer)).toBe('Bryan Cranston');
        expect(extractImageUrl(subject?.metadataContainer)).toBe('https://example.com/profile.jpg');
    });

    it('should handle errors', async () => {
        mockApiGet.mockRejectedValue(new Error('Not found'));

        const { result } = renderHook(() => useSubject('nonexistent'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.subject).toBeNull();
        expect(result.current.error).toBeInstanceOf(Error);
    });
});
