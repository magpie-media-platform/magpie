import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { AuthTestProvider as AuthProvider } from '@/test/AuthTestProvider';
import { AuthProvider as ProductionAuthProvider } from './AuthContext';
import { useAuth } from './useAuth';
import { Role, type EffectivePermissions } from './types';
import { clearAuthStorage, tokenExpirationStorage, tokenStorage } from '@/lib/tokenStorage';
import * as authApiModule from '@/lib/authApi';
import { ApiError } from '@/lib/api';
import { ToastStateProvider, useToast } from '@/components/ui/Toast';

vi.mock('@/lib/authApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/authApi')>();
    return {
        ...actual,
        login: vi.fn(),
        logout: vi.fn(),
        refreshAccessToken: vi.fn(),
        fetchEffectivePermissions: vi.fn(),
    };
});

const mockFetch = vi.fn();
global.fetch = mockFetch;

function fakeJwt(claims: Record<string, string | number>): string {
    const claimsWithExp: Record<string, string | number> = {
        exp: Math.floor(Date.now() / 1000) + 3600,
        ...claims,
    };
    const header = btoa(JSON.stringify({ alg: 'none' }));
    const payload = btoa(JSON.stringify(claimsWithExp));
    return `${header}.${payload}.sig`;
}

function AuthConsumer() {
    const { isAuthenticated, isLoading, user, hasPermission, hasRole, signIn, signOut, login } = useAuth();

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
            <button onClick={() => void login('viewer', 'password')}>Log In</button>
        </div>
    );
}

