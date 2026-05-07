/* eslint-disable react-refresh/only-export-components */
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { hasPermission, meetsRoleRequirement, Role, type AuthState, type Permission, type User } from './types';
import {
    login as apiLogin,
    logout as apiLogout,
    refreshAccessToken,
    getCurrentUserFromToken,
    hasStoredSession,
    isTokenExpired,
    clearAuthStorage,
    tokenExpirationStorage,
} from '@/lib/authApi';
import { AUTH_FAILURE_EVENT } from '@/lib/api';
import { ToastContext } from '@/components/ui/Toast';

export interface AuthContextValue extends AuthState {
    hasPermission: (permission: Permission) => boolean;

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
}

const TOKEN_REFRESH_BUFFER_MS = 60_000;

export function AuthProvider({
    children,
    initialUser = null,
    initialLoading,
    skipSessionInit = false,
}: AuthProviderProps) {
    const defaultLoading = initialLoading ?? (!skipSessionInit && hasStoredSession());
    const [isLoading, setIsLoading] = useState(defaultLoading);
    const [user, setUser] = useState<User | null>(initialUser);
    const refreshTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const isRefreshingRef = useRef(false);

    const rawToastCtx = useContext(ToastContext);
    const toastFnRef = useRef(rawToastCtx?.toast);
    toastFnRef.current = rawToastCtx?.toast;

    const isAuthenticated = user !== null;

    const performRefresh = useCallback(async () => {
        if (isRefreshingRef.current) {
            return;
        }

        isRefreshingRef.current = true;
        try {
            // Don't reuse the existing token if it's within the refresh buffer of expiring -- otherwise
            // the next request races expiry and only refreshes after a 401. isTokenExpired takes seconds.
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
                description: 'Your session could not be refreshed. Please sign in again.',
                variant: 'error',
            });
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
            performRefresh();
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

                if (isTokenExpired()) {
                    try {
                        const jwtUser = await refreshAccessToken();
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
                        setUser({
                            id: jwtUser.id,
                            username: jwtUser.username,
                            role: jwtUser.role,
                        });
                    }
                }
            } finally {
                setIsLoading(false);
            }
        }

        initSession();
    }, [skipSessionInit, initialUser, initialLoading]);

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
            if (e.key !== 'magpie_token_expires') {
                return;
            }

            if (!e.newValue) {
                setUser(null);
                return;
            }

            // Token couldn't be decoded into a usable user; treat as cross-tab logout to keep state in sync.
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
            if (!user) return false;
            return hasPermission(user.role, permission);
        },
        [user],
    );

    const checkRole = useCallback(
        (minRole: Role): boolean => {
            if (!user) return false;
            return meetsRoleRequirement(user.role, minRole);
        },
        [user],
    );

    const signIn = useCallback((newUser: User) => {
        setUser(newUser);
        setIsLoading(false);
    }, []);

    const signOut = useCallback(async () => {
        if (refreshTimerRef.current) {
            clearTimeout(refreshTimerRef.current);
            refreshTimerRef.current = null;
        }

        await apiLogout();
        setUser(null);
    }, []);

    const login = useCallback(async (username: string, password: string) => {
        const jwtUser = await apiLogin(username, password);
        setUser({
            id: jwtUser.id,
            username: jwtUser.username,
            role: jwtUser.role,
        });
        setIsLoading(false);
    }, []);

    const contextValue = useMemo<AuthContextValue>(
        () => ({
            isLoading,
            isAuthenticated,
            user,
            hasPermission: checkPermission,
            hasRole: checkRole,
            signIn,
            signOut,
            login,
        }),
        [isLoading, isAuthenticated, user, checkPermission, checkRole, signIn, signOut, login],
    );

    return <AuthContext.Provider value={contextValue}>{children}</AuthContext.Provider>;
}
