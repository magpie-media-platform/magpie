import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { AuthProvider } from '@/auth/AuthContext';
import { Role } from '@/auth/types';
import { ApiError } from '@/lib/api';
import * as setupApiModule from '@/lib/setupApi';

import { SetupPage } from './SetupPage';

vi.mock('@/lib/setupApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/setupApi')>();
    return {
        ...actual,
        fetchSetupStatus: vi.fn(),
        createSetupAdmin: vi.fn(),
    };
});

interface RenderOptions {
    initialUser?: { id: string; username: string; role: Role } | null;
}

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
}

function renderSetupPage({ initialUser = null }: RenderOptions = {}) {
    const queryClient = createTestQueryClient();
    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={['/setup']}>
                <AuthProvider initialUser={initialUser} skipSessionInit>
                    <Routes>
                        <Route path="/setup" element={<SetupPage />} />
                        <Route path="/login" element={<div>Login page</div>} />
                        <Route path="/" element={<div>Home page</div>} />
                    </Routes>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('SetupPage', () => {
    beforeEach(() => {
        vi.resetAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the setup form when status indicates setup is required', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
        });

        renderSetupPage();

        expect(await screen.findByRole('heading', { name: /welcome to magpie/i })).toBeInTheDocument();
        expect(screen.getByLabelText(/^username/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/^password/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /create administrator/i })).toBeInTheDocument();
    });

    it('redirects unauthenticated users to /login when setup is already complete', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: false,
            isComplete: true,
        });

        renderSetupPage();

        expect(await screen.findByText('Login page')).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: /welcome to magpie/i })).not.toBeInTheDocument();
    });

    it('redirects authenticated users to / when setup is already complete', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: false,
            isComplete: true,
        });

        renderSetupPage({ initialUser: { id: 'user-1', username: 'admin', role: Role.Admin } });

        expect(await screen.findByText('Home page')).toBeInTheDocument();
    });

    it('submits the create-admin form and navigates to /login on success', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
        });
        vi.mocked(setupApiModule.createSetupAdmin).mockResolvedValue({
            id: 'user-1',
            username: 'admin',
            email: null,
            role: Role.Admin,
        });

        renderSetupPage();

        await user.type(await screen.findByLabelText(/^username/i), 'admin');
        await user.type(screen.getByLabelText(/^password/i), 'secret123');
        await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /create administrator/i }));

        await waitFor(() => {
            expect(vi.mocked(setupApiModule.createSetupAdmin)).toHaveBeenCalledWith({
                username: 'admin',
                password: 'secret123',
            });
        });

        await waitFor(() => {
            expect(screen.getByText('Login page')).toBeInTheDocument();
        });
    });

    it('shows a validation error when passwords do not match and does not call the API', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
        });

        renderSetupPage();

        await user.type(await screen.findByLabelText(/^username/i), 'admin');
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
        });

        renderSetupPage();

        await user.click(await screen.findByRole('button', { name: /create administrator/i }));

        const errorEl = await screen.findByTestId('setup-error');
        expect(errorEl).toHaveTextContent(/please enter a username and password/i);
        expect(vi.mocked(setupApiModule.createSetupAdmin)).not.toHaveBeenCalled();
    });

    it('shows an inline error message when the API rejects the request', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
        });
        vi.mocked(setupApiModule.createSetupAdmin).mockRejectedValue(new ApiError(409, 'Setup already complete'));

        renderSetupPage();

        await user.type(await screen.findByLabelText(/^username/i), 'admin');
        await user.type(screen.getByLabelText(/^password/i), 'secret123');
        await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /create administrator/i }));

        const errorEl = await screen.findByTestId('setup-error');
        expect(errorEl).toHaveTextContent(/setup has already been completed/i);
        expect(screen.queryByText('Login page')).not.toBeInTheDocument();
    });

    it('shows a generic error message for non-API failures', async () => {
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
        });
        vi.mocked(setupApiModule.createSetupAdmin).mockRejectedValue(new Error('Network down'));

        renderSetupPage();

        await user.type(await screen.findByLabelText(/^username/i), 'admin');
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
        });

        renderSetupPage();

        await screen.findByRole('heading', { name: /welcome to magpie/i });
        expect(document.querySelector('[data-page="setup"]')).toBeInTheDocument();
    });
});
