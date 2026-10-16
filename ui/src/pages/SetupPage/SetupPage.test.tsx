import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { onlineManager, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { AuthProvider } from '@/auth/AuthContext';
import { Role } from '@/auth/types';
import { ToastProvider, ToastStateProvider } from '@/components/ui/Toast';
import { RestartRequiredProvider } from '@/hooks/ui/useRestartRequired';
import { testEffectivePermissions } from '@/test/permissions';
import { ApiError } from '@/lib/api';
import * as apiModule from '@/lib/api';
import * as authApiModule from '@/lib/authApi';
import * as setupApiModule from '@/lib/setupApi';
import type { ApiSettingsGroup } from '@/hooks/useSettings';
import { LoginPage } from '@/pages/LoginPage/LoginPage';

import { SetupPage } from './SetupPage';

const instanceNameState = vi.hoisted(() => ({ name: 'Magpie' }));

vi.mock('@/hooks/useInstanceName', () => ({
    useInstanceName: () => instanceNameState.name,
}));

vi.mock('@/lib/setupApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/setupApi')>();
    return {
        ...actual,
        fetchSetupStatus: vi.fn(),
        createSetupAdmin: vi.fn(),
        fetchSetupDatabaseStatus: vi.fn(),
        configureSetupDatabase: vi.fn(),
    };
});

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});

function mockApiGet(settingsGroups: ApiSettingsGroup[]) {
    vi.mocked(apiModule.apiGet).mockImplementation(async (path: string) => {
        if (path === '/settings/groups') {
            return settingsGroups as never;
        }
        return [] as never;
    });
}

vi.mock('@/lib/authApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/authApi')>();
    return {
        ...actual,
        login: vi.fn(),
        logout: vi.fn(),
        refreshAccessToken: vi.fn(),
    };
});

interface RenderOptions {
    initialUser?: { id: string; username: string; role: Role } | null;
    loginElement?: ReactNode;
}

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
}

function renderSetupPage({ initialUser = null, loginElement = <div>Login page</div> }: RenderOptions = {}) {
    const queryClient = createTestQueryClient();
    return render(
        <QueryClientProvider client={queryClient}>
            <ToastStateProvider>
                <ToastProvider>
                    <MemoryRouter initialEntries={['/setup']}>
                        <AuthProvider
                            initialUser={initialUser}
                            skipSessionInit
                            initialPermissions={testEffectivePermissions()}
                        >
                            <RestartRequiredProvider>
                                <Routes>
                                    <Route path="/setup" element={<SetupPage />} />
                                    <Route path="/login" element={loginElement} />
                                    <Route path="/" element={<div>Home page</div>} />
                                </Routes>
                            </RestartRequiredProvider>
                        </AuthProvider>
                    </MemoryRouter>
                </ToastProvider>
            </ToastStateProvider>
        </QueryClientProvider>,
    );
}

async function waitForStep(targetStep: string) {
    const { waitFor } = await import('@testing-library/react');
    await waitFor(() => {
        const el = document.querySelector(`[data-step="${targetStep}"]`);
        if (!el) {
            throw new Error(`Step ${targetStep} not yet rendered`);
        }
    });
}

async function claimAdmin(user: ReturnType<typeof userEvent.setup>) {
    await waitForStep('admin');
    await user.type(screen.getByLabelText(/^username/i), 'admin');
    await user.type(screen.getByLabelText(/^password/i), 'secret123');
    await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
    await user.click(screen.getByRole('button', { name: /create administrator/i }));
}

function mockAdminClaim() {
    vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
        isRequired: true,
        isComplete: false,
        requiresAdminClaim: true,
    });
    vi.mocked(setupApiModule.createSetupAdmin).mockResolvedValue({
        id: 'user-1',
        username: 'admin',
        email: null,
        role: Role.Admin,
    });
    vi.mocked(authApiModule.login).mockResolvedValue({
        id: 'user-1',
        username: 'admin',
        role: Role.Admin,
    });
}

const SETTINGS_GROUP_WITH_KEYS: ApiSettingsGroup = {
    id: 'group-1',
    name: 'Provider A',
    extensionId: 'ext-1',
    createdAt: '2024-01-01T00:00:00Z',
    settings: [
        {
            id: 'set-1',
            settingsGroupId: 'group-1',
            key: 'apiKey',
            value: 'configured-key',
            createdAt: '2024-01-01T00:00:00Z',
        },
    ],
    schema: [{ key: 'apiKey', type: 'String' }],
};

