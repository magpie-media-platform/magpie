import { render, screen, within, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter, useRoutes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

import { AuthProvider, Role, RequireRole, type User } from '@/auth';
import { testEffectivePermissions } from './permissions';
import { routes } from './stubRoutes';
import { AppShell, AppShellSidebar } from '@/components/ui/AppShell';
import { TooltipProvider } from '@radix-ui/react-tooltip';
import { ToastProvider, ToastStateProvider, ToastList } from '@/components/ui/Toast';

import { Sidebar, SidebarContent, SidebarGroup, SidebarItem, SidebarSeparator } from '@/components/ui/Sidebar';
import { BottomNav, BottomNavItem } from '@/components/ui/BottomNav';
import { HouseIcon, MagnifyingGlassIcon, DownloadSimpleIcon, UsersIcon, PuzzlePieceIcon } from '@phosphor-icons/react';

function createUser(role: Role, username = 'testuser'): User {
    return {
        id: '1',
        username,
        role,
    };
}

function TestSidebar() {
    return (
        <AppShell>
            <AppShellSidebar>
                <Sidebar collapsed={false}>
                    <SidebarContent>
                        <SidebarGroup>
                            <SidebarItem to="/" icon={<HouseIcon weight="duotone" />}>
                                Dashboard
                            </SidebarItem>
                            <SidebarItem to="/library" icon={<MagnifyingGlassIcon weight="duotone" />}>
                                Library
                            </SidebarItem>
                            <SidebarItem to="/downloads" icon={<DownloadSimpleIcon weight="duotone" />}>
                                Downloads
                            </SidebarItem>
                        </SidebarGroup>
                        <RequireRole min={Role.Admin}>
                            <SidebarSeparator />
                            <SidebarGroup label="Admin">
                                <SidebarItem to="/admin/users" icon={<UsersIcon weight="duotone" />}>
                                    Users
                                </SidebarItem>
                                <SidebarItem to="/admin/extensions" icon={<PuzzlePieceIcon weight="duotone" />}>
                                    Extensions
                                </SidebarItem>
                            </SidebarGroup>
                        </RequireRole>
                    </SidebarContent>
                </Sidebar>
            </AppShellSidebar>
        </AppShell>
    );
}

function TestBottomNav() {
    return (
        <BottomNav>
            <BottomNavItem to="/" icon={<HouseIcon weight="duotone" />} label="Dashboard" />
            <BottomNavItem to="/library" icon={<MagnifyingGlassIcon weight="duotone" />} label="Library" />
            <BottomNavItem to="/downloads" icon={<DownloadSimpleIcon weight="duotone" />} label="Downloads" />
        </BottomNav>
    );
}

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
                staleTime: 0,
            },
        },
    });
}

function renderWithRole(ui: React.ReactNode, role: Role, { route = '/' }: { route?: string } = {}) {
    const user = createUser(role);
    const queryClient = createTestQueryClient();
    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[route]}>
                <AuthProvider initialUser={user} initialPermissions={testEffectivePermissions(role)}>
                    {ui}
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

function renderRoutesWithRole(role: Role, initialRoute: string) {
    const user = createUser(role);
    const queryClient = createTestQueryClient();

    function AppRoutes() {
        return useRoutes(routes);
    }

    return render(
        <QueryClientProvider client={queryClient}>
            <TooltipProvider>
                <MemoryRouter initialEntries={[initialRoute]}>
                    <AuthProvider initialUser={user} initialPermissions={testEffectivePermissions(role)}>
                        <ToastProvider>
                            <ToastStateProvider>
                                <AppRoutes />
                                <ToastList />
                            </ToastStateProvider>
                        </ToastProvider>
                    </AuthProvider>
                </MemoryRouter>
            </TooltipProvider>
        </QueryClientProvider>,
    );
}

