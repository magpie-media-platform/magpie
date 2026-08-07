import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { useQualityFields, qualityFieldsQueryKeys } from './useQualityFields';
import * as api from '@/lib/api';

vi.mock('@/lib/api', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...original,
        apiGet: vi.fn(),
    };
});

vi.mock('@/auth', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/auth')>();
    return {
        ...original,
        useAuth: () => ({ isAuthenticated: true }) as ReturnType<typeof original.useAuth>,
    };
});

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
                staleTime: 0,
            },
        },
    });
}

function createWrapper(queryClient: QueryClient) {
    return function Wrapper({ children }: { children: ReactNode }) {
        return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
}

const mockResponse = {
    universalFields: [
        { name: 'Title', label: 'Title', isNumeric: false },
        { name: 'Protocol', label: 'Protocol', isNumeric: false },
        { name: 'Size', label: 'Size', isNumeric: true },
        { name: 'Age', label: 'Age', isNumeric: true },
        { name: 'Language', label: 'Language', isNumeric: false },
    ],
};

describe('useQualityFields', () => {
    let queryClient: QueryClient;

    beforeEach(() => {
        queryClient = createTestQueryClient();
        vi.clearAllMocks();
    });

    afterEach(() => {
        queryClient.clear();
    });

    describe('qualityFieldsQueryKeys', () => {
        it('generates correct query keys', () => {
            expect(qualityFieldsQueryKeys.all).toEqual(['qualityFields']);
            expect(qualityFieldsQueryKeys.qualityAttributes()).toEqual(['qualityFields', 'quality-attributes']);
        });
    });

    describe('useQualityFields', () => {
        it('fetches from the quality-attributes endpoint', async () => {
            vi.mocked(api.apiGet).mockResolvedValue(mockResponse);

            const { result } = renderHook(() => useQualityFields(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(api.apiGet).toHaveBeenCalledWith('/content-types/quality-attributes');
            expect(result.current.universalFields).toHaveLength(5);
        });

        it('returns empty arrays when loading', () => {
            vi.mocked(api.apiGet).mockImplementation(() => new Promise(() => {}));

            const { result } = renderHook(() => useQualityFields(), {
                wrapper: createWrapper(queryClient),
            });

            expect(result.current.isLoading).toBe(true);
            expect(result.current.universalFields).toEqual([]);
        });

        it('handles API errors gracefully', async () => {
            const error = new Error('Network error');
            vi.mocked(api.apiGet).mockRejectedValue(error);

            const { result } = renderHook(() => useQualityFields(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(result.current.error).toBe(error);
            expect(result.current.universalFields).toEqual([]);
        });

        it('surfaces the isNumeric flag from the API response', async () => {
            vi.mocked(api.apiGet).mockResolvedValue(mockResponse);

            const { result } = renderHook(() => useQualityFields(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            const size = result.current.universalFields.find((f) => f.name === 'Size');
            expect(size?.isNumeric).toBe(true);

            const title = result.current.universalFields.find((f) => f.name === 'Title');
            expect(title?.isNumeric).toBe(false);
        });
    });
});
