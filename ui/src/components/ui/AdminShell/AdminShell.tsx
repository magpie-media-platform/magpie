import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { Link } from 'react-router-dom';

import {
    ArrowLeftIcon,
    ArrowsLeftRightIcon,
    BellIcon,
    ClockCountdownIcon,
    DatabaseIcon,
    FolderOpenIcon,
    GaugeIcon,
    GearIcon,
    HardDrivesIcon,
    PuzzlePieceIcon,
    ShieldWarningIcon,
    ShapesIcon,
    SlidersHorizontalIcon,
    TelevisionIcon,
    UsersIcon,
} from '@phosphor-icons/react';

import { Shell, ShellHeader, ShellSidebar, ShellMain, ShellBottomNav, useShell, type ShellProps } from '../Shell';
import { Sidebar, SidebarContent, SidebarGroup, SidebarItem, SidebarSeparator } from '../Sidebar';
import { Header } from '../Header';
import { UserMenu } from '../UserMenu';
import { ExtensionAdminNavigation } from '../Slot';
import { useMediaQuery } from '@/hooks/ui/useMediaQuery';
import { useIsActive } from '@/lib/routes';

export interface AdminShellProps extends Omit<ComponentPropsWithoutRef<'div'>, keyof ShellProps>, ShellProps {}

const AdminShell = forwardRef<HTMLDivElement, AdminShellProps>((props, ref) => (
    <Shell ref={ref} classPrefix="admin-shell" {...props} />
));

AdminShell.displayName = 'AdminShell';

export type AdminShellHeaderProps = ComponentPropsWithoutRef<'div'>;

const AdminShellHeader = forwardRef<HTMLDivElement, AdminShellHeaderProps>((props, ref) => (
    <ShellHeader ref={ref} classPrefix="admin-shell" {...props} />
));

AdminShellHeader.displayName = 'AdminShellHeader';

export interface AdminShellSidebarProps extends ComponentPropsWithoutRef<'nav'> {
    ariaLabel?: string;
}

const AdminShellSidebar = forwardRef<HTMLElement, AdminShellSidebarProps>(
    ({ ariaLabel = 'Admin navigation', ...props }, ref) => (
        <ShellSidebar
            ref={ref}
            classPrefix="admin-shell"
            ariaLabel={ariaLabel}
            navId="admin-navigation-menu"
            {...props}
        />
    ),
);

AdminShellSidebar.displayName = 'AdminShellSidebar';

export type AdminShellMainProps = ComponentPropsWithoutRef<'main'>;

const AdminShellMain = forwardRef<HTMLElement, AdminShellMainProps>((props, ref) => (
    <ShellMain ref={ref} classPrefix="admin-shell" mainId="admin-main-content" {...props} />
));

AdminShellMain.displayName = 'AdminShellMain';

export type AdminShellBottomNavProps = ComponentPropsWithoutRef<'div'>;

const AdminShellBottomNav = forwardRef<HTMLDivElement, AdminShellBottomNavProps>((props, ref) => (
    <ShellBottomNav ref={ref} classPrefix="admin-shell" {...props} />
));

AdminShellBottomNav.displayName = 'AdminShellBottomNav';

function AdminHeader() {
    const { toggleSidebar, sidebarOpen, sidebarCollapsed } = useShell();
    const isDesktopSidebar = useMediaQuery('(min-width: 768px)');
    const sidebarExpanded = isDesktopSidebar ? !sidebarCollapsed : sidebarOpen;

    return (
        <Header
            brand={
                <Link to="/admin">
                    <GearIcon size={26} weight="duotone" aria-hidden="true" />
                    <span>Admin</span>
                </Link>
            }
            onSidebarToggle={toggleSidebar}
            sidebarOpen={sidebarExpanded}
            sidebarNavId="admin-navigation-menu"
            actions={<UserMenu />}
        />
    );
}

