import { useQueryClient } from '@tanstack/react-query';

import { libraryImportCandidateQueryKeys } from '@/hooks/useLibraryImportCandidates';
import { useJobAction } from '@/hooks/useJobAction';
import type { RunJobRequest } from '@/hooks/useJobs';
import { useScanCompletionTracker, type ScanBatchInfo } from '@/hooks/useScanCompletionTracker';
import { executeJobRequest, requireJobActionExecutionId, type JobActionDisposition } from '@/lib/jobActions';

interface StartRootFolderContentDiscoveryVariables {
    rootFolderId: string;
    rootFolderName?: string;
    batch?: ScanBatchInfo;
}

type StartRootFolderContentDiscoveryResult = JobActionDisposition;

async function startRootFolderContentDiscovery({
    rootFolderId,
}: StartRootFolderContentDiscoveryVariables): Promise<StartRootFolderContentDiscoveryResult> {
    // Immediate mode holds the HTTP request open for the full scan, so a large root folder times out.
    const request: RunJobRequest = {
        jobType: 'RootFolderContentDiscovery',
        mode: 'Queue',
        parameters: { rootFolderId },
    };
    const disposition = await executeJobRequest(request);
    requireJobActionExecutionId(disposition, 'Root folder content discovery');
    return disposition;
}

export function useStartRootFolderContentDiscovery() {
    const queryClient = useQueryClient();
    const { trackScan } = useScanCompletionTracker();

    return useJobAction<StartRootFolderContentDiscoveryVariables>({
        mutationFn: startRootFolderContentDiscovery,
        onJobStarted: (result, variables) => {
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });
            if (result.executionId) {
                trackScan(result.executionId, variables.rootFolderName ?? 'root folder', variables.batch);
            }
        },
    });
}
