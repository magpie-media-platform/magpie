import { useQuery } from '@tanstack/react-query';

import { apiGet } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { LogEntry } from '@/types/logs';

export const logQueryKeys = {
    all: ['logs'] as const,
    list: () => ['logs', 'list'] as const,
};

async function fetchLogs(): Promise<LogEntry[]> {
    return apiGet<LogEntry[]>('/system/logs');
}

export function useLogs(liveTail: boolean) {
    // liveTail off: stop polling; callers must refetch manually or re-enable.
    const refetchInterval = liveTail ? POLLING_INTERVALS.fast : (false as const);
    const staleTime = liveTail ? POLLING_INTERVALS.fast / 2 : 0;

    const logsQuery = useQuery({
        queryKey: [...logQueryKeys.list(), { liveTail }],
        queryFn: fetchLogs,
        refetchInterval,
        staleTime,
    });

    return {
        logs: logsQuery.data ?? [],
        isLoading: logsQuery.isLoading,
        error: queryErrorOrNull(logsQuery),
        refetch: logsQuery.refetch,
    };
}
