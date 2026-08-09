import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';

import {
    meetsRoleRequirement,
    permissionCatalogFromEffective,
    Role,
    type AuthState,
    type EffectivePermissions,
    type Permission,
    type PermissionCatalog,
    type User,
} from './types';
import {
    login as apiLogin,
    logout as apiLogout,
    refreshAccessToken,
    getCurrentUserFromToken,
    hasStoredSession,
    isTokenExpired,
    clearAuthStorage,
    tokenExpirationStorage,
    fetchEffectivePermissions,
} from '@/lib/authApi';
import { AUTH_FAILURE_EVENT, isApiError } from '@/lib/api';
import { ToastContext } from '@/components/ui/Toast';
import { useEventStream } from '@/hooks/useEventStream';

export interface AuthContextValue extends AuthState {
    hasPermission: (permission: Permission) => boolean;

    getRequiredRole: (permission: Permission) => Role | undefined;

    hasRole: (minRole: Role) => boolean;

    signIn: (user: User) => void;

    signOut: () => Promise<void>;

    login: (username: string, password: string) => Promise<void>;
}

const defaultContextValue: AuthContextValue = {
    isLoading: true,
    isAuthenticated: false,
    user: null,
    hasPermission: () => false,
    getRequiredRole: () => undefined,
    hasRole: () => false,
    signIn: () => {},
    signOut: async () => {},
    login: async () => {},
};

export const AuthContext = createContext<AuthContextValue>(defaultContextValue);
AuthContext.displayName = 'AuthContext';

export interface AuthProviderProps {
    children: ReactNode;

    initialUser?: User | null;

    initialLoading?: boolean;

    skipSessionInit?: boolean;

    initialPermissions?: EffectivePermissions;
}

const TOKEN_REFRESH_BUFFER_MS = 60_000;

const permissionsQueryKey = (userId: string | undefined) => ['auth', 'permissions', userId] as const;

function isAuthenticationFailure(error: unknown): boolean {
    return isApiError(error) && (error.status === 401 || error.status === 403);
}

