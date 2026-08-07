import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import { libraryImportCandidateQueryKeys } from '@/hooks/useLibraryImportCandidates';

interface BulkAcceptLibraryImportCandidatesFilter {
    rootFolderId?: string;

    excludeIds: string[];
}

type BulkAcceptLibraryImportCandidatesVariables =
    | { ids: string[] }
    | { filter: BulkAcceptLibraryImportCandidatesFilter };

interface LibraryImportQueuedResult {
    candidateId: string;

    jobExecutionId: string;
}

interface LibraryImportSkippedResult {
    candidateId: string;

    reason: string;
}

interface BulkAcceptLibraryImportCandidatesResponse {
    queued: LibraryImportQueuedResult[];

    skipped: LibraryImportSkippedResult[];
}

async function bulkAcceptLibraryImportCandidates(
    variables: BulkAcceptLibraryImportCandidatesVariables,
): Promise<BulkAcceptLibraryImportCandidatesResponse> {
    return apiPost<BulkAcceptLibraryImportCandidatesResponse>('/import/candidates/bulk-accept', variables);
}

export function useBulkAcceptLibraryImportCandidates() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: bulkAcceptLibraryImportCandidates,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });
        },
    });
}
