import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

import {
    apiFetch,
    apiGet,
    apiPost,
    apiPut,
    apiPatch,
    apiDelete,
    buildUrl,
    isApiError,
    AUTH_FAILURE_EVENT,
    ApiError,
    renewAccessToken,
} from './api';
import { clearAuthStorage, tokenExpirationStorage, tokenStorage } from './tokenStorage';

vi.mock('./authSessionLock', () => ({
    withAuthSessionLock: (_scope: string, action: (signal: AbortSignal) => Promise<unknown>) =>
        action(new AbortController().signal),
}));

describe('session failure on a 401', () => {
    afterEach(() => {
        vi.restoreAllMocks();
        clearAuthStorage();
    });
    it('uses the recovered cookie to renew an access token that expired during the interruption', async () => {
        tokenStorage.set('before-interruption');
        const fetch = vi
            .spyOn(globalThis, 'fetch')
            .mockResolvedValueOnce(
                new Response(
                    JSON.stringify({
                        accessToken: 'committed-but-expired',
                        accessTokenExpiresAt: '2000-01-01T00:00:00Z',
                    }),
                    { headers: { 'Content-Type': 'application/json' } },
                ),
            )
            .mockResolvedValueOnce(
                new Response(JSON.stringify({ accessToken: 'current', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }), {
                    headers: { 'Content-Type': 'application/json' },
                }),
            );
        await expect(renewAccessToken()).resolves.toBe('current');
        expect(new Headers(fetch.mock.calls[0][1]?.headers).get('Authorization')).toBe('Bearer before-interruption');
        expect(new Headers(fetch.mock.calls[1][1]?.headers).get('Authorization')).toBe('Bearer committed-but-expired');
        expect(tokenStorage.get()).toBe('current');
    });
    it('reports session failure when refresh fails', async () => {
        tokenStorage.set('expired-token');
        const fetch = vi
            .spyOn(globalThis, 'fetch')
            .mockResolvedValueOnce(new Response(null, { status: 401 }))
            .mockResolvedValueOnce(
                new Response(JSON.stringify({ status: 401, title: 'Invalid refresh token' }), {
                    status: 401,
                    headers: { 'Content-Type': 'application/problem+json' },
                }),
            );
        const dispatch = vi.spyOn(window, 'dispatchEvent');
        await expect(apiFetch('/upstream')).rejects.toMatchObject({ status: 401 });
        expect(fetch).toHaveBeenCalledTimes(2);
        expect(fetch.mock.calls[1][0]).toBe('/api/auth/refresh');
        expect(dispatch.mock.calls.map(([event]) => event.type)).toEqual([
            'magpie:auth-storage',
            'magpie:auth:failure',
        ]);
        expect(tokenStorage.get()).toBeNull();
    });
});

describe('tokenStorage', () => {
    beforeEach(() => {
        localStorage.clear();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('stores and retrieves token', () => {
        tokenStorage.set('test-token-123');
        expect(tokenStorage.get()).toBe('test-token-123');
    });

    it('returns null when no token exists', () => {
        expect(tokenStorage.get()).toBeNull();
    });

    it('removes token', () => {
        tokenStorage.set('test-token');
        tokenStorage.remove();
        expect(tokenStorage.get()).toBeNull();
    });

    it('exists returns true when token is present', () => {
        tokenStorage.set('test-token');
        expect(tokenStorage.exists()).toBe(true);
    });

    it('exists returns false when no token', () => {
        expect(tokenStorage.exists()).toBe(false);
    });

    it('handles localStorage errors gracefully on get', () => {
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
            throw new Error('localStorage unavailable');
        });

        expect(tokenStorage.get()).toBeNull();
    });

    it('handles localStorage errors gracefully on set', () => {
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
            throw new Error('localStorage unavailable');
        });

        expect(() => tokenStorage.set('test')).not.toThrow();
    });

    it('handles localStorage errors gracefully on remove', () => {
        vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
            throw new Error('localStorage unavailable');
        });

        expect(() => tokenStorage.remove()).not.toThrow();
    });
});

