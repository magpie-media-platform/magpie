import { apiFetch, tokenStorage, ApiError } from './api';
import { Role, roleFromApi, type EffectivePermissions, type PermissionGrant } from '@/auth/types';

interface LoginRequest {
    username: string;

    password: string;
}

interface AuthTokenResponse {
    accessToken: string;

    accessTokenExpiresAt: string;
}

interface JwtUser {
    id: string;

    username: string;

    role: Role;
}

const TOKEN_EXPIRES_KEY = 'magpie_token_expires';

export const tokenExpirationStorage = {
    storageKey: TOKEN_EXPIRES_KEY,

    isStorageKey(key: string | null): boolean {
        return key === TOKEN_EXPIRES_KEY;
    },

    get(): Date | null {
        try {
            const value = localStorage.getItem(TOKEN_EXPIRES_KEY);
            return value ? new Date(value) : null;
        } catch {
            return null;
        }
    },

    set(expiresAt: Date): void {
        try {
            localStorage.setItem(TOKEN_EXPIRES_KEY, expiresAt.toISOString());
        } catch {
            void 0;
        }
    },

    remove(): void {
        try {
            localStorage.removeItem(TOKEN_EXPIRES_KEY);
        } catch {
            void 0;
        }
    },
};

export function decodeJwtUser(token: string): JwtUser | null {
    try {
        const parts = token.split('.');
        if (parts.length !== 3) {
            return null;
        }

        const payload = parts[1];
        const decodedPayload = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
        const claims = JSON.parse(decodedPayload);

        if (typeof claims.exp !== 'number' || claims.exp * 1000 <= Date.now()) {
            return null;
        }

        const id =
            claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] ||
            claims.sub ||
            claims.nameid;
        const username =
            claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'] || claims.name || claims.unique_name;
        const roleStr = claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || claims.role;

        if (!id || !username) {
            return null;
        }

        const numeric = parseInt(roleStr, 10);
        const role = isNaN(numeric) ? (Role as Record<string, number>)[roleStr] : numeric;
        if (typeof role !== 'number' || isNaN(role) || role < 0 || role > 3) {
            return null;
        }

        return {
            id,
            username,
            role: role as Role,
        };
    } catch {
        return null;
    }
}

export async function login(username: string, password: string): Promise<JwtUser> {
    const response = await apiFetch<AuthTokenResponse>('/auth/login', {
        method: 'POST',
        body: { username, password } satisfies LoginRequest,
        skipAuth: true,
    });

    tokenStorage.set(response.accessToken);
    tokenExpirationStorage.set(new Date(response.accessTokenExpiresAt));

    const user = decodeJwtUser(response.accessToken);
    if (!user) {
        throw new ApiError(500, 'Failed to decode user from token');
    }

    return user;
}

export async function logout(): Promise<void> {
    const token = tokenStorage.get();

    try {
        await apiFetch('/auth/logout', {
            method: 'POST',
            ...(token ? { headers: { Authorization: `Bearer ${token}` } } : { skipAuth: true }),
        });
    } catch {
        void 0;
    } finally {
        clearAuthStorage();
    }
}

export async function refreshAccessToken(): Promise<JwtUser> {
    try {
        const response = await apiFetch<AuthTokenResponse>('/auth/refresh', {
            method: 'POST',
            skipAuth: true,
        });

        tokenStorage.set(response.accessToken);
        tokenExpirationStorage.set(new Date(response.accessTokenExpiresAt));

        const user = decodeJwtUser(response.accessToken);
        if (!user) {
            throw new ApiError(500, 'Failed to decode user from token');
        }

        return user;
    } catch (error) {
        clearAuthStorage();
        throw error;
    }
}

interface RawPermissionGrant {
    requiredRole: Role | string | number;

    granted: boolean;
}

interface RawEffectivePermissions {
    role: Role | string | number;

    permissions: Record<string, RawPermissionGrant>;
}

export async function fetchEffectivePermissions(): Promise<EffectivePermissions> {
    const raw = await apiFetch<RawEffectivePermissions>('/auth/permissions', { method: 'GET' });

    const permissions: Record<string, PermissionGrant> = {};
    for (const [key, grant] of Object.entries(raw?.permissions ?? {})) {
        permissions[key] = {
            requiredRole: roleFromApi(grant.requiredRole),
            granted: Boolean(grant.granted),
        };
    }

    return { role: roleFromApi(raw?.role), permissions };
}

export function isTokenExpired(bufferSeconds = 60): boolean {
    const expiresAt = tokenExpirationStorage.get();
    if (!expiresAt) {
        return true;
    }

    const now = new Date();
    const bufferMs = bufferSeconds * 1000;
    return now.getTime() >= expiresAt.getTime() - bufferMs;
}

export function hasStoredSession(): boolean {
    return tokenStorage.exists();
}

export function getCurrentUserFromToken(): JwtUser | null {
    const token = tokenStorage.get();
    if (!token) {
        return null;
    }
    return decodeJwtUser(token);
}

export function clearAuthStorage(): void {
    tokenStorage.remove();
    tokenExpirationStorage.remove();
}
