import { useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { ApiKeyItem, ApiKeyCreatedItem, ApiKeyCreate } from '@/types';

export const apiKeyQueryKeys = {
    all: ['api-keys'] as const,
    my: () => ['api-keys', 'my'] as const,
    user: (userId: string) => ['api-keys', 'user', userId] as const,
};

async function fetchMyApiKeys(): Promise<ApiKeyItem[]> {
    return apiGet<ApiKeyItem[]>('/api-keys/my');
}

async function fetchUserApiKeys(userId: string): Promise<ApiKeyItem[]> {
    return apiGet<ApiKeyItem[]>(`/api-keys/${encodeURIComponent(userId)}`);
}

async function createApiKey(request: ApiKeyCreate): Promise<ApiKeyCreatedItem> {
    return apiPost<ApiKeyCreatedItem>('/api-keys/my', request);
}

async function deleteMyApiKey(id: string): Promise<void> {
    return apiDelete(`/api-keys/my/${encodeURIComponent(id)}`);
}

async function deleteUserApiKey({ userId, id }: { userId: string; id: string }): Promise<void> {
    return apiDelete(`/api-keys/${encodeURIComponent(userId)}/${encodeURIComponent(id)}`);
}

async function rotateApiKey(id: string): Promise<ApiKeyCreatedItem> {
    return apiPost<ApiKeyCreatedItem>(`/api-keys/my/${encodeURIComponent(id)}/rotate`);
}

async function enableApiKey(id: string): Promise<ApiKeyItem> {
    return apiPost<ApiKeyItem>(`/api-keys/my/${encodeURIComponent(id)}/enable`);
}

async function disableApiKey(id: string): Promise<ApiKeyItem> {
    return apiPost<ApiKeyItem>(`/api-keys/my/${encodeURIComponent(id)}/disable`);
}

export function useApiKeys() {
    const queryClient = useQueryClient();

    const keysQuery = useQuery({
        queryKey: apiKeyQueryKeys.my(),
        queryFn: fetchMyApiKeys,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const createMutation = useMutation({
        mutationFn: createApiKey,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteMyApiKey,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
        },
    });

    const rotateMutation = useMutation({
        mutationFn: rotateApiKey,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
        },
    });

    const enableMutation = useMutation({
        mutationFn: enableApiKey,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
        },
    });

    const disableMutation = useMutation({
        mutationFn: disableApiKey,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
        },
    });

    const apiKeys: ApiKeyItem[] = useMemo(() => keysQuery.data ?? [], [keysQuery.data]);

    const toggleEnabled = async (id: string, enabled: boolean): Promise<ApiKeyItem> => {
        if (enabled) {
            return enableMutation.mutateAsync(id);
        } else {
            return disableMutation.mutateAsync(id);
        }
    };

    return {
        apiKeys,
        isLoading: keysQuery.isLoading,
        error: queryErrorOrNull(keysQuery),

        createApiKey: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        deleteApiKey: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        rotateApiKey: rotateMutation.mutateAsync,
        isRotating: rotateMutation.isPending,

        toggleEnabled,
        isToggling: enableMutation.isPending || disableMutation.isPending,

        refetch: keysQuery.refetch,
    };
}

export function useUserApiKeys(userId: string | null) {
    const queryClient = useQueryClient();

    const keysQuery = useQuery({
        queryKey: apiKeyQueryKeys.user(userId ?? ''),
        queryFn: () => fetchUserApiKeys(userId!),
        enabled: userId !== null,
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const deleteMutation = useMutation({
        mutationFn: deleteUserApiKey,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
        },
    });

    const apiKeys: ApiKeyItem[] = useMemo(() => keysQuery.data ?? [], [keysQuery.data]);

    return {
        apiKeys,
        isLoading: keysQuery.isLoading,
        error: queryErrorOrNull(keysQuery),

        deleteApiKey: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        refetch: keysQuery.refetch,
    };
}
