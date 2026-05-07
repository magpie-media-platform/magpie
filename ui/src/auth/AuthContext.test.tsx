import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';

import { AuthProvider } from './AuthContext';
import { useAuth } from './useAuth';
import { Role } from './types';
import { clearAuthStorage, tokenExpirationStorage, refreshTokenStorage } from '@/lib/authApi';
import * as authApiModule from '@/lib/authApi';
import { tokenStorage } from '@/lib/api';
import { ToastStateProvider, useToast } from '@/components/ui/Toast';

vi.mock('@/lib/authApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/authApi')>();
    return { ...actual, refreshAccessToken: vi.fn() };
});

const mockFetch = vi.fn();
global.fetch = mockFetch;

function fakeJwt(claims: Record<string, string | number>): string {
    // Default to a far-future `exp` so callers don't have to specify one for every test.
    // decodeJwtUser strictly requires a numeric `exp` claim.
    const claimsWithExp: Record<string, string | number> = {
        exp: Math.floor(Date.now() / 1000) + 3600,
        ...claims,
    };
    const header = btoa(JSON.stringify({ alg: 'none' }));
    const payload = btoa(JSON.stringify(claimsWithExp));
    return `${header}.${payload}.sig`;
}

function AuthConsumer() {
    const { isAuthenticated, isLoading, user, hasPermission, hasRole, signIn, signOut } = useAuth();

    return (
        <div>
            <span data-testid="loading">{isLoading.toString()}</span>
            <span data-testid="authenticated">{isAuthenticated.toString()}</span>
            <span data-testid="username">{user?.username ?? 'none'}</span>
            <span data-testid="role">{user?.role ?? 'none'}</span>
            <span data-testid="has-view">{hasPermission('content.view').toString()}</span>
            <span data-testid="has-edit">{hasPermission('content.edit').toString()}</span>
            <span data-testid="has-admin">{hasPermission('users.manage').toString()}</span>
            <span data-testid="is-manager">{hasRole(Role.Manager).toString()}</span>
            <span data-testid="is-admin">{hasRole(Role.Admin).toString()}</span>
            <button onClick={() => signIn({ id: '1', username: 'testuser', role: Role.Manager })}>Sign In</button>
            <button onClick={signOut}>Sign Out</button>
        </div>
    );
}

describe('AuthProvider', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        clearAuthStorage();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('provides default unauthenticated state', () => {
        render(
            <AuthProvider skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('loading')).toHaveTextContent('false');
        expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
        expect(screen.getByTestId('username')).toHaveTextContent('none');
    });

    it('provides initial user when specified', () => {
        const initialUser = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={initialUser} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
        expect(screen.getByTestId('username')).toHaveTextContent('admin');
        expect(screen.getByTestId('role')).toHaveTextContent('3');
    });

    it('provides initial loading state when specified', () => {
        render(
            <AuthProvider initialLoading={true} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('loading')).toHaveTextContent('true');
    });
});

describe('useAuth', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        clearAuthStorage();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('returns false for all permissions when not authenticated', () => {
        render(
            <AuthProvider skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('has-view')).toHaveTextContent('false');
        expect(screen.getByTestId('has-edit')).toHaveTextContent('false');
        expect(screen.getByTestId('has-admin')).toHaveTextContent('false');
    });

    it('returns false for all role checks when not authenticated', () => {
        render(
            <AuthProvider skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('is-manager')).toHaveTextContent('false');
        expect(screen.getByTestId('is-admin')).toHaveTextContent('false');
    });

    it('checks permissions correctly for Manager role', () => {
        const user = { id: '1', username: 'manager', role: Role.Manager };

        render(
            <AuthProvider initialUser={user} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('has-view')).toHaveTextContent('true');

        expect(screen.getByTestId('has-edit')).toHaveTextContent('true');

        expect(screen.getByTestId('has-admin')).toHaveTextContent('false');
    });

    it('checks role requirements correctly', () => {
        const user = { id: '1', username: 'manager', role: Role.Manager };

        render(
            <AuthProvider initialUser={user} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('is-manager')).toHaveTextContent('true');
        expect(screen.getByTestId('is-admin')).toHaveTextContent('false');
    });

    it('updates state on signIn', async () => {
        const user = userEvent.setup();

        render(
            <AuthProvider skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('authenticated')).toHaveTextContent('false');

        await user.click(screen.getByRole('button', { name: 'Sign In' }));

        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
        expect(screen.getByTestId('username')).toHaveTextContent('testuser');
    });

    it('clears state on signOut', async () => {
        const user = userEvent.setup();
        const initialUser = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={initialUser} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');

        await user.click(screen.getByRole('button', { name: 'Sign Out' }));

        await waitFor(() => {
            expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
        });
        expect(screen.getByTestId('username')).toHaveTextContent('none');
    });
});

