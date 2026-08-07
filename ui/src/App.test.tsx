import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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

const testRoute = (name: string) => () => <div data-testid="app-test-route">{name}</div>;

vi.mock('@/pages/DashboardPage/DashboardPage', () => ({ default: testRoute('Dashboard') }));
vi.mock('@/pages/LibraryAddPage/LibraryAddPage', () => ({ default: testRoute('Library add') }));
vi.mock('@/pages/LibraryImportPage/LibraryImportPage', () => ({ default: testRoute('Library import') }));
vi.mock('@/pages/LibraryPage/LibraryPage', () => ({ default: testRoute('Library') }));
vi.mock('@/pages/ContentDetailPage/ContentDetailPage', () => ({ default: testRoute('Content detail') }));
vi.mock('@/pages/SubjectDetailPage/SubjectDetailPage', () => ({ default: testRoute('Subject detail') }));
vi.mock('@/pages/NotFoundPage/NotFoundPage', () => ({ default: testRoute('Not found') }));
vi.mock('@/pages/SetupPage/SetupPage', () => ({ default: testRoute('Setup') }));
vi.mock('@/pages/ForbiddenPage/ForbiddenPage', () => ({
    default: () => (
        <div data-testid="app-test-route">
            <h1>Access denied</h1>
        </div>
    ),
}));
vi.mock('@/pages/LoginPage/LoginPage', () => ({
    default: () => (
        <div data-testid="app-test-route">
            <button type="button">Sign in</button>
        </div>
    ),
}));

vi.mock('@/lib/setupApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/setupApi')>();
    return {
        ...actual,
        fetchSetupStatus: vi.fn(async () => ({ isRequired: false, isComplete: true, requiresAdminClaim: false })),
        createSetupAdmin: vi.fn(),
    };
});

import App from './App';
import { tokenStorage } from '@/lib/api';
import { clearAuthStorage, tokenExpirationStorage } from '@/lib/authApi';
import { queryClient } from '@/lib/queryClient';
import * as setupApiModule from '@/lib/setupApi';

function fakeJwt(claims: Record<string, string | number>): string {
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
    tokenExpirationStorage.set(new Date(Date.now() + 300_000));
}

function createMatchMedia(width: number) {
    return vi.fn((query: string) => {
        const matches =
            query === '(min-width: 768px)'
                ? width >= 768
                : query === '(max-width: 767px)'
                  ? width <= 767
                  : query === '(min-width: 768px) and (max-width: 1023px)'
                    ? width >= 768 && width <= 1023
                    : false;

        return {
            matches,
            media: query,
            onchange: null,
            addListener: () => {},
            removeListener: () => {},
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => false,
        };
    });
}

beforeEach(() => {
    seedAuthenticatedSession();
    vi.stubGlobal(
        'fetch',
        vi.fn(async (input: RequestInfo | URL) => {
            const url = String(input);
            if (url.includes('/ui/manifest')) {
                return new Response(JSON.stringify({ assets: [], extensions: [] }), {
                    status: 200,
                    headers: { 'content-type': 'application/json' },
                });
            }
            if (url.endsWith('/system')) {
                return new Response(JSON.stringify({ version: 'test' }), {
                    status: 200,
                    headers: { 'content-type': 'application/json' },
                });
            }
            return new Response('{}', { status: 404, headers: { 'content-type': 'application/json' } });
        }),
    );
    queryClient.clear();
    window.history.replaceState({}, '', '/');
    vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
        isRequired: false,
        isComplete: true,
        requiresAdminClaim: false,
    });
});

