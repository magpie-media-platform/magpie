import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';
import { useBulkAcceptLibraryImportCandidates } from './useBulkAcceptLibraryImportCandidates';
import { libraryImportCandidateQueryKeys } from './useLibraryImportCandidates';

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

describe('useBulkAcceptLibraryImportCandidates', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('posts to bulk-accept with an explicit id list', async () => {
        const queued = [
            { candidateId: 'm-1', jobExecutionId: 'job-1' },
            { candidateId: 'm-2', jobExecutionId: 'job-2' },
        ];
        mockApiPost.mockResolvedValue({ queued, skipped: [] });
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useBulkAcceptLibraryImportCandidates(), { wrapper: Wrapper });

        let response:
            | {
                  queued: { candidateId: string; jobExecutionId: string }[];
                  skipped: { candidateId: string; reason: string }[];
              }
            | undefined;
        await act(async () => {
            response = await result.current.mutateAsync({ ids: ['m-1', 'm-2'] });
        });

        expect(mockApiPost).toHaveBeenCalledWith('/import/candidates/bulk-accept', {
            ids: ['m-1', 'm-2'],
        });
        expect(response).toEqual({ queued, skipped: [] });
    });

    it('invalidates the import/candidates query family on success', async () => {
        mockApiPost.mockResolvedValue({ queued: [{ candidateId: 'm-1', jobExecutionId: 'job-1' }], skipped: [] });
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useBulkAcceptLibraryImportCandidates(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({ ids: ['m-1'] });
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateQueryKeys.all });
    });
});
