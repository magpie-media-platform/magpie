import { QueryClient } from '@tanstack/react-query';

import { isNotFoundError } from './errors';

export const POLLING_INTERVALS = {
    fast: 5_000,

    normal: 15_000,

    slow: 30_000,
} as const;

const DEFAULT_QUERY_RETRY_COUNT = 1;

export function retryUnlessNotFound(failureCount: number, error: unknown): boolean {
    return !isNotFoundError(error) && failureCount < DEFAULT_QUERY_RETRY_COUNT;
}

export const queryClient = new QueryClient({
    defaultOptions: {
        queries: {
            staleTime: 10_000,
            gcTime: 5 * 60_000,
            retry: retryUnlessNotFound,
            refetchOnWindowFocus: true,
        },
    },
});
