import { Suspense, memo, useState, type ReactNode } from 'react';
import { BrowserRouter, Navigate, useRoutes, useLocation, Link } from 'react-router-dom';
import {
    CalendarBlank,
    ClockCountdown,
    House,
    Books,
    DownloadSimple,
    Gear,
    Star,
    Funnel,
    FilmStrip,
    Cube,
    Database,
    Users,
} from '@phosphor-icons/react';
import { QueryClientProvider } from '@tanstack/react-query';

import {
    AppShell,
    AppShellHeader,
    AppShellSidebar,
    AppShellMain,
    AppShellBottomNav,
    useAppShell,
} from './components/ui/AppShell';
import { AdminHeader, AdminSidebar } from './components/ui/AdminShell';
import { Header } from './components/ui/Header';
import { UserMenu } from './components/ui/UserMenu';
import {
    Sidebar,
    SidebarContent,
    SidebarFooter,
    SidebarGroup,
    SidebarItem,
    SidebarSubMenu,
    SidebarSeparator,
} from './components/ui/Sidebar';
import { useLibraryTypes, type LibraryTypeDefinition } from './hooks/useLibraryTypes';
import { useScrollRestoration } from './hooks/useScrollRestoration';
import { BottomNav, BottomNavItem } from './components/ui/BottomNav';
import { HeaderSlot, HeaderLeftSlot, HeaderRightSlot, NavMainSlot } from './components/ui/Slot/GlobalSlots';
import { ToastProvider, ToastViewport, ToastStateProvider, ToastList } from './components/ui/Toast';
import { TooltipProvider } from './components/ui/Tooltip';
import { LiveAnnouncerProvider } from './components/ui/LiveAnnouncer';
import { RestartRequiredBanner, RestartRequiredProvider } from './components/ui/RestartRequiredBanner';
import { AuthProvider, RequireRole, Role, RoleName, useAuth } from './auth';
import { useSetupStatus } from './hooks/useSetup';
import { routes, RouteLoadingState, RouteErrorState } from './router';
import { CommandPalette } from './components/ui/CommandPalette';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { SkipLink } from './components/ui/SkipLink';
import { ExtensionProvider } from './extensions';
import { queryClient } from '@/lib/queryClient';
import { useIsActive } from '@/lib/routes';
import { Logo } from './components/ui/Logo';
import './App.css';

function AppHeader() {
    const { toggleSidebar, sidebarOpen, sidebarCollapsed } = useAppShell();

    return (
        <HeaderSlot>
            <Header
                brand={
                    <Link to="/">
                        <Logo size={26} variant="solid" />
                        <span>Magpie</span>
                    </Link>
                }
                onSidebarToggle={toggleSidebar}
                sidebarOpen={sidebarOpen || !sidebarCollapsed}
                leftSlot={<HeaderLeftSlot />}
                rightSlot={<HeaderRightSlot />}
                actions={<UserMenu />}
            />
        </HeaderSlot>
    );
}

interface AppSidebarProps {
    contentTypes: LibraryTypeDefinition[];
    subjectTypes: LibraryTypeDefinition[];
}

