import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { useRemoveContent } from './useRemoveContent';
import * as api from '@/lib/api';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiDelete: vi.fn(),
    apiPatch: vi.fn(),
    apiPost: vi.fn(),
    apiPut: vi.fn(),
    buildUrl: vi.fn(),
}));

function createWrapper(queryClient: QueryClient) {
    return function Wrapper({ children }: { children: ReactNode }) {
        return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
}

describe('content command hooks', () => {
    let queryClient: QueryClient;

    beforeEach(() => {
        queryClient = new QueryClient({
            defaultOptions: {
                queries: {
                    retry: false,
                    gcTime: 0,
                    staleTime: 0,
                },
            },
        });
        vi.clearAllMocks();
    });

    afterEach(() => {
        queryClient.clear();
    });

    it.each([
        [false, '/content/content-1?deleteFromLibrary=false'],
        [true, '/content/content-1?deleteFromLibrary=true'],
    ])('deletes content with deleteFromLibrary=%s', async (deleteFiles, expectedPath) => {
        vi.mocked(api.apiDelete).mockResolvedValue(undefined);

        const { result } = renderHook(() => useRemoveContent(), {
            wrapper: createWrapper(queryClient),
        });

        result.current.mutate({
            contentHierarchyItemId: 'content-1',
            deleteFiles,
        });

        await waitFor(() => expect(result.current.isSuccess).toBe(true));

        expect(api.apiDelete).toHaveBeenCalledWith(expectedPath);
    });
});
