import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
}));

import { apiGet } from '@/lib/api';
import { logQueryKeys, useLogs } from './useLogs';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import type { LogEntry } from '@/types/logs';

const mockLogs: LogEntry[] = [
    {
        id: 'log-1',
        timestamp: '2024-01-15T10:00:00Z',
        level: 'info',
        source: 'Magpie.Core',
        message: 'Initialized',
    },
];

function createWrapperWithClient() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });

    const Wrapper = ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);

    return { queryClient, Wrapper };
}

// Read the refetchInterval option that React Query stores on the active query
// observer. Whether the hook passes a literal (5000 / false) or a gating
// function, we surface whatever was set so the test can assert on it directly.
function getRefetchInterval(queryClient: QueryClient, liveTail: boolean) {
    const cache = queryClient.getQueryCache();
    const query = cache.find({ queryKey: [...logQueryKeys.list(), { liveTail }] });
    expect(query, `query for liveTail=${liveTail} should be in cache`).toBeDefined();
    const observer = query!.observers[0];
    expect(observer, 'query should have an observer').toBeDefined();
    return observer.options.refetchInterval;
}

describe('logQueryKeys', () => {
    it('all returns the base key', () => {
        expect(logQueryKeys.all).toEqual(['logs']);
    });

    it('list returns a namespaced key', () => {
        expect(logQueryKeys.list()).toEqual(['logs', 'list']);
    });
});

describe('useLogs polling gating', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockLogs);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('uses fast refetch interval when liveTail=true', async () => {
        const { queryClient, Wrapper } = createWrapperWithClient();
        const { result } = renderHook(() => useLogs(true), { wrapper: Wrapper });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(getRefetchInterval(queryClient, true)).toBe(POLLING_INTERVALS.fast);
    });

    it('disables polling when liveTail=false', async () => {
        const { queryClient, Wrapper } = createWrapperWithClient();
        const { result } = renderHook(() => useLogs(false), { wrapper: Wrapper });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(getRefetchInterval(queryClient, false)).toBe(false);
    });

    it('refetch() pulls latest logs on demand when polling is disabled', async () => {
        const { Wrapper } = createWrapperWithClient();
        const { result } = renderHook(() => useLogs(false), { wrapper: Wrapper });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        const initialCalls = mockApiGet.mock.calls.length;
        await act(async () => {
            await result.current.refetch();
        });
        expect(mockApiGet.mock.calls.length).toBeGreaterThan(initialCalls);
    });

    it('toggling liveTail from off to on triggers a fresh fetch', async () => {
        const { Wrapper } = createWrapperWithClient();
        const { result, rerender } = renderHook(({ live }: { live: boolean }) => useLogs(live), {
            wrapper: Wrapper,
            initialProps: { live: false },
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        const callsBeforeToggle = mockApiGet.mock.calls.length;

        rerender({ live: true });

        // The hook scopes its query key by liveTail, so flipping the toggle
        // mounts a new query and immediately fetches.
        await waitFor(() => {
            expect(mockApiGet.mock.calls.length).toBeGreaterThan(callsBeforeToggle);
        });
    });
});