const AppSidebar = memo(function AppSidebar({ contentTypes, subjectTypes }: AppSidebarProps) {
    const { sidebarOpen, sidebarCollapsed, toggleSidebarCollapsed, closeSidebar } = useAppShell();
    const location = useLocation();

    const isActive = useIsActive();

    const isLibraryActive = isActive('/library') || isActive('/subjects');

    return (
        <Sidebar open={sidebarOpen} collapsed={sidebarCollapsed} onCollapsedChange={toggleSidebarCollapsed}>
            <SidebarContent>
                <NavMainSlot>
                    <SidebarGroup>
                        <SidebarItem
                            to="/"
                            icon={<House weight="duotone" />}
                            active={isActive('/')}
                            onClick={closeSidebar}
                        >
                            Dashboard
                        </SidebarItem>
                        <SidebarSubMenu label="Library" icon={<Books weight="duotone" />} active={isLibraryActive}>
                            <SidebarItem to="/library" active={location.pathname === '/library'} onClick={closeSidebar}>
                                Overview
                            </SidebarItem>
                            {contentTypes.map((type) => (
                                <SidebarItem
                                    key={type.id}
                                    to={`/library/content/${type.slug}`}
                                    icon={<FilmStrip weight="duotone" />}
                                    active={isActive(`/library/content/${type.slug}`)}
                                    onClick={closeSidebar}
                                >
                                    {type.name}
                                </SidebarItem>
                            ))}
                            {subjectTypes.length > 0 && <SidebarSeparator />}
                            {subjectTypes.map((type) => (
                                <SidebarItem
                                    key={type.id}
                                    to={`/library/subjects/${type.slug}`}
                                    icon={<Cube weight="duotone" />}
                                    active={isActive(`/library/subjects/${type.slug}`)}
                                    onClick={closeSidebar}
                                >
                                    {type.name}
                                </SidebarItem>
                            ))}
                        </SidebarSubMenu>
                        <SidebarItem
                            to="/downloads"
                            icon={<DownloadSimple weight="duotone" />}
                            active={isActive('/downloads')}
                            onClick={closeSidebar}
                        >
                            Downloads
                        </SidebarItem>
                        <SidebarItem
                            to="/calendar"
                            icon={<CalendarBlank weight="duotone" />}
                            active={isActive('/calendar')}
                            onClick={closeSidebar}
                        >
                            Calendar
                        </SidebarItem>
                        <SidebarItem
                            to="/filters"
                            icon={<Funnel weight="duotone" />}
                            active={isActive('/filters')}
                            onClick={closeSidebar}
                        >
                            Saved Filters
                        </SidebarItem>
                        <SidebarItem
                            to="/wishlists"
                            icon={<Star weight="duotone" />}
                            active={isActive('/wishlists')}
                            onClick={closeSidebar}
                        >
                            Wishlists
                        </SidebarItem>
                        <SidebarItem
                            to="/my-jobs"
                            icon={<ClockCountdown weight="duotone" />}
                            active={isActive('/my-jobs')}
                            onClick={closeSidebar}
                        >
                            My Jobs
                        </SidebarItem>
                    </SidebarGroup>
                    <RequireRole min={Role.Admin}>
                        <SidebarGroup>
                            <SidebarItem
                                to="/admin"
                                icon={<Gear weight="duotone" />}
                                active={isActive('/admin')}
                                onClick={closeSidebar}
                            >
                                Admin
                            </SidebarItem>
                        </SidebarGroup>
                    </RequireRole>
                </NavMainSlot>
            </SidebarContent>
            <SidebarFooter>
                <Link to="/about">Magpie v0.0.0</Link>
                <span aria-hidden="true">|</span>
                <a href="https://github.com/magpie-media/magpie" target="_blank" rel="noopener noreferrer">
                    GitHub
                </a>
            </SidebarFooter>
        </Sidebar>
    );
});

function AppBottomNav() {
    const isActive = useIsActive();

    return (
        <BottomNav>
            <BottomNavItem to="/" icon={<House weight="duotone" />} label="Dashboard" active={isActive('/')} />
            <BottomNavItem
                to="/library"
                icon={<Books weight="duotone" />}
                label="Library"
                active={isActive('/library')}
            />
            <BottomNavItem
                to="/downloads"
                icon={<DownloadSimple weight="duotone" />}
                label="Downloads"
                active={isActive('/downloads')}
            />
            <BottomNavItem
                to="/wishlists"
                icon={<Star weight="duotone" />}
                label="Wishlists"
                active={isActive('/wishlists')}
            />
        </BottomNav>
    );
}

function AdminBottomNav() {
    const isActive = useIsActive();

    return (
        <BottomNav>
            <BottomNavItem
                to="/admin/general"
                icon={<Gear weight="duotone" />}
                label="General"
                active={isActive('/admin/general')}
            />
            <BottomNavItem
                to="/admin/users"
                icon={<Users weight="duotone" />}
                label="Users"
                active={isActive('/admin/users')}
            />
            <BottomNavItem
                to="/admin/jobs"
                icon={<ClockCountdown weight="duotone" />}
                label="Jobs"
                active={isActive('/admin/jobs')}
            />
            <BottomNavItem
                to="/admin/backups"
                icon={<Database weight="duotone" />}
                label="Backups"
                active={isActive('/admin/backups')}
            />
        </BottomNav>
    );
}

