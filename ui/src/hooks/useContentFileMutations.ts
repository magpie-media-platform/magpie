import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost } from '@/lib/api';
import { contentQueryKeys } from './useContent';

export interface ContentFileBrowserLocation {
    rootPath: string;
    initialPath: string;
}

export interface ContentFileAttachmentResult {
    mediaFile: { id: string; filePath: string };
    isPrimary: boolean;
}

interface AttachContentFileVariables {
    contentHierarchyItemId: string;
    filePath: string;
}

interface UnlinkContentFileVariables {
    contentHierarchyItemId: string;
    mediaFileId: string;
}

function invalidateContent(queryClient: ReturnType<typeof useQueryClient>, contentHierarchyItemId: string): void {
    void queryClient.invalidateQueries({ queryKey: contentQueryKeys.detail(contentHierarchyItemId) });
    void queryClient.invalidateQueries({ queryKey: contentQueryKeys.all });
}

export function useContentFileBrowserLocation(contentHierarchyItemId: string | undefined) {
    return useQuery({
        queryKey: ['content-file-browser-location', contentHierarchyItemId],
        queryFn: () => {
            if (!contentHierarchyItemId) {
                throw new Error('A content id is required.');
            }

            return apiGet<ContentFileBrowserLocation>(`/content/${contentHierarchyItemId}/files/browser-location`);
        },
        enabled: false,
    });
}

export function useAttachContentFile() {
    const queryClient = useQueryClient();
    return useMutation<ContentFileAttachmentResult, Error, AttachContentFileVariables>({
        mutationFn: ({ contentHierarchyItemId, filePath }) =>
            apiPost<ContentFileAttachmentResult>(`/content/${contentHierarchyItemId}/files`, { filePath }),
        onSuccess: (_, variables) => invalidateContent(queryClient, variables.contentHierarchyItemId),
    });
}

export function useUnlinkContentFile() {
    const queryClient = useQueryClient();
    return useMutation<void, Error, UnlinkContentFileVariables>({
        mutationFn: ({ contentHierarchyItemId, mediaFileId }) =>
            apiPost(`/content/${contentHierarchyItemId}/files/${mediaFileId}/unlink`),
        onSuccess: (_, variables) => invalidateContent(queryClient, variables.contentHierarchyItemId),
    });
}
