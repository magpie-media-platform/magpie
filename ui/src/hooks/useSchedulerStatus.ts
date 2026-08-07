import { useQuery } from '@tanstack/react-query';

import { apiGet } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';

export interface SchedulerStatus {
    isStalled: boolean;
    stalledSinceUtc: string | null;
    zeroEligibleSinceUtc: string | null;
    recoveredAtUtc: string | null;
    eligibleNodeCount: number;
}

const SCHEDULER_STATUS_PATH = '/system/scheduler-status';

const schedulerStatusQueryKeys = {
    all: ['scheduler-status'] as const,
};

async function fetchSchedulerStatus(): Promise<SchedulerStatus> {
    return apiGet<SchedulerStatus>(SCHEDULER_STATUS_PATH);
}

export function useSchedulerStatus() {
    useInvalidateQueriesOnEvents(
        [
            'job.execution.created',
            'job.execution.queued',
            'job.execution.started',
            'job.execution.completed',
            'job.execution.failed',
            'job.execution.cancelled',
        ],
        [schedulerStatusQueryKeys.all],
        { debounceMs: 200, exact: true, maxWaitMs: 800, owner: 'scheduler-status' },
    );
    return useQuery({
        queryKey: schedulerStatusQueryKeys.all,
        queryFn: fetchSchedulerStatus,
    });
}
