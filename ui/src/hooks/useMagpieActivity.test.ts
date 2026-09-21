import { createElement } from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiGet } from '@/lib/api';
import { useEventStream } from './useEventStream';
import { useJobExecutionAudits } from './useMagpieActivity';

vi.mock('@/lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/lib/api')>()),
    apiGet: vi.fn(),
}));
vi.mock('./useEventStream', () => ({ useEventStream: vi.fn() }));

const pages = [1, 2, 3, 4, 5, 6].map((pageNumber) => ({
    items: Array.from({ length: 100 }, (_, index) => ({ id: `${pageNumber}-${index}` })),
    pageNumber,
    pageSize: 100,
    totalItems: 600,
}));
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
const wrapper = ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children);
const mockApiGet = vi.mocked(apiGet);

describe('useJobExecutionAudits', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        queryClient.clear();
        queryClient.setQueryData(['magpie-activity', 'execution-audits', 'execution-1'], {
            pages: pages.slice(0, 5),
            pageParams: [1, 2, 3, 4, 5],
        });
    });

    afterEach(() => vi.useRealTimers());

    it('keeps a five-page window, stops at the total, and can reload evicted records', async () => {
        // Arrange
        mockApiGet.mockResolvedValueOnce(pages[5]).mockResolvedValueOnce(pages[0]);
        const { result } = renderHook(() => useJobExecutionAudits('execution-1'), { wrapper });

        // Act
        await act(async () => {
            await result.current.loadMore();
        });

        // Assert
        await waitFor(() => expect(result.current.hasPrevious).toBe(true));
        expect(result.current.audits).toHaveLength(500);
        expect(result.current.audits?.[0].id).toBe('2-0');
        expect(result.current.hasMore).toBe(false);
        expect(result.current.remainingCount).toBe(0);
        expect(mockApiGet).toHaveBeenCalledExactlyOnceWith(
            '/audit-history?jobExecutionId=execution-1&kind=AuditRecord&pageNumber=6&pageSize=100',
        );

        await act(async () => {
            await result.current.loadPrevious();
        });
        await waitFor(() => expect(result.current.hasPrevious).toBe(false));
        expect(result.current.audits).toHaveLength(500);
        expect(result.current.audits?.[0].id).toBe('1-0');
        expect(result.current.hasMore).toBe(true);
        expect(result.current.remainingCount).toBe(100);
        expect(mockApiGet).toHaveBeenLastCalledWith(
            '/audit-history?jobExecutionId=execution-1&kind=AuditRecord&pageNumber=1&pageSize=100',
        );
    });

    it('refetches at most five retained pages after an audit event', async () => {
        // Arrange
        mockApiGet.mockResolvedValueOnce(pages[5]);
        const { result } = renderHook(() => useJobExecutionAudits('execution-1'), { wrapper });
        await act(async () => {
            await result.current.loadMore();
        });
        await waitFor(() => expect(result.current.hasPrevious).toBe(true));
        mockApiGet.mockClear();
        mockApiGet
            .mockResolvedValueOnce(pages[1])
            .mockResolvedValueOnce(pages[2])
            .mockResolvedValueOnce(pages[3])
            .mockResolvedValueOnce(pages[4])
            .mockResolvedValueOnce(pages[5]);
        vi.useFakeTimers();

        // Act
        vi.mocked(useEventStream).mock.calls[0][0]({
            name: 'audit.record.created',
            context: { 'audit.recordId': 'audit-1' },
        });
        await act(async () => {
            await vi.advanceTimersByTimeAsync(200);
        });

        // Assert
        expect(mockApiGet).toHaveBeenCalledTimes(5);
        expect(mockApiGet).toHaveBeenLastCalledWith(
            '/audit-history?jobExecutionId=execution-1&kind=AuditRecord&pageNumber=6&pageSize=100',
        );
        expect(result.current.audits).toHaveLength(500);
        expect(result.current.hasMore).toBe(false);
    });
});
