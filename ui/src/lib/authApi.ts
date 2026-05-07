import { apiFetch, tokenStorage, ApiError } from './api';
import type { Role } from '@/auth/types';

export interface LoginRequest {
    username: string;

    password: string;
}

export interface AuthTokenResponse {
    accessToken: string;

    accessTokenExpiresAt: string;

    refreshToken: string;
}

export interface JwtUser {
    id: string;

    username: string;

    role: Role;
}

const REFRESH_TOKEN_KEY = 'magpie_refresh_token';

const TOKEN_EXPIRES_KEY = 'magpie_token_expires';

export const refreshTokenStorage = {
    get(): string | null {
        try {
            return localStorage.getItem(REFRESH_TOKEN_KEY);
        } catch {
            return null;
        }
    },

    set(token: string): void {
        try {
            localStorage.setItem(REFRESH_TOKEN_KEY, token);
        } catch {
            /* empty */
        }
    },

    remove(): void {
        try {
            localStorage.removeItem(REFRESH_TOKEN_KEY);
        } catch {
            /* empty */
        }
    },
};

export const tokenExpirationStorage = {
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
            /* empty */
        }
    },

    remove(): void {
        try {
            localStorage.removeItem(TOKEN_EXPIRES_KEY);
        } catch {
            /* empty */
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

        // Reject tokens that don't carry a numeric `exp` claim, or whose `exp` claim has
        // passed. The Magpie auth backend always mints tokens with `exp`; treating a
        // missing claim as "no expiry constraint" would let a maliciously crafted JWT
        // bypass session expiry entirely, so we fail closed here.
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

        const role = parseInt(roleStr, 10);
        if (isNaN(role) || role < 0 || role > 3) {
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
    refreshTokenStorage.set(response.refreshToken);
    tokenExpirationStorage.set(new Date(response.accessTokenExpiresAt));

    const user = decodeJwtUser(response.accessToken);
    if (!user) {
        throw new ApiError(500, 'Failed to decode user from token');
    }

    return user;
}

export async function logout(): Promise<void> {
    const refreshToken = refreshTokenStorage.get();

    clearAuthStorage();

    if (refreshToken) {
        try {
            await apiFetch('/auth/logout', {
                method: 'POST',
                body: { refreshToken },
            });
        } catch {
            /* empty */
        }
    }
}

export async function refreshAccessToken(): Promise<JwtUser> {
    const refreshToken = refreshTokenStorage.get();

    if (!refreshToken) {
        throw new ApiError(401, 'No refresh token available');
    }

    try {
        const response = await apiFetch<AuthTokenResponse>('/auth/refresh', {
            method: 'POST',
            body: { refreshToken },
            skipAuth: true,
        });

        tokenStorage.set(response.accessToken);
        refreshTokenStorage.set(response.refreshToken);
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
    return tokenStorage.exists() && refreshTokenStorage.get() !== null;
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
    refreshTokenStorage.remove();
    tokenExpirationStorage.remove();
}