function setupStoredAdminSession() {
    const expSeconds = Math.floor(Date.now() / 1000) + 600;
    const token = fakeJwt({
        sub: '1',
        name: 'admin',
        role: 3,
        exp: expSeconds,
    });
    tokenStorage.set(token);
    tokenExpirationStorage.set(new Date(expSeconds * 1000));
    return token;
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

    it('clears a malformed stored token even when its separate expiry is in the future', async () => {
        tokenStorage.set('not-a-valid-jwt');
        tokenExpirationStorage.set(new Date(Date.now() + 300_000));

        render(
            <AuthProvider>
                <AuthConsumer />
            </AuthProvider>,
        );

        await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('false'));

        expect(tokenStorage.get()).toBeNull();
        expect(tokenExpirationStorage.get()).toBeNull();
        expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
    });

    it('restores stream authentication for a retained bearer session without a stream cookie', async () => {
        setupStoredAdminSession();
        let resolvePermissions: ((value: EffectivePermissions) => void) | undefined;
        vi.mocked(authApiModule.fetchEffectivePermissions).mockImplementation(
            () =>
                new Promise((resolve) => {
                    resolvePermissions = resolve;
                }),
        );

        render(
            <AuthProvider>
                <AuthConsumer />
            </AuthProvider>,
        );

        await waitFor(() => expect(vi.mocked(authApiModule.fetchEffectivePermissions)).toHaveBeenCalledOnce());

        expect(screen.getByTestId('authenticated')).toHaveTextContent('false');

        resolvePermissions?.({
            role: Role.Admin,
            permissions: {},
        });

        await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

        expect(vi.mocked(authApiModule.refreshAccessToken)).not.toHaveBeenCalled();
    });

    it('fetches permissions exactly once when restoring a retained session', async () => {
        setupStoredAdminSession();
        vi.mocked(authApiModule.fetchEffectivePermissions).mockResolvedValue({
            role: Role.Admin,
            permissions: {},
        });

        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        render(
            <QueryClientProvider client={queryClient}>
                <ProductionAuthProvider>
                    <AuthConsumer />
                </ProductionAuthProvider>
            </QueryClientProvider>,
        );

        await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));
        expect(vi.mocked(authApiModule.fetchEffectivePermissions)).toHaveBeenCalledOnce();
    });

    it('does not restore a bearer session after another tab signs out during permission restoration', async () => {
        setupStoredAdminSession();
        let resolvePermissions: ((value: EffectivePermissions) => void) | undefined;
        vi.mocked(authApiModule.fetchEffectivePermissions).mockImplementation(
            () =>
                new Promise((resolve) => {
                    resolvePermissions = resolve;
                }),
        );

        render(
            <AuthProvider>
                <AuthConsumer />
            </AuthProvider>,
        );

        await waitFor(() => expect(vi.mocked(authApiModule.fetchEffectivePermissions)).toHaveBeenCalledOnce());

        clearAuthStorage();
        await act(async () => {
            window.dispatchEvent(
                new StorageEvent('storage', {
                    key: tokenExpirationStorage.storageKey,
                    newValue: null,
                }),
            );
        });

        resolvePermissions?.({
            role: Role.Admin,
            permissions: {},
        });

        await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('false'));

        expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
        expect(tokenStorage.get()).toBeNull();
        expect(tokenExpirationStorage.get()).toBeNull();
    });

    it.each([401, 403])('clears a bearer session when permissions returns %i', async (status) => {
        setupStoredAdminSession();
        vi.mocked(authApiModule.fetchEffectivePermissions).mockRejectedValue(
            new ApiError(status, 'Session refresh rejected'),
        );

        render(
            <AuthProvider>
                <AuthConsumer />
            </AuthProvider>,
        );

        await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('false'));

        expect(vi.mocked(authApiModule.fetchEffectivePermissions)).toHaveBeenCalledOnce();
        expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
        expect(tokenStorage.get()).toBeNull();
        expect(tokenExpirationStorage.get()).toBeNull();
    });

    it.each([0, 503])('preserves a bearer session when permissions returns transient status %i', async (status) => {
        const token = setupStoredAdminSession();
        vi.mocked(authApiModule.fetchEffectivePermissions).mockRejectedValue(
            new ApiError(status, 'Permissions temporarily unavailable'),
        );

        render(
            <AuthProvider>
                <AuthConsumer />
            </AuthProvider>,
        );

        await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('false'));

        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
        expect(tokenStorage.get()).toBe(token);
        expect(tokenExpirationStorage.get()).not.toBeNull();
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

    it('clears admin permissions and data across logout and viewer login', async () => {
        const user = userEvent.setup();
        const queryClient = new QueryClient({
            defaultOptions: { queries: { retry: false, staleTime: Infinity } },
        });
        const admin = { id: 'admin-id', username: 'admin', role: Role.Admin };
        const adminPermissions: EffectivePermissions = { role: Role.Admin, permissions: {} };
        const viewerPermissions: EffectivePermissions = { role: Role.Viewer, permissions: {} };
        queryClient.setQueryData(['auth', 'permissions', admin.id], adminPermissions);
        queryClient.setQueryData(['users', 'list'], { items: [{ id: admin.id }] });
        vi.mocked(authApiModule.login).mockResolvedValue({
            id: 'viewer-id',
            username: 'viewer',
            role: Role.Viewer,
        });
        vi.mocked(authApiModule.logout).mockResolvedValue();
        vi.mocked(authApiModule.fetchEffectivePermissions).mockResolvedValue(viewerPermissions);

        render(
            <QueryClientProvider client={queryClient}>
                <ProductionAuthProvider initialUser={admin} skipSessionInit>
                    <AuthConsumer />
                </ProductionAuthProvider>
            </QueryClientProvider>,
        );

        expect(screen.getByTestId('is-admin')).toHaveTextContent('true');
        await user.click(screen.getByRole('button', { name: 'Sign Out' }));

        await waitFor(() => {
            expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
        });
        expect(queryClient.getQueryData(['auth', 'permissions', admin.id])).toBeUndefined();
        expect(queryClient.getQueryData(['users', 'list'])).toBeUndefined();

        queryClient.setQueryData(['auth', 'permissions'], adminPermissions);
        queryClient.setQueryData(['users', 'list'], { items: [{ id: admin.id }] });
        await user.click(screen.getByRole('button', { name: 'Log In' }));

        await waitFor(() => {
            expect(screen.getByTestId('username')).toHaveTextContent('viewer');
            expect(screen.getByTestId('role')).toHaveTextContent(String(Role.Viewer));
            expect(screen.getByTestId('is-admin')).toHaveTextContent('false');
        });
        expect(queryClient.getQueryData(['auth', 'permissions'])).toBeUndefined();
        expect(queryClient.getQueryData(['users', 'list'])).toBeUndefined();
        expect(vi.mocked(authApiModule.fetchEffectivePermissions)).toHaveBeenCalled();
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
                key: tokenExpirationStorage.storageKey,
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
                key: tokenExpirationStorage.storageKey,
                newValue: null,
            });
            window.dispatchEvent(event);
        });

        await waitFor(() => {
            expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
        });
    });

    it('logs out when another tab updates the expiry but the access token decodes to null', async () => {
        const initialUser = { id: '1', username: 'admin', role: Role.Admin };

        render(
            <AuthProvider initialUser={initialUser} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');

        tokenStorage.set('not-a-valid-jwt');
        tokenExpirationStorage.set(new Date(Date.now() + 300_000));

        await act(async () => {
            const event = new StorageEvent('storage', {
                key: tokenExpirationStorage.storageKey,
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
        const expSeconds = Math.floor(Date.now() / 1000) + 30;
        tokenStorage.set(
            fakeJwt({
                sub: '1',
                name: 'admin',
                role: 3,
                exp: expSeconds,
            }),
        );
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
        const expSeconds = Math.floor(Date.now() / 1000) + 600;
        tokenStorage.set(
            fakeJwt({
                sub: '1',
                name: 'admin',
                role: 3,
                exp: expSeconds,
            }),
        );
        tokenExpirationStorage.set(new Date(expSeconds * 1000));

        const initialUser = { id: '1', username: 'admin', role: Role.Admin };
        render(
            <AuthProvider initialUser={initialUser}>
                <AuthConsumer />
            </AuthProvider>,
        );

        await waitFor(() => {
            expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
        });

        expect(vi.mocked(authApiModule.refreshAccessToken)).not.toHaveBeenCalled();
    });
});
