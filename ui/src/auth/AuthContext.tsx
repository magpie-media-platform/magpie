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
    getStoredSessionCacheUserId,
    hasStoredSession,
    isTokenExpired,
    fetchEffectivePermissions,
} from '@/lib/authApi';
import { AUTH_FAILURE_EVENT, AUTH_RENEWED_EVENT, isApiError } from '@/lib/api';
import { authScope, clearAuthStorage, sessionGeneration, tokenExpirationStorage } from '@/lib/tokenStorage';
import { ToastContext } from '@/components/ui/Toast';
import { useEventStream } from '@/hooks/useEventStream';

export interface AuthContextValue extends AuthState {
    hasPermission: (permission: Permission) => boolean;

    getRequiredRole: (permission: Permission) => Role | undefined;

    hasRole: (minRole: Role) => boolean;

    permissionsFailed: boolean;

    retryPermissions: () => void;

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
    permissionsFailed: false,
    retryPermissions: () => {},
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
const TOKEN_REFRESH_RETRY_MS = 5_000;

const permissionsQueryKey = (userId: string | undefined, cacheRevision: number) =>
    ['auth', 'permissions', userId, cacheRevision] as const;

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
    const [cacheRevision, setCacheRevision] = useState(0);
    const refreshTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const isRefreshingRef = useRef(false);
    const sessionGenerationRef = useRef(0);
    const cacheSessionRef = useRef({
        generation: sessionGeneration(),
        userId: initialUser?.id ?? getStoredSessionCacheUserId(),
    });
    const queryClient = useQueryClient();
    const clearSessionCache = useCallback(() => {
        queryClient.clear();
        setCacheRevision((revision) => revision + 1);
    }, [queryClient]);

    const restartAnonymousQueries = useCallback(() => {
        queryClient.getMutationCache().clear();
        for (const query of queryClient.getQueryCache().getAll()) {
            if (query.getObserversCount() === 0) {
                queryClient.removeQueries({ queryKey: query.queryKey, exact: true });
            } else {
                query.reset();
                query.setState({ data: undefined, dataUpdatedAt: 0, error: null, status: 'pending' });
            }
        }
        void queryClient.refetchQueries({ type: 'active' });
    }, [queryClient]);

    const rawToastCtx = useContext(ToastContext);
    const toastFnRef = useRef(rawToastCtx?.toast);
    toastFnRef.current = rawToastCtx?.toast;

    const isAuthenticated = user !== null;

