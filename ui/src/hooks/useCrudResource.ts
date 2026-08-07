import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';

interface CrudResourceConfig {
    basePath: string;
    eventNames?: readonly string[];
}

interface EnabledPatch {
    enabled?: { value: boolean };
}

interface CrudQueryKeys {
    all: readonly string[];
    list: () => readonly string[];
    detail: (id: string) => readonly string[];
}

interface CrudResourceResult<TItem, TCreate, TPatch> {
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

    refetch: () => Promise<void>;
}

export function buildQueryKeys(prefix: string): CrudQueryKeys {
    return {
        all: [prefix] as const,
        list: () => [prefix, 'list'] as const,
        detail: (id: string) => [prefix, 'detail', id] as const,
    };
}

export function useCrudResource<TItem, TCreate, TPatch extends EnabledPatch>(
    config: CrudResourceConfig,
    queryKeys: CrudQueryKeys,
): CrudResourceResult<TItem, TCreate, TPatch> {
    const { basePath } = config;
    const queryClient = useQueryClient();
    useInvalidateQueriesOnEvents(config.eventNames ?? [], [[...queryKeys.list(), basePath]], {
        exact: true,
        owner: `crud:${basePath}`,
    });

    const listQuery = useQuery({
        queryKey: [...queryKeys.list(), basePath],
        queryFn: () => apiGet<TItem[]>(basePath),
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const invalidateList = () => {
        void queryClient.invalidateQueries({ queryKey: queryKeys.list() });
    };

    const createMutation = useMutation({
        mutationFn: (data: TCreate) => apiPost<TItem>(basePath, data),
        onSuccess: invalidateList,
    });

    const patchMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: TPatch }) => apiPatch(`${basePath}/${id}`, patch),
        onSuccess: invalidateList,
    });

    const toggleMutation = useMutation({
        mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) =>
            apiPatch(`${basePath}/${id}`, { enabled: { value: enabled } }),
        onSuccess: invalidateList,
    });

    const deleteMutation = useMutation({
        mutationFn: (id: string) => apiDelete(`${basePath}/${id}`),
        onSuccess: invalidateList,
    });

    const toggleEnabled = async (id: string, enabled: boolean): Promise<void> => {
        await toggleMutation.mutateAsync({ id, enabled });
    };

    const update = async (id: string, patch: TPatch): Promise<void> => {
        await patchMutation.mutateAsync({ id, patch });
    };

    const refetch = async (): Promise<void> => {
        await listQuery.refetch();
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
        isToggling: toggleMutation.isPending,

        deleteItem: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        refetch,
    };
}