describe('cross-tab token sync', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        clearAuthStorage();
    });

    afterEach(() => {
        vi.restoreAllMocks();
        clearAuthStorage();
    });

    it('picks up tokens written by another tab via storage event', async () => {
        render(
            <AuthProvider skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('authenticated')).toHaveTextContent('false');

        const jwt = fakeJwt({
            sub: '42',
            name: 'othertab',
            role: 2,
        });
        tokenStorage.set(jwt);
        tokenExpirationStorage.set(new Date(Date.now() + 300_000));

        await act(async () => {
            const event = new StorageEvent('storage', {
                key: 'magpie_token_expires',
                newValue: new Date(Date.now() + 300_000).toISOString(),
            });
            window.dispatchEvent(event);
        });

        await waitFor(() => {
            expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
            expect(screen.getByTestId('username')).toHaveTextContent('othertab');
        });
    });

    it('logs out when another tab clears tokens via storage event', async () => {
        const initialUser = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={initialUser} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');

        await act(async () => {
            const event = new StorageEvent('storage', {
                key: 'magpie_token_expires',
                newValue: null,
            });
            window.dispatchEvent(event);
        });

        await waitFor(() => {
            expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
        });
    });

    it('logs out when another tab updates the expiry but the access token decodes to null', async () => {
        // Reproduces the desync the listener now guards against: another tab wrote
        // a fresh expiry value but localStorage's access token is missing/garbage so
        // getCurrentUserFromToken() returns null. Local state must clear rather than
        // remain stuck on the old user.
        const initialUser = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={initialUser} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');

        // Put a non-decodable token in storage so getCurrentUserFromToken() -> null.
        tokenStorage.set('not-a-valid-jwt');
        tokenExpirationStorage.set(new Date(Date.now() + 300_000));

        await act(async () => {
            const event = new StorageEvent('storage', {
                key: 'magpie_token_expires',
                newValue: new Date(Date.now() + 300_000).toISOString(),
            });
            window.dispatchEvent(event);
        });

        await waitFor(() => {
            expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
        });
    });

    it('ignores storage events for unrelated keys', () => {
        const initialUser = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={initialUser} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        act(() => {
            window.dispatchEvent(
                new StorageEvent('storage', {
                    key: 'some_other_key',
                    newValue: 'whatever',
                }),
            );
        });

        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
        expect(screen.getByTestId('username')).toHaveTextContent('admin');
    });
});

describe('performRefresh', () => {
    function setupExpiredSession() {
        tokenStorage.set('fake-access-token');
        refreshTokenStorage.set('test-refresh-token');
        tokenExpirationStorage.set(new Date(Date.now() - 1000));
    }

    beforeEach(() => {
        vi.resetAllMocks();
        clearAuthStorage();
    });

    afterEach(() => {
        vi.restoreAllMocks();
        clearAuthStorage();
    });

    it('logs out when refresh fails', async () => {
        setupExpiredSession();
        vi.mocked(authApiModule.refreshAccessToken).mockRejectedValue(new Error('Network error'));

        const initialUser = { id: '1', username: 'admin', role: Role.Admin };
        render(
            <AuthProvider initialUser={initialUser}>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(vi.mocked(authApiModule.refreshAccessToken)).toHaveBeenCalled();

        await waitFor(() => {
            expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
        });
    });

    it('shows a toast notification when refresh fails', async () => {
        setupExpiredSession();
        vi.mocked(authApiModule.refreshAccessToken).mockRejectedValue(new Error('Network error'));

        function ToastConsumer() {
            const { toasts } = useToast();
            return (
                <ul>
                    {toasts.map((t) => (
                        <li key={t.id} data-testid="toast">
                            {t.title}
                        </li>
                    ))}
                </ul>
            );
        }

        const initialUser = { id: '1', username: 'admin', role: Role.Admin };
        render(
            <ToastStateProvider>
                <AuthProvider initialUser={initialUser}>
                    <AuthConsumer />
                    <ToastConsumer />
                </AuthProvider>
            </ToastStateProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('toast')).toHaveTextContent('Session expired');
        });
    });

    it('refreshes (not fast-path) when token is within the refresh buffer', async () => {
        // Token is not strictly expired (30s in the future), but it IS inside the
        // 60s refresh buffer used by scheduleRefresh. The fast-path early-return
        // must NOT short-circuit here -- the next request would 401 otherwise.
        // The JWT is fully decodable, so without the buffer check the fast-path
        // would happily return the decoded user and never call refresh; this is
        // what discriminates the buffered fast-path from the unbuffered one.
        const expSeconds = Math.floor(Date.now() / 1000) + 30;
        tokenStorage.set(
            fakeJwt({
                sub: '1',
                name: 'admin',
                role: 3,
                exp: expSeconds,
            }),
        );
        refreshTokenStorage.set('test-refresh-token');
        tokenExpirationStorage.set(new Date(expSeconds * 1000));

        vi.mocked(authApiModule.refreshAccessToken).mockResolvedValue({
            id: '1',
            username: 'admin',
            role: Role.Admin,
        });

        const initialUser = { id: '1', username: 'admin', role: Role.Admin };
        render(
            <AuthProvider initialUser={initialUser}>
                <AuthConsumer />
            </AuthProvider>,
        );

        await waitFor(() => {
            expect(vi.mocked(authApiModule.refreshAccessToken)).toHaveBeenCalled();
        });
    });

    it('takes the fast-path (no refresh) when token expiry is outside the refresh buffer', async () => {
        // Paired with the within-buffer test above: a token whose expiry is well
        // outside the 60s refresh buffer must take the fast-path and NOT trigger
        // a network refresh. This nails down the other side of the buffer logic.
        const expSeconds = Math.floor(Date.now() / 1000) + 600;
        tokenStorage.set(
            fakeJwt({
                sub: '1',
                name: 'admin',
                role: 3,
                exp: expSeconds,
            }),
        );
        refreshTokenStorage.set('test-refresh-token');
        tokenExpirationStorage.set(new Date(expSeconds * 1000));

        const initialUser = { id: '1', username: 'admin', role: Role.Admin };
        render(
            <AuthProvider initialUser={initialUser}>
                <AuthConsumer />
            </AuthProvider>,
        );

        // Wait for the provider's session-init effect to settle so any refresh it
        // would have triggered has had a chance to run.
        await waitFor(() => {
            expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
        });

        expect(vi.mocked(authApiModule.refreshAccessToken)).not.toHaveBeenCalled();
    });
});
