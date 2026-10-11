import { apiFetch, ApiError, renewAccessToken, type AuthTokenResponse } from './api';
import {
    authScope,
    clearAuthStorage,
    sessionGeneration,
    storeAuthTokens,
    tokenExpirationStorage,
    tokenStorage,
} from './tokenStorage';
import { withAuthSessionLock } from './authSessionLock';
import { isRole, Role, roleFromApi, type EffectivePermissions, type PermissionGrant } from '@/auth/types';

interface LoginRequest {
    username: string;

    password: string;
}

interface JwtUser {
    id: string;

    username: string;

    role: Role;
}

function readJwtClaims(token: string) {
    try {
        const parts = token.split('.');
        if (parts.length !== 3) {
            return null;
        }

        const payload = parts[1];
        const decodedPayload = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
        return JSON.parse(decodedPayload);
    } catch {
        return null;
    }
}

export function getStoredSessionCacheUserId(): string | undefined {
    const claims = readJwtClaims(tokenStorage.get() ?? '');
    const id =
        claims?.['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] ||
        claims?.sub ||
        claims?.nameid;
    return typeof id === 'string' && id ? id : undefined;
}

export function decodeJwtUser(token: string): JwtUser | null {
    try {
        const claims = readJwtClaims(token);
        if (!claims) return null;

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
        if (!isRole(role)) {
            return null;
        }

        return {
            id,
            username,
            role,
        };
    } catch {
        return null;
    }
}

export async function login(username: string, password: string): Promise<JwtUser> {
    clearAuthStorage();
    const generation = sessionGeneration();
    return withAuthSessionLock(authScope(), async (signal) => {
        if (sessionGeneration() !== generation) throw new ApiError(409, 'The session changed.');
        const response = await apiFetch<AuthTokenResponse>('/auth/login', {
            method: 'POST',
            body: { username, password } satisfies LoginRequest,
            skipAuth: true,
            signal,
        });
        signal.throwIfAborted();
        if (sessionGeneration() !== generation) throw new ApiError(409, 'The session changed.');
        const user = decodeJwtUser(response.accessToken);
        if (!user) throw new ApiError(500, 'Failed to decode user from token');
        storeAuthTokens(response.accessToken, response.accessTokenExpiresAt);
        return user;
    });
}

export async function logout(): Promise<void> {
    const token = tokenStorage.get();
    clearAuthStorage();
    try {
        await withAuthSessionLock(authScope(), async (signal) => {
            await apiFetch('/auth/logout', {
                method: 'POST',
                signal,
                ...(token ? { headers: { Authorization: `Bearer ${token}` } } : { skipAuth: true }),
            });
        });
    } catch {
        void 0;
    }
}

export async function refreshAccessToken(baseUrl?: string): Promise<JwtUser> {
    const accessToken = await renewAccessToken(baseUrl);
    const user = decodeJwtUser(accessToken);
    if (!user) throw new ApiError(500, 'Failed to decode user from token');
    return user;
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
