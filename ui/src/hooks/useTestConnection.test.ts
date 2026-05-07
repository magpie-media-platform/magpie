import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';
import {
    useTestDownloadClient,
    useTestIndexer,
    useTestMediaServer,
    type ConnectionTestResult,
} from './useTestConnection';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('useTestConnection', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('useTestDownloadClient', () => {
        it('posts to /download-clients/{id}/test and returns the result', async () => {
            const expected: ConnectionTestResult = { success: true, message: 'Reachable' };
            mockApiPost.mockResolvedValue(expected);

            const { result } = renderHook(() => useTestDownloadClient(), { wrapper: createWrapper() });

            let returned: ConnectionTestResult | undefined;
            await act(async () => {
                returned = await result.current.test('dc-1');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/download-clients/dc-1/test');
            expect(returned).toEqual(expected);
        });

        it('exposes isPending while in flight', async () => {
            let resolvePost: (value: ConnectionTestResult) => void;
            const postPromise = new Promise<ConnectionTestResult>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useTestDownloadClient(), { wrapper: createWrapper() });

            act(() => {
                void result.current.test('dc-1');
            });

            await waitFor(() => {
                expect(result.current.isPending).toBe(true);
            });

            act(() => {
                resolvePost!({ success: true, message: 'ok' });
            });

            await waitFor(() => {
                expect(result.current.isPending).toBe(false);
            });
        });

        it('surfaces errors via the rejected promise', async () => {
            mockApiPost.mockRejectedValue(new Error('boom'));

            const { result } = renderHook(() => useTestDownloadClient(), { wrapper: createWrapper() });

            await expect(result.current.test('dc-1')).rejects.toThrow('boom');
        });
    });

    describe('useTestIndexer', () => {
        it('posts to /indexers/{id}/test', async () => {
            mockApiPost.mockResolvedValue({ success: false, message: 'Bad creds' });

            const { result } = renderHook(() => useTestIndexer(), { wrapper: createWrapper() });

            await act(async () => {
                await result.current.test('idx-1');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/indexers/idx-1/test');
        });
    });

    describe('useTestMediaServer', () => {
        it('posts to /media-servers/{id}/test', async () => {
            mockApiPost.mockResolvedValue({ success: true, message: 'Connected' });

            const { result } = renderHook(() => useTestMediaServer(), { wrapper: createWrapper() });

            await act(async () => {
                await result.current.test('ms-1');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/media-servers/ms-1/test');
        });
    });
});
