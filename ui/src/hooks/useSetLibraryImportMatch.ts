import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiPost } from '@/lib/api';
import { libraryImportCandidateQueryKeys, type LibraryImportCandidate } from '@/hooks/useLibraryImportCandidates';

interface SetLibraryImportMatchVariables {
    id: string;

    providerType: string;

    externalIdKey: string;

    externalId: string;

    title: string;

    subtitle?: string | null;

    imageUrl?: string | null;

    levelSlug?: string | null;
}

async function setLibraryImportMatch({ id, ...body }: SetLibraryImportMatchVariables): Promise<LibraryImportCandidate> {
    return apiPost<LibraryImportCandidate>(`/import/candidates/${id}/match`, body);
}

export function useSetLibraryImportMatch() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: setLibraryImportMatch,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: libraryImportCandidateQueryKeys.all });
        },
    });
}