describe('Role-based navigation visibility', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);
    });

    describe('Sidebar navigation', () => {
        it('Viewer sees Dashboard, Library, Downloads but not Admin', () => {
            renderWithRole(<TestSidebar />, Role.Viewer);

            const nav = screen.getByRole('navigation', { name: 'Main navigation' });

            expect(within(nav).getByText('Dashboard')).toBeInTheDocument();
            expect(within(nav).getByText('Library')).toBeInTheDocument();
            expect(within(nav).getByText('Downloads')).toBeInTheDocument();

            expect(within(nav).queryByText('Users')).not.toBeInTheDocument();
            expect(within(nav).queryByText('Extensions')).not.toBeInTheDocument();
        });

        it('Requester sees Dashboard, Library, Downloads but not Admin', () => {
            renderWithRole(<TestSidebar />, Role.Requester);

            const nav = screen.getByRole('navigation', { name: 'Main navigation' });

            expect(within(nav).getByText('Dashboard')).toBeInTheDocument();
            expect(within(nav).getByText('Library')).toBeInTheDocument();
            expect(within(nav).getByText('Downloads')).toBeInTheDocument();

            expect(within(nav).queryByText('Users')).not.toBeInTheDocument();
            expect(within(nav).queryByText('Extensions')).not.toBeInTheDocument();
        });

        it('Manager sees Dashboard, Library, Downloads but not Admin', () => {
            renderWithRole(<TestSidebar />, Role.Manager);

            const nav = screen.getByRole('navigation', { name: 'Main navigation' });

            expect(within(nav).getByText('Dashboard')).toBeInTheDocument();
            expect(within(nav).getByText('Library')).toBeInTheDocument();
            expect(within(nav).getByText('Downloads')).toBeInTheDocument();

            expect(within(nav).queryByText('Users')).not.toBeInTheDocument();
            expect(within(nav).queryByText('Extensions')).not.toBeInTheDocument();
        });

        it('Admin sees all navigation items including Admin section', () => {
            renderWithRole(<TestSidebar />, Role.Admin);

            const nav = screen.getByRole('navigation', { name: 'Main navigation' });

            expect(within(nav).getByText('Dashboard')).toBeInTheDocument();
            expect(within(nav).getByText('Library')).toBeInTheDocument();
            expect(within(nav).getByText('Downloads')).toBeInTheDocument();
            expect(within(nav).getByText('Users')).toBeInTheDocument();
            expect(within(nav).getByText('Extensions')).toBeInTheDocument();
        });
    });

    describe('Bottom navigation', () => {
        it('shows navigation items on mobile', () => {
            renderWithRole(<TestBottomNav />, Role.Viewer);

            const nav = screen.getByRole('navigation', { name: 'Primary navigation' });

            expect(within(nav).getByText('Dashboard')).toBeInTheDocument();
            expect(within(nav).getByText('Library')).toBeInTheDocument();
            expect(within(nav).getByText('Downloads')).toBeInTheDocument();
        });
    });
});

