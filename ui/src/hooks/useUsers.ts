import type { ScalarPatch } from '@/types/patch';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { useInvalidateQueriesOnEvents } from './useInvalidateQueriesOnEvents';
import { queryErrorOrNull } from '@/lib/utils';
import { RoleName, roleFromApi, type Role } from '@/auth';
import type { UserItem, UserCreate, UserUpdate } from '@/types';
import type { PagedList, SortDirectionName } from '@/types/paging';

function normalizeUser(user: UserItem): UserItem {
    return { ...user, role: roleFromApi(user.role) };
}

export interface UserQueryParams {
    providerType?: string;
    role?: Role;
    enabled?: boolean;
    pageNumber?: number;
    pageSize?: number;
    sortBy?: string;
    sortDirection?: SortDirectionName;
}

interface UseUsersOptions {
    enabled?: boolean;
}

interface ApiUserPatch {
    enabled?: ScalarPatch<boolean>;
    role?: ScalarPatch<string>;
}

export const userQueryKeys = {
    all: ['users'] as const,
    list: (params?: UserQueryParams) => ['users', 'list', params] as const,
    me: () => ['users', 'me'] as const,
};

function buildUserQueryUrl(params?: UserQueryParams): string {
    if (!params) {
        return '/users';
    }

    return buildUrl('/users', {
        providerType: params.providerType,
        role: params.role === undefined ? undefined : RoleName[params.role],
        enabled: params.enabled,
        pageNumber: params.pageNumber,
        pageSize: params.pageSize,
        sortBy: params.sortBy,
        sortDirection: params.sortDirection,
    });
}

async function fetchUsers(params?: UserQueryParams): Promise<PagedList<UserItem>> {
    const url = buildUserQueryUrl(params);
    const result = await apiGet<PagedList<UserItem>>(url);
    return { ...result, items: result.items.map(normalizeUser) };
}

async function fetchCurrentUser(): Promise<UserItem> {
    return normalizeUser(await apiGet<UserItem>('/users/my'));
}

async function createUser(data: UserCreate): Promise<UserItem> {
    return normalizeUser(await apiPost<UserItem>('/users', { ...data, role: RoleName[data.role] }));
}

async function updateUser(id: string, data: ApiUserPatch): Promise<void> {
    return apiPatch(`/users/${id}`, data);
}

async function deleteUser(id: string): Promise<void> {
    return apiDelete(`/users/${id}`);
}

async function unlockUser(id: string): Promise<void> {
    return apiPost(`/users/${id}/unlock`);
}

export function useUsers(params?: UserQueryParams, options?: UseUsersOptions) {
    useInvalidateQueriesOnEvents(
        ['user.created', 'user.updated', 'user.deleted', 'user.lockedOut', 'user.unlocked'],
        [userQueryKeys.list(params)],
        { exact: true },
    );
    const queryClient = useQueryClient();
    const enabled = options?.enabled ?? true;

    const usersQuery = useQuery({
        queryKey: userQueryKeys.list(params),
        queryFn: () => fetchUsers(params),
        enabled,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const createMutation = useMutation({
        mutationFn: createUser,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const updateMutation = useMutation({
        mutationFn: async ({ id, data }: { id: string; data: UserUpdate }) => {
            const apiData: ApiUserPatch = {};
            if (data.enabled !== undefined) {
                apiData.enabled = { value: data.enabled };
            }
            if (data.role !== undefined) {
                apiData.role = { value: RoleName[data.role] };
            }
            return updateUser(id, apiData);
        },
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const toggleMutation = useMutation({
        mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) =>
            updateUser(id, { enabled: { value: enabled } }),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteUser,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const unlockMutation = useMutation({
        mutationFn: unlockUser,
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const pagedResult = usersQuery.data ?? { items: [], pageNumber: null, pageSize: null, totalItems: 0 };
    const users: UserItem[] = pagedResult.items;

    const toggleUserEnabled = async (id: string, enabled: boolean): Promise<void> => {
        await toggleMutation.mutateAsync({ id, enabled });
    };

    const patchUser = async (id: string, data: UserUpdate): Promise<void> => {
        await updateMutation.mutateAsync({ id, data });
    };

    return {
        users,
        totalItems: pagedResult.totalItems,
        pageNumber: pagedResult.pageNumber,
        pageSize: pagedResult.pageSize,
        isLoading: usersQuery.isLoading,
        error: queryErrorOrNull(usersQuery),

        createUser: createMutation.mutateAsync,
        isCreating: createMutation.isPending,

        updateUser: patchUser,
        isUpdating: updateMutation.isPending,

        toggleUserEnabled,
        isToggling: toggleMutation.isPending,

        deleteUser: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        unlockUser: unlockMutation.mutateAsync,
        isUnlocking: unlockMutation.isPending,

        refetch: usersQuery.refetch,
    };
}

export function useCurrentUser() {
    const currentUserQuery = useQuery({
        queryKey: userQueryKeys.me(),
        queryFn: fetchCurrentUser,
        staleTime: POLLING_INTERVALS.slow,
    });
    useInvalidateQueriesOnEvents(
        ['user.updated', 'user.deleted', 'user.lockedOut', 'user.unlocked'],
        [userQueryKeys.me()],
        {
            entityIds: currentUserQuery.data?.id ? [currentUserQuery.data.id] : [],
            exact: true,
        },
    );

    return {
        currentUser: currentUserQuery.data ?? null,
        isLoading: currentUserQuery.isLoading,
        error: queryErrorOrNull(currentUserQuery),
        refetch: currentUserQuery.refetch,
    };
}
