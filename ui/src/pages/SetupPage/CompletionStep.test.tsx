import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import * as apiModule from '@/lib/api';
import * as setupApiModule from '@/lib/setupApi';
import { AuthProvider } from '@/auth/AuthContext';
import { Role } from '@/auth/types';
import { ToastProvider, ToastStateProvider } from '@/components/ui/Toast';
import { testEffectivePermissions } from '@/test/permissions';

import { CompletionStep } from './CompletionStep';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
        apiPatch: vi.fn(),
        apiDelete: vi.fn(),
    };
});

vi.mock('@/lib/setupApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/setupApi')>();
    return {
        ...actual,
        completeSetup: vi.fn(),
    };
});

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
}

interface RenderOptions {
    authed?: boolean;
}

function renderStep(onAdvance: () => void, { authed = false }: RenderOptions = {}) {
    const queryClient = createTestQueryClient();
    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={['/setup']}>
                <AuthProvider
                    initialUser={authed ? { id: 'u1', username: 'admin', role: Role.Admin } : null}
                    skipSessionInit
                    initialPermissions={testEffectivePermissions()}
                >
                    <ToastStateProvider>
                        <ToastProvider>
                            <Routes>
                                <Route path="/setup" element={<CompletionStep onAdvance={onAdvance} />} />
                                <Route path="/login" element={<div>Login page</div>} />
                                <Route path="/" element={<div>Home page</div>} />
                            </Routes>
                        </ToastProvider>
                    </ToastStateProvider>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('CompletionStep', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        vi.mocked(apiModule.apiGet).mockResolvedValue([]);
        vi.mocked(setupApiModule.completeSetup).mockResolvedValue({
            isRequired: false,
            isComplete: true,
            requiresAdminClaim: false,
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders a finish-setup button and a summary list', async () => {
        const onAdvance = vi.fn();
        renderStep(onAdvance);

        expect(await screen.findByRole('button', { name: /finish setup/i })).toBeInTheDocument();
        expect(document.querySelector('[data-component="setup-summary"]')).not.toBeNull();
    });

    it('summarizes configured integrations instead of available catalog entries', async () => {
        const onAdvance = vi.fn();
        vi.mocked(apiModule.apiGet).mockImplementation(async (path: string) => {
            if (path === '/settings/groups') {
                return [
                    {
                        id: 'metadata-empty',
                        name: 'Provider A',
                        extensionId: 'ext-empty',
                        createdAt: '2024-01-01T00:00:00Z',
                        settings: [
                            {
                                id: 'setting-empty',
                                settingsGroupId: 'metadata-empty',
                                key: 'apiKey',
                                value: '',
                                createdAt: '2024-01-01T00:00:00Z',
                            },
                        ],
                    },
                    {
                        id: 'metadata-configured',
                        name: 'Provider D',
                        extensionId: 'ext-configured',
                        createdAt: '2024-01-01T00:00:00Z',
                        settings: [
                            {
                                id: 'setting-configured',
                                settingsGroupId: 'metadata-configured',
                                key: 'apiKey',
                                value: 'configured',
                                createdAt: '2024-01-01T00:00:00Z',
                            },
                        ],
                    },
                ] as never;
            }
            if (path === '/indexers') {
                return [
                    {
                        id: 'idx-available',
                        providerType: 'newznab',
                        name: 'Available only',
                        availability: 'Public',
                        createdAt: '2024-01-01T00:00:00Z',
                        connectionCount: 0,
                    },
                    {
                        id: 'idx-configured',
                        providerType: 'torznab',
                        name: 'Configured',
                        availability: 'Private',
                        createdAt: '2024-01-01T00:00:00Z',
                        connectionCount: 2,
                    },
                ] as never;
            }
            if (path === '/download-clients') {
                return [{ id: 'client-1' }] as never;
            }
            if (path === '/media-servers') {
                return [{ id: 'server-1' }] as never;
            }
            if (path === '/root-folders') {
                return [{ id: 'root-1' }] as never;
            }
            return [] as never;
        });

        renderStep(onAdvance);

        await screen.findByRole('button', { name: /finish setup/i });
        await waitFor(() => {
            expect(screen.getByText('Metadata-provider configs').nextElementSibling).toHaveTextContent('1');
        });
        expect(screen.getByText('Indexers').nextElementSibling).toHaveTextContent('2');
        expect(screen.getByText('Download clients').nextElementSibling).toHaveTextContent('1');
        expect(screen.getByText('Media servers').nextElementSibling).toHaveTextContent('1');
        expect(screen.getByText('Root folders').nextElementSibling).toHaveTextContent('1');
    });

    it('calls the complete endpoint and navigates unauthed users to /login', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        renderStep(onAdvance);

        await user.click(await screen.findByRole('button', { name: /finish setup/i }));

        await waitFor(() => {
            expect(vi.mocked(setupApiModule.completeSetup)).toHaveBeenCalled();
        });
        expect(await screen.findByText('Login page')).toBeInTheDocument();
        expect(onAdvance).toHaveBeenCalled();
    });

    it('navigates authed users to the dashboard after completion', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        renderStep(onAdvance, { authed: true });

        await user.click(await screen.findByRole('button', { name: /finish setup/i }));

        await waitFor(() => {
            expect(vi.mocked(setupApiModule.completeSetup)).toHaveBeenCalled();
        });
        expect(await screen.findByText('Home page')).toBeInTheDocument();
    });
});
