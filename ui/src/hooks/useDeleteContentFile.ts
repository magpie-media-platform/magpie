import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiDelete } from '@/lib/api';
import { contentQueryKeys } from './useContent';

interface DeleteContentFileVariables {
    contentHierarchyItemId: string;
    mediaFileId: string;
}

interface DeleteContentFileResult {
    mediaFileId: string;
    filePath: string;
    fileWasMissingOnDisk: boolean;
}

export function useDeleteContentFile() {
    const queryClient = useQueryClient();
    return useMutation<DeleteContentFileResult, Error, DeleteContentFileVariables>({
        mutationFn: async ({ contentHierarchyItemId, mediaFileId }) => {
            return apiDelete<DeleteContentFileResult>(`/content/${contentHierarchyItemId}/files/${mediaFileId}`);
        },
        onSuccess: (_, variables) => {
            void queryClient.invalidateQueries({
                queryKey: contentQueryKeys.detail(variables.contentHierarchyItemId),
            });
            void queryClient.invalidateQueries({
                queryKey: contentQueryKeys.all,
            });
        },
    });
}
