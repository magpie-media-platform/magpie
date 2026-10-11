import { Link } from 'react-router-dom';

import {
    ArrowsLeftRightIcon,
    BellIcon,
    ClockCountdownIcon,
    DatabaseIcon,
    FolderOpenIcon,
    GaugeIcon,
    GearIcon,
    HardDrivesIcon,
    PulseIcon,
    PuzzlePieceIcon,
    ShieldWarningIcon,
    ShapesIcon,
    SlidersHorizontalIcon,
    TelevisionIcon,
    UsersIcon,
} from '@phosphor-icons/react';

import { useShell } from '../Shell';
import { Sidebar, SidebarContent, SidebarGroup, SidebarItem, SidebarSeparator } from '../Sidebar';
import { Header } from '../Header';
import { LiveUpdateStatus } from '../LiveUpdateStatus/LiveUpdateStatus';
import { UserMenu } from '../UserMenu';
import { ExtensionAdminNavigation } from '../Slot';
import { AdminLink } from '@/auth';
import { LibraryQuickSearch } from '@/components/content/LibraryQuickSearch';
import { useMediaQuery } from '@/hooks/ui/useMediaQuery';
import { useIsActive } from '@/lib/routes';

function AdminHeader({ instanceName }: { instanceName: string }) {
    const { toggleSidebar, sidebarOpen, sidebarCollapsed } = useShell();
    const isDesktopSidebar = useMediaQuery('(min-width: 768px)');
    const sidebarExpanded = isDesktopSidebar ? !sidebarCollapsed : sidebarOpen;

    return (
        <Header
            brand={
                <Link to="/admin">
                    <GearIcon size={26} weight="duotone" aria-hidden="true" />
                    <span>{instanceName}</span>
                </Link>
            }
            status={<LiveUpdateStatus />}
            onSidebarToggle={toggleSidebar}
            sidebarOpen={sidebarExpanded}
            actions={
                <>
                    <LibraryQuickSearch />
                    <AdminLink />
                    <UserMenu />
                </>
            }
        />
    );
}

function AdminSidebar() {
    const { sidebarOpen, sidebarCollapsed, closeSidebar } = useShell();

    const isActive = useIsActive();

    return (
        <Sidebar open={sidebarOpen} collapsed={sidebarCollapsed}>
            <SidebarContent>
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
                        to="/admin/activity"
                        icon={<PulseIcon weight="duotone" />}
                        active={isActive('/admin/activity')}
                        onClick={closeSidebar}
                    >
                        Activity Log
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

export { AdminHeader, AdminSidebar };
