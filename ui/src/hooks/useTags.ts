import { useQuery } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';

export interface ApiTag {
    name: string;
}

interface TagsQueryParams {
    prefix?: string;

    entityId?: string;
}

const NO_TAGS: ApiTag[] = [];

export const tagQueryKeys = {
    all: ['tags'] as const,
    list: (params?: TagsQueryParams) => ['tags', 'list', params ?? {}] as const,
    byEntity: (entityId: string) => ['tags', 'entity', entityId] as const,
};

const TAG_CHANGE_EVENTS = [
    'content.hierarchyItem.created',
    'content.hierarchyItem.deleted',
    'content.hierarchyItem.updated',
    'subject.created',
    'subject.deleted',
    'subject.updated',
    'wishlist.created',
    'wishlist.deleted',
    'wishlist.updated',
] as const;

async function fetchTags(params?: TagsQueryParams): Promise<ApiTag[]> {
    const url = buildUrl('/tags', params as Record<string, string | undefined>);
    return apiGet<ApiTag[]>(url);
}

export function useTags(params?: TagsQueryParams) {
    useInvalidateQueriesOnEvents(TAG_CHANGE_EVENTS, [tagQueryKeys.list(params)], {
        exact: true,
        owner: 'tags:list',
    });
    const query = useQuery({
        queryKey: tagQueryKeys.list(params),
        queryFn: () => fetchTags(params),
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const tags: ApiTag[] = query.data ?? NO_TAGS;

    return {
        tags,
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}

export function useTagsByEntity(entityId: string, enabled = true) {
    useInvalidateQueriesOnEvents(
        enabled && entityId.length > 0 ? TAG_CHANGE_EVENTS : [],
        [tagQueryKeys.byEntity(entityId)],
        { entityIds: entityId ? [entityId] : [], exact: true, owner: 'tags:entity' },
    );
    const query = useQuery({
        queryKey: tagQueryKeys.byEntity(entityId),
        queryFn: () => fetchTags({ entityId }),
        enabled: enabled && entityId.length > 0,
        staleTime: POLLING_INTERVALS.slow,
    });

    const tags: ApiTag[] = query.data ?? NO_TAGS;

    return {
        tags,
        isLoading: query.isLoading,
        error: queryErrorOrNull(query),
    };
}