describe('isApiError', () => {
    it('returns true for ApiError instance', () => {
        const error = new ApiError(400, 'Bad request');
        expect(isApiError(error)).toBe(true);
    });

    it('returns true for ApiError instance with details', () => {
        const error = new ApiError(500, 'Error', { field: 'value' });
        expect(isApiError(error)).toBe(true);
    });

    it('ApiError is instanceof ApiError', () => {
        expect(new ApiError(400, 'Bad request') instanceof ApiError).toBe(true);
    });

    it('ApiError is instanceof Error', () => {
        expect(new ApiError(400, 'Bad request') instanceof Error).toBe(true);
    });

    it('ApiError has correct name', () => {
        expect(new ApiError(400, 'Bad request').name).toBe('ApiError');
    });

    it('returns true for duck-typed plain object with name ApiError', () => {
        const error = { name: 'ApiError', status: 400, message: 'Bad request' };
        expect(isApiError(error)).toBe(true);
    });

    it('returns true for duck-typed plain object with details', () => {
        const error = { name: 'ApiError', status: 500, message: 'Error', details: { field: 'value' } };
        expect(isApiError(error)).toBe(true);
    });

    it('returns false for object with status and message but no name', () => {
        const error = { status: 400, message: 'Bad request' };
        expect(isApiError(error)).toBe(false);
    });

    it('returns false for object with wrong name', () => {
        const error = { name: 'TypeError', status: 400, message: 'Bad request' };
        expect(isApiError(error)).toBe(false);
    });

    it('returns false for null', () => {
        expect(isApiError(null)).toBe(false);
    });

    it('returns false for undefined', () => {
        expect(isApiError(undefined)).toBe(false);
    });

    it('returns false for string', () => {
        expect(isApiError('error')).toBe(false);
    });

    it('returns false for object without status', () => {
        expect(isApiError({ message: 'error' })).toBe(false);
    });

    it('returns false for object without message', () => {
        expect(isApiError({ status: 400 })).toBe(false);
    });

    it('returns false for object with non-number status', () => {
        expect(isApiError({ status: '400', message: 'error' })).toBe(false);
    });

    it('returns false for object with non-string message', () => {
        expect(isApiError({ status: 400, message: 123 })).toBe(false);
    });
});

