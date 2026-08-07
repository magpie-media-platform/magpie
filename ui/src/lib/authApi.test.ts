import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

import {
    decodeJwtUser,
    login,
    logout,
    refreshAccessToken,
    isTokenExpired,
    hasStoredSession,
    getCurrentUserFromToken,
    clearAuthStorage,
    tokenExpirationStorage,
} from './authApi';
import { tokenStorage } from './api';
import { Role } from '@/auth/types';

const mockFetch = vi.fn();
global.fetch = mockFetch;

function createMockJwt(claims: Record<string, unknown>): string {
    const claimsWithExp: Record<string, unknown> = {
        exp: Math.floor(Date.now() / 1000) + 3600,
        ...claims,
    };
    if (claimsWithExp.exp === undefined) {
        delete claimsWithExp.exp;
    }
    const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
    const payload = btoa(JSON.stringify(claimsWithExp));
    const signature = 'mock-signature';
    return `${header}.${payload}.${signature}`;
}

describe('authApi', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        clearAuthStorage();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('decodeJwtUser', () => {
        it('decodes user from JWT with standard .NET claims', () => {
            const token = createMockJwt({
                'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier': 'user-123',
                'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name': 'testuser',
                'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': '2',
            });

            const user = decodeJwtUser(token);

            expect(user).toEqual({
                id: 'user-123',
                username: 'testuser',
                role: Role.Manager,
            });
        });

        it('decodes user from JWT with short claim names', () => {
            const token = createMockJwt({
                sub: 'user-456',
                name: 'admin',
                role: '3',
            });

            const user = decodeJwtUser(token);

            expect(user).toEqual({
                id: 'user-456',
                username: 'admin',
                role: Role.Admin,
            });
        });

        it('returns null for invalid token format', () => {
            expect(decodeJwtUser('not-a-jwt')).toBeNull();
            expect(decodeJwtUser('only.two')).toBeNull();
            expect(decodeJwtUser('')).toBeNull();
        });

        it('returns null for token with missing required claims', () => {
            const token = createMockJwt({ foo: 'bar' });
            expect(decodeJwtUser(token)).toBeNull();
        });

        it('returns null for token with invalid role', () => {
            const token = createMockJwt({
                sub: 'user-123',
                name: 'testuser',
                role: '5',
            });
            expect(decodeJwtUser(token)).toBeNull();
        });

        it('decodes the C# enum role name', () => {
            const token = createMockJwt({
                sub: 'user-123',
                name: 'testuser',
                role: 'Admin',
            });
            expect(decodeJwtUser(token)).toEqual({
                id: 'user-123',
                username: 'testuser',
                role: Role.Admin,
            });
        });

        it('returns null for an unrecognized role name', () => {
            const token = createMockJwt({
                sub: 'user-123',
                name: 'testuser',
                role: 'Wizard',
            });
            expect(decodeJwtUser(token)).toBeNull();
        });

        it('returns null when the exp claim has already passed', () => {
            const token = createMockJwt({
                sub: 'user-123',
                name: 'testuser',
                role: '2',
                exp: Math.floor(Date.now() / 1000) - 60,
            });
            expect(decodeJwtUser(token)).toBeNull();
        });

        it('returns user when the exp claim is in the future', () => {
            const token = createMockJwt({
                sub: 'user-123',
                name: 'testuser',
                role: '2',
                exp: Math.floor(Date.now() / 1000) + 3600,
            });
            expect(decodeJwtUser(token)).toEqual({
                id: 'user-123',
                username: 'testuser',
                role: Role.Manager,
            });
        });

        it('returns null when the exp claim is missing', () => {
            const token = createMockJwt({
                sub: 'user-123',
                name: 'testuser',
                role: '2',
                exp: undefined,
            });
            expect(decodeJwtUser(token)).toBeNull();
        });

        it('returns null when the exp claim is not a number', () => {
            const token = createMockJwt({
                sub: 'user-123',
                name: 'testuser',
                role: '2',
                exp: 'never',
            });
            expect(decodeJwtUser(token)).toBeNull();
        });
    });

    describe('login', () => {
        const mockTokenResponse = {
            accessToken: createMockJwt({
                sub: 'user-123',
                name: 'testuser',
                role: '2',
            }),
            accessTokenExpiresAt: new Date(Date.now() + 3600000).toISOString(),
        };

        it('calls API and stores tokens on successful login', async () => {
            mockFetch.mockResolvedValueOnce({
                ok: true,
                status: 200,
                json: async () => mockTokenResponse,
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            const user = await login('testuser', 'password');

            expect(mockFetch).toHaveBeenCalledWith(
                expect.stringContaining('/auth/login'),
                expect.objectContaining({
                    method: 'POST',
                    body: JSON.stringify({ username: 'testuser', password: 'password' }),
                    credentials: 'include',
                }),
            );

            expect(user).toEqual({
                id: 'user-123',
                username: 'testuser',
                role: Role.Manager,
            });

            expect(tokenStorage.get()).toBe(mockTokenResponse.accessToken);
            expect(tokenExpirationStorage.get()).toBeInstanceOf(Date);
        });

        it('throws on 401 unauthorized', async () => {
            mockFetch.mockResolvedValueOnce({
                ok: false,
                status: 401,
                json: async () => ({ title: 'Unauthorized' }),
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            await expect(login('wrong', 'creds')).rejects.toMatchObject({
                status: 401,
            });
        });

        it('throws on 400 bad request', async () => {
            mockFetch.mockResolvedValueOnce({
                ok: false,
                status: 400,
                json: async () => ({ title: 'Bad Request' }),
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            await expect(login('', '')).rejects.toMatchObject({
                status: 400,
            });
        });
    });

    describe('logout', () => {
        it('clears storage and calls API', async () => {
            tokenStorage.set('access-token');
            tokenExpirationStorage.set(new Date());

            mockFetch.mockResolvedValueOnce({
                ok: true,
                status: 204,
                headers: new Headers(),
            });

            await logout();

            expect(tokenStorage.get()).toBeNull();
            expect(tokenExpirationStorage.get()).toBeNull();

            expect(mockFetch).toHaveBeenCalledWith(
                expect.stringContaining('/auth/logout'),
                expect.objectContaining({
                    method: 'POST',
                    credentials: 'include',
                }),
            );
            const [, init] = mockFetch.mock.calls[0];
            expect(init.body).toBeUndefined();
        });

        it('clears storage even if API call fails', async () => {
            tokenStorage.set('access-token');
            tokenExpirationStorage.set(new Date());

            mockFetch.mockRejectedValueOnce(new Error('Network error'));

            await logout();

            expect(tokenStorage.get()).toBeNull();
            expect(tokenExpirationStorage.get()).toBeNull();
        });

        it('treats a 401 from the logout call as a successful sign-out', async () => {
            tokenStorage.set('access-token');
            tokenExpirationStorage.set(new Date());

            const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

            mockFetch.mockResolvedValueOnce({
                ok: false,
                status: 401,
                headers: new Headers(),
                text: async () => '',
            });

            await expect(logout()).resolves.toBeUndefined();

            expect(tokenStorage.get()).toBeNull();
            expect(tokenExpirationStorage.get()).toBeNull();
            expect(consoleErrorSpy).not.toHaveBeenCalled();

            consoleErrorSpy.mockRestore();
        });

        it('authenticates the logout call with the access token', async () => {
            tokenStorage.set('access-token');
            tokenExpirationStorage.set(new Date());

            mockFetch.mockResolvedValueOnce({
                ok: true,
                status: 204,
                headers: new Headers(),
            });

            await logout();

            const [, init] = mockFetch.mock.calls[0];
            const headers = new Headers(init.headers);
            expect(headers.get('Authorization')).toBe('Bearer access-token');
        });
    });

    describe('refreshAccessToken', () => {
        const mockTokenResponse = {
            accessToken: createMockJwt({
                sub: 'user-123',
                name: 'testuser',
                role: '2',
            }),
            accessTokenExpiresAt: new Date(Date.now() + 3600000).toISOString(),
        };

        it('refreshes tokens and returns user', async () => {
            mockFetch.mockResolvedValueOnce({
                ok: true,
                status: 200,
                json: async () => mockTokenResponse,
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            const user = await refreshAccessToken();

            expect(user).toEqual({
                id: 'user-123',
                username: 'testuser',
                role: Role.Manager,
            });

            expect(tokenStorage.get()).toBe(mockTokenResponse.accessToken);
            expect(tokenExpirationStorage.get()).toBeInstanceOf(Date);

            expect(mockFetch).toHaveBeenCalledWith(
                expect.stringContaining('/auth/refresh'),
                expect.objectContaining({
                    method: 'POST',
                    credentials: 'include',
                }),
            );
            const [, init] = mockFetch.mock.calls[0];
            expect(init.body).toBeUndefined();
        });

        it('clears storage on refresh failure', async () => {
            tokenStorage.set('old-access-token');
            tokenExpirationStorage.set(new Date());

            mockFetch.mockResolvedValueOnce({
                ok: false,
                status: 401,
                json: async () => ({ title: 'Invalid refresh token' }),
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            await expect(refreshAccessToken()).rejects.toMatchObject({
                status: 401,
            });

            expect(tokenStorage.get()).toBeNull();
            expect(tokenExpirationStorage.get()).toBeNull();
        });
    });

    describe('isTokenExpired', () => {
        it('returns true when no expiration is stored', () => {
            expect(isTokenExpired()).toBe(true);
        });

        it('returns true when token is expired', () => {
            const pastDate = new Date(Date.now() - 1000);
            tokenExpirationStorage.set(pastDate);
            expect(isTokenExpired()).toBe(true);
        });

        it('returns true when token expires within buffer', () => {
            const almostExpired = new Date(Date.now() + 30000);
            tokenExpirationStorage.set(almostExpired);
            expect(isTokenExpired(60)).toBe(true);
        });

        it('returns false when token is valid', () => {
            const futureDate = new Date(Date.now() + 3600000);
            tokenExpirationStorage.set(futureDate);
            expect(isTokenExpired()).toBe(false);
        });
    });

    describe('hasStoredSession', () => {
        it('returns false when no access token is stored', () => {
            expect(hasStoredSession()).toBe(false);
        });

        it('returns true when an access token is stored', () => {
            tokenStorage.set('access-token');
            expect(hasStoredSession()).toBe(true);
        });
    });

    describe('getCurrentUserFromToken', () => {
        it('returns null when no token stored', () => {
            expect(getCurrentUserFromToken()).toBeNull();
        });

        it('returns user from stored token', () => {
            const token = createMockJwt({
                sub: 'user-789',
                name: 'viewer',
                role: '0',
            });
            tokenStorage.set(token);

            const user = getCurrentUserFromToken();

            expect(user).toEqual({
                id: 'user-789',
                username: 'viewer',
                role: Role.Viewer,
            });
        });
    });

    describe('clearAuthStorage', () => {
        it('clears all auth-related storage', () => {
            tokenStorage.set('access-token');
            tokenExpirationStorage.set(new Date());

            clearAuthStorage();

            expect(tokenStorage.get()).toBeNull();
            expect(tokenExpirationStorage.get()).toBeNull();
        });
    });
});
