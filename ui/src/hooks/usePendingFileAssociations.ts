import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { libraryImportCandidateGroupQueryKeys } from '@/hooks/useLibraryImportCandidateGroups';
import { libraryImportCandidateQueryKeys, type LibraryImportCandidate } from '@/hooks/useLibraryImportCandidates';
import type { LibraryImportSubmissionResult } from '@/hooks/useBulkAcceptLibraryImportCandidates';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet, apiPost } from '@/lib/api';
import type { BulkResult } from '@/types/contentMetadata';

export const pendingFileAssociationQueryKeys = {
    content: (contentHierarchyItemId: string) =>
        [...libraryImportCandidateQueryKeys.all, 'content', contentHierarchyItemId] as const,
};

interface CandidateSelection {
    ids: string[];
}

export function usePendingFileAssociations(contentHierarchyItemId: string | undefined) {
    const queryKey = pendingFileAssociationQueryKeys.content(contentHierarchyItemId ?? '');
    useInvalidateQueriesOnEvents(
        contentHierarchyItemId ? ['library.scan.completed', 'library.importCandidate.updated'] : [],
        [queryKey],
        { debounceMs: 300, exact: true, maxWaitMs: 1200, owner: 'pending-file-associations' },
    );

    return useQuery({
        queryKey,
        queryFn: () => apiGet<LibraryImportCandidate[]>(`/import/content/${contentHierarchyItemId}/candidates`),
        enabled: !!contentHierarchyItemId,
    });
}

function useCandidateMutation<TVariables, TResult>(mutationFn: (variables: TVariables) => Promise<TResult>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateGroupQueryKeys.all });
        },
    });
}

export function useAssociatePendingFiles() {
    return useCandidateMutation(({ contentHierarchyItemId, ids }: { contentHierarchyItemId: string; ids?: string[] }) =>
        apiPost<LibraryImportSubmissionResult>(`/import/content/${contentHierarchyItemId}/associate`, { ids }),
    );
}

export function useRejectLibraryImportCandidates() {
    return useCandidateMutation((selection: CandidateSelection) =>
        apiPost<BulkResult>('/import/candidates/reject', selection),
    );
}

export function useRestoreLibraryImportCandidates() {
    return useCandidateMutation((selection: CandidateSelection) =>
        apiPost<BulkResult>('/import/candidates/restore', selection),
    );
}
