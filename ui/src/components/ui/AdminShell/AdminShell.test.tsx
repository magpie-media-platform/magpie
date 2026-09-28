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

import {
    AdminShell,
    AdminShellHeader,
    AdminShellSidebar,
    AdminShellMain,
    AdminShellBottomNav,
    AdminHeader,
    AdminSidebar,
} from './AdminShell';

function renderWithRouter(ui: React.ReactElement) {
    return render(<MemoryRouter initialEntries={['/admin']}>{ui}</MemoryRouter>);
}

describe('AdminShell', () => {
    it('renders with classPrefix admin-shell', () => {
        const { container } = render(
            <AdminShell>
                <div>content</div>
            </AdminShell>,
        );

        expect(container.querySelector('[class*="admin-shell"]')).toBeInTheDocument();
    });

    it('sets displayName on all sub-components', () => {
        expect(AdminShell.displayName).toBe('AdminShell');
        expect(AdminShellHeader.displayName).toBe('AdminShellHeader');
        expect(AdminShellSidebar.displayName).toBe('AdminShellSidebar');
        expect(AdminShellMain.displayName).toBe('AdminShellMain');
        expect(AdminShellBottomNav.displayName).toBe('AdminShellBottomNav');
    });

    it('renders AdminShellMain with main landmark', () => {
        render(
            <AdminShell>
                <AdminShellMain>Main content</AdminShellMain>
            </AdminShell>,
        );

        expect(screen.getByRole('main')).toHaveTextContent('Main content');
    });

    it('renders AdminShellSidebar with default aria label', () => {
        render(
            <AdminShell>
                <AdminShellSidebar>Nav</AdminShellSidebar>
            </AdminShell>,
            { wrapper: MemoryRouter },
        );

        expect(screen.getByRole('navigation', { name: 'Admin navigation' })).toBeInTheDocument();
    });

    it('announces the initially expanded desktop sidebar correctly', () => {
        renderWithRouter(
            <AdminShell>
                <AdminShellHeader>
                    <AdminHeader />
                </AdminShellHeader>
                <AdminShellSidebar>Navigation</AdminShellSidebar>
            </AdminShell>,
        );

        expect(screen.getByRole('button', { name: 'Collapse sidebar' })).toHaveAttribute('aria-expanded', 'true');
    });

    it('offers library search on administration pages', () => {
        renderWithRouter(
            <AdminShell>
                <AdminShellHeader>
                    <AdminHeader />
                </AdminShellHeader>
            </AdminShell>,
        );

        expect(screen.getByRole('button', { name: 'Search library' })).toBeInTheDocument();
    });
});

describe('AdminSidebar', () => {
    it('renders admin navigation links', () => {
        renderWithRouter(
            <AdminShell>
                <AdminShellSidebar>
                    <AdminSidebar />
                </AdminShellSidebar>
            </AdminShell>,
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
            <AdminShell>
                <AdminShellHeader>
                    <AdminHeader />
                </AdminShellHeader>
                <AdminShellSidebar>
                    <AdminSidebar />
                </AdminShellSidebar>
            </AdminShell>,
        );

        const sidebar = screen.getByRole('navigation', { name: 'Admin navigation' });
        expect(within(sidebar).queryByRole('link', { name: 'Home' })).not.toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/');
    });

    it('places Advanced as the last item of the System group, not its own section', () => {
        const { container } = renderWithRouter(
            <AdminShell>
                <AdminShellSidebar>
                    <AdminSidebar />
                </AdminShellSidebar>
            </AdminShell>,
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
            <AdminShell>
                <AdminShellSidebar>
                    <AdminSidebar />
                </AdminShellSidebar>
            </AdminShell>,
        );

        const extensionsLink = screen.getByRole('link', { name: 'Extensions' });
        const extensionsGroup = extensionsLink.closest('.sidebar-group');
        expect(extensionsGroup?.querySelector('.sidebar-group-label')?.textContent).toBe('Extensions');
    });
});
