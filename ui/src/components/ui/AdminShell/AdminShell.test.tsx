import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

vi.mock('@/auth/useAuth', () => ({
    useAuth: () => ({
        user: { id: 'u1', username: 'admin', role: 3 },
        isAuthenticated: true,
        hasPermission: () => true,
        hasRole: () => true,
    }),
}));

vi.mock('../UserMenu', () => ({
    UserMenu: () => <div data-testid="user-menu">UserMenu</div>,
}));

import { AppShell, AppShellHeader, AppShellSidebar } from '../AppShell';
import { AdminHeader, AdminSidebar } from './AdminShell';

function renderWithRouter(ui: React.ReactElement) {
    return render(<MemoryRouter initialEntries={['/admin']}>{ui}</MemoryRouter>);
}

describe('AdminHeader', () => {
    it('announces the initially expanded desktop sidebar correctly', () => {
        renderWithRouter(
            <AppShell>
                <AppShellHeader>
                    <AdminHeader instanceName="Home Lab" />
                </AppShellHeader>
                <AppShellSidebar ariaLabel="Admin navigation">Navigation</AppShellSidebar>
            </AppShell>,
        );

        expect(screen.getByRole('button', { name: 'Collapse sidebar' })).toHaveAttribute('aria-expanded', 'true');
    });

    it('shows the instance name in the header brand', () => {
        renderWithRouter(
            <AppShell>
                <AppShellHeader>
                    <AdminHeader instanceName="Home Lab" />
                </AppShellHeader>
            </AppShell>,
        );

        expect(screen.getByRole('link', { name: 'Home Lab' })).toHaveAttribute('href', '/admin');
    });

    it('offers library search on administration pages', () => {
        renderWithRouter(
            <AppShell>
                <AppShellHeader>
                    <AdminHeader instanceName="Home Lab" />
                </AppShellHeader>
            </AppShell>,
        );

        expect(screen.getByRole('button', { name: 'Search' })).toBeInTheDocument();
    });
});

describe('AdminSidebar', () => {
    it('renders admin navigation links', () => {
        renderWithRouter(
            <AppShell>
                <AppShellSidebar ariaLabel="Admin navigation">
                    <AdminSidebar />
                </AppShellSidebar>
            </AppShell>,
        );

        expect(screen.getByRole('link', { name: 'Advanced' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Users' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Indexers' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Download Clients' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Remote Path Mappings' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Media Servers' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Root Folders' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'File Types' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Notifications' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Quality Tiers' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'System Jobs' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Extensions' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Database' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'System Status' })).toBeInTheDocument();
    });

    it('leaves the way out of admin to the header, not the sidebar', () => {
        renderWithRouter(
            <AppShell>
                <AppShellHeader>
                    <AdminHeader instanceName="Home Lab" />
                </AppShellHeader>
                <AppShellSidebar ariaLabel="Admin navigation">
                    <AdminSidebar />
                </AppShellSidebar>
            </AppShell>,
        );

        const sidebar = screen.getByRole('navigation', { name: 'Admin navigation' });
        expect(within(sidebar).queryByRole('link', { name: 'Home' })).not.toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/');
    });

    it('places Advanced as the last item of the System group, not its own section', () => {
        const { container } = renderWithRouter(
            <AppShell>
                <AppShellSidebar ariaLabel="Admin navigation">
                    <AdminSidebar />
                </AppShellSidebar>
            </AppShell>,
        );

        const groupLabels = Array.from(container.querySelectorAll('.sidebar-group-label')).map((el) => el.textContent);
        expect(groupLabels).toContain('System');
        expect(groupLabels).toContain('Extensions');
        expect(groupLabels).not.toContain('Advanced');

        const advancedLink = screen.getByRole('link', { name: 'Advanced' });
        const systemGroup = advancedLink.closest('.sidebar-group');
        expect(systemGroup?.querySelector('.sidebar-group-label')?.textContent).toBe('System');
    });

    it('places Extensions as the top item of the Extensions section', () => {
        renderWithRouter(
            <AppShell>
                <AppShellSidebar ariaLabel="Admin navigation">
                    <AdminSidebar />
                </AppShellSidebar>
            </AppShell>,
        );

        const extensionsLink = screen.getByRole('link', { name: 'Extensions' });
        const extensionsGroup = extensionsLink.closest('.sidebar-group');
        expect(extensionsGroup?.querySelector('.sidebar-group-label')?.textContent).toBe('Extensions');
    });
});
