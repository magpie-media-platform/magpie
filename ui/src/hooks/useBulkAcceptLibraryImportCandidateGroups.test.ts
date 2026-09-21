import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';
import { useBulkAcceptLibraryImportCandidateGroups } from './useBulkAcceptLibraryImportCandidateGroups';
import { libraryImportCandidateGroupQueryKeys } from './useLibraryImportCandidateGroups';
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

describe('useBulkAcceptLibraryImportCandidateGroups', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('posts the root folder filter and excluded group keys to the bulk-accept endpoint', async () => {
        const queued = [{ candidateId: 'm-1', jobExecutionId: 'job-1' }];
        mockApiPost.mockResolvedValue({ queued, skipped: [] });
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useBulkAcceptLibraryImportCandidateGroups(), { wrapper: Wrapper });

        let response: { queued: unknown[]; skipped: unknown[] } | undefined;
        await act(async () => {
            response = await result.current.mutateAsync({
                rootFolderId: 'folder-1',
                excludeGroupKeys: ['query:stale show'],
                monitoringMode: 'Future',
            });
        });

        expect(mockApiPost).toHaveBeenCalledWith('/import/candidate-groups/bulk-accept', {
            filter: {
                rootFolderId: 'folder-1',
                excludeGroupKeys: ['query:stale show'],
            },
            monitoringMode: 'Future',
        });
        expect(response).toEqual({ queued, skipped: [] });
    });

    it('invalidates the candidate and candidate-group query families on success', async () => {
        mockApiPost.mockResolvedValue({ queued: [], skipped: [] });
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useBulkAcceptLibraryImportCandidateGroups(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({ excludeGroupKeys: [], monitoringMode: 'Inherit' });
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateQueryKeys.all });
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateGroupQueryKeys.all });
    });
});
