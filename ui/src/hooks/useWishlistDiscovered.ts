import { useQuery } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { PagedList } from '@/types/paging';

type WishlistDiscoveredItemStatus = 'Missing' | 'Found';

export interface WishlistDiscoveredRow {
    discoveredId: string;

    source: string;

    externalId: string;

    title: string | null;

    discoveredAt: string;

    linkId: string | null;

    posterUrl: string | null;

    publishDate: string | null;

    addedAt: string | null;

    status: WishlistDiscoveredItemStatus | null;
}

interface WishlistDiscoveredCounts {
    totalDiscovered: number;
    inLibrary: number;
    notInLibrary: number;
    missing: number;
}

export type WishlistDiscoveredInLibraryFilter = 'All' | 'InLibrary' | 'NotInLibrary';

export type WishlistDiscoveredStatusFilter = 'All' | 'Missing' | 'Found';

export type WishlistDiscoveredSort = 'Discovered' | 'Added' | 'PublishDate' | 'Title' | 'Status';

interface WishlistDiscoveredQueryParams {
    pageNumber?: number;

    pageSize?: number;

    inLibrary?: WishlistDiscoveredInLibraryFilter;

    status?: WishlistDiscoveredStatusFilter;

    sort?: WishlistDiscoveredSort;

    sortDescending?: boolean;

    search?: string;
}

interface WishlistDiscoveredLedger {
    page: PagedList<WishlistDiscoveredRow>;
    counts: WishlistDiscoveredCounts;
}

export const wishlistDiscoveredQueryKeys = {
    wishlist: (wishlistId: string) => ['wishlist-discovered', wishlistId] as const,
    list: (wishlistId: string, params?: WishlistDiscoveredQueryParams) =>
        ['wishlist-discovered', wishlistId, params ?? {}] as const,
};

function buildDiscoveredUrl(wishlistId: string, params?: WishlistDiscoveredQueryParams): string {
    return buildUrl(`/wishlists/${wishlistId}/discovered`, {
        pageNumber: params?.pageNumber,
        pageSize: params?.pageSize,
        inLibrary: params?.inLibrary === 'InLibrary' ? true : params?.inLibrary === 'NotInLibrary' ? false : undefined,
        status: params?.status && params.status !== 'All' ? params.status : undefined,
        sort: params?.sort,
        sortDescending: params?.sortDescending,
        search: params?.search?.trim() || undefined,
    });
}

async function fetchWishlistDiscovered(
    wishlistId: string,
    params?: WishlistDiscoveredQueryParams,
): Promise<WishlistDiscoveredLedger> {
    return apiGet<WishlistDiscoveredLedger>(buildDiscoveredUrl(wishlistId, params));
}

export function useWishlistDiscovered(wishlistId: string | undefined, params?: WishlistDiscoveredQueryParams) {
    const discoveredQuery = useQuery({
        queryKey: wishlistDiscoveredQueryKeys.list(wishlistId ?? '', params),
        queryFn: () => fetchWishlistDiscovered(wishlistId!, params),
        enabled: !!wishlistId,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
    const linkedContentIds = discoveredQuery.data?.page.items.flatMap((row) => (row.linkId ? [row.linkId] : [])) ?? [];
    useInvalidateQueriesOnEvents(
        wishlistId ? ['wishlist.updated', 'wishlist.item.matched'] : [],
        [wishlistDiscoveredQueryKeys.list(wishlistId ?? '', params)],
        {
            entityIds: wishlistId ? [wishlistId] : [],
            exact: true,
            owner: 'wishlist-discovered',
        },
    );
    useInvalidateQueriesOnEvents(
        wishlistId
            ? [
                  'content.hierarchyItem.created',
                  'content.hierarchyItem.updated',
                  'content.hierarchyItem.deleted',
                  'content.fulfillment.changed',
                  'metadata.refreshed',
                  'download.imported',
              ]
            : [],
        [wishlistDiscoveredQueryKeys.list(wishlistId ?? '', params)],
        {
            debounceMs: 250,
            entityIds: linkedContentIds,
            exact: true,
            failOpenEventNames: ['content.hierarchyItem.created'],
            maxWaitMs: 1000,
            owner: 'wishlist-discovered',
            predicate: (event) => linkedContentIds.includes(event.context['content.hierarchyItemId']),
        },
    );

    const rows: WishlistDiscoveredRow[] = discoveredQuery.data?.page.items ?? [];
    const pagination = discoveredQuery.data
        ? {
              pageNumber: discoveredQuery.data.page.pageNumber,
              pageSize: discoveredQuery.data.page.pageSize,
              totalItems: discoveredQuery.data.page.totalItems,
          }
        : null;

    return {
        rows,
        pagination,
        counts: discoveredQuery.data?.counts ?? null,
        isLoading: discoveredQuery.isLoading,
        isFetching: discoveredQuery.isFetching,
        error: queryErrorOrNull(discoveredQuery),
        refetch: discoveredQuery.refetch,
    };
}
