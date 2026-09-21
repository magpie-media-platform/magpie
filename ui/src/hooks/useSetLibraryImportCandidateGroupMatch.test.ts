import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return { ...actual, apiPost: vi.fn() };
});

import { apiPost, ApiError } from '@/lib/api';
import { useSetLibraryImportCandidateGroupMatch } from './useSetLibraryImportCandidateGroupMatch';
import { libraryImportCandidateQueryKeys } from './useLibraryImportCandidates';
import { libraryImportCandidateGroupQueryKeys } from './useLibraryImportCandidateGroups';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    const Wrapper = ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
    return { Wrapper, queryClient };
}

describe('useSetLibraryImportCandidateGroupMatch', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('posts the match fields to candidate-groups/match with the group key in the query string, not the body', async () => {
        mockApiPost.mockResolvedValue({ groupKey: '/media/tv/Bluey' });
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useSetLibraryImportCandidateGroupMatch(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({
                groupKey: '/media/tv/Bluey',
                rootFolderId: 'folder-1',
                providerType: 'tvmaze',
                externalIdKey: 'tvmaze',
                externalId: '44796',
                title: 'Bluey',
            });
        });

        expect(mockApiPost).toHaveBeenCalledWith(
            '/import/candidate-groups/match?groupKey=%2Fmedia%2Ftv%2FBluey&rootFolderId=folder-1',
            {
                providerType: 'tvmaze',
                externalIdKey: 'tvmaze',
                externalId: '44796',
                title: 'Bluey',
            },
        );
    });

    it('invalidates both the candidate and candidate-group query families on success', async () => {
        mockApiPost.mockResolvedValue({ groupKey: 'query:bluey' });
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useSetLibraryImportCandidateGroupMatch(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({
                groupKey: 'query:bluey',
                providerType: 'tvmaze',
                externalIdKey: 'tvmaze',
                externalId: '44796',
                title: 'Bluey',
            });
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateQueryKeys.all });
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateGroupQueryKeys.all });
    });

    it('invalidates the candidate-group query family when the group no longer exists (404)', async () => {
        mockApiPost.mockRejectedValue(new ApiError(404, 'Not Found'));
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useSetLibraryImportCandidateGroupMatch(), { wrapper: Wrapper });

        await act(async () => {
            await result.current
                .mutateAsync({
                    groupKey: 'query:bluey',
                    providerType: 'tvmaze',
                    externalIdKey: 'tvmaze',
                    externalId: '44796',
                    title: 'Bluey',
                })
                .catch(() => undefined);
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateGroupQueryKeys.all });
    });

    it('does not invalidate any query when the request fails with a non-404 status', async () => {
        mockApiPost.mockRejectedValue(new ApiError(500, 'Internal Server Error'));
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useSetLibraryImportCandidateGroupMatch(), { wrapper: Wrapper });

        await act(async () => {
            await result.current
                .mutateAsync({
                    groupKey: 'query:bluey',
                    providerType: 'tvmaze',
                    externalIdKey: 'tvmaze',
                    externalId: '44796',
                    title: 'Bluey',
                })
                .catch(() => undefined);
        });

        expect(invalidateSpy).not.toHaveBeenCalled();
    });
});
