import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { executeImmediateJobRequest, type JobActionDisposition } from '@/lib/jobActions';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';
import { useJobAction, type JobActionLifecycleOptions } from '@/hooks/useJobAction';
import { wishlistDiscoveredQueryKeys } from '@/hooks/useWishlistDiscovered';
import { contentFilterQueryKeys, type ContentFilterCriteria } from './useFilters';
import { tagQueryKeys } from './useTags';

const WISHLIST_CHANGE_EVENTS = [
    'wishlist.created',
    'wishlist.updated',
    'wishlist.deleted',
    'wishlist.item.matched',
] as const;

export interface ApiWishlist {
    id: string;

    contentFilterId: string;

    qualityTierListId?: string | null;

    rootFolderId?: string | null;

    ownerUserId?: string | null;

    name: string;

    ownerLock: boolean;

    enabled: boolean;

    lastSyncedAt?: string | null;

    createdAt: string;

    createdByUserId?: string | null;

    modifiedAt?: string | null;

    modifiedByUserId?: string | null;

    contentFilter?: {
        id: string;
        name: string;
        criteria?: ContentFilterCriteria;
    } | null;

    qualityTierList?: {
        id: string;
        name: string;
    } | null;
}

export interface WishlistPatch {
    contentFilterId?: { value: string | null };
    criteria?: { value: ContentFilterCriteria };
    qualityTierListId?: { value: string | null };
    rootFolderId?: { value: string | null };
    name?: { value: string };
    ownerLock?: { value: boolean };
    enabled?: { value: boolean };
    tags?: {
        add?: string[];
        remove?: string[];
    };
}

interface WishlistCreate {
    name: string;

    criteria?: ContentFilterCriteria;

    contentFilterId?: string | null;

    qualityTierListId?: string | null;

    rootFolderId?: string | null;

    ownerLock?: boolean;

    enabled?: boolean;
}

const wishlistQueryKeys = {
    all: ['wishlists'] as const,
    list: () => ['wishlists', 'list'] as const,
    myList: () => ['wishlists', 'my'] as const,
    detail: (id: string) => ['wishlists', 'detail', id] as const,
};

async function fetchWishlists(): Promise<ApiWishlist[]> {
    return apiGet<ApiWishlist[]>('/wishlists');
}

async function fetchWishlistById(id: string): Promise<ApiWishlist> {
    return apiGet<ApiWishlist>(`/wishlists/${id}`);
}

async function createWishlist(data: WishlistCreate): Promise<ApiWishlist> {
    return apiPost<ApiWishlist>('/wishlists', data);
}

async function patchMyWishlist(id: string, patch: WishlistPatch): Promise<void> {
    return apiPatch(`/wishlists/${id}`, patch);
}

async function patchSharedWishlist(id: string, patch: WishlistPatch): Promise<void> {
    return apiPatch(`/wishlists/${id}`, patch);
}

async function lockWishlist(id: string): Promise<void> {
    return apiPatch(`/wishlists/${id}`, { ownerLock: { value: true } });
}

async function unlockWishlist(id: string): Promise<void> {
    return apiPatch(`/wishlists/${id}`, { ownerLock: { value: false } });
}

function deleteFilterQuery(deleteFilter: boolean): string {
    return deleteFilter ? '?deleteFilter=true' : '';
}

async function deleteMyWishlist(id: string, deleteFilter: boolean): Promise<void> {
    return apiDelete(`/wishlists/${id}${deleteFilterQuery(deleteFilter)}`);
}

async function refreshMyWishlist(id: string): Promise<JobActionDisposition> {
    return executeImmediateJobRequest({
        jobType: 'WishlistRefreshItem',
        parameters: { wishlistId: id },
    });
}

async function deleteUserWishlist(id: string, deleteFilter: boolean): Promise<void> {
    return apiDelete(`/wishlists/${id}${deleteFilterQuery(deleteFilter)}`);
}

