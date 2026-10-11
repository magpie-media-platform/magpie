import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';

import { AuthTestProvider as AuthProvider } from '@/test/AuthTestProvider';
import { Role } from '@/auth/types';
import { ApiError } from '@/lib/api';
import * as authApiModule from '@/lib/authApi';
import { clearAuthStorage } from '@/lib/tokenStorage';

import { LoginPage } from './LoginPage';

const instanceNameState = vi.hoisted(() => ({ name: 'Magpie' }));

vi.mock('@/hooks/useInstanceName', () => ({
    useInstanceName: () => instanceNameState.name,
}));

vi.mock('@/lib/authApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/authApi')>();
    return {
        ...actual,
        login: vi.fn(),
        logout: vi.fn(),
        refreshAccessToken: vi.fn(),
    };
});

function renderLoginPage(initialPath: string | { pathname: string; state?: unknown } = '/login') {
    return render(
        <MemoryRouter initialEntries={[initialPath]}>
            <AuthProvider skipSessionInit>
                <Routes>
                    <Route path="/login" element={<LoginPage />} />
                    <Route path="/" element={<div>Home page</div>} />
                    <Route
                        path="/library"
                        element={
                            <>
                                <div>Library page</div>
                                <LocationProbe />
                            </>
                        }
                    />
                </Routes>
            </AuthProvider>
        </MemoryRouter>,
    );
}

function LocationProbe() {
    const location = useLocation();
    return <div data-testid="location-probe">{`${location.pathname}${location.search}${location.hash}`}</div>;
}

