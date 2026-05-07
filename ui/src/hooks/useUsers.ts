import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

import { apiGet, apiPost, apiPatch, apiDelete, buildUrl } from '@/lib/api';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { Role } from '@/auth';
import type { UserItem, UserCreate, UserUpdate } from '@/types';
import type { PagedList } from '@/types/paging';

export interface UserQueryParams {
    providerType?: string;
    role?: Role;
    enabled?: boolean;
    page?: number;
    pageSize?: number;
    sortBy?: string;
    sortDirection?: 0 | 1 | 2;
}

interface ApiUserPatch {
    enabled?: { value: boolean };
    role?: { value: Role };
}

export const userQueryKeys = {
    all: ['users'] as const,
    list: (params?: UserQueryParams) => ['users', 'list', params] as const,
    detail: (id: string) => ['users', 'detail', id] as const,
    me: () => ['users', 'me'] as const,
};

function buildUserQueryUrl(params?: UserQueryParams): string {
    if (!params) {
        return '/users';
    }

    return buildUrl('/users', {
        providerType: params.providerType,
        role: params.role,
        enabled: params.enabled,
        page: params.page,
        pageSize: params.pageSize,
        sortBy: params.sortBy,
        sortDirection: params.sortDirection,
    });
}

async function fetchUsers(params?: UserQueryParams): Promise<PagedList<UserItem>> {
    const url = buildUserQueryUrl(params);
    return apiGet<PagedList<UserItem>>(url);
}

async function fetchCurrentUser(): Promise<UserItem> {
    return apiGet<UserItem>('/users/me');
}

async function fetchUser(id: string): Promise<UserItem> {
    return apiGet<UserItem>(`/users/${id}`);
}

async function createUser(data: UserCreate): Promise<UserItem> {
    return apiPost<UserItem>('/users', data);
}

async function updateUser(id: string, data: ApiUserPatch): Promise<void> {
    return apiPatch(`/users/${id}`, data);
}

async function enableUser(id: string): Promise<UserItem> {
    return apiPost<UserItem>(`/users/${id}/enable`);
}

async function disableUser(id: string): Promise<UserItem> {
    return apiPost<UserItem>(`/users/${id}/disable`);
}

async function deleteUser(id: string): Promise<void> {
    return apiDelete(`/users/${id}`);
}

export function useUsers(params?: UserQueryParams) {
    const queryClient = useQueryClient();

    const usersQuery = useQuery({
        queryKey: userQueryKeys.list(params),
        queryFn: () => fetchUsers(params),
        refetchInterval: POLLING_INTERVALS.slow,
        staleTime: POLLING_INTERVALS.slow / 2,
    });

    const createMutation = useMutation({
        mutationFn: (data: { username: string; password: string; role: Role }) =>
            createUser({
                username: data.username,
                password: data.password,
                role: data.role,
            }),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const updateMutation = useMutation({
        mutationFn: async ({ id, data }: { id: string; data: UserUpdate }) => {
            const apiData: ApiUserPatch = {};
            if (data.enabled !== undefined) {
                apiData.enabled = { value: data.enabled };
            }
            if (data.role !== undefined) {
                apiData.role = { value: data.role };
            }
            return updateUser(id, apiData);
        },
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const enableMutation = useMutation({
        mutationFn: enableUser,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const disableMutation = useMutation({
        mutationFn: disableUser,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const deleteMutation = useMutation({
        mutationFn: deleteUser,
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
        },
    });

    const pagedResult = usersQuery.data ?? { items: [], pageNumber: null, pageSize: null, totalItems: 0 };
    const users: UserItem[] = pagedResult.items;

    const toggleUserEnabled = async (id: string, enabled: boolean): Promise<UserItem> => {
        if (enabled) {
            return enableMutation.mutateAsync(id);
        } else {
            return disableMutation.mutateAsync(id);
        }
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
        isToggling: enableMutation.isPending || disableMutation.isPending,

        deleteUser: deleteMutation.mutateAsync,
        isDeleting: deleteMutation.isPending,

        refetch: usersQuery.refetch,
    };
}

export function useCurrentUser() {
    const currentUserQuery = useQuery({
        queryKey: userQueryKeys.me(),
        queryFn: fetchCurrentUser,
        staleTime: POLLING_INTERVALS.slow,
    });

    return {
        currentUser: currentUserQuery.data ?? null,
        isLoading: currentUserQuery.isLoading,
        error: queryErrorOrNull(currentUserQuery),
        refetch: currentUserQuery.refetch,
    };
}

export function useUser(id: string | null) {
    const userQuery = useQuery({
        queryKey: userQueryKeys.detail(id ?? ''),
        queryFn: () => fetchUser(id!),
        enabled: !!id,
        staleTime: POLLING_INTERVALS.slow,
    });

    return {
        user: userQuery.data ?? null,
        isLoading: userQuery.isLoading,
        error: queryErrorOrNull(userQuery),
        refetch: userQuery.refetch,
    };
}
