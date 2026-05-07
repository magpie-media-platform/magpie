import { QueryClient } from '@tanstack/react-query';

export const POLLING_INTERVALS = {
    fast: 5_000,

    normal: 15_000,

    slow: 30_000,
} as const;

export type PollingSpeed = keyof typeof POLLING_INTERVALS;

export const queryClient = new QueryClient({
    defaultOptions: {
        queries: {
            staleTime: 10_000,
            gcTime: 5 * 60_000,
            retry: 1,
            refetchOnWindowFocus: true,
        },
    },
});
