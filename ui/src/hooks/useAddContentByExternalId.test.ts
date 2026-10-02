import { createElement, type ReactNode } from 'react';

import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { installSharedWorkerHarness } from '@/test/sharedWorkerHarness';

import { AuthContext, Role, type AuthContextValue } from '@/auth';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';
import { useAddContentByExternalId, type AddContentByExternalIdRequest } from './useAddContentByExternalId';
import { contentQueryKeys } from './useContent';
import { useGlobalEventStreamConnection } from './useEventStream';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    const Wrapper = ({ children }: { children: ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
    return { Wrapper, queryClient };
}

const authedAuthValue: AuthContextValue = {
    isLoading: false,
    isAuthenticated: true,
    user: { id: 'u1', username: 'admin', role: Role.Admin },
    hasPermission: () => true,
    getRequiredRole: () => undefined,
    hasRole: () => true,
    signOut: async () => {},
    login: async () => {},
};

function createAuthedWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    const ActiveEventStream = ({ children }: { children: ReactNode }) => {
        useGlobalEventStreamConnection(true);
        return children;
    };
    const Wrapper = ({ children }: { children: ReactNode }) =>
        createElement(
            AuthContext.Provider,
            { value: authedAuthValue },
            createElement(
                QueryClientProvider,
                { client: queryClient },
                createElement(ActiveEventStream, null, children),
            ),
        );
    return { Wrapper, queryClient };
}

type Listener = (evt: { data: string }) => void;

interface MockEventSource {
    url: string;
    closed: boolean;
    onmessage: Listener | null;
    onopen: (() => void) | null;
    onerror: (() => void) | null;
    emit: (data: string) => void;
    close: () => void;
}

let lastSource: MockEventSource | null = null;

function installFakeEventSource() {
    class FakeEventSource implements MockEventSource {
        url: string;
        closed = false;
        onmessage: Listener | null = null;
        onopen: (() => void) | null = null;
        onerror: (() => void) | null = null;
        constructor(url: string) {
            this.url = url;
            // eslint-disable-next-line @typescript-eslint/no-this-alias
            lastSource = this;
        }
        emit(data: string) {
            this.onmessage?.({ data });
        }
        close() {
            this.closed = true;
        }
    }
    (globalThis as unknown as { EventSource: typeof FakeEventSource }).EventSource = FakeEventSource;
    installSharedWorkerHarness(FakeEventSource);
}

function metadataRefreshedFrame(itemId: string): string {
    return JSON.stringify({
        name: 'metadata.refreshed',
        context: { 'content.hierarchyItemId': itemId },
    });
}

describe('useAddContentByExternalId', () => {
    const mockApiPost = vi.mocked(apiPost);

    const request: AddContentByExternalIdRequest = {
        providerType: 'provider-a',
        externalIdKey: 'provider-a.movie',
        externalId: '27205',
        contentType: 'movies',
    };

    beforeEach(() => {
        vi.clearAllMocks();
        lastSource = null;
        installFakeEventSource();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('posts to /content/from-external-id', async () => {
        const newId = 'new-content-id';
        mockApiPost.mockResolvedValue(newId);
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useAddContentByExternalId(), { wrapper: Wrapper });

        let returnedId: string | undefined;
        await act(async () => {
            returnedId = await result.current.mutateAsync(request);
        });

        expect(mockApiPost).toHaveBeenCalledTimes(1);
        expect(mockApiPost).toHaveBeenCalledWith('/content/from-external-id', request);
        expect(returnedId).toBe(newId);
    });

    it('sends the root folder as a query parameter', async () => {
        mockApiPost.mockResolvedValue('new-content-id');
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useAddContentByExternalId(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync({ ...request, rootFolderId: 'root-folder-id' });
        });

        expect(mockApiPost).toHaveBeenCalledWith('/content/from-external-id?rootFolderId=root-folder-id', request);
    });

    it('invalidates the content list query family immediately on success', async () => {
        mockApiPost.mockResolvedValue('new-id');
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useAddContentByExternalId(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync(request);
        });

        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: contentQueryKeys.list() });
    });

    it('does not invalidate when the mutation fails', async () => {
        mockApiPost.mockRejectedValue(new Error('boom'));
        const { Wrapper, queryClient } = createWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useAddContentByExternalId(), { wrapper: Wrapper });

        await expect(
            act(async () => {
                await result.current.mutateAsync(request);
            }),
        ).rejects.toThrow('boom');

        expect(invalidateSpy).not.toHaveBeenCalled();
    });

    it('surfaces an API error to the caller', async () => {
        const apiError = new Error('Bad request');
        mockApiPost.mockRejectedValue(apiError);
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useAddContentByExternalId(), { wrapper: Wrapper });

        await expect(
            act(async () => {
                await result.current.mutateAsync(request);
            }),
        ).rejects.toThrow('Bad request');
    });

    it('refetches when metadata refresh completes for the added item', async () => {
        const newId = 'added-item-id';
        mockApiPost.mockResolvedValue(newId);
        const { Wrapper, queryClient } = createAuthedWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useAddContentByExternalId(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync(request);
        });

        expect(invalidateSpy).toHaveBeenCalledTimes(1);
        expect(lastSource).not.toBeNull();
        expect(lastSource!.url).toContain('/api/events/stream');

        act(() => {
            lastSource!.emit(metadataRefreshedFrame(newId));
        });

        expect(invalidateSpy).toHaveBeenCalledTimes(2);
    });

    it('ignores metadata.refreshed events for items it did not add', async () => {
        mockApiPost.mockResolvedValue('added-item-id');
        const { Wrapper, queryClient } = createAuthedWrapper();
        const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

        const { result } = renderHook(() => useAddContentByExternalId(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.mutateAsync(request);
        });

        expect(invalidateSpy).toHaveBeenCalledTimes(1);
        expect(lastSource).not.toBeNull();

        act(() => {
            lastSource!.emit(metadataRefreshedFrame('some-other-item'));
        });

        expect(invalidateSpy).toHaveBeenCalledTimes(1);
    });
});
