import { useMutation, useQueryClient } from '@tanstack/react-query';

import {
    libraryImportCandidateGroupQueryKeys,
    type LibraryImportCandidateGroup,
} from '@/hooks/useLibraryImportCandidateGroups';
import { libraryImportCandidateQueryKeys } from '@/hooks/useLibraryImportCandidates';
import { apiPost, buildUrl, isApiError } from '@/lib/api';

interface SetLibraryImportCandidateGroupMatchVariables {
    groupKey: string;

    rootFolderId?: string;

    providerType: string;

    externalIdKey: string;

    externalId: string;

    title: string;

    subtitle?: string | null;

    imageUrl?: string | null;

    levelSlug?: string | null;
}

async function setLibraryImportCandidateGroupMatch({
    groupKey,
    rootFolderId,
    ...body
}: SetLibraryImportCandidateGroupMatchVariables): Promise<LibraryImportCandidateGroup> {
    const url = buildUrl('/import/candidate-groups/match', { groupKey, rootFolderId });
    return apiPost<LibraryImportCandidateGroup>(url, body);
}

export function useSetLibraryImportCandidateGroupMatch() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: setLibraryImportCandidateGroupMatch,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateGroupQueryKeys.all });
        },
        onError: (error) => {
            if (isApiError(error) && error.status === 404) {
                void queryClient.invalidateQueries({ queryKey: libraryImportCandidateGroupQueryKeys.all });
            }
        },
    });
}
