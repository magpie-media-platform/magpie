import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

vi.mock('@/auth', async () => {
    const actual = await vi.importActual('@/auth');
    return {
        ...actual,
        useAuth: () => ({
            user: { id: 'u1', username: 'admin', role: 3 },
            isAuthenticated: true,
            hasPermission: () => true,
            hasRole: () => true,
        }),
    };
});

vi.mock('../UserMenu', () => ({
    UserMenu: () => <div data-testid="user-menu">UserMenu</div>,
}));

import {
    AdminShell,
    AdminShellHeader,
    AdminShellSidebar,
    AdminShellMain,
    AdminShellBottomNav,
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
        );

        expect(screen.getByRole('navigation', { name: 'Admin navigation' })).toBeInTheDocument();
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

        expect(screen.getByText('General')).toBeInTheDocument();
        expect(screen.getByText('Users')).toBeInTheDocument();
        expect(screen.getByText('Indexers')).toBeInTheDocument();
        expect(screen.getByText('Download Clients')).toBeInTheDocument();
        expect(screen.getByText('Media Servers')).toBeInTheDocument();
        expect(screen.getByText('Root Folders')).toBeInTheDocument();
        expect(screen.getByText('Quality Tiers')).toBeInTheDocument();
        expect(screen.getByText('Jobs')).toBeInTheDocument();
        expect(screen.getByText('Extensions')).toBeInTheDocument();
        expect(screen.getByText('Backups')).toBeInTheDocument();
        expect(screen.getByText('System Status')).toBeInTheDocument();
    });

    it('renders exit admin link', () => {
        renderWithRouter(
            <AdminShell>
                <AdminShellSidebar>
                    <AdminSidebar />
                </AdminShellSidebar>
            </AdminShell>,
        );

        expect(screen.getByText('Exit admin')).toBeInTheDocument();
    });
});