describe('apiFetch', () => {
    const mockFetch = vi.fn();

    beforeEach(() => {
        vi.stubGlobal('fetch', mockFetch);
        localStorage.clear();
        mockFetch.mockReset();
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('makes GET request to correct URL', async () => {
        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({ data: 'test' }),
        });

        const result = await apiFetch('/users');

        expect(mockFetch).toHaveBeenCalledWith('/api/users', expect.objectContaining({ headers: expect.any(Headers) }));
        expect(result).toEqual({ data: 'test' });
    });

    it('includes auth token in headers when present', async () => {
        tokenStorage.set('my-auth-token');

        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({}),
        });

        await apiFetch('/users');

        const [, init] = mockFetch.mock.calls[0];
        const headers = init.headers as Headers;
        expect(headers.get('Authorization')).toBe('Bearer my-auth-token');
    });

    it('does not include auth token when skipAuth is true', async () => {
        tokenStorage.set('my-auth-token');

        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({}),
        });

        await apiFetch('/auth/login/providers', { skipAuth: true });

        const [, init] = mockFetch.mock.calls[0];
        const headers = init.headers as Headers;
        expect(headers.get('Authorization')).toBeNull();
    });

    it('serializes body as JSON', async () => {
        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({}),
        });

        await apiFetch('/users', {
            method: 'POST',
            body: { name: 'John', email: 'john@example.com' },
        });

        const [, init] = mockFetch.mock.calls[0];
        expect(init.body).toBe('{"name":"John","email":"john@example.com"}');
        expect((init.headers as Headers).get('Content-Type')).toBe('application/json');
    });

    it('passes string body as-is', async () => {
        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({}),
        });

        await apiFetch('/data', {
            method: 'POST',
            body: 'raw string body',
        });

        const [, init] = mockFetch.mock.calls[0];
        expect(init.body).toBe('raw string body');
    });

    it('handles 204 No Content response', async () => {
        mockFetch.mockResolvedValue({
            ok: true,
            status: 204,
            headers: new Headers(),
        });

        const result = await apiFetch('/users/1', { method: 'DELETE' });

        expect(result).toBeUndefined();
    });

    it('returns undefined for an empty-body 202 Accepted response', async () => {
        mockFetch.mockResolvedValue({
            ok: true,
            status: 202,
            headers: new Headers(),
            text: () => Promise.resolve(''),
        });

        const result = await apiFetch('/notification-rules/x/test', { method: 'POST' });

        expect(result).toBeUndefined();
    });

    it('throws ApiError for non-JSON success response', async () => {
        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'text/plain' }),
            text: () => Promise.resolve('plain text response'),
        });

        await expect(apiFetch('/health')).rejects.toMatchObject({
            status: 200,
            message: 'Expected JSON response but received text/plain',
            details: 'plain text response',
        });
    });

    it('throws ApiError on 400 response', async () => {
        mockFetch.mockResolvedValue({
            ok: false,
            status: 400,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({ title: 'Validation failed', errors: { name: ['Required'] } }),
        });

        await expect(apiFetch('/users', { method: 'POST', body: {} })).rejects.toMatchObject({
            status: 400,
            message: 'Validation failed',
            details: { title: 'Validation failed', errors: { name: ['Required'] } },
        });
    });

    it('throws ApiError on 500 response', async () => {
        mockFetch.mockResolvedValue({
            ok: false,
            status: 500,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({ detail: 'Internal server error' }),
        });

        await expect(apiFetch('/users')).rejects.toMatchObject({
            status: 500,
            message: 'Internal server error',
            details: { detail: 'Internal server error' },
        });
    });

    it('parses application/problem+json body into structured details (RFC 7807)', async () => {
        mockFetch.mockResolvedValue({
            ok: false,
            status: 502,
            headers: new Headers({ 'content-type': 'application/problem+json' }),
            json: () =>
                Promise.resolve({
                    type: 'https://tools.ietf.org/html/rfc9110#section-15.6.3',
                    title: 'Notification test delivery failed',
                    status: 502,
                    detail: 'Webhook delivery failed due to a transport error.',
                }),
        });

        await expect(apiFetch('/notification-rules/x/test', { method: 'POST' })).rejects.toMatchObject({
            status: 502,
            message: 'Webhook delivery failed due to a transport error.',
            details: { detail: 'Webhook delivery failed due to a transport error.' },
        });
    });

    it('prefers the ProblemDetails detail over the generic status title', async () => {
        mockFetch.mockResolvedValue({
            ok: false,
            status: 400,
            headers: new Headers({ 'content-type': 'application/problem+json' }),
            json: () =>
                Promise.resolve({
                    title: 'Bad Request',
                    status: 400,
                    detail: 'The root folder path is not writable.',
                }),
        });

        await expect(apiFetch('/library/roots', { method: 'POST', body: {} })).rejects.toMatchObject({
            status: 400,
            message: 'The root folder path is not writable.',
        });
    });

    it('falls back to the generic message when the ProblemDetails detail is empty', async () => {
        mockFetch.mockResolvedValue({
            ok: false,
            status: 500,
            headers: new Headers({ 'content-type': 'application/problem+json' }),
            json: () => Promise.resolve({ status: 500, detail: '' }),
        });

        await expect(apiFetch('/library/roots')).rejects.toMatchObject({
            status: 500,
            message: 'Request failed with status 500',
        });
    });

    it('preserves the reason and clusterMode from the install-file ProblemDetails body', async () => {
        mockFetch.mockResolvedValue({
            ok: false,
            status: 400,
            headers: new Headers({ 'content-type': 'application/problem+json' }),
            json: () =>
                Promise.resolve({
                    type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
                    title: 'Bad Request',
                    status: 400,
                    detail:
                        'Direct local extension package uploads are disabled in MultiNodeLocalExtensions mode. ' +
                        'Install extensions from a configured NuGet package source instead.',
                    clusterMode: 'MultiNodeLocalExtensions',
                }),
        });

        await expect(apiFetch('/extensions/install-file', { method: 'POST' })).rejects.toMatchObject({
            status: 400,
            details: {
                detail:
                    'Direct local extension package uploads are disabled in MultiNodeLocalExtensions mode. ' +
                    'Install extensions from a configured NuGet package source instead.',
                clusterMode: 'MultiNodeLocalExtensions',
            },
        });
    });

    it('handles 401 response by attempting refresh, then dispatches auth failure when refresh also fails', async () => {
        tokenStorage.set('expired-token');

        mockFetch
            .mockResolvedValueOnce({
                ok: false,
                status: 401,
                headers: new Headers({ 'content-type': 'application/json' }),
                json: () => Promise.resolve({ message: 'Token expired' }),
            })
            .mockResolvedValueOnce({
                ok: false,
                status: 401,
                headers: new Headers({ 'content-type': 'application/json' }),
                json: () => Promise.resolve({ title: 'Refresh rejected' }),
            });

        const eventHandler = vi.fn();
        window.addEventListener(AUTH_FAILURE_EVENT, eventHandler);

        await expect(apiFetch('/users')).rejects.toMatchObject({ status: 401 });

        expect(eventHandler).toHaveBeenCalled();
        expect(tokenStorage.exists()).toBe(false);
        expect(tokenExpirationStorage.get()).toBeNull();

        window.removeEventListener(AUTH_FAILURE_EVENT, eventHandler);
    });

    it('throws ApiError on network error', async () => {
        mockFetch.mockRejectedValue(new Error('Network request failed'));

        await expect(apiFetch('/users')).rejects.toMatchObject({
            status: 0,
            message: 'Network request failed',
            details: expect.any(Error),
        });
    });

    it('uses custom baseUrl when provided', async () => {
        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({}),
        });

        await apiFetch('/data', { baseUrl: 'https://custom.api.com' });

        expect(mockFetch).toHaveBeenCalledWith('https://custom.api.com/data', expect.any(Object));
    });

    it('uses absolute URL directly when path starts with http', async () => {
        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({}),
        });

        await apiFetch('https://external.api.com/data');

        expect(mockFetch).toHaveBeenCalledWith('https://external.api.com/data', expect.any(Object));
    });

    it('sends no token and no cookies to an absolute URL off the api origin', async () => {
        tokenStorage.set('my-auth-token');
        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({}),
        });

        await apiFetch('https://external.api.com/data');

        const init = mockFetch.mock.calls[0][1] as RequestInit;
        expect((init.headers as Headers).has('Authorization')).toBe(false);
        expect(init.credentials).toBe('omit');
    });

    it('handles error response with plain text', async () => {
        mockFetch.mockResolvedValue({
            ok: false,
            status: 500,
            headers: new Headers({ 'content-type': 'text/plain' }),
            text: () => Promise.resolve('Server error occurred'),
        });

        await expect(apiFetch('/users')).rejects.toMatchObject({
            status: 500,
            message: 'Server error occurred',
            details: 'Server error occurred',
        });
    });

    it('handles error response with empty body', async () => {
        mockFetch.mockResolvedValue({
            ok: false,
            status: 404,
            headers: new Headers(),
            text: () => Promise.resolve(''),
        });

        await expect(apiFetch('/users/999')).rejects.toMatchObject({
            status: 404,
            message: 'Request failed with status 404',
            details: null,
        });
    });
});

