import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';
import { describe, expect, it, vi, beforeEach } from 'vitest';

vi.mock('@/lib/api', () => ({
    apiDelete: vi.fn(),
}));

import { apiDelete } from '@/lib/api';
import { useDeleteContentFile } from './useDeleteContentFile';

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

describe('useDeleteContentFile', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('runs the caller onSuccess when the endpoint answers with no body', async () => {
        // Arrange
        vi.mocked(apiDelete).mockResolvedValue(undefined);
        const onSuccess = vi.fn();
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useDeleteContentFile(), { wrapper: Wrapper });

        // Act
        await act(async () => {
            await result.current.mutateAsync(
                { contentHierarchyItemId: 'content-1', mediaFileId: 'file-1' },
                { onSuccess },
            );
        });

        // Assert
        expect(apiDelete).toHaveBeenCalledWith('/content/content-1/files/file-1');
        expect(onSuccess).toHaveBeenCalledTimes(1);
    });
});
