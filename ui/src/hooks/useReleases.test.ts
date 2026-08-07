import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
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
}));

import { apiPost } from '@/lib/api';
import { useGrabRelease } from './useReleases';

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

describe('useGrabRelease', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('starts a release acquisition job and returns the disposition', async () => {
        const executionId = 'exec-uuid-123';
        mockApiPost.mockResolvedValue(executionId);
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useGrabRelease(), { wrapper: Wrapper });

        let returnedExecutionId: string | null | undefined;
        await act(async () => {
            const disposition = await result.current.mutateAsync({
                id: 'release-1',
                contentHierarchyItemId: 'content-1',
            });
            returnedExecutionId = disposition.executionId;
        });

        expect(mockApiPost).toHaveBeenCalledWith('/jobs/execute-immediate', {
            jobType: 'ReleaseDownloadItem',
            parameters: {
                releaseId: 'release-1',
                ignoreFailedTaint: 'true',
                contentHierarchyItemId: 'content-1',
            },
        });
        expect(returnedExecutionId).toBe(executionId);
    });

    it('surfaces an error from the API call', async () => {
        const apiError = new Error('Not found');
        mockApiPost.mockRejectedValue(apiError);
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useGrabRelease(), { wrapper: Wrapper });

        await expect(
            act(async () => {
                await result.current.mutateAsync({
                    id: 'missing-release',
                    contentHierarchyItemId: 'content-1',
                });
            }),
        ).rejects.toThrow('Not found');
    });
});