describe('apiFetch 401 refresh behavior', () => {
    const mockFetch = vi.fn();

    function makeJwt(claims: Record<string, unknown>): string {
        const claimsWithExp: Record<string, unknown> = {
            exp: Math.floor(Date.now() / 1000) + 3600,
            ...claims,
        };
        const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
        const payload = btoa(JSON.stringify(claimsWithExp));
        return `${header}.${payload}.sig`;
    }

    function jsonResponse(status: number, body: unknown, ok = status < 400): Response {
        return {
            ok,
            status,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve(body),
        } as unknown as Response;
    }

    function emptyResponse(status: number): Response {
        return {
            ok: status < 400,
            status,
            headers: new Headers(),
        } as unknown as Response;
    }

    beforeEach(() => {
        vi.stubGlobal('fetch', mockFetch);
        clearAuthStorage();
        mockFetch.mockReset();
    });

    afterEach(() => {
        vi.unstubAllGlobals();
        clearAuthStorage();
    });

    it('refreshes and retries the original request on 401', async () => {
        const newAccessToken = makeJwt({ sub: 'user-1', name: 'alice', role: '2' });

        tokenStorage.set('stale-access-token');
        tokenExpirationStorage.set(new Date(Date.now() - 1000));

        mockFetch
            .mockResolvedValueOnce(jsonResponse(401, { title: 'Unauthorized' }, false))
            .mockResolvedValueOnce(
                jsonResponse(200, {
                    accessToken: newAccessToken,
                    accessTokenExpiresAt: new Date(Date.now() + 3600_000).toISOString(),
                }),
            )
            .mockResolvedValueOnce(jsonResponse(200, { ok: true }));

        const result = await apiFetch<{ ok: boolean }>('/users/me');

        expect(result).toEqual({ ok: true });
        expect(mockFetch).toHaveBeenCalledTimes(3);

        const firstHeaders = mockFetch.mock.calls[0][1].headers as Headers;
        expect(firstHeaders.get('Authorization')).toBe('Bearer stale-access-token');

        expect(mockFetch.mock.calls[1][0]).toContain('/auth/refresh');
        expect(mockFetch.mock.calls[1][1].credentials).toBe('include');
        expect(mockFetch.mock.calls[1][1].body).toBeUndefined();

        const retryHeaders = mockFetch.mock.calls[2][1].headers as Headers;
        expect(retryHeaders.get('Authorization')).toBe(`Bearer ${newAccessToken}`);

        expect(tokenStorage.get()).toBe(newAccessToken);
    });

    it('clears all storage and dispatches auth failure when refresh itself returns 401', async () => {
        tokenStorage.set('stale-access-token');
        tokenExpirationStorage.set(new Date(Date.now() + 60_000));

        mockFetch
            .mockResolvedValueOnce(jsonResponse(401, { title: 'Unauthorized' }, false))
            .mockResolvedValueOnce(jsonResponse(401, { title: 'Refresh rejected' }, false));

        const eventHandler = vi.fn();
        window.addEventListener(AUTH_FAILURE_EVENT, eventHandler);

        await expect(apiFetch('/users/me')).rejects.toMatchObject({ status: 401 });

        expect(mockFetch).toHaveBeenCalledTimes(2);
        expect(eventHandler).toHaveBeenCalledTimes(1);
        expect(tokenStorage.get()).toBeNull();
        expect(tokenExpirationStorage.get()).toBeNull();

        window.removeEventListener(AUTH_FAILURE_EVENT, eventHandler);
    });

    it('shares a single refresh promise across concurrent 401s and retries each request once', async () => {
        const newAccessToken = makeJwt({ sub: 'user-1', name: 'alice', role: '2' });

        tokenStorage.set('stale-access-token');
        tokenExpirationStorage.set(new Date(Date.now() - 1000));

        const refreshBody = {
            accessToken: newAccessToken,
            accessTokenExpiresAt: new Date(Date.now() + 3600_000).toISOString(),
        };
        let refreshCalls = 0;
        const retriedTokens: (string | null)[] = [];

        mockFetch.mockImplementation(async (url: string, init: RequestInit) => {
            const headers = init.headers as Headers;
            const auth = headers.get('Authorization');

            if (url.includes('/auth/refresh')) {
                refreshCalls += 1;
                return jsonResponse(200, refreshBody);
            }

            if (auth === 'Bearer stale-access-token') {
                return jsonResponse(401, { title: 'Unauthorized' }, false);
            }

            retriedTokens.push(auth);
            return jsonResponse(200, { url });
        });

        const [r1, r2, r3] = await Promise.all([
            apiFetch<{ url: string }>('/a'),
            apiFetch<{ url: string }>('/b'),
            apiFetch<{ url: string }>('/c'),
        ]);

        expect(r1.url).toContain('/a');
        expect(r2.url).toContain('/b');
        expect(r3.url).toContain('/c');

        expect(refreshCalls).toBe(1);
        expect(retriedTokens).toHaveLength(3);
        for (const t of retriedTokens) {
            expect(t).toBe(`Bearer ${newAccessToken}`);
        }
    });

    it('does not recurse when 401 comes from /auth/refresh itself', async () => {
        tokenExpirationStorage.set(new Date(Date.now() + 60_000));

        mockFetch.mockResolvedValueOnce(jsonResponse(401, { title: 'Refresh rejected' }, false));

        const eventHandler = vi.fn();
        window.addEventListener(AUTH_FAILURE_EVENT, eventHandler);

        await expect(
            apiFetch('/auth/refresh', {
                method: 'POST',
                skipAuth: true,
            }),
        ).rejects.toMatchObject({ status: 401 });

        expect(mockFetch).toHaveBeenCalledTimes(1);
        expect(eventHandler).not.toHaveBeenCalled();
        expect(tokenExpirationStorage.get()).not.toBeNull();

        window.removeEventListener(AUTH_FAILURE_EVENT, eventHandler);
    });

    it('does not recurse when 401 comes from /auth/refresh/ with trailing slash', async () => {
        tokenExpirationStorage.set(new Date(Date.now() + 60_000));

        mockFetch.mockResolvedValueOnce(jsonResponse(401, { title: 'Refresh rejected' }, false));

        const eventHandler = vi.fn();
        window.addEventListener(AUTH_FAILURE_EVENT, eventHandler);

        await expect(
            apiFetch('/auth/refresh/', {
                method: 'POST',
                skipAuth: true,
            }),
        ).rejects.toMatchObject({ status: 401 });

        expect(mockFetch).toHaveBeenCalledTimes(1);
        expect(eventHandler).not.toHaveBeenCalled();
        expect(tokenExpirationStorage.get()).not.toBeNull();

        window.removeEventListener(AUTH_FAILURE_EVENT, eventHandler);
    });

    it('handles 204 No Content on retry', async () => {
        const newAccessToken = makeJwt({ sub: 'user-1', name: 'alice', role: '2' });

        tokenStorage.set('stale-access-token');
        tokenExpirationStorage.set(new Date(Date.now() - 1000));

        mockFetch
            .mockResolvedValueOnce(jsonResponse(401, { title: 'Unauthorized' }, false))
            .mockResolvedValueOnce(
                jsonResponse(200, {
                    accessToken: newAccessToken,
                    accessTokenExpiresAt: new Date(Date.now() + 3600_000).toISOString(),
                }),
            )
            .mockResolvedValueOnce(emptyResponse(204));

        const result = await apiFetch('/items/1', { method: 'DELETE' });
        expect(result).toBeUndefined();
        expect(mockFetch).toHaveBeenCalledTimes(3);
    });
});

