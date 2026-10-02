import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';

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
    const { isAuthenticated, isLoading, user, hasPermission, hasRole, signOut, login } = useAuth();

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

    it.each([
        { expired: false, nextId: '1', newSession: false },
        { expired: true, nextId: '1', newSession: false },
        { expired: false, nextId: '2', newSession: false },
        { expired: true, nextId: '2', newSession: false },
        { expired: false, nextId: '1', newSession: true },
        { expired: true, nextId: '1', newSession: true },
    ])('keeps startup usable across a storage identity transition %j', async ({ expired, nextId, newSession }) => {
        setupStoredAdminSession();
        if (expired) {
            tokenStorage.set(fakeJwt({ sub: '1', name: 'admin', role: 3, exp: Math.floor(Date.now() / 1000) - 60 }));
            tokenExpirationStorage.set(new Date('2000-01-01T00:00:00Z'));
        }
        vi.mocked(authApiModule.refreshAccessToken).mockReturnValue(new Promise(() => {}));
        let finishSetup!: (value: string) => void;
        const setup = new Promise<string>((resolve) => {
            finishSetup = resolve;
        });
        vi.mocked(authApiModule.fetchEffectivePermissions).mockReturnValue(new Promise(() => {}));
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        queryClient.setQueryData(['private-account-data'], 'old account');
        function StartupObserver() {
            const query = useQuery({
                queryKey: ['setup', 'status'],
                queryFn: () => setup,
                retry: false,
                refetchOnMount: false,
                refetchOnReconnect: false,
                refetchOnWindowFocus: false,
            });
            return (
                <span data-testid="setup-observer">
                    {query.status}:{query.data}
                </span>
            );
        }
        render(
            <QueryClientProvider client={queryClient}>
                <ProductionAuthProvider>
                    <AuthConsumer />
                    <StartupObserver />
                </ProductionAuthProvider>
            </QueryClientProvider>,
        );
        await waitFor(() => expect(queryClient.getQueryState(['setup', 'status'])?.fetchStatus).toBe('fetching'));
        if (newSession) clearAuthStorage();
        tokenStorage.set(fakeJwt({ sub: nextId, name: 'renewed', role: 3 }));
        tokenExpirationStorage.set(new Date(Date.now() + 600_000));
        await act(() =>
            window.dispatchEvent(
                new StorageEvent('storage', {
                    key: tokenExpirationStorage.storageKey,
                    newValue: new Date(Date.now() + 600_000).toISOString(),
                }),
            ),
        );
        await act(async () => {
            finishSetup('ready');
            await setup;
        });
        if (newSession || nextId !== '1') {
            expect(queryClient.getQueryData(['private-account-data'])).toBeUndefined();
        }
        await waitFor(() => expect(screen.getByTestId('setup-observer')).toHaveTextContent('success:ready'));
        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
    });

    it.each([
        { expired: false, nextId: '2', newSession: false },
        { expired: true, nextId: '2', newSession: false },
        { expired: false, nextId: '1', newSession: true },
        { expired: true, nextId: '1', newSession: true },
    ])('clears cached account data on a real identity transition %j', async ({ expired, nextId, newSession }) => {
        // Arrange
        tokenStorage.set(
            fakeJwt({ sub: '1', name: 'admin', role: 3, exp: expired ? 1 : Math.floor(Date.now() / 1000) + 600 }),
        );
        tokenExpirationStorage.set(new Date(expired ? 1000 : Date.now() + 600_000));
        vi.mocked(authApiModule.refreshAccessToken).mockReturnValue(new Promise(() => {}));
        vi.mocked(authApiModule.fetchEffectivePermissions).mockReturnValue(new Promise(() => {}));
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        queryClient.setQueryData(['private-account-data'], 'old account');
        render(
            <QueryClientProvider client={queryClient}>
                <ProductionAuthProvider>
                    <AuthConsumer />
                </ProductionAuthProvider>
            </QueryClientProvider>,
        );

        // Act
        if (newSession) clearAuthStorage();
        tokenStorage.set(fakeJwt({ sub: nextId, name: 'renewed', role: 3 }));
        tokenExpirationStorage.set(new Date(Date.now() + 600_000));
        await act(() =>
            window.dispatchEvent(
                new StorageEvent('storage', {
                    key: tokenExpirationStorage.storageKey,
                    newValue: new Date(Date.now() + 600_000).toISOString(),
                }),
            ),
        );

        // Assert
        expect(queryClient.getQueryData(['private-account-data'])).toBeUndefined();
        expect(screen.getByTestId('username')).toHaveTextContent('renewed');
    });

    it.each([false, true])(
        'does not restore old private data or permissions after a session transition (same account: %s)',
        async (sameAccount) => {
            setupStoredAdminSession();
            let finishPrivate!: (value: string) => void;
            const oldPrivate = new Promise<string>((resolve) => {
                finishPrivate = resolve;
            });
            let finishPermissions!: (value: EffectivePermissions) => void;
            const oldPermissions = new Promise<EffectivePermissions>((resolve) => {
                finishPermissions = resolve;
            });
            vi.mocked(authApiModule.fetchEffectivePermissions)
                .mockReturnValueOnce(oldPermissions)
                .mockResolvedValue({
                    role: Role.Viewer,
                    permissions: { 'users.manage': { requiredRole: Role.Admin, granted: false } },
                });
            const fetchPrivate = vi.fn().mockReturnValueOnce(oldPrivate).mockResolvedValue('new session data');
            const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
            queryClient.setQueryData(['private-resource'], 'old private data');
            function PrivateObserver() {
                const query = useQuery({ queryKey: ['private-resource'], queryFn: fetchPrivate });
                return <span data-testid="private-resource">{query.data}</span>;
            }
            render(
                <QueryClientProvider client={queryClient}>
                    <ProductionAuthProvider
                        initialUser={{ id: '1', username: 'admin', role: Role.Admin }}
                        skipSessionInit
                    >
                        <AuthConsumer />
                        <PrivateObserver />
                    </ProductionAuthProvider>
                </QueryClientProvider>,
            );
            await waitFor(() => expect(fetchPrivate).toHaveBeenCalledOnce());
            expect(authApiModule.fetchEffectivePermissions).toHaveBeenCalledOnce();
            expect(screen.getByTestId('private-resource')).toHaveTextContent('old private data');

            clearAuthStorage();
            tokenStorage.set(fakeJwt({ sub: sameAccount ? '1' : '2', name: 'viewer', role: 0 }));
            tokenExpirationStorage.set(new Date(Date.now() + 600_000));
            await act(() =>
                window.dispatchEvent(
                    new StorageEvent('storage', {
                        key: tokenExpirationStorage.storageKey,
                        newValue: new Date(Date.now() + 600_000).toISOString(),
                    }),
                ),
            );
            await waitFor(() => expect(screen.getByTestId('private-resource')).toHaveTextContent('new session data'));
            await waitFor(() => expect(screen.getByTestId('role')).toHaveTextContent('0'));

            await act(async () => {
                finishPrivate('late old private data');
                finishPermissions({
                    role: Role.Admin,
                    permissions: { 'users.manage': { requiredRole: Role.Admin, granted: true } },
                });
                await Promise.all([oldPrivate, oldPermissions]);
            });

            expect(queryClient.getQueryData(['private-resource'])).toBe('new session data');
            expect(screen.getByTestId('private-resource')).toHaveTextContent('new session data');
            expect(screen.getByTestId('role')).toHaveTextContent('0');
            expect(screen.getByTestId('has-admin')).toHaveTextContent('false');
        },
    );

    it('keeps first-login local state while discarding cached and late anonymous query data', async () => {
        const user = userEvent.setup();
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        queryClient.setQueryData(['inactive-private-data'], 'discard me');
        let finishOld!: (value: string) => void;
        const oldResponse = new Promise<string>((resolve) => {
            finishOld = resolve;
        });
        let finishNew!: (value: string) => void;
        const newResponse = new Promise<string>((resolve) => {
            finishNew = resolve;
        });
        const fetchData = vi.fn().mockReturnValueOnce(oldResponse).mockReturnValue(newResponse);
        vi.mocked(authApiModule.login).mockResolvedValue({ id: 'viewer-id', username: 'viewer', role: Role.Viewer });
        vi.mocked(authApiModule.fetchEffectivePermissions).mockResolvedValue({ role: Role.Viewer, permissions: {} });
        function LoginObserver() {
            const query = useQuery({
                queryKey: ['observed-data'],
                queryFn: fetchData,
                initialData: () => queryClient.getQueryData<string>(['inactive-private-data']),
            });
            return (
                <>
                    <input aria-label="Local login draft" defaultValue="" />
                    <span data-testid="observed-data">{query.data ?? 'pending'}</span>
                </>
            );
        }
        render(
            <QueryClientProvider client={queryClient}>
                <ProductionAuthProvider skipSessionInit>
                    <AuthConsumer />
                    <LoginObserver />
                </ProductionAuthProvider>
            </QueryClientProvider>,
        );
        await user.type(screen.getByLabelText('Local login draft'), 'keep this draft');
        await waitFor(() => expect(fetchData).toHaveBeenCalledOnce());

        await user.click(screen.getByRole('button', { name: 'Log In' }));

        await waitFor(() => expect(screen.getByTestId('username')).toHaveTextContent('viewer'));
        expect(screen.getByLabelText('Local login draft')).toHaveValue('keep this draft');
        expect(queryClient.getQueryData(['inactive-private-data'])).toBeUndefined();
        expect(queryClient.getQueryData(['observed-data'])).toBeUndefined();
        expect(screen.getByTestId('observed-data')).toHaveTextContent('pending');
        await act(async () => {
            finishOld('late old response');
            await oldResponse;
        });
        expect(queryClient.getQueryData(['observed-data'])).toBeUndefined();
        await act(async () => {
            finishNew('new authenticated data');
            await newResponse;
        });
        await waitFor(() => expect(screen.getByTestId('observed-data')).toHaveTextContent('new authenticated data'));
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

    it('does not show login while another tab advances an expired recovered token', async () => {
        setupStoredAdminSession();
        vi.mocked(authApiModule.refreshAccessToken).mockReturnValue(new Promise(() => {}));
        render(
            <AuthProvider initialUser={{ id: '1', username: 'admin', role: Role.Admin }} skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );
        tokenStorage.set(fakeJwt({ sub: '1', name: 'admin', role: 3, exp: Math.floor(Date.now() / 1000) - 60 }));
        tokenExpirationStorage.set(new Date('2000-01-01T00:00:00Z'));
        await act(() =>
            window.dispatchEvent(
                new StorageEvent('storage', {
                    key: tokenExpirationStorage.storageKey,
                    newValue: '2000-01-01T00:00:00Z',
                }),
            ),
        );
        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
        expect(screen.getByTestId('username')).toHaveTextContent('admin');
        expect(authApiModule.refreshAccessToken).toHaveBeenCalledOnce();
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

    it('updates state on login', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockResolvedValue({ id: 'viewer-id', username: 'viewer', role: Role.Viewer });
        vi.mocked(authApiModule.fetchEffectivePermissions).mockResolvedValue({ role: Role.Viewer, permissions: {} });

        render(
            <AuthProvider skipSessionInit>
                <AuthConsumer />
            </AuthProvider>,
        );

        expect(screen.getByTestId('authenticated')).toHaveTextContent('false');

        await user.click(screen.getByRole('button', { name: 'Log In' }));

        await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));
        expect(screen.getByTestId('username')).toHaveTextContent('viewer');
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

    it.each([0, 503])('keeps startup recoverable after temporary refresh status %i', async (status) => {
        // Arrange
        setupExpiredSession();
        vi.mocked(authApiModule.refreshAccessToken).mockRejectedValueOnce(new ApiError(status, 'Temporary failure'));
        render(
            <AuthProvider>
                <AuthConsumer />
            </AuthProvider>,
        );
        await waitFor(() => expect(authApiModule.refreshAccessToken).toHaveBeenCalledOnce());

        // Assert
        expect(tokenStorage.get()).toBe('fake-access-token');
        expect(screen.getByTestId('loading')).toHaveTextContent('true');
        expect(screen.getByTestId('authenticated')).toHaveTextContent('false');

        // Act
        vi.mocked(authApiModule.refreshAccessToken).mockImplementationOnce(async () => {
            tokenStorage.set(fakeJwt({ sub: '1', name: 'admin', role: 3 }));
            tokenExpirationStorage.set(new Date(Date.now() + 3_600_000));
            return { id: '1', username: 'admin', role: Role.Admin };
        });
        act(() => {
            window.dispatchEvent(new Event('online'));
        });

        // Assert
        await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));
        expect(screen.getByTestId('loading')).toHaveTextContent('false');
    });

    it.each([0, 503])('retains the current user on a scheduled temporary refresh status %i', async (status) => {
        // Arrange
        setupExpiredSession();
        vi.mocked(authApiModule.refreshAccessToken).mockRejectedValueOnce(new ApiError(status, 'Temporary failure'));
        render(
            <AuthProvider initialUser={{ id: '1', username: 'admin', role: Role.Admin }}>
                <AuthConsumer />
            </AuthProvider>,
        );

        // Act
        await waitFor(() => expect(authApiModule.refreshAccessToken).toHaveBeenCalledOnce());

        // Assert
        expect(tokenStorage.get()).toBe('fake-access-token');
        expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
    });

    it('logs out when refresh fails', async () => {
        setupExpiredSession();
        vi.mocked(authApiModule.refreshAccessToken).mockRejectedValue(new ApiError(401, 'Session revoked'));

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
        vi.mocked(authApiModule.refreshAccessToken).mockRejectedValue(new ApiError(401, 'Session revoked'));

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
