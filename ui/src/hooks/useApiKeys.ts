import type { ScalarPatch } from '@/types/patch';
import { useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { useAuth } from '@/auth';
import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { ApiKeyItem, ApiKeyCreatedItem, ApiKeyCreate } from '@/types';

export const apiKeyQueryKeys = {
    all: ['api-keys'] as const,
    everyone: () => ['api-keys', 'everyone'] as const,
    my: () => ['api-keys', 'my'] as const,
};

const API_KEY_CHANGE_EVENTS = ['apiKey.created', 'apiKey.updated', 'apiKey.deleted'] as const;

interface ApiApiKeyPatch {
    enabled?: ScalarPatch<boolean>;
}

async function fetchMyApiKeys(): Promise<ApiKeyItem[]> {
    return apiGet<ApiKeyItem[]>('/api-keys/my');
}

async function fetchAllApiKeys(): Promise<ApiKeyItem[]> {
    return apiGet<ApiKeyItem[]>('/api-keys');
}

async function createApiKey(request: ApiKeyCreate): Promise<ApiKeyCreatedItem> {
    return apiPost<ApiKeyCreatedItem>('/api-keys', request);
}

async function deleteMyApiKey(id: string): Promise<void> {
    return apiDelete(`/api-keys/${encodeURIComponent(id)}`);
}

async function deleteUserApiKey({ id }: { userId: string; id: string }): Promise<void> {
    return apiDelete(`/api-keys/${encodeURIComponent(id)}`);
}

async function updateApiKey(id: string, patch: ApiApiKeyPatch): Promise<void> {
    return apiPatch(`/api-keys/${encodeURIComponent(id)}`, patch);
}

export function useApiKeys() {
    const { user } = useAuth();
    useInvalidateQueriesOnEvents(API_KEY_CHANGE_EVENTS, [apiKeyQueryKeys.my()], {
        entityIds: user?.id ? [user.id] : [],
        exact: true,
    });
    const queryClient = useQueryClient();

    const keysQuery = useQuery({
        queryKey: apiKeyQueryKeys.my(),
        queryFn: fetchMyApiKeys,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const createMutation = useMutation({
        mutationFn: createApiKey,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteMyApiKey,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
        },
    });

    const toggleMutation = useMutation({
        mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) =>
            updateApiKey(id, { enabled: { value: enabled } }),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
        },
    });

    const apiKeys: ApiKeyItem[] = useMemo(() => keysQuery.data ?? [], [keysQuery.data]);

    const toggleEnabled = async (id: string, enabled: boolean): Promise<void> => {
        await toggleMutation.mutateAsync({ id, enabled });
    };

    return {
        apiKeys,
        isLoading: keysQuery.isLoading,
        error: queryErrorOrNull(keysQuery),

        createApiKey: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        deleteApiKey: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        toggleEnabled,
        isToggling: toggleMutation.isPending,

        refetch: keysQuery.refetch,
    };
}

export function useAllApiKeys(enabled = true) {
    useInvalidateQueriesOnEvents(enabled ? API_KEY_CHANGE_EVENTS : [], [apiKeyQueryKeys.everyone()], {
        exact: true,
    });
    const queryClient = useQueryClient();

    const keysQuery = useQuery({
        queryKey: apiKeyQueryKeys.everyone(),
        queryFn: fetchAllApiKeys,
        enabled,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const deleteMutation = useMutation({
        mutationFn: deleteUserApiKey,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: apiKeyQueryKeys.all });
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