function AdminSidebar() {
    const { sidebarOpen, sidebarCollapsed, toggleSidebarCollapsed, closeSidebar } = useShell();

    const isActive = useIsActive();

    return (
        <Sidebar open={sidebarOpen} collapsed={sidebarCollapsed} onCollapsedChange={toggleSidebarCollapsed}>
            <SidebarContent>
                <SidebarGroup>
                    <SidebarItem to="/" icon={<ArrowLeftIcon weight="duotone" />} onClick={closeSidebar}>
                        Exit admin
                    </SidebarItem>
                </SidebarGroup>

                <SidebarSeparator />

                <SidebarGroup label="System">
                    <SidebarItem
                        to="/admin/system"
                        icon={<GaugeIcon weight="duotone" />}
                        active={isActive('/admin/system')}
                        onClick={closeSidebar}
                    >
                        System Status
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/jobs"
                        icon={<ClockCountdownIcon weight="duotone" />}
                        active={isActive('/admin/jobs')}
                        onClick={closeSidebar}
                    >
                        System Jobs
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/users"
                        icon={<UsersIcon weight="duotone" />}
                        active={isActive('/admin/users')}
                        onClick={closeSidebar}
                    >
                        Users
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/quality-tiers"
                        icon={<SlidersHorizontalIcon weight="duotone" />}
                        active={isActive('/admin/quality-tiers')}
                        onClick={closeSidebar}
                    >
                        Quality Tiers
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/content-types"
                        icon={<ShapesIcon weight="duotone" />}
                        active={isActive('/admin/content-types')}
                        onClick={closeSidebar}
                    >
                        Content Types
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/database"
                        icon={<DatabaseIcon weight="duotone" />}
                        active={isActive('/admin/database')}
                        onClick={closeSidebar}
                    >
                        Database
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/general"
                        icon={<GearIcon weight="duotone" />}
                        active={isActive('/admin/general')}
                        onClick={closeSidebar}
                    >
                        Advanced
                    </SidebarItem>
                </SidebarGroup>

                <SidebarSeparator />

                <SidebarGroup label="Integrations">
                    <SidebarItem
                        to="/admin/indexers"
                        icon={<DatabaseIcon weight="duotone" />}
                        active={isActive('/admin/indexers')}
                        onClick={closeSidebar}
                    >
                        Indexers
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/download-clients"
                        icon={<HardDrivesIcon weight="duotone" />}
                        active={isActive('/admin/download-clients')}
                        onClick={closeSidebar}
                    >
                        Download Clients
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/media-servers"
                        icon={<TelevisionIcon weight="duotone" />}
                        active={isActive('/admin/media-servers')}
                        onClick={closeSidebar}
                    >
                        Media Servers
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/root-folders"
                        icon={<FolderOpenIcon weight="duotone" />}
                        active={isActive('/admin/root-folders')}
                        onClick={closeSidebar}
                    >
                        Root Folders
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/remote-path-mappings"
                        icon={<ArrowsLeftRightIcon weight="duotone" />}
                        active={isActive('/admin/remote-path-mappings')}
                        onClick={closeSidebar}
                    >
                        Remote Path Mappings
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/file-types"
                        icon={<ShieldWarningIcon weight="duotone" />}
                        active={isActive('/admin/file-types')}
                        onClick={closeSidebar}
                    >
                        File Types
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/notifications"
                        icon={<BellIcon weight="duotone" />}
                        active={isActive('/admin/notifications')}
                        onClick={closeSidebar}
                    >
                        Notifications
                    </SidebarItem>
                </SidebarGroup>

                <ExtensionAdminNavigation
                    onNavigate={closeSidebar}
                    leading={
                        <SidebarItem
                            to="/admin/extensions"
                            icon={<PuzzlePieceIcon weight="duotone" />}
                            active={isActive('/admin/extensions')}
                            onClick={closeSidebar}
                        >
                            Extensions
                        </SidebarItem>
                    }
                />
            </SidebarContent>
        </Sidebar>
    );
}

export {
    AdminShell,
    AdminShellHeader,
    AdminShellSidebar,
    AdminShellMain,
    AdminShellBottomNav,
    AdminHeader,
    AdminSidebar,
};

export default AdminShell;
