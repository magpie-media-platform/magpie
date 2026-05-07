import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import { tagQueryKeys } from './useTags';

export interface ApiWishlist {
    id: string;

    contentFilterId: string;

    qualityTierListId?: string | null;

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
    } | null;

    qualityTierList?: {
        id: string;
        name: string;
    } | null;
}

export interface WishlistPatch {
    contentFilterId?: { value: string };
    qualityTierListId?: { value: string | null };
    name?: { value: string };
    ownerLock?: { value: boolean };
    enabled?: { value: boolean };
    tags?: {
        add?: string[];
        remove?: string[];
    };
}

export interface WishlistCreate {
    contentFilterId: string;

    qualityTierListId?: string | null;

    name: string;

    enabled?: boolean;

    ownerLock?: boolean;
}

export const wishlistQueryKeys = {
    all: ['wishlists'] as const,
    list: () => ['wishlists', 'list'] as const,
    myList: () => ['wishlists', 'my'] as const,
    detail: (id: string) => ['wishlists', 'detail', id] as const,
};

async function fetchWishlists(): Promise<ApiWishlist[]> {
    return apiGet<ApiWishlist[]>('/wishlists');
}

export async function fetchMyWishlists(): Promise<ApiWishlist[]> {
    return apiGet<ApiWishlist[]>('/wishlists/my');
}

async function fetchWishlistById(id: string): Promise<ApiWishlist> {
    return apiGet<ApiWishlist>(`/wishlists/${id}`);
}

async function createWishlist(data: WishlistCreate): Promise<ApiWishlist> {
    return apiPost<ApiWishlist>('/wishlists/my', data);
}

async function patchMyWishlist(id: string, patch: WishlistPatch): Promise<void> {
    return apiPatch(`/wishlists/my/${id}`, patch);
}

async function patchSharedWishlist(id: string, patch: WishlistPatch): Promise<void> {
    return apiPatch(`/wishlists/${id}`, patch);
}

async function lockWishlist(id: string): Promise<ApiWishlist> {
    return apiPost<ApiWishlist>(`/wishlists/my/${id}/lock`);
}

async function unlockWishlist(id: string): Promise<ApiWishlist> {
    return apiPost<ApiWishlist>(`/wishlists/my/${id}/unlock`);
}

async function deleteMyWishlist(id: string): Promise<void> {
    return apiDelete(`/wishlists/my/${id}`);
}

async function deleteUserWishlist(userId: string, id: string): Promise<void> {
    return apiDelete(`/wishlists/${userId}/${id}`);
}

export function useWishlists() {
    const queryClient = useQueryClient();

    const wishlistsQuery = useQuery({
        queryKey: wishlistQueryKeys.list(),
        queryFn: fetchWishlists,
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const createMutation = useMutation({
        mutationFn: createWishlist,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
        },
    });

    const patchMyMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: WishlistPatch }) => patchMyWishlist(id, patch),
        onSuccess: (_data, { id }) => {
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
            queryClient.invalidateQueries({ queryKey: tagQueryKeys.all });
        },
    });

    const patchSharedMutation = useMutation({
        mutationFn: ({ id, patch }: { id: string; patch: WishlistPatch }) => patchSharedWishlist(id, patch),
        onSuccess: (_data, { id }) => {
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
            queryClient.invalidateQueries({ queryKey: tagQueryKeys.all });
        },
    });

    const lockMutation = useMutation({
        mutationFn: lockWishlist,
        onSuccess: (_data, id) => {
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
        },
    });

    const unlockMutation = useMutation({
        mutationFn: unlockWishlist,
        onSuccess: (_data, id) => {
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
        },
    });

    const deleteMyMutation = useMutation({
        mutationFn: deleteMyWishlist,
        onSuccess: (_data, id) => {
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.myList() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
        },
    });

    const deleteUserMutation = useMutation({
        mutationFn: ({ userId, id }: { userId: string; id: string }) => deleteUserWishlist(userId, id),
        onSuccess: (_data, { id }) => {
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.list() });
            queryClient.invalidateQueries({ queryKey: wishlistQueryKeys.detail(id) });
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

    const deleteWishlist = async (id: string, isOwner: boolean, ownerUserId?: string | null): Promise<void> => {
        if (isOwner) {
            await deleteMyMutation.mutateAsync(id);
        } else if (ownerUserId) {
            await deleteUserMutation.mutateAsync({ userId: ownerUserId, id });
        }
    };

    return {
        wishlists,
        isLoading: wishlistsQuery.isLoading,
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
    return useQuery({
        queryKey: wishlistQueryKeys.detail(id ?? ''),
        queryFn: () => fetchWishlistById(id!),
        enabled: !!id,
        staleTime: POLLING_INTERVALS.normal / 2,
    });
}
