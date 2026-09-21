import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

import { Role } from '@/auth';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
        if (!params) {
            return path;
        }
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        const queryString = searchParams.toString();
        return queryString ? `${path}?${queryString}` : path;
    }),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { useUsers, useCurrentUser } from './useUsers';
import type { UserItem } from '@/types';

const mockUsers: UserItem[] = [
    {
        id: 'user-1',
        username: 'admin',
        email: 'admin@example.com',
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: true,
        role: Role.Admin,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: '2024-01-15T10:30:00Z',
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: null,
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-2',
        username: 'manager',
        email: 'manager@example.com',
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: true,
        role: Role.Manager,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: '2024-01-15T09:00:00Z',
        createdAt: '2024-01-02T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
    {
        id: 'user-3',
        username: 'disabled_user',
        email: 'disabled@example.com',
        extensionId: null,
        providerType: 'local',
        externalId: null,
        settings: null,
        enabled: false,
        role: Role.Viewer,
        failedLoginAttempts: 0,
        lockedUntil: null,
        lastLoginAt: null,
        createdAt: '2024-01-03T00:00:00Z',
        createdByUserId: 'user-1',
        modifiedAt: null,
        modifiedByUserId: null,
    },
];

const mockPagedResponse = {
    items: mockUsers,
    pageNumber: 1,
    pageSize: 25,
    totalItems: 3,
};

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
            mutations: {
                retry: false,
            },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('useUsers', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);
    const mockApiPatch = vi.mocked(apiPatch);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching users', () => {
        it('should fetch users from the API', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/users');
        });

        it('should return users from API', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.users).toHaveLength(3);
            expect(result.current.users[0].username).toBe('admin');
            expect(result.current.users[1].username).toBe('manager');
            expect(result.current.totalItems).toBe(3);
        });

        it('should fetch users with query parameters', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useUsers({ role: Role.Admin, enabled: true, pageNumber: 1 }), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/users?role=Admin&enabled=true&pageNumber=1');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.users).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns no items', async () => {
            mockApiGet.mockResolvedValue({
                items: [],
                pageNumber: 1,
                pageSize: 25,
                totalItems: 0,
            });

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.users).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('creating users', () => {
        it('should create a user via POST /users', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPost.mockResolvedValue({
                id: 'user-4',
                username: 'newuser',
                email: null,
                extensionId: null,
                providerType: 'local',
                externalId: null,
                settings: null,
                enabled: true,
                role: Role.Viewer,
                failedLoginAttempts: 0,
                lockedUntil: null,
                lastLoginAt: null,
                createdAt: '2024-01-04T00:00:00Z',
                createdByUserId: 'user-1',
                modifiedAt: null,
                modifiedByUserId: null,
            });

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createUser({
                    username: 'newuser',
                    password: 'password123',
                    role: Role.Viewer,
                    enabled: true,
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/users', {
                username: 'newuser',
                password: 'password123',
                role: 'Viewer',
                enabled: true,
            });
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolvePost: (value: UserItem) => void;
            const postPromise = new Promise<UserItem>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.createUser({
                    username: 'newuser',
                    password: 'password123',
                    role: Role.Viewer,
                    enabled: true,
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(true);
            });

            act(() => {
                resolvePost!({
                    id: 'user-4',
                    username: 'newuser',
                    email: null,
                    extensionId: null,
                    providerType: 'local',
                    externalId: null,
                    settings: null,
                    enabled: true,
                    role: Role.Viewer,
                    failedLoginAttempts: 0,
                    lockedUntil: null,
                    lastLoginAt: null,
                    createdAt: '2024-01-04T00:00:00Z',
                    createdByUserId: 'user-1',
                    modifiedAt: null,
                    modifiedByUserId: null,
                });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(false);
            });
        });
    });

    describe('updating users', () => {
        it('should update a user via PATCH /users/{id}', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateUser('user-1', {
                    role: Role.Manager,
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/users/user-1', {
                role: { value: 'Manager' },
            });
        });

        it('should update enabled status via PATCH', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.updateUser('user-1', {
                    enabled: false,
                });
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/users/user-1', {
                enabled: { value: false },
            });
        });

        it('should set isUpdating while updating', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.updateUser('user-1', { role: Role.Manager });
            });

            await waitFor(() => {
                expect(result.current.isUpdating).toBe(true);
            });

            act(() => {
                resolvePatch!();
            });

            await waitFor(() => {
                expect(result.current.isUpdating).toBe(false);
            });
        });
    });

    describe('toggling user enabled', () => {
        it('should enable a user via PATCH /users/{id}', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleUserEnabled('user-3', true);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/users/user-3', {
                enabled: { value: true },
            });
        });

        it('should disable a user via PATCH /users/{id}', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPatch.mockResolvedValue(undefined);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.toggleUserEnabled('user-1', false);
            });

            expect(mockApiPatch).toHaveBeenCalledWith('/users/user-1', {
                enabled: { value: false },
            });
        });

        it('should set isToggling while toggling', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolvePatch: () => void;
            const patchPromise = new Promise<void>((resolve) => {
                resolvePatch = resolve;
            });
            mockApiPatch.mockReturnValue(patchPromise);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.toggleUserEnabled('user-3', true);
            });

            await waitFor(() => {
                expect(result.current.isToggling).toBe(true);
            });

            act(() => {
                resolvePatch!();
            });

            await waitFor(() => {
                expect(result.current.isToggling).toBe(false);
            });
        });
    });

    describe('deleting users', () => {
        it('should delete a user via DELETE /users/{id}', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteUser('user-3');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/users/user-3');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.deleteUser('user-3');
            });

            await waitFor(() => {
                expect(result.current.isDeleting).toBe(true);
            });

            act(() => {
                resolveDelete!();
            });

            await waitFor(() => {
                expect(result.current.isDeleting).toBe(false);
            });
        });
    });

    describe('unlocking users', () => {
        it('should unlock a user via POST /users/{id}/unlock', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPost.mockResolvedValue(undefined);

            const { result } = renderHook(() => useUsers(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.unlockUser('user-3');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/users/user-3/unlock');
        });
    });

    describe('query keys', () => {
        it('should export query keys for external use', async () => {
            const { userQueryKeys } = await import('./useUsers');

            expect(userQueryKeys.all).toEqual(['users']);
            expect(userQueryKeys.list()).toEqual(['users', 'list', undefined]);
            expect(userQueryKeys.list({ role: Role.Admin })).toEqual(['users', 'list', { role: Role.Admin }]);
            expect(userQueryKeys.me()).toEqual(['users', 'me']);
        });
    });
});

describe('useCurrentUser', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch current user from the API', async () => {
        mockApiGet.mockResolvedValue(mockUsers[0]);

        const { result } = renderHook(() => useCurrentUser(), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/users/my');
        expect(result.current.currentUser?.username).toBe('admin');
    });

    it('should return null when not authenticated', async () => {
        mockApiGet.mockRejectedValue({ status: 401, message: 'Unauthorized' });

        const { result } = renderHook(() => useCurrentUser(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.currentUser).toBeNull();
    });
});
