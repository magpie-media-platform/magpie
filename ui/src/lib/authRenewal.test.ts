import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiFetch, AUTH_FAILURE_EVENT, renewAccessToken } from './api';
import { clearAuthStorage, storeAuthTokens, tokenStorage } from './tokenStorage';

vi.mock('./authSessionLock', () => ({
    withAuthSessionLock: (_scope: string, action: (signal: AbortSignal) => Promise<unknown>) =>
        action(new AbortController().signal),
}));

const fetchMock = vi.fn<typeof fetch>();
const failure = vi.fn();

describe('session renewal', () => {
    beforeEach(() => {
        localStorage.clear();
        vi.stubGlobal('fetch', fetchMock);
        fetchMock.mockReset();
        failure.mockClear();
        window.addEventListener(AUTH_FAILURE_EVENT, failure);
        storeAuthTokens('old-token', '2020-01-01T00:00:00Z');
    });

    afterEach(() => {
        window.removeEventListener(AUTH_FAILURE_EVENT, failure);
        vi.unstubAllGlobals();
        localStorage.clear();
    });

    it('shares renewal between initialization, scheduled refresh, and API retry', async () => {
        let finish!: (response: Response) => void;
        const response = new Promise<Response>((resolve) => {
            finish = resolve;
        });
        fetchMock.mockImplementation(async (url) => {
            if (String(url).endsWith('/auth/refresh')) return response;
            return new Response('{}', {
                status: tokenStorage.get() === 'old-token' ? 401 : 200,
                headers: { 'Content-Type': 'application/json' },
            });
        });

        // Arrange
        const startup = renewAccessToken();
        const scheduled = renewAccessToken();
        const request = apiFetch('/content');
        await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));

        // Act
        finish(
            new Response(JSON.stringify({ accessToken: 'new-token', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }), {
                headers: { 'Content-Type': 'application/json' },
            }),
        );
        await Promise.all([startup, scheduled, request]);

        // Assert
        expect(fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh'))).toHaveLength(1);
        expect(tokenStorage.get()).toBe('new-token');
        expect(failure).not.toHaveBeenCalled();
    });

    it.each([
        [0, () => Promise.reject(new TypeError('Connection interrupted'))],
        [503, () => Promise.resolve(new Response(null, { status: 503 }))],
    ])('retains credentials after temporary status %i and permits recovery', async (status, respond) => {
        // Arrange
        fetchMock.mockImplementationOnce(respond);

        // Act
        await expect(renewAccessToken()).rejects.toMatchObject({ status });

        // Assert
        expect(tokenStorage.get()).toBe('old-token');
        expect(failure).not.toHaveBeenCalled();

        fetchMock.mockResolvedValueOnce(
            new Response(
                JSON.stringify({ accessToken: 'recovered-token', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }),
                { headers: { 'Content-Type': 'application/json' } },
            ),
        );
        await expect(renewAccessToken()).resolves.toBe('recovered-token');
        expect(tokenStorage.get()).toBe('recovered-token');
    });

    it('does not turn an API retry transport failure into a session-expired failure', async () => {
        // Arrange
        fetchMock.mockResolvedValueOnce(new Response(null, { status: 401 }));
        fetchMock.mockResolvedValueOnce(new Response(null, { status: 503 }));

        // Act
        await expect(apiFetch('/content')).rejects.toMatchObject({ status: 503 });

        // Assert
        expect(tokenStorage.get()).toBe('old-token');
        expect(failure).not.toHaveBeenCalled();
    });

    it.each([401, 503])('ignores an older renewal failure with status %i after a newer renewal', async (status) => {
        // Arrange
        let finish!: (response: Response) => void;
        fetchMock.mockReturnValueOnce(
            new Promise<Response>((resolve) => {
                finish = resolve;
            }),
        );
        const renewal = renewAccessToken();
        storeAuthTokens('newer-token', '2099-01-01T00:00:00Z');

        // Act
        finish(new Response(null, { status }));

        // Assert
        await expect(renewal).resolves.toBe('newer-token');
        expect(tokenStorage.get()).toBe('newer-token');
        expect(failure).not.toHaveBeenCalled();
    });

    it.each([
        [200, 409],
        [401, 401],
    ])(
        'does not apply an older renewal response with status %i to a different account',
        async (status, expectedStatus) => {
            // Arrange
            let finish!: (response: Response) => void;
            fetchMock.mockReturnValueOnce(
                new Promise<Response>((resolve) => {
                    finish = resolve;
                }),
            );
            const renewal = renewAccessToken();
            const rejected = expect(renewal).rejects.toMatchObject({ status: expectedStatus });
            clearAuthStorage();
            storeAuthTokens('other-account', '2099-01-01T00:00:00Z');

            // Act
            finish(
                new Response(
                    JSON.stringify({ accessToken: 'old-account', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }),
                    { status, headers: { 'Content-Type': 'application/json' } },
                ),
            );
            await rejected;

            // Assert
            expect(tokenStorage.get()).toBe('other-account');
            expect(failure).not.toHaveBeenCalled();
        },
    );

    it('does not restore a logged-out session from a delayed success', async () => {
        // Arrange
        let finish!: (response: Response) => void;
        fetchMock.mockReturnValueOnce(
            new Promise<Response>((resolve) => {
                finish = resolve;
            }),
        );
        const renewal = renewAccessToken();
        const rejected = expect(renewal).rejects.toMatchObject({ status: 409 });
        clearAuthStorage();

        // Act
        finish(
            new Response(JSON.stringify({ accessToken: 'late-token', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }), {
                headers: { 'Content-Type': 'application/json' },
            }),
        );
        await rejected;

        // Assert
        expect(tokenStorage.get()).toBeNull();
        await expect(renewAccessToken()).rejects.toMatchObject({ status: 401 });
        expect(fetchMock).toHaveBeenCalledOnce();
    });

    it('does not retry an old account API request as the new account', async () => {
        // Arrange
        let finish!: (response: Response) => void;
        fetchMock.mockReturnValueOnce(
            new Promise<Response>((resolve) => {
                finish = resolve;
            }),
        );
        const request = apiFetch('/content', { method: 'DELETE' });
        const rejected = expect(request).rejects.toMatchObject({ status: 401 });
        clearAuthStorage();
        storeAuthTokens('other-account', '2099-01-01T00:00:00Z');

        // Act
        finish(new Response(null, { status: 401 }));
        await rejected;

        // Assert
        expect(fetchMock).toHaveBeenCalledOnce();
        expect(tokenStorage.get()).toBe('other-account');
    });

    it('keeps independent API scopes isolated on renewal rejection', async () => {
        // Arrange
        storeAuthTokens('other-instance', '2099-01-01T00:00:00Z', '/other-api');
        fetchMock.mockResolvedValueOnce(new Response(null, { status: 401 }));

        // Act
        await expect(renewAccessToken('/other-api')).rejects.toMatchObject({ status: 401 });

        // Assert
        expect(tokenStorage.get('/other-api')).toBeNull();
        expect(tokenStorage.get()).toBe('old-token');
        expect(failure).not.toHaveBeenCalled();
    });

    it.each([401, 403])('clears a genuinely rejected session with status %i', async (status) => {
        // Arrange
        fetchMock.mockResolvedValueOnce(new Response(null, { status }));

        // Act
        await expect(renewAccessToken()).rejects.toMatchObject({ status });

        // Assert
        expect(tokenStorage.get()).toBeNull();
        expect(failure).toHaveBeenCalledOnce();
    });
});
