import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('./useEventStream', () => ({
    useEventStream: vi.fn(),
    useGlobalEventStreamConnection: vi.fn(),
}));

vi.mock('@/lib/api', async () => {
    const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
    return { ...actual, apiGet: vi.fn() };
});

import { apiGet } from '@/lib/api';
import {
    deriveOnboardingSteps,
    isOnboardingComplete,
    useOnboardingStatus,
    type OnboardingStatus,
} from './useOnboardingStatus';
import { useEventStream } from './useEventStream';
import { indexerQueryKeys } from './useIndexers';
import { downloadClientQueryKeys } from './useDownloadClients';
import { rootFolderQueryKeys } from './useRootFolders';

const mockedUseEventStream = vi.mocked(useEventStream);
const mockApiGet = vi.mocked(apiGet);

const allDone: OnboardingStatus = {
    hasIndexer: true,
    hasDownloadClient: true,
    hasRootFolder: true,
    hasMonitoredContent: true,
    isComplete: true,
};

describe('deriveOnboardingSteps', () => {
    it('returns the four steps in pipeline order', () => {
        const steps = deriveOnboardingSteps(undefined, 'Magpie');
        expect(steps.map((s) => s.key)).toEqual(['indexer', 'downloadClient', 'rootFolder', 'monitoredContent']);
    });

    it('treats every step as not done when status is undefined', () => {
        const steps = deriveOnboardingSteps(undefined, 'Magpie');
        expect(steps.every((s) => !s.done)).toBe(true);
    });

    it('maps each flag to the matching step done-state', () => {
        const steps = deriveOnboardingSteps(
            {
                hasIndexer: true,
                hasDownloadClient: false,
                hasRootFolder: true,
                hasMonitoredContent: false,
                isComplete: false,
            },
            'Magpie',
        );
        const byKey = Object.fromEntries(steps.map((s) => [s.key, s.done]));
        expect(byKey).toEqual({
            indexer: true,
            downloadClient: false,
            rootFolder: true,
            monitoredContent: false,
        });
    });

    it('points each step at its setup route', () => {
        const steps = deriveOnboardingSteps(allDone, 'Magpie');
        const byKey = Object.fromEntries(steps.map((s) => [s.key, s.to]));
        expect(byKey).toEqual({
            indexer: '/admin/indexers',
            downloadClient: '/admin/download-clients',
            rootFolder: '/admin/root-folders',
            monitoredContent: '/library/add',
        });
    });

    it('gives every step a one-line reason', () => {
        const steps = deriveOnboardingSteps(undefined, 'Magpie');
        expect(steps.every((s) => s.reason.length > 0)).toBe(true);
    });
});

describe('isOnboardingComplete', () => {
    it('is false for undefined status', () => {
        expect(isOnboardingComplete(undefined)).toBe(false);
    });

    it('honours the server-computed isComplete flag', () => {
        expect(isOnboardingComplete(allDone)).toBe(true);
    });

    it('is false when any step is missing', () => {
        expect(
            isOnboardingComplete({
                hasIndexer: true,
                hasDownloadClient: true,
                hasRootFolder: true,
                hasMonitoredContent: false,
                isComplete: false,
            }),
        ).toBe(false);
    });

    it('uses the server completion flag when all local flags are true', () => {
        // Arrange
        const status: OnboardingStatus = {
            hasIndexer: true,
            hasDownloadClient: true,
            hasRootFolder: true,
            hasMonitoredContent: true,
            isComplete: false,
        };
        // Act
        const complete = isOnboardingComplete(status);
        // Assert
        expect(complete).toBe(false);
    });
});