export function AuthProvider({
    children,
    initialUser = null,
    initialLoading,
    skipSessionInit = false,
    initialPermissions,
}: AuthProviderProps) {
    const defaultLoading = initialLoading ?? (!skipSessionInit && hasStoredSession());
    const [isLoading, setIsLoading] = useState(defaultLoading);
    const [user, setUser] = useState<User | null>(initialUser);
    const refreshTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const isRefreshingRef = useRef(false);
    const sessionGenerationRef = useRef(0);
    const queryClient = useQueryClient();

    const rawToastCtx = useContext(ToastContext);
    const toastFnRef = useRef(rawToastCtx?.toast);
    toastFnRef.current = rawToastCtx?.toast;

    const isAuthenticated = user !== null;

    const permissionsQuery = useQuery({
        queryKey: permissionsQueryKey(user?.id),
        queryFn: fetchEffectivePermissions,
        enabled: isAuthenticated && initialPermissions === undefined,
        initialData: initialPermissions,
        staleTime: Infinity,
        gcTime: Infinity,
    });

    useEventStream(
        () => {
            void permissionsQuery.refetch();
        },
        {
            eventName: ['user.updated', 'user.deleted', 'user.lockedOut', 'user.unlocked'],
            entityId: user?.id,
            enabled: isAuthenticated,
            owner: 'auth-permissions',
        },
    );

    const effectiveUser = useMemo<User | null>(
        () =>
            user
                ? {
                      ...user,
                      role: initialPermissions === undefined ? (permissionsQuery.data?.role ?? user.role) : user.role,
                  }
                : null,
        [initialPermissions, permissionsQuery.data?.role, user],
    );

    const permissionCatalog = useMemo<PermissionCatalog | null>(
        () => (permissionsQuery.data ? permissionCatalogFromEffective(permissionsQuery.data) : null),
        [permissionsQuery.data],
    );

    const performRefresh = useCallback(async () => {
        if (isRefreshingRef.current) {
            return;
        }

        isRefreshingRef.current = true;
        try {
            if (!isTokenExpired(TOKEN_REFRESH_BUFFER_MS / 1000)) {
                const existing = getCurrentUserFromToken();
                if (existing) {
                    setUser({
                        id: existing.id,
                        username: existing.username,
                        role: existing.role,
                    });
                    return;
                }
            }

            const jwtUser = await refreshAccessToken();
            setUser({
                id: jwtUser.id,
                username: jwtUser.username,
                role: jwtUser.role,
            });
        } catch {
            toastFnRef.current?.({
                title: 'Session expired',
                description: 'The session expired. Sign in again.',
                variant: 'error',
            });
            sessionGenerationRef.current += 1;
            setUser(null);
        } finally {
            isRefreshingRef.current = false;
        }
    }, []);

    const scheduleRefresh = useCallback(() => {
        if (refreshTimerRef.current) {
            clearTimeout(refreshTimerRef.current);
            refreshTimerRef.current = null;
        }

        if (!hasStoredSession()) {
            return;
        }

        const expiresAt = tokenExpirationStorage.get();
        if (!expiresAt) {
            return;
        }

        const now = new Date();
        const msUntilRefresh = expiresAt.getTime() - now.getTime() - TOKEN_REFRESH_BUFFER_MS;

        if (msUntilRefresh <= 0) {
            void performRefresh();
        } else {
            refreshTimerRef.current = setTimeout(performRefresh, msUntilRefresh);
        }
    }, [performRefresh]);

    useEffect(() => {
        if (skipSessionInit || initialUser) {
            if (initialLoading === undefined) {
                setIsLoading(false);
            }
            return;
        }

        async function initSession() {
            try {
                if (!hasStoredSession()) {
                    setIsLoading(false);
                    return;
                }

                const sessionGeneration = sessionGenerationRef.current;
                if (isTokenExpired()) {
                    try {
                        const jwtUser = await refreshAccessToken();
                        if (sessionGeneration !== sessionGenerationRef.current || !hasStoredSession()) {
                            return;
                        }
                        setUser({
                            id: jwtUser.id,
                            username: jwtUser.username,
                            role: jwtUser.role,
                        });
                    } catch {
                        clearAuthStorage();
                    }
                } else {
                    const jwtUser = getCurrentUserFromToken();
                    if (jwtUser) {
                        try {
                            await queryClient.fetchQuery({
                                queryKey: permissionsQueryKey(jwtUser.id),
                                queryFn: fetchEffectivePermissions,
                                retry: false,
                                staleTime: Infinity,
                            });
                        } catch (error) {
                            if (isAuthenticationFailure(error)) {
                                clearAuthStorage();
                                return;
                            }
                        }
                        if (sessionGeneration === sessionGenerationRef.current && hasStoredSession()) {
                            setUser({
                                id: jwtUser.id,
                                username: jwtUser.username,
                                role: jwtUser.role,
                            });
                        }
                    } else {
                        clearAuthStorage();
                    }
                }
            } finally {
                setIsLoading(false);
            }
        }

        void initSession();
    }, [skipSessionInit, initialUser, initialLoading, queryClient]);

    useEffect(() => {
        if (user && !skipSessionInit) {
            scheduleRefresh();
        }

        return () => {
            if (refreshTimerRef.current) {
                clearTimeout(refreshTimerRef.current);
                refreshTimerRef.current = null;
            }
        };
    }, [user, skipSessionInit, scheduleRefresh]);

    useEffect(() => {
        function handleStorageChange(e: StorageEvent) {
            if (!tokenExpirationStorage.isStorageKey(e.key)) {
                return;
            }

            sessionGenerationRef.current += 1;
            if (!e.newValue) {
                setUser(null);
                return;
            }

            const updated = getCurrentUserFromToken();
            if (updated) {
                setUser({
                    id: updated.id,
                    username: updated.username,
                    role: updated.role,
                });
            } else {
                setUser(null);
            }
        }

        function handleAuthFailure() {
            sessionGenerationRef.current += 1;
            setUser(null);
        }

        window.addEventListener('storage', handleStorageChange);
        window.addEventListener(AUTH_FAILURE_EVENT, handleAuthFailure);
        return () => {
            window.removeEventListener('storage', handleStorageChange);
            window.removeEventListener(AUTH_FAILURE_EVENT, handleAuthFailure);
        };
    }, []);

    const checkPermission = useCallback(
        (permission: Permission): boolean => {
            if (!effectiveUser || !permissionCatalog) return false;
            const requiredRole = permissionCatalog[permission];
            return requiredRole !== undefined && effectiveUser.role >= requiredRole;
        },
        [effectiveUser, permissionCatalog],
    );

    const getRequiredRole = useCallback(
        (permission: Permission): Role | undefined => permissionCatalog?.[permission],
        [permissionCatalog],
    );

    const checkRole = useCallback(
        (minRole: Role): boolean => {
            if (!effectiveUser) return false;
            return meetsRoleRequirement(effectiveUser.role, minRole);
        },
        [effectiveUser],
    );

    const signIn = useCallback(
        (newUser: User) => {
            sessionGenerationRef.current += 1;
            queryClient.clear();
            setUser(newUser);
            setIsLoading(false);
        },
        [queryClient],
    );

    const signOut = useCallback(async () => {
        sessionGenerationRef.current += 1;
        if (refreshTimerRef.current) {
            clearTimeout(refreshTimerRef.current);
            refreshTimerRef.current = null;
        }

        await apiLogout();
        queryClient.clear();
        setUser(null);
    }, [queryClient]);

    const login = useCallback(
        async (username: string, password: string) => {
            sessionGenerationRef.current += 1;
            const jwtUser = await apiLogin(username, password);
            queryClient.clear();
            setUser({
                id: jwtUser.id,
                username: jwtUser.username,
                role: jwtUser.role,
            });
            setIsLoading(false);
        },
        [queryClient],
    );

    const contextValue = useMemo<AuthContextValue>(
        () => ({
            isLoading,
            isAuthenticated,
            user: effectiveUser,
            hasPermission: checkPermission,
            getRequiredRole,
            hasRole: checkRole,
            signIn,
            signOut,
            login,
        }),
        [
            isLoading,
            isAuthenticated,
            effectiveUser,
            checkPermission,
            getRequiredRole,
            checkRole,
            signIn,
            signOut,
            login,
        ],
    );

    return <AuthContext.Provider value={contextValue}>{children}</AuthContext.Provider>;
}
