import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return { ...actual, apiPost: vi.fn() };
});

import { apiPost, ApiError } from '@/lib/api';
import { useAcceptLibraryImportCandidateGroup } from './useAcceptLibraryImportCandidateGroup';
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

describe('useAcceptLibraryImportCandidateGroup', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('posts to candidate-groups/accept with the group key and root folder as query params', async () => {
        mockApiPost.mockResolvedValue({ queued: [{ candidateId: 'c-1', jobExecutionId: 'job-1' }], skipped: [] });
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useAcceptLibraryImportCandidateGroup(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({
                groupKey: '/media/tv/Bluey',
                rootFolderId: 'folder-1',
                monitoringMode: 'Inherit',
            });
        });

        expect(mockApiPost).toHaveBeenCalledWith(
            '/import/candidate-groups/accept?groupKey=%2Fmedia%2Ftv%2FBluey&rootFolderId=folder-1&monitoringMode=Inherit',
        );
    });

    it('invalidates both the candidate and candidate-group query families on success', async () => {
        mockApiPost.mockResolvedValue({ queued: [], skipped: [] });
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useAcceptLibraryImportCandidateGroup(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({ groupKey: 'query:the office', monitoringMode: 'Inherit' });
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateQueryKeys.all });
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateGroupQueryKeys.all });
    });

    it('holds the invalidation until invalidate is called when it is deferred', async () => {
        mockApiPost.mockResolvedValue({ queued: [], skipped: [] });
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useAcceptLibraryImportCandidateGroup({ deferInvalidation: true }), {
            wrapper: Wrapper,
        });

        await act(async () => {
            await result.current.mutateAsync({ groupKey: 'query:the office', monitoringMode: 'Inherit' });
        });
        expect(invalidateSpy).not.toHaveBeenCalled();

        act(() => {
            result.current.invalidate();
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateQueryKeys.all });
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateGroupQueryKeys.all });
    });

    it('invalidates the candidate-group query family when the group no longer exists (404)', async () => {
        mockApiPost.mockRejectedValue(new ApiError(404, 'Not Found'));
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useAcceptLibraryImportCandidateGroup(), { wrapper: Wrapper });

        await act(async () => {
            await result.current
                .mutateAsync({ groupKey: 'query:the office', monitoringMode: 'Inherit' })
                .catch(() => undefined);
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateGroupQueryKeys.all });
    });

    it('does not invalidate any query when the request fails with a non-404 status', async () => {
        mockApiPost.mockRejectedValue(new ApiError(500, 'Internal Server Error'));
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useAcceptLibraryImportCandidateGroup(), { wrapper: Wrapper });

        await act(async () => {
            await result.current
                .mutateAsync({ groupKey: 'query:the office', monitoringMode: 'Inherit' })
                .catch(() => undefined);
        });

        expect(invalidateSpy).not.toHaveBeenCalled();
    });
});
