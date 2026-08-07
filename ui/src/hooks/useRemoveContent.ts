import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiDelete } from '@/lib/api';
import { contentQueryKeys } from './useContent';

interface RemoveContentVariables {
    contentHierarchyItemId: string;
    deleteFiles: boolean;
}

export function useRemoveContent() {
    const queryClient = useQueryClient();
    return useMutation<void, Error, RemoveContentVariables>({
        mutationFn: async ({ contentHierarchyItemId, deleteFiles }) => {
            return apiDelete(`/content/${contentHierarchyItemId}?deleteFromLibrary=${deleteFiles}`);
        },
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
        },
    });
}
