import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';
import { useStartRootFolderContentDiscovery } from './useStartRootFolderContentDiscovery';
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

describe('useStartRootFolderContentDiscovery', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('runs RootFolderContentDiscovery through the generic jobs endpoint', async () => {
        const executionId = 'execution-123';
        mockApiPost.mockResolvedValue(executionId);
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useStartRootFolderContentDiscovery(), { wrapper: Wrapper });

        let returnedExecutionId: string | null | undefined;
        await act(async () => {
            const disposition = await result.current.mutateAsync({
                rootFolderId: 'folder-1',
            });
            returnedExecutionId = disposition.executionId;
        });

        expect(mockApiPost).toHaveBeenCalledTimes(1);
        expect(mockApiPost).toHaveBeenCalledWith('/jobs/enqueue', {
            jobType: 'RootFolderContentDiscovery',
            mode: 'Queue',
            parameters: { rootFolderId: 'folder-1' },
        });
        expect(returnedExecutionId).toBe(executionId);
    });

    it('invalidates the import/candidates query family on success', async () => {
        mockApiPost.mockResolvedValue('id');
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useStartRootFolderContentDiscovery(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({
                rootFolderId: 'folder-1',
            });
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateQueryKeys.all });
    });

    it('does not invalidate when the mutation fails', async () => {
        mockApiPost.mockRejectedValue(new Error('boom'));
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useStartRootFolderContentDiscovery(), { wrapper: Wrapper });

        await expect(
            act(async () => {
                await result.current.mutateAsync({
                    rootFolderId: 'folder-1',
                });
            }),
        ).rejects.toThrow('boom');

        expect(invalidateSpy).not.toHaveBeenCalled();
    });
});