describe('LoginPage', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        instanceNameState.name = 'Magpie';
        clearAuthStorage();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the instance name as the heading when the instance name is set', () => {
        instanceNameState.name = 'Home Lab';

        renderLoginPage();

        expect(screen.getByRole('heading', { name: 'Home Lab' })).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Magpie' })).not.toBeInTheDocument();
    });

    it('renders the sign in form', () => {
        renderLoginPage();

        expect(screen.getByRole('heading', { name: 'Magpie' })).toBeInTheDocument();
        expect(screen.getByLabelText(/username/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
    });

    it('has correct data-page attribute', () => {
        renderLoginPage();

        expect(document.querySelector('[data-page="login"]')).toBeInTheDocument();
    });

    it('calls authApi.login with the entered credentials on submit', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockResolvedValue({
            id: 'user-1',
            username: 'admin',
            role: Role.Admin,
        });

        renderLoginPage();

        await user.type(screen.getByLabelText(/username/i), 'admin');
        await user.type(screen.getByLabelText(/password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        await waitFor(() => {
            expect(vi.mocked(authApiModule.login)).toHaveBeenCalledWith('admin', 'secret123');
        });
    });

    it('keeps an accessible name while sign in is loading', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockImplementation(() => new Promise(() => {}));

        renderLoginPage();

        await user.type(screen.getByLabelText(/username/i), 'admin');
        await user.type(screen.getByLabelText(/password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: 'Sign in' }));

        const submitButton = screen.getByRole('button', { name: 'Sign in' });
        expect(submitButton).toHaveAttribute('aria-busy', 'true');
        expect(submitButton).toBeDisabled();
    });

    it('navigates to the dashboard on successful login by default', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockResolvedValue({
            id: 'user-1',
            username: 'admin',
            role: Role.Admin,
        });

        renderLoginPage();

        await user.type(screen.getByLabelText(/username/i), 'admin');
        await user.type(screen.getByLabelText(/password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        await waitFor(() => {
            expect(screen.getByText('Home page')).toBeInTheDocument();
        });
    });

    it('navigates to the from location stored in router state on success', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockResolvedValue({
            id: 'user-1',
            username: 'admin',
            role: Role.Admin,
        });

        renderLoginPage({
            pathname: '/login',
            state: { from: { pathname: '/library' } },
        });

        await user.type(screen.getByLabelText(/username/i), 'admin');
        await user.type(screen.getByLabelText(/password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        await waitFor(() => {
            expect(screen.getByText('Library page')).toBeInTheDocument();
        });
    });

    it('preserves query string and hash from the from location on success', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockResolvedValue({
            id: 'user-1',
            username: 'admin',
            role: Role.Admin,
        });

        renderLoginPage({
            pathname: '/login',
            state: { from: { pathname: '/library', search: '?filter=missing', hash: '#results' } },
        });

        await user.type(screen.getByLabelText(/username/i), 'admin');
        await user.type(screen.getByLabelText(/password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        await waitFor(() => {
            expect(screen.getByTestId('location-probe')).toHaveTextContent('/library?filter=missing#results');
        });
    });

    it('shows an inline error message when authentication fails', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockRejectedValue(new ApiError(401, 'Unauthorized'));

        renderLoginPage();

        await user.type(screen.getByLabelText(/username/i), 'admin');
        await user.type(screen.getByLabelText(/password/i), 'wrong-password');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        const errorEl = await screen.findByTestId('login-error');
        expect(errorEl).toHaveTextContent(/invalid username or password/i);
    });

    it('keeps the user on the login page when authentication fails', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockRejectedValue(new ApiError(401, 'Unauthorized'));

        renderLoginPage();

        await user.type(screen.getByLabelText(/username/i), 'admin');
        await user.type(screen.getByLabelText(/password/i), 'wrong');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        await screen.findByTestId('login-error');

        expect(screen.queryByText('Home page')).not.toBeInTheDocument();
        expect(screen.queryByText('Library page')).not.toBeInTheDocument();
    });

    it('shows a generic error for non-auth API failures', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockRejectedValue(new Error('Network down'));

        renderLoginPage();

        await user.type(screen.getByLabelText(/username/i), 'admin');
        await user.type(screen.getByLabelText(/password/i), 'secret123');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        const errorEl = await screen.findByTestId('login-error');
        expect(errorEl).toHaveTextContent('Network down');
    });

    it('does not call login when fields are empty', async () => {
        const user = userEvent.setup();
        renderLoginPage();

        await user.click(screen.getByRole('button', { name: /sign in/i }));

        expect(vi.mocked(authApiModule.login)).not.toHaveBeenCalled();
    });

    it('redirects authenticated users to the home page', () => {
        render(
            <MemoryRouter initialEntries={['/login']}>
                <AuthProvider initialUser={{ id: 'user-1', username: 'admin', role: Role.Admin }} skipSessionInit>
                    <Routes>
                        <Route path="/login" element={<LoginPage />} />
                        <Route path="/" element={<div>Home page</div>} />
                    </Routes>
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.getByText('Home page')).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Magpie' })).not.toBeInTheDocument();
    });

    it('renders a loading state while the auth session is initializing', () => {
        render(
            <MemoryRouter initialEntries={['/login']}>
                <AuthProvider skipSessionInit initialLoading>
                    <Routes>
                        <Route path="/login" element={<LoginPage />} />
                        <Route path="/" element={<div>Home page</div>} />
                    </Routes>
                </AuthProvider>
            </MemoryRouter>,
        );

        expect(screen.getByRole('status', { name: /loading page/i })).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Magpie' })).not.toBeInTheDocument();
    });

    it('associates the error message with both inputs via aria-describedby', async () => {
        const user = userEvent.setup();
        vi.mocked(authApiModule.login).mockRejectedValue(new ApiError(401, 'Unauthorized'));

        renderLoginPage();

        const usernameInput = screen.getByLabelText(/username/i);
        const passwordInput = screen.getByLabelText(/password/i);

        await user.type(usernameInput, 'admin');
        await user.type(passwordInput, 'wrong');
        await user.click(screen.getByRole('button', { name: /sign in/i }));

        const errorEl = await screen.findByTestId('login-error');
        expect(errorEl).toHaveAttribute('id', 'login-error');
        expect(usernameInput).toHaveAttribute('aria-describedby', 'login-error');
        expect(usernameInput).toHaveAttribute('aria-invalid', 'true');
        expect(passwordInput).toHaveAttribute('aria-describedby', 'login-error');
        expect(passwordInput).toHaveAttribute('aria-invalid', 'true');
    });
});
