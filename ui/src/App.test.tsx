import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => ({
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'admin',
        role: 'Admin',
    }),
    useSlotExtensions: () => ({
        extensions: [],
        isReady: true,
        isLoading: false,
    }),
}));

vi.mock('@/lib/setupApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/setupApi')>();
    return {
        ...actual,
        fetchSetupStatus: vi.fn(async () => ({ isRequired: false, isComplete: true })),
        createSetupAdmin: vi.fn(),
    };
});

import App from './App';
import { tokenStorage } from '@/lib/api';
import { clearAuthStorage, refreshTokenStorage, tokenExpirationStorage } from '@/lib/authApi';
import { queryClient } from '@/lib/queryClient';
import * as setupApiModule from '@/lib/setupApi';

function fakeJwt(claims: Record<string, string | number>): string {
    // Default to a far-future `exp` so callers don't have to specify one. decodeJwtUser
    // strictly requires a numeric `exp` claim.
    const claimsWithExp: Record<string, string | number> = {
        exp: Math.floor(Date.now() / 1000) + 3600,
        ...claims,
    };
    const header = btoa(JSON.stringify({ alg: 'none' }));
    const payload = btoa(JSON.stringify(claimsWithExp));
    return `${header}.${payload}.sig`;
}

function seedAuthenticatedSession() {
    const jwt = fakeJwt({
        sub: 'user-1',
        name: 'admin',
        role: 3,
    });
    tokenStorage.set(jwt);
    refreshTokenStorage.set('test-refresh-token');
    tokenExpirationStorage.set(new Date(Date.now() + 300_000));
}

beforeEach(() => {
    seedAuthenticatedSession();
    queryClient.clear();
    window.history.replaceState({}, '', '/');
    vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
        isRequired: false,
        isComplete: true,
    });
});

afterEach(() => {
    clearAuthStorage();
    queryClient.clear();
    window.history.replaceState({}, '', '/');
});

describe('App', () => {
    it('renders the header with brand', async () => {
        render(<App />);

        await waitFor(() => {
            expect(screen.getAllByText('Magpie').length).toBeGreaterThanOrEqual(1);
        });
    });

    it('renders the sidebar navigation', async () => {
        render(<App />);
        expect(await screen.findByRole('navigation', { name: 'Main navigation' })).toBeInTheDocument();
    });

    it('renders the main content area', async () => {
        render(<App />);
        expect(await screen.findByRole('main')).toBeInTheDocument();
    });

    it('renders the Dashboard heading on root route', async () => {
        render(<App />);
        await waitFor(
            () => {
                expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
            },
            { timeout: 3000 },
        );
    });

    it('renders navigation items in sidebar', async () => {
        render(<App />);

        await waitFor(() => {
            expect(screen.getAllByText('Library').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Downloads').length).toBeGreaterThanOrEqual(1);

            expect(screen.getAllByText('Saved Filters').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Wishlists').length).toBeGreaterThanOrEqual(1);
        });
    });

    it('renders admin link for Admin users', async () => {
        render(<App />);

        await waitFor(() => {
            expect(screen.getAllByText('Admin').length).toBeGreaterThanOrEqual(1);
        });
    });

    it('uses router Links for navigation', async () => {
        render(<App />);

        await waitFor(() => {
            const brandLinks = screen.getAllByRole('link', { name: /magpie/i });
            expect(brandLinks.length).toBeGreaterThanOrEqual(1);
            expect(brandLinks[0]).toHaveAttribute('href', '/');
        });
    });
});

describe('App routing', () => {
    it('renders Dashboard page on root route', async () => {
        render(<App />);
        await waitFor(
            () => {
                expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
            },
            { timeout: 3000 },
        );
    });
});

describe('App with different roles', () => {
    it('shows the Admin section when signed in as Admin', async () => {
        render(<App />);

        await waitFor(() => {
            expect(screen.getAllByText('Admin').length).toBeGreaterThanOrEqual(1);
        });
    });
});

describe('SetupRedirectGate', () => {
    it('redirects to /setup when setup is required and the user is not on /setup', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
        });
        window.history.replaceState({}, '', '/');

        render(<App />);

        await waitFor(() => {
            expect(window.location.pathname).toBe('/setup');
        });
        expect(await screen.findByRole('heading', { name: /welcome to magpie/i })).toBeInTheDocument();
    });

    it('does not redirect when already on /setup (no infinite loop)', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
        });
        window.history.replaceState({}, '', '/setup');

        render(<App />);

        expect(await screen.findByRole('heading', { name: /welcome to magpie/i })).toBeInTheDocument();
        expect(window.location.pathname).toBe('/setup');
    });

    it('does not redirect from / when setup is not required', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: false,
            isComplete: true,
        });
        window.history.replaceState({}, '', '/');

        render(<App />);

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
        });
        expect(window.location.pathname).toBe('/');
    });
});
