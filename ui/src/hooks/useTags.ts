import { useQuery } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export interface ApiTag {
    name: string;
}

export interface TagsQueryParams {
    prefix?: string;

    entityId?: string;
}

export const tagQueryKeys = {
    all: ['tags'] as const,
    list: (params?: TagsQueryParams) => ['tags', 'list', params ?? {}] as const,
    byPrefix: (prefix: string) => ['tags', 'prefix', prefix] as const,
    byEntity: (entityId: string) => ['tags', 'entity', entityId] as const,
};

async function fetchTags(params?: TagsQueryParams): Promise<ApiTag[]> {
    const url = buildUrl('/tags', params as Record<string, string | undefined>);
    return apiGet<ApiTag[]>(url);
}

export function useTags(params?: TagsQueryParams) {
    const query = useQuery({
        queryKey: tagQueryKeys.list(params),
        queryFn: () => fetchTags(params),
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const tags: ApiTag[] = query.data ?? [];

    return {
        tags,
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}

export function useTagsByPrefix(prefix: string, enabled = true) {
    const query = useQuery({
        queryKey: tagQueryKeys.byPrefix(prefix),
        queryFn: () => fetchTags({ prefix }),
        enabled: enabled && prefix.length > 0,
        staleTime: POLLING_INTERVALS.slow,
    });

    const tags: ApiTag[] = query.data ?? [];

    return {
        tags,
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
    };
}

export function useTagsByEntity(entityId: string, enabled = true) {
    const query = useQuery({
        queryKey: tagQueryKeys.byEntity(entityId),
        queryFn: () => fetchTags({ entityId }),
        enabled: enabled && entityId.length > 0,
        staleTime: POLLING_INTERVALS.slow,
    });

    const tags: ApiTag[] = query.data ?? [];

    return {
        tags,
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
    };
}