describe('SetupPage', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        instanceNameState.name = 'Magpie';
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus).mockResolvedValue({
            initialized: true,
            provider: 'SQLite',
        });
        mockApiGet([]);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the instance name in the heading and the title when the instance name is set', async () => {
        instanceNameState.name = 'Home Lab';
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });

        renderSetupPage();

        expect(await screen.findByRole('heading', { name: 'Welcome to Home Lab' })).toBeInTheDocument();
        expect(document.title).toBe('Set Up - Home Lab');
    });

    it('renders the wizard shell when setup is required', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });

        renderSetupPage();

        expect(await screen.findByRole('heading', { name: /welcome to magpie/i })).toBeInTheDocument();
        const page = document.querySelector('[data-page="setup"]');
        expect(page).not.toBeNull();
    });

    it('shows an accessible failure state and retries setup status only on request', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus)
            .mockRejectedValueOnce(new Error('Unexpected response type'))
            .mockResolvedValueOnce({
                isRequired: false,
                isComplete: true,
                requiresAdminClaim: false,
            });

        renderSetupPage();

        const alert = await screen.findByRole('alert');
        expect(alert).toHaveTextContent(/failed to load browser setup status/i);
        expect(document.querySelector('[data-page="setup"]')).toHaveAttribute('data-current-step', 'error');
        expect(vi.mocked(setupApiModule.fetchSetupStatus)).toHaveBeenCalledTimes(1);

        window.dispatchEvent(new Event('focus'));
        expect(vi.mocked(setupApiModule.fetchSetupStatus)).toHaveBeenCalledTimes(1);

        await user.click(screen.getByRole('button', { name: 'Retry' }));

        expect(await screen.findByText('Login page')).toBeInTheDocument();
        expect(vi.mocked(setupApiModule.fetchSetupStatus)).toHaveBeenCalledTimes(2);
    });

    it('does not retry setup status on reconnect and keeps retry manual', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockRejectedValueOnce(new Error('Offline')).mockResolvedValueOnce({
            isRequired: false,
            isComplete: true,
            requiresAdminClaim: false,
        });

        renderSetupPage();

        expect(await screen.findByTestId('setup-status-error')).toBeInTheDocument();
        expect(vi.mocked(setupApiModule.fetchSetupStatus)).toHaveBeenCalledTimes(1);

        await act(async () => {
            onlineManager.setOnline(false);
            onlineManager.setOnline(true);
            await Promise.resolve();
        });

        expect(vi.mocked(setupApiModule.fetchSetupStatus)).toHaveBeenCalledTimes(1);

        await user.click(screen.getByRole('button', { name: 'Retry' }));

        expect(await screen.findByText('Login page')).toBeInTheDocument();
        expect(vi.mocked(setupApiModule.fetchSetupStatus)).toHaveBeenCalledTimes(2);
    });

    it('lands on the admin-claim step when the db is already initialised', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });

        renderSetupPage();

        await waitForStep('admin');

        expect(screen.getByLabelText(/^username/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/^password/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /create administrator/i })).toBeInTheDocument();
        expect(vi.mocked(apiModule.apiGet)).not.toHaveBeenCalledWith('/settings/groups');
    });

    it('writes the roles and the job list of a node that joins a cluster and then opens login', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus).mockResolvedValue({
            initialized: true,
            provider: 'SQLite',
            jobTypes: { MediaInfoExtractItem: 'Media info extraction', DownloadCleanup: 'Download cleanup' },
        });
        vi.mocked(setupApiModule.configureSetupDatabase).mockResolvedValue({
            provider: 'postgresql',
            restartRequired: true,
        });

        renderSetupPage();

        await waitForStep('admin');
        await user.click(screen.getByRole('button', { name: 'Join an existing cluster' }));
        await waitForStep('join');
        await user.type(screen.getByLabelText(/connection string/i), 'Host=db;Database=magpie');
        await user.click(screen.getByLabelText('API'));
        await user.click(screen.getByText('Job types'));
        await user.click(screen.getByLabelText('Download cleanup'));
        await user.click(screen.getByRole('button', { name: /save and continue/i }));
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: false,
            isComplete: true,
            requiresAdminClaim: false,
        });
        await user.click(await screen.findByRole('button', { name: /i have restarted magpie/i }));

        expect(await screen.findByText('Login page')).toBeInTheDocument();
        expect(screen.queryByLabelText(/sqlite/i)).not.toBeInTheDocument();
        expect(vi.mocked(setupApiModule.configureSetupDatabase).mock.calls[0][0]).toEqual({
            provider: 'postgresql',
            connectionString: 'Host=db;Database=magpie',
            roles: ['Ui', 'JobRunner'],
            jobTypes: ['MediaInfoExtractItem'],
        });
    });

    it('keeps save disabled while a joining job runner has no job type selected', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus).mockResolvedValue({
            initialized: true,
            provider: 'SQLite',
            jobTypes: { MediaInfoExtractItem: 'Media info extraction', DownloadCleanup: 'Download cleanup' },
        });

        renderSetupPage();

        await waitForStep('admin');
        await user.click(screen.getByRole('button', { name: 'Join an existing cluster' }));
        await waitForStep('join');
        await user.click(screen.getByText('Job types'));
        await user.click(screen.getByLabelText('All job types'));

        expect(screen.getByRole('button', { name: /save and continue/i })).toBeDisabled();
        await user.click(screen.getByLabelText('Download cleanup'));
        expect(screen.getByRole('button', { name: /save and continue/i })).toBeEnabled();
    });

    it('asks only for a restart after a node without the UI role joins a cluster', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus).mockResolvedValue({
            initialized: true,
            provider: 'SQLite',
            jobTypes: { MediaInfoExtractItem: 'Media info extraction', DownloadCleanup: 'Download cleanup' },
        });
        vi.mocked(setupApiModule.configureSetupDatabase).mockResolvedValue({
            provider: 'postgresql',
            restartRequired: true,
        });

        renderSetupPage();

        await waitForStep('admin');
        await user.click(screen.getByRole('button', { name: 'Join an existing cluster' }));
        await waitForStep('join');
        await user.type(screen.getByLabelText(/connection string/i), 'Host=db;Database=magpie');
        await user.click(screen.getByLabelText('UI'));
        await user.click(screen.getByLabelText('API'));
        await user.click(screen.getByRole('button', { name: /^save$/i }));

        expect(await screen.findByText(/so the new database provider takes effect\.$/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /i have restarted magpie/i })).not.toBeInTheDocument();
        expect(vi.mocked(setupApiModule.configureSetupDatabase).mock.calls[0][0]).toEqual({
            provider: 'postgresql',
            connectionString: 'Host=db;Database=magpie',
            roles: ['JobRunner'],
            jobTypes: [],
        });
    });

    it('redirects unauthenticated users to /login when setup is already complete', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: false,
            isComplete: true,
            requiresAdminClaim: false,
        });

        renderSetupPage();

        expect(await screen.findByText('Login page')).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: /welcome to magpie/i })).not.toBeInTheDocument();
    });

    it('redirects authenticated users to / when setup is already complete', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: false,
            isComplete: true,
            requiresAdminClaim: false,
        });

        renderSetupPage({ initialUser: { id: 'user-1', username: 'admin', role: Role.Admin } });

        expect(await screen.findByText('Home page')).toBeInTheDocument();
    });

    it('sends an externally claimed unauthenticated setup through login before loading protected settings', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: false,
        });
        vi.mocked(authApiModule.login).mockResolvedValue({
            id: 'user-1',
            username: 'admin',
            role: Role.Admin,
        });
        let resolveGroups!: (groups: ApiSettingsGroup[]) => void;
        const groupsResponse = new Promise<ApiSettingsGroup[]>((resolve) => {
            resolveGroups = resolve;
        });
        vi.mocked(apiModule.apiGet).mockImplementation(async (path: string) => {
            if (path === '/settings/groups') {
                return groupsResponse as never;
            }
            return [] as never;
        });

        renderSetupPage({ loginElement: <LoginPage /> });

        expect(await screen.findByRole('button', { name: /sign in/i })).toBeInTheDocument();
        expect(vi.mocked(apiModule.apiGet)).not.toHaveBeenCalledWith('/settings/groups');

        await user.type(screen.getByLabelText(/username/i), 'admin');
        await user.type(screen.getByLabelText(/password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        await waitFor(() => {
            expect(vi.mocked(apiModule.apiGet)).toHaveBeenCalledWith('/settings/groups');
        });
        await waitForStep('extensions');
        await user.click(screen.getByRole('button', { name: 'Continue' }));
        expect(screen.queryByText('Provider A')).not.toBeInTheDocument();

        await act(async () => {
            resolveGroups([SETTINGS_GROUP_WITH_KEYS]);
        });

        await waitForStep('metadata');
        expect(await screen.findByText('Provider A')).toBeInTheDocument();
        expect(vi.mocked(apiModule.apiGet)).toHaveBeenCalledWith('/settings/groups');
    });

    it('submits the create-admin form and advances the wizard', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });
        vi.mocked(setupApiModule.createSetupAdmin).mockResolvedValue({
            id: 'user-1',
            username: 'admin',
            email: null,
            role: Role.Admin,
        });
        vi.mocked(authApiModule.login).mockResolvedValue({
            id: 'user-1',
            username: 'admin',
            role: Role.Admin,
        });

        renderSetupPage();
        await waitForStep('admin');

        await user.type(screen.getByLabelText(/^username/i), 'admin');
        await user.type(screen.getByLabelText(/^password/i), 'secret123');
        await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /create administrator/i }));

        await waitFor(() => {
            expect(vi.mocked(setupApiModule.createSetupAdmin)).toHaveBeenCalled();
        });
        expect(vi.mocked(setupApiModule.createSetupAdmin).mock.calls[0][0]).toEqual({
            username: 'admin',
            password: 'secret123',
        });

        await waitFor(() => {
            const page = document.querySelector('[data-page="setup"]');
            expect(page).not.toBeNull();
            expect(page?.getAttribute('data-current-step')).not.toBe('admin');
        });
    });

    it('shows a validation error when passwords do not match and does not call the API', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });

        renderSetupPage();
        await waitForStep('admin');

        await user.type(screen.getByLabelText(/^username/i), 'admin');
        await user.type(screen.getByLabelText(/^password/i), 'secret123');
        await user.type(screen.getByLabelText(/confirm password/i), 'different');
        await user.click(screen.getByRole('button', { name: /create administrator/i }));

        const errorEl = await screen.findByTestId('setup-error');
        expect(errorEl).toHaveTextContent(/passwords do not match/i);
        expect(vi.mocked(setupApiModule.createSetupAdmin)).not.toHaveBeenCalled();
    });

    it('shows a validation error when fields are empty', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });

        renderSetupPage();
        await waitForStep('admin');

        await user.click(screen.getByRole('button', { name: /create administrator/i }));

        const errorEl = await screen.findByTestId('setup-error');
        expect(errorEl).toHaveTextContent(/please enter a username and password/i);
        expect(vi.mocked(setupApiModule.createSetupAdmin)).not.toHaveBeenCalled();
    });

    it('shows an inline error message when the API rejects the request', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });
        vi.mocked(setupApiModule.createSetupAdmin).mockRejectedValue(new ApiError(409, 'Setup already complete'));

        renderSetupPage();
        await waitForStep('admin');

        await user.type(screen.getByLabelText(/^username/i), 'admin');
        await user.type(screen.getByLabelText(/^password/i), 'secret123');
        await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /create administrator/i }));

        const errorEl = await screen.findByTestId('setup-error');
        expect(errorEl).toHaveTextContent(/administrator account has already been claimed/i);
        expect(screen.queryByText('Login page')).not.toBeInTheDocument();
    });

    it('shows a generic error message for non-API failures', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });
        vi.mocked(setupApiModule.createSetupAdmin).mockRejectedValue(new Error('Network down'));

        renderSetupPage();
        await waitForStep('admin');

        await user.type(screen.getByLabelText(/^username/i), 'admin');
        await user.type(screen.getByLabelText(/^password/i), 'secret123');
        await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /create administrator/i }));

        const errorEl = await screen.findByTestId('setup-error');
        expect(errorEl).toHaveTextContent('Network down');
    });

    it('has the expected data-page attribute', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });

        renderSetupPage();

        await screen.findByRole('heading', { name: /welcome to magpie/i });
        expect(document.querySelector('[data-page="setup"]')).toBeInTheDocument();
    });

    it('keeps the active claim mounted when the server reports it claimed before sign-in finishes', async () => {
        const user = userEvent.setup();
        let claimed = false;
        vi.mocked(setupApiModule.fetchSetupStatus).mockImplementation(async () => ({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: !claimed,
        }));
        vi.mocked(setupApiModule.createSetupAdmin).mockImplementation(async () => {
            claimed = true;
            return { id: 'user-1', username: 'admin', email: null, role: Role.Admin };
        });
        let finishLogin!: (user: Awaited<ReturnType<typeof authApiModule.login>>) => void;
        vi.mocked(authApiModule.login).mockReturnValue(
            new Promise((resolve) => {
                finishLogin = resolve;
            }),
        );
        renderSetupPage();
        await claimAdmin(user);

        await waitFor(() => expect(authApiModule.login).toHaveBeenCalledOnce());
        expect(screen.getByRole('button', { name: /continue setup/i })).toBeDisabled();
        expect(screen.queryByText('Login page')).not.toBeInTheDocument();
        await act(async () => {
            finishLogin({ id: 'user-1', username: 'admin', role: Role.Admin });
        });
        await waitForStep('extensions');
        expect(setupApiModule.createSetupAdmin).toHaveBeenCalledOnce();
        expect(authApiModule.login).toHaveBeenCalledOnce();
    });

    it('skips the metadata step when no extension settings groups exist', async () => {
        const user = userEvent.setup();
        mockAdminClaim();
        mockApiGet([]);

        renderSetupPage();
        await claimAdmin(user);
        await waitForStep('extensions');
        await user.click(screen.getByRole('button', { name: 'Continue' }));

        await waitForStep('indexer');
        expect(document.querySelector('[data-step="metadata"]')).toBeNull();
    });

    it('continues from configured metadata without marking it skipped', async () => {
        const user = userEvent.setup();
        mockAdminClaim();
        mockApiGet([SETTINGS_GROUP_WITH_KEYS]);

        renderSetupPage();
        await claimAdmin(user);
        await waitForStep('extensions');
        await user.click(screen.getByRole('button', { name: 'Continue' }));

        await waitForStep('metadata');
        expect(await screen.findByText('Provider A')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Continue' }));
        await waitForStep('indexer');
        await user.click(screen.getByRole('button', { name: /skip for now/i }));
        await waitForStep('download');
        await user.click(screen.getByRole('button', { name: /skip for now/i }));
        await waitForStep('root');
        await user.click(screen.getByRole('button', { name: /skip for now/i }));
        await waitForStep('server');
        await user.click(screen.getByRole('button', { name: /skip for now/i }));

        await waitForStep('done');
        const metadataSummary = screen.getByText('Metadata-provider configs').closest('li');
        expect(metadataSummary).toHaveTextContent('1');
        expect(metadataSummary).toHaveTextContent('Provider A');
        expect(metadataSummary).not.toHaveTextContent('Skipped');
    });

    it('handleAdvance_StaysOnTheExtensionsStep_WhenExtensionSettingsAreLoading', async () => {
        // Arrange
        const user = userEvent.setup();
        mockAdminClaim();
        let resolveGroups!: (groups: ApiSettingsGroup[]) => void;
        const groupsResponse = new Promise<ApiSettingsGroup[]>((resolve) => {
            resolveGroups = resolve;
        });
        vi.mocked(apiModule.apiGet).mockImplementation(async (path: string) => {
            if (path === '/settings/groups') {
                return groupsResponse as never;
            }
            return [] as never;
        });
        renderSetupPage();
        await claimAdmin(user);
        await waitForStep('extensions');

        // Act
        await user.click(screen.getByRole('button', { name: 'Continue' }));

        // Assert
        expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
        await act(async () => {
            resolveGroups([]);
        });
        await waitForStep('indexer');
        expect(vi.mocked(setupApiModule.createSetupAdmin)).toHaveBeenCalledTimes(1);
        expect(vi.mocked(authApiModule.login)).toHaveBeenCalledTimes(1);
    });

    it('handleAdvance_KeepsTheExtensionsStepWithTheError_WhenExtensionSettingsFailToLoad', async () => {
        // Arrange
        const user = userEvent.setup();
        mockAdminClaim();
        vi.mocked(apiModule.apiGet).mockImplementation(async (path: string) => {
            if (path === '/settings/groups') {
                throw new ApiError(500, 'Could not load extension settings');
            }
            return [] as never;
        });
        renderSetupPage();
        await claimAdmin(user);
        await waitForStep('extensions');

        // Act
        await user.click(screen.getByRole('button', { name: 'Continue' }));

        // Assert
        expect(await screen.findByTestId('setup-error')).toHaveTextContent('Could not load extension settings');
        expect(document.querySelector('[data-step="extensions"]')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled();
    });

    it('presents the root-folder step before the media-server step', async () => {
        const user = userEvent.setup();
        mockAdminClaim();
        mockApiGet([]);

        renderSetupPage();
        await claimAdmin(user);
        await waitForStep('extensions');
        await user.click(screen.getByRole('button', { name: 'Continue' }));

        await waitForStep('indexer');
        await user.click(screen.getByRole('button', { name: /skip for now/i }));

        await waitForStep('download');
        await user.click(screen.getByRole('button', { name: /skip for now/i }));

        await waitForStep('root');
        expect(document.querySelector('[data-step="server"]')).toBeNull();
        await user.click(screen.getByRole('button', { name: /skip for now/i }));

        await waitForStep('server');
    });
});
