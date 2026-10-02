import type { MonitoringMode } from '@/types';
import type { ScalarPatch } from '@/types/patch';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';

export type UpgradeFileHandling = 'Replace' | 'KeepBoth';

export interface RootFolderDefault {
    qualityTierListId: string | null;
}

export interface ApiRootFolder {
    id: string;

    name: string;

    path: string;

    contentType: string;

    isDefault: boolean;

    defaultMonitoring: MonitoringMode | null;

    renameTemplate?: string | null;

    upgradeFileHandling: UpgradeFileHandling;

    autoImport: boolean;

    defaults: Record<string, RootFolderDefault>;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

interface RootFolderCreate {
    name: string;

    path: string;

    contentType: string;

    isDefault?: boolean;

    defaultMonitoring?: MonitoringMode | null;

    renameTemplate?: string | null;

    upgradeFileHandling?: UpgradeFileHandling;

    autoImport?: boolean;

    defaults: Record<string, RootFolderDefault>;
}

interface RootFolderPatch {
    name?: ScalarPatch<string>;

    path?: ScalarPatch<string>;

    contentType?: ScalarPatch<string>;

    isDefault?: ScalarPatch<boolean>;

    defaultMonitoring?: ScalarPatch<MonitoringMode | null>;

    renameTemplate?: ScalarPatch<string | null>;

    upgradeFileHandling?: ScalarPatch<UpgradeFileHandling>;

    autoImport?: ScalarPatch<boolean>;

    defaults?: ScalarPatch<Record<string, RootFolderDefault>>;
}

export const rootFolderQueryKeys = {
    list: () => ['root-folders', 'list'] as const,
};

export async function fetchRootFolders(): Promise<ApiRootFolder[]> {
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
    useInvalidateQueriesOnEvents(
        ['library.rootFolder.created', 'library.rootFolder.updated', 'library.rootFolder.deleted'],
        [rootFolderQueryKeys.list()],
        { exact: true },
    );
    const queryClient = useQueryClient();

    const foldersQuery = useQuery({
        queryKey: rootFolderQueryKeys.list(),
        queryFn: fetchRootFolders,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const createMutation = useMutation({
        mutationFn: createRootFolder,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: rootFolderQueryKeys.list() });
        },
    });

    const updateMutation = useMutation({
        mutationFn: ({ id, data }: { id: string; data: RootFolderPatch }) => updateRootFolder(id, data),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: rootFolderQueryKeys.list() });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteRootFolder,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: rootFolderQueryKeys.list() });
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
