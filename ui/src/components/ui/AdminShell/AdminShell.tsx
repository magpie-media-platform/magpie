import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { Link } from 'react-router-dom';

import {
    ArrowLeft,
    ClockCountdown,
    Database,
    FolderOpen,
    Gauge,
    Gear,
    HardDrives,
    PuzzlePiece,
    SlidersHorizontal,
    Television,
    Users,
} from '@phosphor-icons/react';

import { Shell, ShellHeader, ShellSidebar, ShellMain, ShellBottomNav, useShell, type ShellProps } from '../Shell';
import { Sidebar, SidebarContent, SidebarGroup, SidebarItem, SidebarSeparator } from '../Sidebar';
import { Header } from '../Header';
import { Badge } from '../Badge';
import { UserMenu } from '../UserMenu';
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
    const { toggleSidebar, sidebarOpen } = useShell();

    return (
        <Header
            brand={
                <Link to="/admin">
                    <Gear size={26} weight="duotone" aria-hidden="true" />
                    <span>Admin</span>
                </Link>
            }
            status={
                <Badge variant="info" dot>
                    Admin Area
                </Badge>
            }
            onSidebarToggle={toggleSidebar}
            sidebarOpen={sidebarOpen}
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
                    <SidebarItem to="/" icon={<ArrowLeft weight="duotone" />} onClick={closeSidebar}>
                        Exit admin
                    </SidebarItem>
                </SidebarGroup>

                <SidebarSeparator />

                <SidebarGroup>
                    <SidebarItem
                        to="/admin/general"
                        icon={<Gear weight="duotone" />}
                        active={isActive('/admin/general')}
                        onClick={closeSidebar}
                    >
                        General
                    </SidebarItem>
                </SidebarGroup>

                <SidebarSeparator />

                <SidebarGroup label="Users">
                    <SidebarItem
                        to="/admin/users"
                        icon={<Users weight="duotone" />}
                        active={isActive('/admin/users')}
                        onClick={closeSidebar}
                    >
                        Users
                    </SidebarItem>
                </SidebarGroup>

                <SidebarSeparator />

                <SidebarGroup label="Integrations">
                    <SidebarItem
                        to="/admin/indexers"
                        icon={<Database weight="duotone" />}
                        active={isActive('/admin/indexers')}
                        onClick={closeSidebar}
                    >
                        Indexers
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/download-clients"
                        icon={<HardDrives weight="duotone" />}
                        active={isActive('/admin/download-clients')}
                        onClick={closeSidebar}
                    >
                        Download Clients
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/media-servers"
                        icon={<Television weight="duotone" />}
                        active={isActive('/admin/media-servers')}
                        onClick={closeSidebar}
                    >
                        Media Servers
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/root-folders"
                        icon={<FolderOpen weight="duotone" />}
                        active={isActive('/admin/root-folders')}
                        onClick={closeSidebar}
                    >
                        Root Folders
                    </SidebarItem>
                </SidebarGroup>

                <SidebarSeparator />

                <SidebarGroup label="Content">
                    <SidebarItem
                        to="/admin/quality-tiers"
                        icon={<SlidersHorizontal weight="duotone" />}
                        active={isActive('/admin/quality-tiers')}
                        onClick={closeSidebar}
                    >
                        Quality Tiers
                    </SidebarItem>
                </SidebarGroup>

                <SidebarSeparator />

                <SidebarGroup label="System">
                    <SidebarItem
                        to="/admin/jobs"
                        icon={<ClockCountdown weight="duotone" />}
                        active={isActive('/admin/jobs')}
                        onClick={closeSidebar}
                    >
                        Jobs
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/extensions"
                        icon={<PuzzlePiece weight="duotone" />}
                        active={isActive('/admin/extensions')}
                        onClick={closeSidebar}
                    >
                        Extensions
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/backups"
                        icon={<Database weight="duotone" />}
                        active={isActive('/admin/backups')}
                        onClick={closeSidebar}
                    >
                        Backups
                    </SidebarItem>
                    <SidebarItem
                        to="/admin/system"
                        icon={<Gauge weight="duotone" />}
                        active={isActive('/admin/system')}
                        onClick={closeSidebar}
                    >
                        System Status
                    </SidebarItem>
                </SidebarGroup>
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