describe('Route guards by role', () => {
    describe('Viewer access', () => {
        it('can access dashboard', async () => {
            renderRoutesWithRole(Role.Viewer, '/');
            expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
        });

        it('can access library', async () => {
            renderRoutesWithRole(Role.Viewer, '/library');
            expect(await screen.findByRole('heading', { name: 'Library' })).toBeInTheDocument();
        });

        it('can access downloads', async () => {
            renderRoutesWithRole(Role.Viewer, '/downloads');
            expect(await screen.findByRole('heading', { name: 'Downloads' })).toBeInTheDocument();
        });

        it('can access subjects', async () => {
            renderRoutesWithRole(Role.Viewer, '/library/subjects/all');
            expect(await screen.findByRole('heading', { name: 'Subjects' })).toBeInTheDocument();
        });

        it('can access wishlists', async () => {
            renderRoutesWithRole(Role.Viewer, '/wishlists');
            await waitFor(() => expect(document.querySelector('[data-page="wishlists"]')).toBeInTheDocument());
        });

        it('can access calendar', async () => {
            renderRoutesWithRole(Role.Viewer, '/calendar');
            expect(await screen.findByRole('heading', { name: 'Calendar' })).toBeInTheDocument();
        });

        it('can access filters', async () => {
            renderRoutesWithRole(Role.Viewer, '/filters');
            await waitFor(() => expect(document.querySelector('[data-page="filters"]')).toBeInTheDocument());
        });

        it('can access about', async () => {
            renderRoutesWithRole(Role.Viewer, '/about');
            await waitFor(() => expect(document.querySelector('[data-page="about"]')).toBeInTheDocument());
        });

        it('cannot access admin users - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/users');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin extensions - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/extensions');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin database - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/database');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin indexers - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/indexers');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin download clients - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/download-clients');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin media servers - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/media-servers');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin root folders - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/root-folders');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin jobs - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/jobs');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin quality tiers - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/quality-tiers');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin system - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/system');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin general - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Viewer, '/admin/general');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });
    });

    describe('Requester access', () => {
        it('can access dashboard', async () => {
            renderRoutesWithRole(Role.Requester, '/');
            expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
        });

        it('can access library', async () => {
            renderRoutesWithRole(Role.Requester, '/library');
            expect(await screen.findByRole('heading', { name: 'Library' })).toBeInTheDocument();
        });

        it('can access downloads', async () => {
            renderRoutesWithRole(Role.Requester, '/downloads');
            expect(await screen.findByRole('heading', { name: 'Downloads' })).toBeInTheDocument();
        });

        it('can access wishlists', async () => {
            renderRoutesWithRole(Role.Requester, '/wishlists');
            await waitFor(() => expect(document.querySelector('[data-page="wishlists"]')).toBeInTheDocument());
        });

        it('can access filters', async () => {
            renderRoutesWithRole(Role.Requester, '/filters');
            await waitFor(() => expect(document.querySelector('[data-page="filters"]')).toBeInTheDocument());
        });

        it('cannot access admin pages', async () => {
            renderRoutesWithRole(Role.Requester, '/admin/users');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
        });
    });

    describe('Manager access', () => {
        it('can access dashboard', async () => {
            renderRoutesWithRole(Role.Manager, '/');
            expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
        });

        it('can access library', async () => {
            renderRoutesWithRole(Role.Manager, '/library');
            expect(await screen.findByRole('heading', { name: 'Library' })).toBeInTheDocument();
        });

        it('can access downloads', async () => {
            renderRoutesWithRole(Role.Manager, '/downloads');
            expect(await screen.findByRole('heading', { name: 'Downloads' })).toBeInTheDocument();
        });

        it('cannot access admin users - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/users');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin extensions - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/extensions');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin database - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/database');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin indexers - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/indexers');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin download clients - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/download-clients');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin media servers - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/media-servers');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin root folders - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/root-folders');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin jobs - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/jobs');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin quality tiers - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/quality-tiers');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin system - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/system');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });

        it('cannot access admin general - redirected to forbidden', async () => {
            renderRoutesWithRole(Role.Manager, '/admin/general');
            expect(await screen.findByText('Access denied')).toBeInTheDocument();
            expect(await screen.findByText(/requires Admin role/i)).toBeInTheDocument();
        });
    });

    describe('Admin access', () => {
        it('can access dashboard', async () => {
            renderRoutesWithRole(Role.Admin, '/');
            expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
        });

        it('can access library', async () => {
            renderRoutesWithRole(Role.Admin, '/library');
            expect(await screen.findByRole('heading', { name: 'Library' })).toBeInTheDocument();
        });

        it('can access downloads', async () => {
            renderRoutesWithRole(Role.Admin, '/downloads');
            expect(await screen.findByRole('heading', { name: 'Downloads' })).toBeInTheDocument();
        });

        it('can access admin users', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/users');
            expect(await screen.findByRole('heading', { name: 'Users' })).toBeInTheDocument();
        });

        it('can access admin extensions', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/extensions');
            expect(await screen.findByRole('heading', { name: 'Extensions' })).toBeInTheDocument();
        });

        it('can access admin database', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/database');
            expect(await screen.findByRole('heading', { name: 'Database' })).toBeInTheDocument();
        });

        it('can access admin indexers', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/indexers');
            expect(await screen.findByRole('heading', { name: 'Indexers' })).toBeInTheDocument();
        });

        it('can access admin download clients', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/download-clients');
            expect(await screen.findByRole('heading', { name: 'Download Clients' })).toBeInTheDocument();
        });

        it('can access admin media servers', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/media-servers');
            expect(await screen.findByRole('heading', { name: 'Media Servers' })).toBeInTheDocument();
        });

        it('can access admin root folders', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/root-folders');
            expect(await screen.findByRole('heading', { name: 'Root Folders' })).toBeInTheDocument();
        });

        it('can access admin jobs', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/jobs');
            expect(await screen.findByRole('heading', { name: 'System Jobs' })).toBeInTheDocument();
        });

        it('can access admin quality tiers', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/quality-tiers');
            await waitFor(() => expect(document.querySelector('[data-page="quality-tiers"]')).toBeInTheDocument());
        });

        it('can access admin system', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/system');
            expect(await screen.findByRole('heading', { name: 'System Status' })).toBeInTheDocument();
        });

        it('can access admin general', async () => {
            renderRoutesWithRole(Role.Admin, '/admin/general');
            expect(await screen.findByRole('heading', { name: 'General' })).toBeInTheDocument();
        });
    });
});

