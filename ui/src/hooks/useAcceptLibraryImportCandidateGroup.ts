import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';

import { libraryImportCandidateGroupQueryKeys } from '@/hooks/useLibraryImportCandidateGroups';
import { libraryImportCandidateQueryKeys } from '@/hooks/useLibraryImportCandidates';
import type {
    LibraryImportMonitoringMode,
    LibraryImportSubmissionResult,
} from '@/hooks/useBulkAcceptLibraryImportCandidates';
import { apiPost, buildUrl, isApiError } from '@/lib/api';

interface AcceptLibraryImportCandidateGroupVariables {
    groupKey: string;

    rootFolderId?: string;

    monitoringMode: LibraryImportMonitoringMode;
}

async function acceptLibraryImportCandidateGroup({
    groupKey,
    rootFolderId,
    monitoringMode,
}: AcceptLibraryImportCandidateGroupVariables): Promise<LibraryImportSubmissionResult> {
    const url = buildUrl('/import/candidate-groups/accept', { groupKey, rootFolderId, monitoringMode });
    return apiPost<LibraryImportSubmissionResult>(url);
}

interface AcceptLibraryImportCandidateGroupOptions {
    /** Hold the query invalidation until `invalidate` is called, so a loop of accepts refetches the lists once. */
    deferInvalidation?: boolean;
}

export function useAcceptLibraryImportCandidateGroup({
    deferInvalidation = false,
}: AcceptLibraryImportCandidateGroupOptions = {}) {
    const queryClient = useQueryClient();
    const invalidate = useCallback(() => {
        void queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });
        void queryClient.invalidateQueries({ queryKey: libraryImportCandidateGroupQueryKeys.all });
    }, [queryClient]);

    const mutation = useMutation({
        mutationFn: acceptLibraryImportCandidateGroup,
        onSuccess: () => {
            if (!deferInvalidation) {
                invalidate();
            }
        },
        onError: (error) => {
            if (!deferInvalidation && isApiError(error) && error.status === 404) {
                void queryClient.invalidateQueries({ queryKey: libraryImportCandidateGroupQueryKeys.all });
            }
        },
    });

    return { ...mutation, invalidate };
}
