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
    useTestDownloadClientSettings,
    useTestIndexer,
    useTestMediaServer,
    useTestMediaServerSettings,
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

        it('posts to /indexers/{id}/connections/{connectionId}/test for a targeted connection', async () => {
            mockApiPost.mockResolvedValue({ success: true, message: 'Connected' });

            const { result } = renderHook(() => useTestIndexer(), { wrapper: createWrapper() });

            await act(async () => {
                await result.current.test({ indexerId: 'idx-1', connectionId: 'idx-conn-1' });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/indexers/idx-1/connections/idx-conn-1/test');
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

    describe('useTestMediaServerSettings', () => {
        it('posts the form payload to /media-servers/test', async () => {
            const expected: ConnectionTestResult = { success: true, message: 'Connected' };
            mockApiPost.mockResolvedValue(expected);

            const { result } = renderHook(() => useTestMediaServerSettings(), { wrapper: createWrapper() });

            const request = { providerType: 'plex', id: 'ms-1', settings: { host: 'localhost' } };
            let returned: ConnectionTestResult | undefined;
            await act(async () => {
                returned = await result.current.test(request);
            });

            expect(mockApiPost).toHaveBeenCalledWith(
                '/media-servers/test',
                request,
                expect.objectContaining({ signal: expect.any(AbortSignal) }),
            );
            expect(returned).toEqual(expected);
        });
    });

    describe('useTestDownloadClientSettings', () => {
        it('posts the form payload to /download-clients/test', async () => {
            const expected: ConnectionTestResult = { success: false, message: 'Unreachable' };
            mockApiPost.mockResolvedValue(expected);

            const { result } = renderHook(() => useTestDownloadClientSettings(), { wrapper: createWrapper() });

            const request = { providerType: 'sabnzbd', settings: { host: 'localhost' } };
            let returned: ConnectionTestResult | undefined;
            await act(async () => {
                returned = await result.current.test(request);
            });

            expect(mockApiPost).toHaveBeenCalledWith(
                '/download-clients/test',
                request,
                expect.objectContaining({ signal: expect.any(AbortSignal) }),
            );
            expect(returned).toEqual(expected);
        });
    });

    describe('cancel (settings probe)', () => {
        it('aborts the in-flight request and resets isPending when cancelled', async () => {
            mockApiPost.mockReturnValue(new Promise<ConnectionTestResult>(() => {}));

            const { result } = renderHook(() => useTestMediaServerSettings(), { wrapper: createWrapper() });

            act(() => {
                result.current.test({ providerType: 'plex', settings: {} }).catch(() => {});
            });

            await waitFor(() => expect(result.current.isPending).toBe(true));

            const options = mockApiPost.mock.calls[0][2] as { signal?: AbortSignal };
            expect(options.signal).toBeInstanceOf(AbortSignal);
            expect(options.signal?.aborted).toBe(false);

            act(() => {
                result.current.cancel();
            });

            expect(options.signal?.aborted).toBe(true);
            await waitFor(() => expect(result.current.isPending).toBe(false));
        });

        it('lets a subsequent test start after a cancel', async () => {
            mockApiPost.mockReturnValueOnce(new Promise<ConnectionTestResult>(() => {}));

            const { result } = renderHook(() => useTestDownloadClientSettings(), { wrapper: createWrapper() });

            act(() => {
                result.current.test({ providerType: 'sabnzbd', settings: {} }).catch(() => {});
            });
            await waitFor(() => expect(result.current.isPending).toBe(true));

            act(() => {
                result.current.cancel();
            });
            await waitFor(() => expect(result.current.isPending).toBe(false));

            const expected: ConnectionTestResult = { success: true, message: 'Reachable' };
            mockApiPost.mockResolvedValueOnce(expected);

            let returned: ConnectionTestResult | undefined;
            await act(async () => {
                returned = await result.current.test({ providerType: 'sabnzbd', settings: {} });
            });

            expect(returned).toEqual(expected);
        });
    });
});
