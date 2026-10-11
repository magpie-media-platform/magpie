import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ApiError } from '@/lib/api';
import * as setupApiModule from '@/lib/setupApi';
import { AuthProvider } from '@/auth/AuthContext';
import { Role } from '@/auth/types';
import { testEffectivePermissions } from '@/test/permissions';

import { AdminClaimStep } from './AdminClaimStep';

vi.mock('@/lib/setupApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/setupApi')>();
    return {
        ...actual,
        fetchSetupStatus: vi.fn(),
        createSetupAdmin: vi.fn(),
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

function renderStep(onAdvance: () => void | Promise<void>, initialUser?: { id: string; username: string; role: Role }) {
    const queryClient = createTestQueryClient();
    const step = <AdminClaimStep onAdvance={onAdvance} />;
    return render(
        <QueryClientProvider client={queryClient}>
            {initialUser ? (
                <AuthProvider
                    initialUser={initialUser}
                    skipSessionInit
                    initialPermissions={testEffectivePermissions(initialUser.role)}
                >
                    {step}
                </AuthProvider>
            ) : (
                step
            )}
        </QueryClientProvider>,
    );
}

describe('AdminClaimStep', () => {
    beforeEach(() => {
        vi.resetAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the credential form when the seed admin still needs claiming', async () => {
        const onAdvance = vi.fn();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });

        renderStep(onAdvance);

        expect(await screen.findByLabelText(/^username/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/^password/i)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /create administrator/i })).toBeInTheDocument();
        expect(onAdvance).not.toHaveBeenCalled();
    });

    it('auto-skips when the admin has already been claimed and this client is authenticated', async () => {
        const onAdvance = vi.fn();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: false,
        });

        renderStep(onAdvance, { id: 'user-1', username: 'admin', role: Role.Admin });

        await waitFor(() => {
            expect(onAdvance).toHaveBeenCalled();
        });
    });

    it('claims the admin and advances on success', async () => {
        const onAdvance = vi.fn();
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

        renderStep(onAdvance);

        await user.type(await screen.findByLabelText(/^username/i), 'admin');
        await user.type(screen.getByLabelText(/^password/i), 'secret123');
        await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /create administrator/i }));

        await waitFor(() => {
            expect(onAdvance).toHaveBeenCalled();
        });
    });

    it('surfaces api errors inline without advancing', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });
        vi.mocked(setupApiModule.createSetupAdmin).mockRejectedValue(new ApiError(409, 'Already claimed'));

        renderStep(onAdvance);

        await user.type(await screen.findByLabelText(/^username/i), 'admin');
        await user.type(screen.getByLabelText(/^password/i), 'secret123');
        await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /create administrator/i }));

        const errorEl = await screen.findByTestId('setup-error');
        expect(errorEl).toHaveTextContent(/already been claimed/i);
        expect(onAdvance).not.toHaveBeenCalled();
    });
});
