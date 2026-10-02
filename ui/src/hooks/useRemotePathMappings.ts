import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import type { ScalarPatch } from '@/types/patch';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';

export interface ApiRemotePathMapping {
    id: string;

    host: string;

    remotePath: string;

    localPath: string;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;
}

interface RemotePathMappingCreate {
    host: string;

    remotePath: string;

    localPath: string;
}

interface RemotePathMappingPatch {
    host?: ScalarPatch<string>;

    remotePath?: ScalarPatch<string>;

    localPath?: ScalarPatch<string>;
}

interface RemotePathMappingLocalPathTestRequest {
    localPath: string;
}

interface RemotePathMappingLocalPathTestResult {
    success: boolean;

    message: string;
}

export const remotePathMappingQueryKeys = {
    list: () => ['remote-path-mappings', 'list'] as const,
};

async function fetchRemotePathMappings(): Promise<ApiRemotePathMapping[]> {
    return apiGet<ApiRemotePathMapping[]>('/remote-path-mappings');
}

async function createRemotePathMapping(data: RemotePathMappingCreate): Promise<ApiRemotePathMapping> {
    return apiPost<ApiRemotePathMapping>('/remote-path-mappings', data);
}

async function updateRemotePathMapping(id: string, data: RemotePathMappingPatch): Promise<void> {
    return apiPatch(`/remote-path-mappings/${id}`, data);
}

async function deleteRemotePathMapping(id: string): Promise<void> {
    return apiDelete(`/remote-path-mappings/${id}`);
}

async function testRemotePathMappingLocalPath(
    data: RemotePathMappingLocalPathTestRequest,
): Promise<RemotePathMappingLocalPathTestResult> {
    return apiPost<RemotePathMappingLocalPathTestResult>('/remote-path-mappings/test', data);
}

export function useRemotePathMappings() {
    useInvalidateQueriesOnEvents(
        ['remotePathMapping.created', 'remotePathMapping.updated', 'remotePathMapping.deleted'],
        [remotePathMappingQueryKeys.list()],
        { exact: true },
    );
    const queryClient = useQueryClient();

    const mappingsQuery = useQuery({
        queryKey: remotePathMappingQueryKeys.list(),
        queryFn: fetchRemotePathMappings,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const createMutation = useMutation({
        mutationFn: createRemotePathMapping,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: remotePathMappingQueryKeys.list() });
        },
    });

    const updateMutation = useMutation({
        mutationFn: ({ id, data }: { id: string; data: RemotePathMappingPatch }) => updateRemotePathMapping(id, data),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: remotePathMappingQueryKeys.list() });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteRemotePathMapping,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: remotePathMappingQueryKeys.list() });
        },
    });

    const testLocalPathMutation = useMutation({
        mutationFn: testRemotePathMappingLocalPath,
    });

    const mappings: ApiRemotePathMapping[] = mappingsQuery.data ?? [];

    const updateMapping = async (id: string, data: RemotePathMappingPatch): Promise<void> => {
        await updateMutation.mutateAsync({ id, data });
    };

    return {
        mappings,
        isLoading: mappingsQuery.isLoading,
        error: queryErrorOrNull(mappingsQuery),

        createMapping: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        updateMapping,
        isUpdating: updateMutation.isPending,

        deleteMapping: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        testLocalPath: testLocalPathMutation.mutateAsync,
        isTestingLocalPath: testLocalPathMutation.isPending,

        refetch: mappingsQuery.refetch,
    };
}
