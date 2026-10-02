import type { PropsWithChildren } from 'react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiGet } from '@/lib/api';

import { useEventStream, type EventStreamPayload } from './useEventStream';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
}));

vi.mock('./useEventStream', () => ({
    useEventStream: vi.fn(),
}));

const mockedUseEventStream = vi.mocked(useEventStream);
const mockedApiGet = vi.mocked(apiGet);
const ACTIVE_ID = 'content-active';
const PARENT_ID = 'content-parent';
const ROOT_ID = 'content-root';

let queryClient: QueryClient;

function Wrapper({ children }: PropsWithChildren) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

function renderContentDetailSurface(queryFn: () => Promise<unknown>) {
    return renderHook(
        () => {
            useQuery({
                initialData: { id: ACTIVE_ID },
                queryFn,
                queryKey: ['content', 'detail', ACTIVE_ID],
                staleTime: Infinity,
            });
            useInvalidateQueriesOnEvents(
                [
                    'content.hierarchyItem.created',
                    'content.hierarchyItem.deleted',
                    'content.hierarchyItem.updated',
                    'content.fulfillment.changed',
                    'metadata.refreshed',
                    'download.updated',
                    'job.execution.completed',
                ],
                [['content', 'detail', ACTIVE_ID]],
                { debounceMs: 0, entityIds: [ACTIVE_ID], exact: true },
            );
        },
        { wrapper: Wrapper },
    );
}

function emit(event: EventStreamPayload) {
    act(() => {
        mockedUseEventStream.mock.calls[0]?.[0]?.(event);
    });
}

describe('content event relevance', () => {
    beforeEach(() => {
        queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        vi.clearAllMocks();
    });

    afterEach(() => {
        queryClient.clear();
    });

    it('rejects a burst of sibling events sharing the open item parent without any request or refetch', () => {
        queryClient.setQueryData(['content', 'detail', ACTIVE_ID], { id: ACTIVE_ID, parentId: PARENT_ID });
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();
        invalidateQueries.mockClear();

        for (let index = 0; index < 100; index += 1) {
            emit({
                name: index % 2 === 0 ? 'content.hierarchyItem.updated' : 'metadata.refreshed',
                context: {
                    'content.hierarchyItemId': `sibling-${index}`,
                    'content.impact.ancestorIds': `${PARENT_ID},${ROOT_ID}`,
                    'content.impact.metadataContainerId': `sibling-container-${index}`,
                    'content.impact.parentId': PARENT_ID,
                    'content.impact.rootId': ROOT_ID,
                },
            });
        }

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).not.toHaveBeenCalled();
        expect(invalidateQueries).not.toHaveBeenCalled();
    });

    it('rejects an event for a descendant of a sibling that shares the open item ancestry', () => {
        queryClient.setQueryData(['content', 'detail', ACTIVE_ID], { id: ACTIVE_ID, parentId: PARENT_ID });
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();

        emit({
            name: 'content.hierarchyItem.updated',
            context: {
                'content.hierarchyItemId': 'sibling-child',
                'content.impact.ancestorIds': `sibling-0,${PARENT_ID},${ROOT_ID}`,
                'content.impact.parentId': 'sibling-0',
                'content.impact.rootId': ROOT_ID,
            },
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).not.toHaveBeenCalled();
    });

    it('routes a relationship endpoint added after an earlier unrelated event', async () => {
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();

        emit({
            name: 'content.hierarchyItem.updated',
            context: { 'content.hierarchyItemId': 'someone-else' },
        });
        act(() => {
            queryClient.setQueryData(['content', 'detail', ACTIVE_ID], {
                id: ACTIVE_ID,
                metadataContainerId: 'container-active',
                parentId: PARENT_ID,
            });
        });
        emit({
            name: 'metadata.refreshed',
            context: {
                'content.hierarchyItemId': 'content-other',
                'content.impact.metadata.addedContainerIds': 'container-active',
                'content.impact.metadata.sourceContainerId': 'container-other',
            },
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).toHaveBeenCalledTimes(1);
    });

    it('rejects unrelated download and job events without any request', () => {
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();

        emit({ name: 'download.updated', context: { 'download.id': 'download-other' } });
        emit({ name: 'job.execution.completed', context: { 'execution.id': 'execution-other' } });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).not.toHaveBeenCalled();
    });

    it('refetches the active query exactly once for a direct content event', async () => {
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();

        emit({
            name: 'content.hierarchyItem.updated',
            context: { 'content.hierarchyItemId': ACTIVE_ID, 'content.impact.parentId': PARENT_ID },
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).toHaveBeenCalledTimes(1);
    });

    it('refetches an ancestor surface when a descendant is deleted with its subtree', async () => {
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();

        emit({
            name: 'content.hierarchyItem.deleted',
            context: {
                'content.hierarchyItemId': 'content-child',
                'content.impact.ancestorIds': `${ACTIVE_ID},${ROOT_ID}`,
                'content.impact.parentId': ACTIVE_ID,
                'content.impact.subtreeIds': 'content-child,content-grandchild',
            },
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).toHaveBeenCalledTimes(1);
    });

    it('refetches both the old and the new scope when an item is reparented', async () => {
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();

        emit({
            name: 'content.hierarchyItem.updated',
            context: {
                'content.hierarchyItemId': 'content-moved',
                'content.impact.parentId': 'content-new-parent',
                'content.impact.previous.parentId': ACTIVE_ID,
            },
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).toHaveBeenCalledTimes(1);
    });

    it('shares the active snapshot so a metadata relationship endpoint routes to the surface', async () => {
        queryClient.setQueryData(['content', 'detail', ACTIVE_ID], {
            id: ACTIVE_ID,
            metadataContainerId: 'container-active',
            parentId: PARENT_ID,
        });
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();

        emit({
            name: 'metadata.refreshed',
            context: {
                'content.hierarchyItemId': 'content-other',
                'content.impact.metadata.addedContainerIds': 'container-active',
                'content.impact.metadata.sourceContainerId': 'container-other',
            },
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).toHaveBeenCalledTimes(1);
    });

    it('refetches once when a truncated subtree deletion cannot be proven irrelevant', async () => {
        queryClient.setQueryData(['content', 'detail', ACTIVE_ID], { id: ACTIVE_ID, parentId: PARENT_ID });
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();

        emit({
            name: 'content.hierarchyItem.deleted',
            context: {
                'content.hierarchyItemId': 'content-elsewhere',
                'content.impact.subtreeIds': 'content-elsewhere,content-elsewhere-child',
                'content.impact.subtreeTruncated': 'true',
            },
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).toHaveBeenCalledTimes(1);
    });

    it('generates no traffic for an inactive query variant', () => {
        queryClient.setQueryData(['content', 'detail', 'content-inactive'], { id: 'content-inactive' });
        const queryFn = vi.fn().mockResolvedValue({ id: ACTIVE_ID });
        renderContentDetailSurface(queryFn);
        queryFn.mockClear();

        emit({
            name: 'content.hierarchyItem.updated',
            context: { 'content.hierarchyItemId': 'content-inactive' },
        });

        expect(mockedApiGet).not.toHaveBeenCalled();
        expect(queryFn).not.toHaveBeenCalled();
    });
});
