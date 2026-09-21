import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import { libraryImportCandidateGroupQueryKeys } from '@/hooks/useLibraryImportCandidateGroups';
import { libraryImportCandidateQueryKeys } from '@/hooks/useLibraryImportCandidates';

export type LibraryImportMonitoringMode = 'Inherit' | 'Monitored' | 'Unmonitored' | 'Future';

interface BulkAcceptLibraryImportCandidatesFilter {
    rootFolderId?: string;

    excludeIds: string[];
}

type BulkAcceptLibraryImportCandidatesVariables =
    | { ids: string[]; monitoringMode: LibraryImportMonitoringMode }
    | { filter: BulkAcceptLibraryImportCandidatesFilter; monitoringMode: LibraryImportMonitoringMode };

export interface LibraryImportQueuedResult {
    candidateId: string;

    jobExecutionId: string;
}

export interface LibraryImportSkippedResult {
    candidateId: string;

    reason: string;
}

export interface LibraryImportSubmissionResult {
    queued: LibraryImportQueuedResult[];

    skipped: LibraryImportSkippedResult[];
}

async function bulkAcceptLibraryImportCandidates(
    variables: BulkAcceptLibraryImportCandidatesVariables,
): Promise<LibraryImportSubmissionResult> {
    return apiPost<LibraryImportSubmissionResult>('/import/candidates/bulk-accept', variables);
}

export function useBulkAcceptLibraryImportCandidates() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: bulkAcceptLibraryImportCandidates,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateGroupQueryKeys.all });
        },
    });
}
