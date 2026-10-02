import type { PropsWithChildren } from 'react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiGet } from '@/lib/api';

import { useEventStream } from './useEventStream';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
}));

vi.mock('./useEventStream', () => ({
    useEventStream: vi.fn(),
}));

const mockedUseEventStream = vi.mocked(useEventStream);
const mockedApiGet = vi.mocked(apiGet);
let queryClient: QueryClient;

function Wrapper({ children }: PropsWithChildren) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe('useInvalidateQueriesOnEvents', () => {
    beforeEach(() => {
        queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        vi.clearAllMocks();
    });

    afterEach(() => {
        queryClient.clear();
        vi.useRealTimers();
    });

    it('does not subscribe a query without event coverage', () => {
        renderHook(
            () =>
                useInvalidateQueriesOnEvents([], [['test-resources']], {
                    exact: true,
                }),
            { wrapper: Wrapper },
        );

        expect(mockedUseEventStream).toHaveBeenCalledWith(expect.any(Function), {
            context: undefined,
            enabled: false,
            eventName: ['stream.reconnected'],
        });
    });

    it('ignores one hundred irrelevant events without invalidating or refetching', () => {
        const queryFn = vi.fn().mockResolvedValue({ id: 'subject-1' });
        const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');
        renderHook(
            () => {
                useQuery({
                    initialData: { id: 'subject-1' },
                    queryFn,
                    queryKey: ['subjects', 'detail', 'subject-1'],
                    staleTime: Infinity,
                });
                useInvalidateQueriesOnEvents(['subject.updated'], [['subjects', 'detail', 'subject-1']], {
                    entityIds: ['subject-1'],
                    exact: true,
                });
            },
            { wrapper: Wrapper },
        );
        const handler = mockedUseEventStream.mock.calls[0]?.[0];

        for (let index = 0; index < 100; index += 1) {
            handler?.({
                name: 'subject.updated',
                context: { 'subject.id': `subject-${index + 2}` },
            });
        }

        expect(queryFn).not.toHaveBeenCalled();
        expect(invalidateQueries).not.toHaveBeenCalled();
        expect(mockedApiGet).not.toHaveBeenCalled();
    });

    it('refetches a relevant active entity query exactly once', async () => {
        const queryFn = vi.fn().mockResolvedValue({ id: 'subject-1', title: 'Updated' });
        renderHook(
            () => {
                useQuery({
                    initialData: { id: 'subject-1', title: 'Original' },
                    queryFn,
                    queryKey: ['subjects', 'detail', 'subject-1'],
                    staleTime: Infinity,
                });
                useInvalidateQueriesOnEvents(['subject.updated'], [['subjects', 'detail', 'subject-1']], {
                    entityIds: ['subject-1'],
                    exact: true,
                });
            },
            { wrapper: Wrapper },
        );

        act(() => {
            mockedUseEventStream.mock.calls[0]?.[0]?.({
                name: 'subject.updated',
                context: { 'subject.id': 'subject-1' },
            });
        });

        await vi.waitFor(() => expect(queryFn).toHaveBeenCalledOnce());
        await vi.waitFor(() =>
            expect(queryClient.getQueryData(['subjects', 'detail', 'subject-1'])).toEqual({
                id: 'subject-1',
                title: 'Updated',
            }),
        );
    });

    it('coalesces many relevant events into one active query refetch', async () => {
        vi.useFakeTimers();
        const queryFn = vi.fn().mockResolvedValue({ items: [] });
        renderHook(
            () => {
                useQuery({
                    initialData: { items: [] },
                    queryFn,
                    queryKey: ['downloads', 'list', {}],
                    staleTime: Infinity,
                });
                useInvalidateQueriesOnEvents(['download.updated'], [['downloads', 'list', {}]], {
                    debounceMs: 100,
                    exact: true,
                    maxWaitMs: 400,
                });
            },
            { wrapper: Wrapper },
        );
        const handler = mockedUseEventStream.mock.calls[0]?.[0];

        for (let index = 0; index < 100; index += 1) {
            handler?.({
                name: 'download.updated',
                context: { 'download.id': `download-${index}` },
            });
        }
        await act(async () => {
            await vi.advanceTimersByTimeAsync(100);
        });

        expect(queryFn).toHaveBeenCalledOnce();
    });

    it('refetches a filtered list once for a fail-open create without resolving the new entity', async () => {
        const queryFn = vi.fn().mockResolvedValue({ items: [] });
        renderHook(
            () => {
                useQuery({
                    initialData: { items: [] },
                    queryFn,
                    queryKey: ['content', 'list', { contentType: 'movies' }],
                    staleTime: Infinity,
                });
                useInvalidateQueriesOnEvents(
                    ['content.hierarchyItem.created', 'content.hierarchyItem.updated'],
                    [['content', 'list', { contentType: 'movies' }]],
                    {
                        debounceMs: 0,
                        entityIds: [],
                        exact: true,
                        failOpenEventNames: ['content.hierarchyItem.created'],
                        predicate: (event) => event.context['content.impact.contentType'] === 'movies',
                        unprovableMembership: true,
                    },
                );
            },
            { wrapper: Wrapper },
        );

        act(() => {
            mockedUseEventStream.mock.calls[0]?.[0]?.({
                name: 'content.hierarchyItem.created',
                context: { 'content.hierarchyItemId': 'content-new' },
            });
        });

        expect(mockedUseEventStream.mock.calls[0]?.[1]?.enabled).toBe(true);
        await vi.waitFor(() => expect(queryFn).toHaveBeenCalledOnce());
        expect(mockedApiGet).not.toHaveBeenCalled();
    });

    it('does not let a pending event from one route scope invalidate the next scope', () => {
        vi.useFakeTimers();
        const queryFn = vi.fn().mockResolvedValue({});
        const { rerender } = renderHook(
            ({ id }) => {
                useQuery({
                    initialData: { id },
                    queryFn,
                    queryKey: ['content', 'detail', id],
                    staleTime: Infinity,
                });
                useInvalidateQueriesOnEvents(['content.hierarchyItem.updated'], [['content', 'detail', id]], {
                    debounceMs: 100,
                    entityIds: [id],
                    exact: true,
                });
            },
            { initialProps: { id: 'content-a' }, wrapper: Wrapper },
        );
        const routeAHandler = mockedUseEventStream.mock.calls[0]?.[0];

        routeAHandler?.({
            name: 'content.hierarchyItem.updated',
            context: { 'content.hierarchyItemId': 'content-a' },
        });
        rerender({ id: 'content-b' });
        routeAHandler?.({
            name: 'content.hierarchyItem.updated',
            context: { 'content.hierarchyItemId': 'content-a' },
        });
        act(() => {
            vi.advanceTimersByTime(100);
        });

        expect(queryFn).not.toHaveBeenCalled();
    });

    it('cancels pending debounce work on unmount', () => {
        vi.useFakeTimers();
        const queryFn = vi.fn().mockResolvedValue({});
        const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');
        const { unmount } = renderHook(
            () => {
                useQuery({
                    initialData: {},
                    queryFn,
                    queryKey: ['downloads', 'list', {}],
                    staleTime: Infinity,
                });
                useInvalidateQueriesOnEvents(['download.updated'], [['downloads', 'list', {}]], {
                    debounceMs: 100,
                    exact: true,
                });
            },
            { wrapper: Wrapper },
        );

        mockedUseEventStream.mock.calls[0]?.[0]?.({
            name: 'download.updated',
            context: { 'download.id': 'download-1' },
        });
        unmount();
        act(() => {
            vi.advanceTimersByTime(100);
        });

        expect(queryFn).not.toHaveBeenCalled();
        expect(invalidateQueries).not.toHaveBeenCalled();
    });

    it('marks inactive variants stale without network traffic', async () => {
        vi.useFakeTimers();
        const activeKey = ['downloads', 'list', { status: 'active' }] as const;
        const inactiveKey = ['downloads', 'list', { status: 'completed' }] as const;
        queryClient.setQueryData(inactiveKey, { items: [] });
        const queryFn = vi.fn().mockResolvedValue({ items: [] });
        renderHook(
            () => {
                useQuery({
                    initialData: { items: [] },
                    queryFn,
                    queryKey: activeKey,
                    staleTime: Infinity,
                });
                useInvalidateQueriesOnEvents(['download.updated'], [['downloads', 'list']], {
                    debounceMs: 100,
                    exact: false,
                });
            },
            { wrapper: Wrapper },
        );

        mockedUseEventStream.mock.calls[0]?.[0]?.({
            name: 'download.updated',
            context: { 'download.id': 'download-1' },
        });
        await act(async () => {
            await vi.advanceTimersByTimeAsync(100);
        });

        expect(queryFn).toHaveBeenCalledOnce();
        expect(queryClient.getQueryCache().find({ queryKey: inactiveKey })?.state.isInvalidated).toBe(true);
    });

    it('marks an inactive related query stale without refetching it', async () => {
        vi.useFakeTimers();
        const queryKey = ['content', 'detail', 'content-1'] as const;
        queryClient.setQueryData(queryKey, { id: 'content-1' });
        renderHook(
            () =>
                useInvalidateQueriesOnEvents(['download.updated'], [queryKey], {
                    entityIds: ['content-1'],
                    exact: true,
                }),
            { wrapper: Wrapper },
        );

        mockedUseEventStream.mock.calls[0]?.[0]?.({
            name: 'download.updated',
            context: { 'content.hierarchyItemId': 'content-1', 'download.id': 'download-1' },
        });
        await act(async () => {
            await vi.advanceTimersByTimeAsync(200);
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryClient.getQueryCache().find({ queryKey })?.state.isInvalidated).toBe(true);
    });

    it('rejects a filtered list event before making a context request', () => {
        const queryFn = vi.fn().mockResolvedValue({ items: [] });
        const predicate = vi.fn(() => true);
        renderHook(
            () => {
                useQuery({
                    initialData: { items: [] },
                    queryFn,
                    queryKey: ['downloads', 'list', { status: 'Downloading' }],
                    staleTime: Infinity,
                });
                useInvalidateQueriesOnEvents(['download.updated'], [['downloads', 'list', { status: 'Downloading' }]], {
                    debounceMs: 0,
                    exact: true,
                    prePredicate: (event) => event.context['download.changedFields']?.toLowerCase() === 'status',
                    predicate,
                    unprovableMembership: true,
                });
            },
            { wrapper: Wrapper },
        );

        act(() => {
            mockedUseEventStream.mock.calls[0]?.[0]?.({
                name: 'download.updated',
                context: {
                    'download.changedFields': 'Progress',
                    'download.id': 'download-1',
                },
            });
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(predicate).not.toHaveBeenCalled();
        expect(queryFn).not.toHaveBeenCalled();
    });

    it('does not queue another refetch while the target query is fetching', () => {
        let resolveFetch!: (value: unknown) => void;
        const queryFn = vi.fn(
            () =>
                new Promise((resolve) => {
                    resolveFetch = resolve;
                }),
        );
        const { unmount } = renderHook(
            () => {
                useQuery({
                    queryFn,
                    queryKey: ['downloads', 'list', {}],
                });
                useInvalidateQueriesOnEvents(['download.created'], [['downloads', 'list', {}]], {
                    exact: true,
                });
            },
            { wrapper: Wrapper },
        );

        act(() => {
            mockedUseEventStream.mock.calls[0]?.[0]?.({
                name: 'download.created',
                context: { 'download.id': 'download-1' },
            });
        });

        expect(queryFn).toHaveBeenCalledOnce();
        unmount();
        resolveFetch({});
    });

    it('reconciles an active query once across overlapping reconnect subscribers', async () => {
        const queryFn = vi.fn().mockResolvedValue({});
        renderHook(
            () => {
                useQuery({
                    initialData: {},
                    queryFn,
                    queryKey: ['shared-shell', 'status'],
                    staleTime: Infinity,
                });
                useInvalidateQueriesOnEvents(['status.updated'], [['shared-shell', 'status']], {
                    exact: true,
                });
                useInvalidateQueriesOnEvents(['health.updated'], [['shared-shell', 'status']], {
                    exact: true,
                });
            },
            { wrapper: Wrapper },
        );
        const reconnect = { name: 'stream.reconnected', context: {} };

        act(() => {
            mockedUseEventStream.mock.calls[0]?.[0]?.(reconnect);
            mockedUseEventStream.mock.calls[1]?.[0]?.(reconnect);
        });

        await vi.waitFor(() => expect(queryFn).toHaveBeenCalledOnce());
    });
});