function AppRoutes() {
    const element = useRoutes(routes);
    const location = useLocation();

    return (
        <ErrorBoundary
            resetKeys={[location.pathname]}
            fallback={({ resetErrorBoundary }) => <RouteErrorState onRetry={resetErrorBoundary} />}
        >
            <Suspense fallback={<RouteLoadingState />}>{element}</Suspense>
        </ErrorBoundary>
    );
}

const STANDALONE_ROUTES: string[] = ['/login', '/setup'];

function isStandaloneRoute(pathname: string): boolean {
    return STANDALONE_ROUTES.some((route) => pathname === route || pathname.startsWith(route + '/'));
}

function isAdminRoute(pathname: string): boolean {
    return pathname === '/admin' || pathname.startsWith('/admin/');
}

function SetupRedirectGate({ children }: { children: ReactNode }) {
    const location = useLocation();
    const { data, isLoading, isError } = useSetupStatus();

    // While the first status check is in flight we render a quiet loading state
    // so that we never flash the login form to a user on a fresh install.
    if (isLoading) {
        return <RouteLoadingState />;
    }

    // If the status endpoint errors, fall through to the normal app -- callers
    // (LoginPage, RequireAuth, etc.) will surface their own errors. Don't block
    // the entire app behind a setup status outage.
    if (!isError && data?.isRequired && location.pathname !== '/setup') {
        return <Navigate to="/setup" replace />;
    }

    return <>{children}</>;
}

function AppLayout() {
    const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
    const location = useLocation();
    const { contentTypes, subjectTypes } = useLibraryTypes();
    useScrollRestoration();

    if (isStandaloneRoute(location.pathname)) {
        return <AppRoutes />;
    }

    const isAdmin = isAdminRoute(location.pathname);

    return (
        <AppShell sidebarCollapsed={sidebarCollapsed} onSidebarCollapsedChange={setSidebarCollapsed}>
            <SkipLink targetId="main-content" />
            <AppShellHeader>{isAdmin ? <AdminHeader /> : <AppHeader />}</AppShellHeader>

            <div className="app-shell__body">
                <AppShellSidebar ariaLabel={isAdmin ? 'Admin navigation' : 'Main navigation'}>
                    {isAdmin ? (
                        <AdminSidebar />
                    ) : (
                        <AppSidebar contentTypes={contentTypes} subjectTypes={subjectTypes} />
                    )}
                </AppShellSidebar>

                <AppShellMain>
                    <RestartRequiredBanner />
                    <AppRoutes />
                </AppShellMain>
            </div>

            <AppShellBottomNav>{isAdmin ? <AdminBottomNav /> : <AppBottomNav />}</AppShellBottomNav>
            <CommandPalette />
        </AppShell>
    );
}

function AuthAwareExtensionProvider({ children }: { children: ReactNode }) {
    const { user } = useAuth();
    return (
        <ExtensionProvider
            userId={user?.id ?? null}
            username={user?.username ?? null}
            role={user !== null ? RoleName[user.role] : null}
        >
            {children}
        </ExtensionProvider>
    );
}

function App() {
    return (
        <QueryClientProvider client={queryClient}>
            <BrowserRouter>
                <LiveAnnouncerProvider>
                    <ToastStateProvider>
                        <AuthProvider>
                            <AuthAwareExtensionProvider>
                                <TooltipProvider>
                                    <ToastProvider>
                                        <RestartRequiredProvider>
                                            <SetupRedirectGate>
                                                <AppLayout />
                                            </SetupRedirectGate>
                                        </RestartRequiredProvider>
                                        <ToastList />
                                        <ToastViewport position="bottom-right" />
                                    </ToastProvider>
                                </TooltipProvider>
                            </AuthAwareExtensionProvider>
                        </AuthProvider>
                    </ToastStateProvider>
                </LiveAnnouncerProvider>
            </BrowserRouter>
        </QueryClientProvider>
    );
}

export default App;
