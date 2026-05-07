import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export interface ApiRootFolder {
    id: string;

    name: string;

    path: string;

    contentTypeId: string;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

export interface RootFolderCreate {
    name: string;

    path: string;

    contentTypeId: string;
}

export interface RootFolderPatch {
    name?: string;

    path?: string;

    contentTypeId?: string;
}

export const rootFolderQueryKeys = {
    all: ['root-folders'] as const,
    list: () => ['root-folders', 'list'] as const,
    detail: (id: string) => ['root-folders', 'detail', id] as const,
};

async function fetchRootFolders(): Promise<ApiRootFolder[]> {
    return apiGet<ApiRootFolder[]>('/root-folders');
}

async function createRootFolder(data: RootFolderCreate): Promise<ApiRootFolder> {
    return apiPost<ApiRootFolder>('/root-folders', data);
}

async function updateRootFolder(id: string, data: RootFolderPatch): Promise<void> {
    return apiPatch(`/root-folders/${id}`, data);
}

async function deleteRootFolder(id: string): Promise<void> {
    return apiDelete(`/root-folders/${id}`);
}

export function useRootFolders() {
    const queryClient = useQueryClient();

    const foldersQuery = useQuery({
        queryKey: rootFolderQueryKeys.list(),
        queryFn: fetchRootFolders,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const createMutation = useMutation({
        mutationFn: createRootFolder,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: rootFolderQueryKeys.list() });
        },
    });

    const updateMutation = useMutation({
        mutationFn: ({ id, data }: { id: string; data: RootFolderPatch }) => updateRootFolder(id, data),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: rootFolderQueryKeys.list() });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteRootFolder,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: rootFolderQueryKeys.list() });
        },
    });

    const folders: ApiRootFolder[] = foldersQuery.data ?? [];

    const updateFolder = async (id: string, data: RootFolderPatch): Promise<void> => {
        await updateMutation.mutateAsync({ id, data });
    };

    return {
        folders,
        isLoading: foldersQuery.isLoading,
        error: queryErrorOrNull(foldersQuery),

        createFolder: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        updateFolder,
        isUpdating: updateMutation.isPending,

        deleteFolder: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        refetch: foldersQuery.refetch,
    };
}
