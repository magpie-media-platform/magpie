import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';
import { useSetLibraryImportMatch } from './useSetLibraryImportMatch';
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

describe('useSetLibraryImportMatch', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('posts the chosen candidate to /import/candidates/{id}/match without the id in the body', async () => {
        mockApiPost.mockResolvedValue({ id: 'm-1' });
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useSetLibraryImportMatch(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({
                id: 'm-1',
                providerType: 'provider-a',
                externalIdKey: 'provider-a.movie',
                externalId: '550',
                title: 'Fight Club',
                subtitle: '1999',
                imageUrl: null,
                levelSlug: null,
            });
        });

        expect(mockApiPost).toHaveBeenCalledWith('/import/candidates/m-1/match', {
            providerType: 'provider-a',
            externalIdKey: 'provider-a.movie',
            externalId: '550',
            title: 'Fight Club',
            subtitle: '1999',
            imageUrl: null,
            levelSlug: null,
        });
    });

    it('invalidates the import/candidates query family on success', async () => {
        mockApiPost.mockResolvedValue({ id: 'm-1' });
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useSetLibraryImportMatch(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({
                id: 'm-1',
                providerType: 'provider-a',
                externalIdKey: 'provider-a.movie',
                externalId: '550',
                title: 'Fight Club',
            });
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: libraryImportCandidateQueryKeys.all });
    });
});
