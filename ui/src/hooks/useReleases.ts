import { executeImmediateJobRequest, type JobActionDisposition } from '@/lib/jobActions';
import { useJobAction, type JobActionLifecycleOptions } from '@/hooks/useJobAction';

export interface ApiRelease {
    id: string;

    indexerId?: string | null;

    contentItemId?: string | null;

    protocol: string;

    externalId?: string | null;

    title: string;

    publishDate: string;

    size?: number | null;

    infoUrl?: string | null;

    categories?: string[];

    languages?: string[];

    properties?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

interface GrabReleaseVariables {
    id: string;

    contentHierarchyItemId: string;
}

async function grabRelease(variables: GrabReleaseVariables): Promise<JobActionDisposition> {
    const parameters: Record<string, string> = {
        releaseId: variables.id,
        interactive: 'true',
        contentHierarchyItemId: variables.contentHierarchyItemId,
    };

    return executeImmediateJobRequest({
        jobType: 'ReleaseDownloadItem',
        parameters,
    });
}

export function useGrabRelease(options?: JobActionLifecycleOptions<GrabReleaseVariables>) {
    return useJobAction<GrabReleaseVariables>({
        mutationFn: grabRelease,
        onJobStarted: options?.onJobStarted,
        onJobExecutionSettled: options?.onJobExecutionSettled,
        trackExecution: options?.trackExecution,
    });
}