describe('Forbidden page context', () => {
    it('shows user role in forbidden message for Viewer', async () => {
        renderRoutesWithRole(Role.Viewer, '/admin/users');

        expect(await screen.findByText(/Signed in as testuser \(Viewer\)/i)).toBeInTheDocument();
    });

    it('shows user role in forbidden message for Requester', async () => {
        renderRoutesWithRole(Role.Requester, '/admin/users');

        expect(await screen.findByText(/Signed in as testuser \(Requester\)/i)).toBeInTheDocument();
    });

    it('shows user role in forbidden message for Manager accessing Admin', async () => {
        renderRoutesWithRole(Role.Manager, '/admin/users');

        expect(await screen.findByText(/Signed in as testuser \(Manager\)/i)).toBeInTheDocument();
    });

    it('shows signed in user info', async () => {
        renderRoutesWithRole(Role.Viewer, '/admin/users');

        expect(await screen.findByText(/Signed in as testuser/)).toBeInTheDocument();
    });
});

describe('Nested library routes', () => {
    it('Viewer can access library content details', async () => {
        renderRoutesWithRole(Role.Viewer, '/library/123');
        await waitFor(() => expect(document.querySelector('[data-page="content-detail"]')).toBeInTheDocument());
    });

    it('Viewer can access library child details', async () => {
        renderRoutesWithRole(Role.Viewer, '/library/123/456');
        await waitFor(() => expect(document.querySelector('[data-page="content-detail"]')).toBeInTheDocument());
    });

    it('Viewer can access library grandchild details', async () => {
        renderRoutesWithRole(Role.Viewer, '/library/123/456/789');
        await waitFor(() => expect(document.querySelector('[data-page="content-detail"]')).toBeInTheDocument());
    });
});

describe('Unauthenticated access', () => {
    it('redirects to login when not authenticated', async () => {
        const queryClient = createTestQueryClient();
        function AppRoutes() {
            return useRoutes(routes);
        }

        render(
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <MemoryRouter initialEntries={['/']}>
                        <AuthProvider>
                            <ToastProvider>
                                <ToastStateProvider>
                                    <AppRoutes />
                                    <ToastList />
                                </ToastStateProvider>
                            </ToastProvider>
                        </AuthProvider>
                    </MemoryRouter>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    });

    it('can access login page without authentication', async () => {
        const queryClient = createTestQueryClient();
        function AppRoutes() {
            return useRoutes(routes);
        }

        render(
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <MemoryRouter initialEntries={['/login']}>
                        <AuthProvider>
                            <ToastProvider>
                                <ToastStateProvider>
                                    <AppRoutes />
                                    <ToastList />
                                </ToastStateProvider>
                            </ToastProvider>
                        </AuthProvider>
                    </MemoryRouter>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    });

    it('can access forbidden page without authentication', async () => {
        const queryClient = createTestQueryClient();
        function AppRoutes() {
            return useRoutes(routes);
        }

        render(
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <MemoryRouter initialEntries={['/forbidden']}>
                        <AuthProvider>
                            <ToastProvider>
                                <ToastStateProvider>
                                    <AppRoutes />
                                    <ToastList />
                                </ToastStateProvider>
                            </ToastProvider>
                        </AuthProvider>
                    </MemoryRouter>
                </TooltipProvider>
            </QueryClientProvider>,
        );

        expect(await screen.findByText('Access denied')).toBeInTheDocument();
    });
});

describe('404 handling', () => {
    it('shows 404 for unknown routes', async () => {
        renderRoutesWithRole(Role.Admin, '/unknown-page');

        expect(await screen.findByText('Page not found')).toBeInTheDocument();
    });
});

describe('Role coherence', () => {
    it('Viewer sidebar has no empty groups', () => {
        renderWithRole(<TestSidebar />, Role.Viewer);

        const nav = screen.getByRole('navigation', { name: 'Main navigation' });

        const items = within(nav).getAllByRole('link');
        expect(items.length).toBeGreaterThanOrEqual(3);
    });

    it('Requester sidebar has no empty groups', () => {
        renderWithRole(<TestSidebar />, Role.Requester);

        const nav = screen.getByRole('navigation', { name: 'Main navigation' });

        const items = within(nav).getAllByRole('link');
        expect(items.length).toBeGreaterThanOrEqual(3);
    });

    it('Manager sidebar shows standard items without Admin section', () => {
        renderWithRole(<TestSidebar />, Role.Manager);

        const nav = screen.getByRole('navigation', { name: 'Main navigation' });

        const items = within(nav).getAllByRole('link');

        expect(items.length).toBe(3);
    });

    it('Admin sidebar shows all sections', () => {
        renderWithRole(<TestSidebar />, Role.Admin);

        const nav = screen.getByRole('navigation', { name: 'Main navigation' });

        const items = within(nav).getAllByRole('link');

        expect(items.length).toBe(5);
    });
});