describe('useOnboardingStatus', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/indexers') {
                return Promise.resolve([{ id: 'indexer-1', connectionCount: 1 }]);
            }
            if (url === '/download-clients') {
                return Promise.resolve([{ id: 'client-1' }]);
            }
            if (url === '/root-folders') {
                return Promise.resolve([{ id: 'folder-1' }]);
            }
            return Promise.resolve({ items: [{ id: 'content-1' }], pageNumber: 1, pageSize: 1, totalItems: 1 });
        });
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('reads every flag from the mapped list endpoints', async () => {
        // Arrange
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);

        // Act
        const { result } = renderHook(() => useOnboardingStatus(), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        // Assert
        expect(result.current.status).toEqual(allDone);
        expect(mockApiGet.mock.calls.map((call) => call[0])).toEqual([
            '/indexers',
            '/download-clients',
            '/root-folders',
            '/content?monitoring=true&pageSize=1',
        ]);
    });

    it('reports an incomplete checklist when no content is monitored', async () => {
        // Arrange
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/content')) {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 1, totalItems: 0 });
            }
            return Promise.resolve([{ id: 'configured-1', connectionCount: 1 }]);
        });
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);

        // Act
        const { result } = renderHook(() => useOnboardingStatus(), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        // Assert
        expect(result.current.status).toEqual({
            hasIndexer: true,
            hasDownloadClient: true,
            hasRootFolder: true,
            hasMonitoredContent: false,
            isComplete: false,
        });
    });

    it('reuses configured lists and observes changes to their shared caches', async () => {
        // Arrange
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);
        queryClient.setQueryData(indexerQueryKeys.list(), [{ id: 'indexer-1', connectionCount: 1 }]);
        queryClient.setQueryData([...downloadClientQueryKeys.list(), '/download-clients'], [{ id: 'client-1' }]);
        queryClient.setQueryData(rootFolderQueryKeys.list(), [{ id: 'folder-1' }]);

        // Act
        const { result } = renderHook(() => useOnboardingStatus(), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        // Assert
        expect(result.current.status).toEqual(allDone);
        expect(mockApiGet).toHaveBeenCalledExactlyOnceWith('/content?monitoring=true&pageSize=1');
        act(() => {
            queryClient.setQueryData(rootFolderQueryKeys.list(), []);
        });
        await waitFor(() => expect(result.current.status.hasRootFolder).toBe(false));
        expect(result.current.isComplete).toBe(false);
    });

    it('keeps the indexer step open when only catalog definitions exist', async () => {
        // Arrange
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/indexers') {
                return Promise.resolve([
                    { id: 'definition-1', connectionCount: 0 },
                    { id: 'definition-2', connectionCount: 0 },
                ]);
            }
            if (url.startsWith('/content')) {
                return Promise.resolve({ items: [{ id: 'content-1' }], pageNumber: 1, pageSize: 1, totalItems: 1 });
            }
            return Promise.resolve([{ id: 'configured-1' }]);
        });
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);

        // Act
        const { result } = renderHook(() => useOnboardingStatus(), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        // Assert
        expect(result.current.status).toEqual({ ...allDone, hasIndexer: false, isComplete: false });
    });

    it('marks only the failed step and keeps the checklist showable when one probe fails', async () => {
        // Arrange
        mockApiGet.mockRejectedValueOnce(new Error('Indexer unavailable'));
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);

        // Act
        const { result } = renderHook(() => useOnboardingStatus(), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        // Assert
        expect(result.current.status).toEqual({ ...allDone, hasIndexer: false, isComplete: false });
        expect(result.current.steps.map((step) => [step.key, step.done, step.failed])).toEqual([
            ['indexer', false, true],
            ['downloadClient', true, false],
            ['rootFolder', true, false],
            ['monitoredContent', true, false],
        ]);
        expect(result.current.error).toBeNull();
    });

    it('reports an error only when every probe fails', async () => {
        // Arrange
        mockApiGet.mockRejectedValue(new Error('Host unreachable'));
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);

        // Act
        const { result } = renderHook(() => useOnboardingStatus(), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        // Assert
        expect(result.current.error).toEqual(new Error('Host unreachable'));
        expect(result.current.steps.every((step) => step.failed)).toBe(true);
    });

    it('coalesces a burst of content events into a single refetch', async () => {
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const wrapper = ({ children }: { children: React.ReactNode }) =>
            createElement(QueryClientProvider, { client: queryClient }, children);
        const { result } = renderHook(() => useOnboardingStatus(), { wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        vi.useFakeTimers();
        mockApiGet.mockClear();
        const handler = mockedUseEventStream.mock.calls.find((call) =>
            call[1]?.eventName?.includes('content.hierarchyItem.created'),
        )?.[0];
        for (let index = 0; index < 100; index += 1) {
            handler?.({
                name: 'content.hierarchyItem.created',
                context: { 'content.hierarchyItemId': `content-${index}` },
            });
        }
        await act(async () => {
            await vi.advanceTimersByTimeAsync(300);
        });

        expect(mockApiGet).toHaveBeenCalledExactlyOnceWith('/content?monitoring=true&pageSize=1');
    });
});
