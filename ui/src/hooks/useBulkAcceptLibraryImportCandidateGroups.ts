import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import { libraryImportCandidateGroupQueryKeys } from '@/hooks/useLibraryImportCandidateGroups';
import { libraryImportCandidateQueryKeys } from '@/hooks/useLibraryImportCandidates';
import type {
    LibraryImportMonitoringMode,
    LibraryImportSubmissionResult,
} from '@/hooks/useBulkAcceptLibraryImportCandidates';

interface BulkAcceptLibraryImportCandidateGroupsVariables {
    rootFolderId?: string;

    excludeGroupKeys: string[];

    monitoringMode: LibraryImportMonitoringMode;
}

async function bulkAcceptLibraryImportCandidateGroups(
    variables: BulkAcceptLibraryImportCandidateGroupsVariables,
): Promise<LibraryImportSubmissionResult> {
    const { rootFolderId, excludeGroupKeys, monitoringMode } = variables;
    return apiPost<LibraryImportSubmissionResult>('/import/candidate-groups/bulk-accept', {
        filter: { rootFolderId, excludeGroupKeys },
        monitoringMode,
    });
}

export function useBulkAcceptLibraryImportCandidateGroups() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: bulkAcceptLibraryImportCandidateGroups,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateGroupQueryKeys.all });
        },
    });
}