export function useWishlists() {
    useInvalidateQueriesOnEvents(
        WISHLIST_CHANGE_EVENTS,
        [wishlistQueryKeys.list(), contentFilterQueryKeys.shared, contentFilterQueryKeys.my],
        { exact: true, owner: 'wishlist-settings' },
    );
    const queryClient = useQueryClient();

    const wishlistsQuery = useQuery({
        queryKey: wishlistQueryKeys.list(),
        queryFn: fetchWishlists,
    });

    const createMutation = useMutation({
        mutationFn: createWishlist,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
        },
    });

    const patchMyMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: WishlistPatch }) => patchMyWishlist(id, patch),
        onSuccess: (_data, { id }) => {
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: tagQueryKeys.all });
        },
    });

    const patchSharedMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: WishlistPatch }) => patchSharedWishlist(id, patch),
        onSuccess: (_data, { id }) => {
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: tagQueryKeys.all });
        },
    });

    const lockMutation = useMutation({
        mutationFn: lockWishlist,
        onSuccess: (_data, id) => {
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
        },
    });

    const unlockMutation = useMutation({
        mutationFn: unlockWishlist,
        onSuccess: (_data, id) => {
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
        },
    });

    const deleteMyMutation = useMutation({
        mutationFn: ({ id, deleteFilter }: { id: string; deleteFilter: boolean }) => deleteMyWishlist(id, deleteFilter),
        onSuccess: (_data, { id }) => {
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: contentFilterQueryKeys.all });
        },
    });

    const deleteUserMutation = useMutation({
        mutationFn: ({ userId: _userId, id, deleteFilter }: { userId: string; id: string; deleteFilter: boolean }) =>
            deleteUserWishlist(id, deleteFilter),
        onSuccess: (_data, { id }) => {
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: contentFilterQueryKeys.all });
        },
    });

    const wishlists: ApiWishlist[] = wishlistsQuery.data ?? [];

    const updateWishlist = async (id: string, patch: WishlistPatch, isOwner: boolean): Promise<void> => {
        if (isOwner) {
            await patchMyMutation.mutateAsync({ id, patch });
        } else {
            await patchSharedMutation.mutateAsync({ id, patch });
        }
    };

    const toggleLock = async (id: string, lock: boolean): Promise<void> => {
        if (lock) {
            await lockMutation.mutateAsync(id);
        } else {
            await unlockMutation.mutateAsync(id);
        }
    };

    const deleteWishlist = async (
        id: string,
        isOwner: boolean,
        deleteFilter: boolean,
        ownerUserId?: string | null,
    ): Promise<void> => {
        if (isOwner) {
            await deleteMyMutation.mutateAsync({ id, deleteFilter });
        } else if (ownerUserId) {
            await deleteUserMutation.mutateAsync({ userId: ownerUserId, id, deleteFilter });
        }
    };

    return {
        wishlists,
        isLoading: wishlistsQuery.isLoading,
        isError: wishlistsQuery.isError,
        error: queryErrorOrNull(wishlistsQuery),

        createWishlist: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        updateWishlist,
        isUpdating: patchMyMutation.isPending || patchSharedMutation.isPending,

        toggleLock,
        isLocking: lockMutation.isPending || unlockMutation.isPending,

        deleteWishlist,
        isDeleting: deleteMyMutation.isPending || deleteUserMutation.isPending,

        refetch: wishlistsQuery.refetch,
    };
}

export function useWishlist(id: string | undefined) {
    useInvalidateQueriesOnEvents(WISHLIST_CHANGE_EVENTS, [wishlistQueryKeys.detail(id ?? '')], {
        entityIds: id ? [id] : [],
        exact: true,
        owner: 'wishlist-detail',
    });
    return useQuery({
        queryKey: wishlistQueryKeys.detail(id ?? ''),
        queryFn: () => fetchWishlistById(id!),
        enabled: !!id,
    });
}

export function useWishlistRefresh(options?: JobActionLifecycleOptions<string>) {
    const queryClient = useQueryClient();

    return useJobAction<string>({
        mutationFn: refreshMyWishlist,
        onJobStarted: (disposition, id) => {
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: contentFilterQueryKeys.all });
            options?.onJobStarted?.(disposition, id);
        },
        onJobExecutionSettled: (execution, id, disposition) => {
            void queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
            void queryClient.invalidateQueries({ queryKey: wishlistDiscoveredQueryKeys.wishlist(id) });
            options?.onJobExecutionSettled?.(execution, id, disposition);
        },
        trackExecution: options?.trackExecution,
    });
}
