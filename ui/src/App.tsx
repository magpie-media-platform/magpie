import { Suspense, memo, useCallback, useEffect, useState, type ReactNode } from 'react';
import { createBrowserRouter, RouterProvider, Navigate, Outlet, useLocation, Link } from 'react-router-dom';
import {
    CalendarBlankIcon,
    ClockCountdownIcon,
    HouseIcon,
    BooksIcon,
    DownloadSimpleIcon,
    FolderSimplePlusIcon,
    GaugeIcon,
    GearIcon,
    StarIcon,
    FunnelIcon,
    DatabaseIcon,
    UsersIcon,
    TargetIcon,
    BinocularsIcon,
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
import { CdnPhosphorIcon } from './components/ui/CdnPhosphorIcon';
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
import { useLibraryTypes, useTypeIconName, type LibraryTypeDefinition } from './hooks/useLibraryTypes';
import { useSystemInfo } from './hooks/useSystemStatus';
import { ScanCompletionTrackerProvider } from './hooks/useScanCompletionTracker';
import { useScrollRestoration } from './hooks/useScrollRestoration';
import { usePersistentState } from './hooks/usePersistentState';
import { useGlobalEventStreamConnection } from './hooks/useEventStream';
import { useMediaQuery } from './hooks/ui/useMediaQuery';
import { BottomNav, BottomNavItem } from './components/ui/BottomNav';
import { HeaderSlot, HeaderLeftSlot, HeaderRightSlot } from './components/ui/Slot/GlobalSlots';
import { ExtensionMainNavigation } from './components/ui/Slot';
import { ToastProvider, ToastViewport, ToastStateProvider, ToastList, ToastTray } from './components/ui/Toast';
import { TooltipProvider } from './components/ui/Tooltip';
import { LiveAnnouncerProvider } from './components/ui/LiveAnnouncer';
import { RestartRequiredBanner, RestartRequiredProvider } from './components/ui/RestartRequiredBanner';
import { AuthProvider, RequireRole, Role, RoleName, useAuth } from './auth';
import { useSetupStatus } from './hooks/useSetup';
import { UnsavedChangesGuardProvider, UnsavedChangesBlocker } from './hooks/useUnsavedChangesGuard';
import { routes, RouteLoadingState, RouteErrorState } from './router';
import { CommandPalette } from './components/ui/CommandPalette';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { SkipLink } from './components/ui/SkipLink';
import { ContentLineageProvider } from './components/content/ContentLineageTitle';
import { ExtensionProvider } from './extensions';
import { queryClient } from '@/lib/queryClient';
import { useIsActive } from '@/lib/routes';
import { Logo } from './components/ui/Logo';

function AppHeader() {
    const { toggleSidebar, sidebarOpen, sidebarCollapsed } = useAppShell();
    const isDesktopSidebar = useMediaQuery('(min-width: 768px)');
    const sidebarExpanded = isDesktopSidebar ? !sidebarCollapsed : sidebarOpen;

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
                sidebarOpen={sidebarExpanded}
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

function isRouteWithin(pathname: string, route: string): boolean {
    return pathname === route || pathname.startsWith(`${route}/`);
}

const AppSidebar = memo(function AppSidebar({ contentTypes, subjectTypes }: AppSidebarProps) {
    const { sidebarOpen, sidebarCollapsed, toggleSidebarCollapsed, closeSidebar } = useAppShell();
    const location = useLocation();
    const typeIconName = useTypeIconName();
    const { data: systemInfo } = useSystemInfo();
    const version = systemInfo?.version;

    const isActive = useIsActive();

    const isAddContentActive = isRouteWithin(location.pathname, '/library/add');
    const isLibraryImportActive = isRouteWithin(location.pathname, '/library/import');
    const isLibraryActive =
        (isRouteWithin(location.pathname, '/library') && !isAddContentActive && !isLibraryImportActive) ||
        isRouteWithin(location.pathname, '/subjects');

    return (
        <Sidebar open={sidebarOpen} collapsed={sidebarCollapsed} onCollapsedChange={toggleSidebarCollapsed}>
            <SidebarContent>
                <SidebarGroup>
                    <SidebarItem
                        to="/"
                        icon={<HouseIcon weight="duotone" />}
                        active={isActive('/')}
                        onClick={closeSidebar}
                    >
                        Dashboard
                    </SidebarItem>
                    <SidebarSubMenu
                        label="Library"
                        icon={<BooksIcon weight="duotone" />}
                        active={isLibraryActive}
                        defaultOpen
                        collapsedTo="/library"
                    >
                        <SidebarItem to="/library" end active={location.pathname === '/library'} onClick={closeSidebar}>
                            Overview
                        </SidebarItem>
                        {contentTypes.map((type) => {
                            return (
                                <SidebarItem
                                    key={type.id}
                                    to={`/library/content/${type.slug}`}
                                    icon={<CdnPhosphorIcon name={typeIconName(type.slug)} weight="duotone" />}
                                    active={isActive(`/library/content/${type.slug}`)}
                                    onClick={closeSidebar}
                                >
                                    {type.pluralName}
                                </SidebarItem>
                            );
                        })}
                        {subjectTypes.length > 0 && <SidebarSeparator />}
                        {subjectTypes.map((type) => {
                            return (
                                <SidebarItem
                                    key={type.id}
                                    to={`/library/subjects/${type.slug}`}
                                    icon={
                                        <CdnPhosphorIcon
                                            name={typeIconName(type.slug, { subject: true })}
                                            weight="duotone"
                                        />
                                    }
                                    active={isActive(`/library/subjects/${type.slug}`)}
                                    onClick={closeSidebar}
                                >
                                    {type.pluralName}
                                </SidebarItem>
                            );
                        })}
                    </SidebarSubMenu>
                    <RequireRole min={Role.Manager}>
                        <SidebarItem
                            to="/library/add"
                            icon={<BinocularsIcon weight="duotone" />}
                            active={isAddContentActive}
                            onClick={closeSidebar}
                            title="Add content to your library"
                        >
                            Add content
                        </SidebarItem>
                    </RequireRole>
                    <SidebarItem
                        to="/filters"
                        icon={<FunnelIcon weight="duotone" />}
                        active={isActive('/filters')}
                        onClick={closeSidebar}
                        title="Reusable filters for the library and wishlists"
                    >
                        Filters
                    </SidebarItem>
                    <SidebarItem
                        to="/wishlists"
                        icon={<StarIcon weight="duotone" />}
                        active={isActive('/wishlists')}
                        onClick={closeSidebar}
                        title="Named groups of content to monitor and act on in bulk"
                    >
                        Wishlists
                    </SidebarItem>
                    <SidebarItem
                        to="/calendar"
                        icon={<CalendarBlankIcon weight="duotone" />}
                        active={isActive('/calendar')}
                        onClick={closeSidebar}
                    >
                        Calendar
                    </SidebarItem>
                    <SidebarItem
                        to="/wanted"
                        icon={<TargetIcon weight="duotone" />}
                        active={isActive('/wanted')}
                        onClick={closeSidebar}
                        title="Monitored content Magpie is actively trying to acquire"
                    >
                        Wanted
                    </SidebarItem>
                    <SidebarItem
                        to="/downloads"
                        icon={<DownloadSimpleIcon weight="duotone" />}
                        active={isActive('/downloads')}
                        onClick={closeSidebar}
                    >
                        Downloads
                    </SidebarItem>
                    <SidebarItem
                        to="/my-jobs"
                        icon={<ClockCountdownIcon weight="duotone" />}
                        active={isActive('/my-jobs')}
                        onClick={closeSidebar}
                    >
                        My Jobs
                    </SidebarItem>
                    <RequireRole min={Role.Manager}>
                        <SidebarItem
                            to="/library/import"
                            icon={<FolderSimplePlusIcon weight="duotone" />}
                            active={isLibraryImportActive}
                            onClick={closeSidebar}
                        >
                            Library import
                        </SidebarItem>
                    </RequireRole>
                </SidebarGroup>
                <ExtensionMainNavigation onNavigate={closeSidebar} />
                <RequireRole min={Role.Admin}>
                    <SidebarGroup>
                        <SidebarItem
                            to="/admin"
                            icon={<GearIcon weight="duotone" />}
                            active={isActive('/admin')}
                            onClick={closeSidebar}
                        >
                            Admin
                        </SidebarItem>
                    </SidebarGroup>
                </RequireRole>
            </SidebarContent>
            <SidebarFooter>
                <Link to="/about">{version ? `Magpie v${version}` : 'Magpie'}</Link>
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
            <BottomNavItem to="/" icon={<HouseIcon weight="duotone" />} label="Dashboard" active={isActive('/')} />
            <BottomNavItem
                to="/library"
                icon={<BooksIcon weight="duotone" />}
                label="Library"
                active={isActive('/library')}
            />
            <BottomNavItem
                to="/wanted"
                icon={<TargetIcon weight="duotone" />}
                label="Wanted"
                active={isActive('/wanted')}
            />
            <BottomNavItem
                to="/downloads"
                icon={<DownloadSimpleIcon weight="duotone" />}
                label="Downloads"
                active={isActive('/downloads')}
            />
        </BottomNav>
    );
}

function AdminBottomNav() {
    const isActive = useIsActive();

    return (
        <BottomNav>
            <BottomNavItem
                to="/admin/system"
                icon={<GaugeIcon weight="duotone" />}
                label="System"
                active={isActive('/admin/system')}
            />
            <BottomNavItem
                to="/admin/users"
                icon={<UsersIcon weight="duotone" />}
                label="Users"
                active={isActive('/admin/users')}
            />
            <BottomNavItem
                to="/admin/jobs"
                icon={<ClockCountdownIcon weight="duotone" />}
                label="System Jobs"
                active={isActive('/admin/jobs')}
            />
            <BottomNavItem
                to="/admin/database"
                icon={<DatabaseIcon weight="duotone" />}
                label="Database"
                active={isActive('/admin/database')}
            />
        </BottomNav>
    );
}

function RouteOutlet() {
    const location = useLocation();

    return (
        <ErrorBoundary
            resetKeys={[location.pathname]}
            fallback={({ resetErrorBoundary }) => <RouteErrorState onRetry={resetErrorBoundary} />}
        >
            <Suspense fallback={<RouteLoadingState />}>
                <Outlet />
            </Suspense>
        </ErrorBoundary>
    );
}

const STANDALONE_ROUTES: string[] = ['/login', '/setup', '/forbidden'];

function isStandaloneRoute(pathname: string): boolean {
    return STANDALONE_ROUTES.some((route) => pathname === route || pathname.startsWith(route + '/'));
}

function isAdminRoute(pathname: string): boolean {
    return pathname === '/admin' || pathname.startsWith('/admin/');
}

function SetupRedirectGate({ children }: { children: ReactNode }) {
    const location = useLocation();
    const { data, isLoading, isError } = useSetupStatus();

    if (isLoading) {
        return <RouteLoadingState />;
    }

    const loginRequiredForClaimedSetup = data?.isRequired && !data.requiresAdminClaim && location.pathname === '/login';

    if (!isError && data?.isRequired && location.pathname !== '/setup' && !loginRequiredForClaimedSetup) {
        return <Navigate to="/setup" replace />;
    }

    return <>{children}</>;
}

function AppLayout() {
    const [persistedCollapsed, setPersistedCollapsed] = usePersistentState('magpie.sidebar.collapsed', false);

    const isTablet = useMediaQuery('(min-width: 768px) and (max-width: 1023px)');
    const [tabletExpanded, setTabletExpanded] = useState(false);
    useEffect(() => {
        setTabletExpanded(false);
    }, [isTablet]);

    const sidebarCollapsed = isTablet ? !tabletExpanded : persistedCollapsed;
    const setSidebarCollapsed = useCallback(
        (next: boolean) => {
            if (isTablet) {
                setTabletExpanded(!next);
            } else {
                setPersistedCollapsed(next);
            }
        },
        [isTablet, setPersistedCollapsed],
    );

    const location = useLocation();
    const { contentTypes, subjectTypes } = useLibraryTypes();
    useScrollRestoration();

    if (isStandaloneRoute(location.pathname)) {
        return <RouteOutlet />;
    }

    const isAdmin = isAdminRoute(location.pathname);

    return (
        <ContentLineageProvider contentTypes={contentTypes}>
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
                        <RouteOutlet />
                    </AppShellMain>
                </div>

                <AppShellBottomNav>{isAdmin ? <AdminBottomNav /> : <AppBottomNav />}</AppShellBottomNav>
                <CommandPalette />
            </AppShell>
        </ContentLineageProvider>
    );
}

function AuthAwareExtensionProvider({ children }: { children: ReactNode }) {
    const { user } = useAuth();
    useGlobalEventStreamConnection(user !== null);
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

function RootLayout() {
    return (
        <LiveAnnouncerProvider>
            <ToastStateProvider>
                <AuthProvider>
                    <AuthAwareExtensionProvider>
                        <TooltipProvider>
                            <ToastProvider>
                                <RestartRequiredProvider>
                                    <ScanCompletionTrackerProvider>
                                        <UnsavedChangesGuardProvider>
                                            <UnsavedChangesBlocker />
                                            <SetupRedirectGate>
                                                <AppLayout />
                                            </SetupRedirectGate>
                                        </UnsavedChangesGuardProvider>
                                    </ScanCompletionTrackerProvider>
                                </RestartRequiredProvider>
                                <ToastList />
                                <ToastViewport position="bottom-right">
                                    <ToastTray />
                                </ToastViewport>
                            </ToastProvider>
                        </TooltipProvider>
                    </AuthAwareExtensionProvider>
                </AuthProvider>
            </ToastStateProvider>
        </LiveAnnouncerProvider>
    );
}

function App() {
    const [router] = useState(() => createBrowserRouter([{ element: <RootLayout />, children: routes }]));

    return (
        <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
        </QueryClientProvider>
    );
}

export default App;