afterEach(async () => {
    await screen.findByTestId('app-test-route');
    vi.unstubAllGlobals();
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

    it('renders the forbidden route outside the authenticated shell when signed out', async () => {
        clearAuthStorage();
        window.history.replaceState({}, '', '/forbidden');

        render(<App />);

        expect(await screen.findByText('Access denied')).toBeInTheDocument();
        expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
        expect(screen.queryByRole('main')).not.toBeInTheDocument();
    });

    it('renders navigation items in sidebar', async () => {
        render(<App />);

        await waitFor(() => {
            expect(screen.getAllByText('Library').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Add content').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Downloads').length).toBeGreaterThanOrEqual(1);

            expect(screen.getAllByText('Filters').length).toBeGreaterThanOrEqual(1);
            expect(screen.getAllByText('Wishlists').length).toBeGreaterThanOrEqual(1);
        });

        const addContentLink = screen.getByRole('link', { name: 'Add content' });
        expect(addContentLink).toHaveAttribute('href', '/library/add');
    });

    it('defaults the Library navigation group to expanded and keeps it manually collapsible', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(1440));
        const user = userEvent.setup();
        render(<App />);

        const libraryMenu = await screen.findByRole('button', { name: 'Library' });
        expect(libraryMenu).toHaveAttribute('aria-expanded', 'true');
        expect(screen.getByRole('link', { name: 'Overview' })).toBeVisible();

        await user.click(libraryMenu);
        expect(libraryMenu).toHaveAttribute('aria-expanded', 'false');

        await user.click(libraryMenu);
        expect(libraryMenu).toHaveAttribute('aria-expanded', 'true');
        expect(screen.getByRole('link', { name: 'Overview' })).toBeVisible();
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

    it('announces the mobile sidebar toggle as closed after closing it', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        window.history.replaceState({}, '', '/library/add');

        const user = userEvent.setup();
        render(<App />);

        const closedToggle = await screen.findByRole('button', { name: 'Expand sidebar' });
        expect(closedToggle).toHaveAttribute('aria-expanded', 'false');

        await user.click(closedToggle);

        const sidebarDialog = screen.getByRole('dialog', { name: 'Main navigation' });
        const openToggle = within(sidebarDialog).getByRole('button', { name: 'Collapse sidebar' });
        expect(openToggle).toHaveTextContent('Collapse sidebar');
        expect(document.querySelector('[data-component="app-shell-header"]')).toHaveProperty('inert', true);

        await user.click(openToggle);

        expect(screen.getByRole('button', { name: 'Expand sidebar' })).toHaveAttribute('aria-expanded', 'false');
        expect(screen.getByRole('navigation', { name: 'Main navigation' })).toHaveAttribute('data-open', 'false');
    });

    it('treats the open mobile sidebar as a modal and closes it with Escape', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();
        render(<App />);

        const toggle = await screen.findByRole('button', { name: 'Expand sidebar' });
        await user.click(toggle);

        expect(screen.getByRole('dialog', { name: 'Main navigation' })).toHaveAttribute('aria-modal', 'true');
        expect(screen.getByRole('main')).toHaveProperty('inert', true);
        expect(document.body.style.overflow).toBe('hidden');
        expect(document.querySelector('.app-shell__sidebar-backdrop')).toHaveAttribute('data-visible', 'true');

        await user.keyboard('{Escape}');

        expect(screen.getByRole('navigation', { name: 'Main navigation' })).toHaveAttribute('data-open', 'false');
        expect(screen.getByRole('main')).toHaveProperty('inert', false);
        expect(screen.getByRole('button', { name: 'Expand sidebar' })).toHaveFocus();
    });

    it('marks only Library import current on the import route', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(1440));
        window.history.replaceState({}, '', '/library/import');
        render(<App />);

        const importLink = await screen.findByRole('link', { name: 'Library import' });
        const libraryMenu = screen.getByRole('button', { name: 'Library' });
        const overviewLink = screen.getByRole('link', { name: 'Overview' });

        expect(importLink).toHaveAttribute('aria-current', 'page');
        expect(overviewLink).not.toHaveAttribute('aria-current');
        expect(libraryMenu).not.toHaveAttribute('data-active');
    });

    it.each([
        ['/library/all', 'the all-content list'],
        ['/library/content-1', 'a content detail'],
        ['/library/content-1/child-1', 'a nested child detail'],
        ['/library/content-1/child-1/grandchild-1', 'a nested grandchild detail'],
        ['/subjects/subject-1', 'a subject detail'],
    ])('keeps Library active for %s (%s)', async (pathname) => {
        vi.stubGlobal('matchMedia', createMatchMedia(1440));
        window.history.replaceState({}, '', pathname);
        render(<App />);

        expect(await screen.findByRole('button', { name: 'Library' })).toHaveAttribute('data-active', 'true');
    });

    it.each(['/library/add', '/library/import'])('does not mark Library active for peer route %s', async (pathname) => {
        vi.stubGlobal('matchMedia', createMatchMedia(1440));
        window.history.replaceState({}, '', pathname);
        render(<App />);

        expect(await screen.findByRole('button', { name: 'Library' })).not.toHaveAttribute('data-active');
    });

    it.each(['/library/additional', '/library/imported'])('uses route-segment boundaries for %s', async (pathname) => {
        vi.stubGlobal('matchMedia', createMatchMedia(1440));
        window.history.replaceState({}, '', pathname);
        render(<App />);

        expect(await screen.findByRole('button', { name: 'Library' })).toHaveAttribute('data-active', 'true');
        expect(screen.getByRole('link', { name: 'Add content' })).not.toHaveAttribute('data-active');
        expect(screen.getByRole('link', { name: 'Library import' })).not.toHaveAttribute('data-active');
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
            requiresAdminClaim: true,
        });
        window.history.replaceState({}, '', '/');

        render(<App />);

        await waitFor(() => {
            expect(window.location.pathname).toBe('/setup');
        });
    });

    it('does not redirect when already on /setup (no infinite loop)', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: true,
        });
        window.history.replaceState({}, '', '/setup');

        render(<App />);

        await waitFor(() => {
            expect(window.location.pathname).toBe('/setup');
        });
    });

    it('allows login when setup is incomplete but the administrator was already claimed', async () => {
        clearAuthStorage();
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: true,
            isComplete: false,
            requiresAdminClaim: false,
        });
        window.history.replaceState({}, '', '/login');

        render(<App />);

        expect(await screen.findByRole('button', { name: /sign in/i })).toBeInTheDocument();
        expect(window.location.pathname).toBe('/login');
    });

    it('does not redirect from / when setup is not required', async () => {
        vi.mocked(setupApiModule.fetchSetupStatus).mockResolvedValue({
            isRequired: false,
            isComplete: true,
            requiresAdminClaim: false,
        });
        window.history.replaceState({}, '', '/');

        render(<App />);

        await waitFor(() => {
            expect(window.location.pathname).toBe('/');
        });
    });
});