describe('HTTP method helpers', () => {
    const mockFetch = vi.fn();

    beforeEach(() => {
        vi.stubGlobal('fetch', mockFetch);
        localStorage.clear();
        mockFetch.mockReset();
        mockFetch.mockResolvedValue({
            ok: true,
            status: 200,
            headers: new Headers({ 'content-type': 'application/json' }),
            json: () => Promise.resolve({ success: true }),
        });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('apiGet makes GET request', async () => {
        await apiGet('/users');

        const [, init] = mockFetch.mock.calls[0];
        expect(init.method).toBe('GET');
    });

    it('apiPost makes POST request with body', async () => {
        await apiPost('/users', { name: 'John' });

        const [, init] = mockFetch.mock.calls[0];
        expect(init.method).toBe('POST');
        expect(init.body).toBe('{"name":"John"}');
    });

    it('apiPut makes PUT request with body', async () => {
        await apiPut('/users/1', { name: 'Jane' });

        const [, init] = mockFetch.mock.calls[0];
        expect(init.method).toBe('PUT');
        expect(init.body).toBe('{"name":"Jane"}');
    });

    it('apiPatch makes PATCH request with body', async () => {
        await apiPatch('/users/1', { name: 'Updated' });

        const [, init] = mockFetch.mock.calls[0];
        expect(init.method).toBe('PATCH');
        expect(init.body).toBe('{"name":"Updated"}');
    });

    it('apiDelete makes DELETE request', async () => {
        mockFetch.mockResolvedValue({
            ok: true,
            status: 204,
            headers: new Headers(),
        });

        await apiDelete('/users/1');

        const [, init] = mockFetch.mock.calls[0];
        expect(init.method).toBe('DELETE');
    });
});

describe('buildUrl', () => {
    it('returns path unchanged when no params', () => {
        expect(buildUrl('/users')).toBe('/users');
    });

    it('returns path unchanged when params is undefined', () => {
        expect(buildUrl('/users', undefined)).toBe('/users');
    });

    it('appends query parameters', () => {
        const result = buildUrl('/users', { page: 1, limit: 10 });
        expect(result).toBe('/users?page=1&limit=10');
    });

    it('appends query parameters to a path that already has a query', () => {
        const result = buildUrl('/system/files?scope=content-1', { path: '/media/library' });
        expect(result).toBe('/system/files?scope=content-1&path=%2Fmedia%2Flibrary');
    });

    it('filters out undefined values', () => {
        const result = buildUrl('/users', { page: 1, status: undefined });
        expect(result).toBe('/users?page=1');
    });

    it('filters out null values', () => {
        const result = buildUrl('/users', { page: 1, status: null });
        expect(result).toBe('/users?page=1');
    });

    it('handles boolean values', () => {
        const result = buildUrl('/users', { active: true, deleted: false });
        expect(result).toBe('/users?active=true&deleted=false');
    });

    it('handles string values', () => {
        const result = buildUrl('/users', { name: 'John Doe' });
        expect(result).toBe('/users?name=John+Doe');
    });

    it('returns path when all params are filtered out', () => {
        const result = buildUrl('/users', { status: undefined, role: null });
        expect(result).toBe('/users');
    });

    it('handles mixed value types', () => {
        const result = buildUrl('/search', {
            q: 'test',
            page: 1,
            active: true,
            deleted: null,
        });
        expect(result).toBe('/search?q=test&page=1&active=true');
    });

    it('appends one entry per array element as a repeated key', () => {
        const result = buildUrl('/jobs', { status: ['Running', 'CancellationRequested'] });
        expect(result).toBe('/jobs?status=Running&status=CancellationRequested');
    });

    it('skips empty arrays', () => {
        const result = buildUrl('/jobs', { page: 1, status: [] });
        expect(result).toBe('/jobs?page=1');
    });

    it('skips null and undefined elements within arrays', () => {
        const result = buildUrl('/jobs', { status: ['Running', null, undefined, 'Failed'] });
        expect(result).toBe('/jobs?status=Running&status=Failed');
    });
});
