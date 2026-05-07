import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

interface CrudResourceConfig {
    basePath: string;
}

export interface CrudQueryKeys {
    all: readonly string[];
    list: () => readonly string[];
    detail: (id: string) => readonly string[];
}

export interface CrudResourceResult<TItem, TCreate, TPatch> {
    items: TItem[];
    isLoading: boolean;
    error: Error | null;

    create: (data: TCreate) => Promise<TItem>;
    isCreating: boolean;

    update: (id: string, patch: TPatch) => Promise<void>;
    isUpdating: boolean;

    toggleEnabled: (id: string, enabled: boolean) => Promise<void>;
    isToggling: boolean;

    deleteItem: (id: string) => Promise<void>;
    isDeleting: boolean;

    refetch: () => void;
}

export function buildQueryKeys(prefix: string): CrudQueryKeys {
    return {
        all: [prefix] as const,
        list: () => [prefix, 'list'] as const,
        detail: (id: string) => [prefix, 'detail', id] as const,
    };
}

export function useCrudResource<TItem, TCreate, TPatch>(
    config: CrudResourceConfig,
    queryKeys: CrudQueryKeys,
): CrudResourceResult<TItem, TCreate, TPatch> {
    const { basePath } = config;
    const queryClient = useQueryClient();

    const listQuery = useQuery({
        queryKey: queryKeys.list(),
        queryFn: () => apiGet<TItem[]>(basePath),
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const invalidateList = () => {
        queryClient.invalidateQueries({ queryKey: queryKeys.list() });
    };

    const createMutation = useMutation({
        mutationFn: (data: TCreate) => apiPost<TItem>(basePath, data),
        onSuccess: invalidateList,
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: TPatch }) => apiPatch(`${basePath}/${id}`, patch),
        onSuccess: invalidateList,
    });

    const enableMutation = useMutation({
        mutationFn: (id: string) => apiPost<TItem>(`${basePath}/${id}/enable`),
        onSuccess: invalidateList,
    });

    const disableMutation = useMutation({
        mutationFn: (id: string) => apiPost<TItem>(`${basePath}/${id}/disable`),
        onSuccess: invalidateList,
    });

    const deleteMutation = useMutation({
        mutationFn: (id: string) => apiDelete(`${basePath}/${id}`),
        onSuccess: invalidateList,
    });

    const toggleEnabled = async (id: string, enabled: boolean): Promise<void> => {
        if (enabled) {
            await enableMutation.mutateAsync(id);
        } else {
            await disableMutation.mutateAsync(id);
        }
    };

    const update = async (id: string, patch: TPatch): Promise<void> => {
        await patchMutation.mutateAsync({ id, patch });
    };

    return {
        items: listQuery.data ?? [],
        isLoading: listQuery.isLoading,
        error: queryErrorOrNull(listQuery),

        create: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        update,
        isUpdating: patchMutation.isPending,

        toggleEnabled,
        isToggling: enableMutation.isPending || disableMutation.isPending,

        deleteItem: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        refetch: listQuery.refetch,
    };
}