    const permissionsQuery = useQuery({
        queryKey: permissionsQueryKey(user?.id, cacheRevision),
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
    const permissionsFailed = permissionsQuery.isLoadingError;
    const { refetch: refetchPermissions } = permissionsQuery;
    const retryPermissions = useCallback(() => void refetchPermissions(), [refetchPermissions]);

    const performRefresh = useCallback(async function refresh() {
        if (!hasStoredSession()) return;
        if (isRefreshingRef.current) {
            return;
        }

        isRefreshingRef.current = true;
        const generation = sessionGenerationRef.current;
        const storedGeneration = sessionGeneration();
        try {
            if (!isTokenExpired(TOKEN_REFRESH_BUFFER_MS / 1000)) {
                const existing = getCurrentUserFromToken();
                if (existing) {
                    setUser(existing);
                    setIsLoading(false);
                    return;
                }
            }

            const jwtUser = await refreshAccessToken();
            if (generation !== sessionGenerationRef.current || storedGeneration !== sessionGeneration()) return;
            setUser(jwtUser);
            setIsLoading(false);
        } catch (error) {
            if (generation !== sessionGenerationRef.current || storedGeneration !== sessionGeneration()) return;
            if (isAuthenticationFailure(error)) {
                clearAuthStorage();
                sessionGenerationRef.current += 1;
                setUser(null);
                setIsLoading(false);
                toastFnRef.current?.({
                    title: 'Session expired',
                    description: 'The session expired. Sign in again.',
                    variant: 'error',
                });
            } else if (hasStoredSession()) {
                refreshTimerRef.current = setTimeout(refresh, TOKEN_REFRESH_RETRY_MS);
            }
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

        if (hasStoredSession() && isTokenExpired()) {
            void performRefresh();
            return;
        }

        async function initSession() {
            try {
                if (!hasStoredSession()) {
                    setIsLoading(false);
                    return;
                }

                const sessionGeneration = sessionGenerationRef.current;
                const jwtUser = getCurrentUserFromToken();
                if (jwtUser) {
                    try {
                        await queryClient.fetchQuery({
                            queryKey: permissionsQueryKey(jwtUser.id, cacheRevision),
                            queryFn: fetchEffectivePermissions,
                            retry: false,
                            staleTime: Infinity,
                        });
                    } catch (error) {
                        if (isAuthenticationFailure(error) && sessionGeneration === sessionGenerationRef.current) {
                            clearAuthStorage();
                            return;
                        }
                    }
                    if (sessionGeneration === sessionGenerationRef.current && hasStoredSession()) {
                        setUser(jwtUser);
                    }
                } else {
                    clearAuthStorage();
                }
            } finally {
                setIsLoading(false);
            }
        }

        void initSession();
    }, [skipSessionInit, initialUser, initialLoading, queryClient, performRefresh, cacheRevision]);

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
                setIsLoading(false);
                clearSessionCache();
                return;
            }

            const updated = getCurrentUserFromToken();
            if (updated) {
                const generation = sessionGeneration();
                if (
                    generation !== cacheSessionRef.current.generation ||
                    updated.id !== cacheSessionRef.current.userId
                ) {
                    clearSessionCache();
                }
                cacheSessionRef.current = { generation, userId: updated.id };
                setUser(updated);
                setIsLoading(false);
            } else if (
                hasStoredSession() &&
                isTokenExpired(0) &&
                sessionGeneration() === cacheSessionRef.current.generation
            ) {
                void performRefresh();
            } else {
                setUser(null);
            }
        }

        function handleAuthFailure() {
            sessionGenerationRef.current += 1;
            setUser(null);
            setIsLoading(false);
            clearSessionCache();
        }

        function handleRenewed(event: Event) {
            if ((event as CustomEvent<{ scope: string }>).detail.scope !== authScope()) return;
            const updated = getCurrentUserFromToken();
            if (updated) {
                setUser(updated);
                setIsLoading(false);
            }
        }

        function handleResume() {
            if (!skipSessionInit && document.visibilityState === 'visible' && hasStoredSession()) {
                void performRefresh();
            }
        }

        window.addEventListener('storage', handleStorageChange);
        window.addEventListener(AUTH_FAILURE_EVENT, handleAuthFailure);
        window.addEventListener(AUTH_RENEWED_EVENT, handleRenewed);
        window.addEventListener('online', handleResume);
        document.addEventListener('visibilitychange', handleResume);
        return () => {
            window.removeEventListener('storage', handleStorageChange);
            window.removeEventListener(AUTH_FAILURE_EVENT, handleAuthFailure);
            window.removeEventListener(AUTH_RENEWED_EVENT, handleRenewed);
            window.removeEventListener('online', handleResume);
            document.removeEventListener('visibilitychange', handleResume);
        };
    }, [performRefresh, clearSessionCache, skipSessionInit, user?.id]);

    const checkPermission = useCallback(
        (permission: Permission): boolean => {
            if (!effectiveUser) return false;
            if (!permissionCatalog) return permissionsFailed;
            const requiredRole = permissionCatalog[permission];
            return requiredRole !== undefined && effectiveUser.role >= requiredRole;
        },
        [effectiveUser, permissionCatalog, permissionsFailed],
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

    const signOut = useCallback(async () => {
        sessionGenerationRef.current += 1;
        if (refreshTimerRef.current) {
            clearTimeout(refreshTimerRef.current);
            refreshTimerRef.current = null;
        }

        clearSessionCache();
        setUser(null);
        setIsLoading(false);
        await apiLogout();
    }, [clearSessionCache]);

    const login = useCallback(
        async (username: string, password: string) => {
            sessionGenerationRef.current += 1;
            const generation = sessionGenerationRef.current;
            const jwtUser = await apiLogin(username, password);
            if (generation !== sessionGenerationRef.current) return;
            const replacesSession = user !== null || cacheSessionRef.current.userId !== undefined;
            cacheSessionRef.current = { generation: sessionGeneration(), userId: jwtUser.id };
            if (replacesSession) clearSessionCache();
            else restartAnonymousQueries();
            setUser(jwtUser);
            setIsLoading(false);
        },
        [clearSessionCache, restartAnonymousQueries, user],
    );

    const contextValue = useMemo<AuthContextValue>(
        () => ({
            isLoading,
            isAuthenticated,
            user: effectiveUser,
            hasPermission: checkPermission,
            getRequiredRole,
            hasRole: checkRole,
            permissionsFailed,
            retryPermissions,
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
            permissionsFailed,
            retryPermissions,
            signOut,
            login,
        ],
    );

    return (
        <AuthContext.Provider key={cacheRevision} value={contextValue}>
            {children}
        </AuthContext.Provider>
    );
}
