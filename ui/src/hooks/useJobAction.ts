import { useCallback, useEffect, useRef, useState } from 'react';
import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query';

import { normalizeJobActionDisposition, type JobActionDisposition, type JobActionResponse } from '@/lib/jobActions';
import { isTerminalJobExecutionStatus, jobQueryKeys, useJobExecution, type ApiJobExecution } from '@/hooks/useJobs';

export interface JobActionLifecycleOptions<TVariables> {
    trackExecution?: boolean;

    onJobStarted?: (disposition: JobActionDisposition, variables: TVariables) => void;

    onJobExecutionSettled?: (
        execution: ApiJobExecution,
        variables: TVariables,
        disposition: JobActionDisposition,
    ) => void;
}

interface UseJobActionOptions<TVariables> extends JobActionLifecycleOptions<TVariables> {
    mutationFn: (variables: TVariables) => Promise<JobActionDisposition | JobActionResponse>;

    invalidateJobsOnStart?: boolean;
}

interface TrackedJobAction<TVariables> {
    disposition: JobActionDisposition;
    variables: TVariables;
}

type UseJobActionResult<TVariables> = UseMutationResult<JobActionDisposition, Error, TVariables> & {
    disposition: JobActionDisposition | null;
    executionId: string | null;
    isStarting: boolean;
    trackedExecution: ApiJobExecution | null;
    clearTrackedExecution: () => void;
};

export function useJobAction<TVariables>({
    mutationFn,
    invalidateJobsOnStart = true,
    trackExecution,
    onJobStarted,
    onJobExecutionSettled,
}: UseJobActionOptions<TVariables>): UseJobActionResult<TVariables> {
    const queryClient = useQueryClient();
    const [trackedAction, setTrackedAction] = useState<TrackedJobAction<TVariables> | null>(null);
    const settledExecutionIdRef = useRef<string | null>(null);
    const shouldTrackExecution = trackExecution ?? onJobExecutionSettled !== undefined;

    const mutation = useMutation<JobActionDisposition, Error, TVariables>({
        mutationFn: async (variables) => normalizeJobActionDisposition(await mutationFn(variables)),
        onSuccess: (disposition, variables) => {
            if (invalidateJobsOnStart) {
                void queryClient.invalidateQueries({ queryKey: jobQueryKeys.all });
            }

            onJobStarted?.(disposition, variables);

            if (shouldTrackExecution && disposition.executionId) {
                settledExecutionIdRef.current = null;
                setTrackedAction({ disposition, variables });
            }
        },
    });

    const trackedExecutionId = trackedAction?.disposition.executionId ?? '';
    const { job: trackedExecution } = useJobExecution(
        trackedExecutionId,
        shouldTrackExecution && trackedExecutionId.length > 0,
    );

    useEffect(() => {
        if (!trackedAction || !trackedExecution || !isTerminalJobExecutionStatus(trackedExecution.status)) {
            return;
        }

        if (settledExecutionIdRef.current === trackedExecution.id) {
            return;
        }

        settledExecutionIdRef.current = trackedExecution.id;
        onJobExecutionSettled?.(trackedExecution, trackedAction.variables, trackedAction.disposition);
        setTrackedAction(null);
    }, [onJobExecutionSettled, trackedAction, trackedExecution]);

    const clearTrackedExecution = useCallback(() => {
        setTrackedAction(null);
        settledExecutionIdRef.current = null;
    }, []);

    const disposition = mutation.data ?? trackedAction?.disposition ?? null;
    const executionId = disposition?.executionId ?? null;

    return {
        ...mutation,
        disposition,
        executionId,
        isStarting: mutation.isPending,
        trackedExecution,
        clearTrackedExecution,
    };
}
