import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';
import { usePreviewByExternalId, type ContentPreview, type PreviewByExternalIdRequest } from './usePreviewByExternalId';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    const Wrapper = ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
    return { Wrapper };
}

const preview: ContentPreview = {
    providerType: 'provider-a',
    contentType: 'movies',
    levelSlug: 'movie',
    title: 'Inception',
    genres: ['Action'],
    contentRatings: [],
    images: [],
    previews: [],
    externalIds: [],
    relatedSubjects: [],
};

describe('usePreviewByExternalId', () => {
    const mockApiPost = vi.mocked(apiPost);

    const request: PreviewByExternalIdRequest = {
        providerType: 'provider-a',
        externalIdKey: 'provider-a.movie',
        externalId: '27205',
        contentType: 'movies',
    };

    beforeEach(() => vi.clearAllMocks());
    afterEach(() => vi.restoreAllMocks());

    it('posts to /content/preview-by-external-id and returns the preview', async () => {
        mockApiPost.mockResolvedValue(preview);
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => usePreviewByExternalId(), { wrapper: Wrapper });

        let returned: ContentPreview | undefined;
        await act(async () => {
            returned = await result.current.mutateAsync(request);
        });

        expect(mockApiPost).toHaveBeenCalledWith('/content/preview-by-external-id', request);
        expect(returned).toEqual(preview);
    });

    it('surfaces the error to the caller', async () => {
        mockApiPost.mockRejectedValue(new Error('Not found'));
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => usePreviewByExternalId(), { wrapper: Wrapper });

        await expect(
            act(async () => {
                await result.current.mutateAsync(request);
            }),
        ).rejects.toThrow('Not found');
    });
});
