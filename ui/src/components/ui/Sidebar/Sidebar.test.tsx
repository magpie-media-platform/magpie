import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { HouseIcon, GearIcon, UsersIcon, FolderIcon } from '@phosphor-icons/react';

import {
    Sidebar,
    SidebarContent,
    SidebarFooter,
    SidebarGroup,
    SidebarItem,
    SidebarSubMenu,
    SidebarSeparator,
} from './Sidebar';

function renderWithRouter(ui: React.ReactElement) {
    return render(<MemoryRouter>{ui}</MemoryRouter>);
}

describe('Sidebar', () => {
    it('renders sidebar with children', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarItem to="/">Home</SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.getByRole('complementary')).toBeInTheDocument();
        expect(screen.getByText('Home')).toBeInTheDocument();
    });

    it('applies custom className', () => {
        render(
            <Sidebar className="custom-sidebar">
                <SidebarContent>
                    <span>Content</span>
                </SidebarContent>
            </Sidebar>,
        );

        const sidebar = screen.getByRole('complementary');
        expect(sidebar).toHaveClass('sidebar');
        expect(sidebar).toHaveClass('custom-sidebar');
    });

    it('renders in expanded state by default', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarItem to="/">Home</SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        const sidebar = screen.getByRole('complementary');
        expect(sidebar).not.toHaveAttribute('data-collapsed', 'true');
        expect(sidebar).toHaveAttribute('data-collapsed', 'false');
    });

    it('supports controlled collapsed state', () => {
        const { rerender } = renderWithRouter(
            <Sidebar collapsed={false}>
                <SidebarContent>
                    <SidebarItem to="/" icon={<HouseIcon />}>
                        Home
                    </SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.getByRole('complementary')).toHaveAttribute('data-collapsed', 'false');
        expect(screen.getByText('Home')).toBeInTheDocument();

        rerender(
            <MemoryRouter>
                <Sidebar collapsed={true}>
                    <SidebarContent>
                        <SidebarItem to="/" icon={<HouseIcon />}>
                            Home
                        </SidebarItem>
                    </SidebarContent>
                </Sidebar>
            </MemoryRouter>,
        );

        expect(screen.getByRole('complementary')).toHaveAttribute('data-collapsed', 'true');
        expect(screen.queryByText('Home', { selector: '.sidebar-item-label' })).not.toBeInTheDocument();
    });

    it('passes open prop as data-open attribute', () => {
        const { rerender } = renderWithRouter(
            <Sidebar open={false}>
                <SidebarContent>
                    <SidebarItem to="/">Home</SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        const sidebar = screen.getByRole('complementary');
        expect(sidebar).toHaveAttribute('data-open', 'false');

        rerender(
            <MemoryRouter>
                <Sidebar open={true}>
                    <SidebarContent>
                        <SidebarItem to="/">Home</SidebarItem>
                    </SidebarContent>
                </Sidebar>
            </MemoryRouter>,
        );

        expect(sidebar).toHaveAttribute('data-open', 'true');
    });
});

describe('SidebarContent', () => {
    it('renders content area', () => {
        render(
            <Sidebar>
                <SidebarContent>
                    <p>Navigation items</p>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.getByText('Navigation items')).toBeInTheDocument();
    });

    it('applies custom className', () => {
        render(
            <Sidebar>
                <SidebarContent className="custom-content" data-testid="content">
                    <span>Content</span>
                </SidebarContent>
            </Sidebar>,
        );

        const content = screen.getByTestId('content');
        expect(content).toHaveClass('sidebar-content');
        expect(content).toHaveClass('custom-content');
    });
});

describe('SidebarFooter', () => {
    it('renders footer content', () => {
        render(
            <Sidebar>
                <SidebarFooter>
                    <span>Footer</span>
                </SidebarFooter>
            </Sidebar>,
        );

        expect(screen.getByText('Footer')).toBeInTheDocument();
    });

    it('applies custom className', () => {
        render(
            <Sidebar>
                <SidebarFooter className="custom-footer" data-testid="footer">
                    <span>Footer</span>
                </SidebarFooter>
            </Sidebar>,
        );

        const footer = screen.getByTestId('footer');
        expect(footer).toHaveClass('sidebar-footer');
        expect(footer).toHaveClass('custom-footer');
    });
});

describe('SidebarGroup', () => {
    it('renders group with label and children', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarGroup label="Navigation">
                        <SidebarItem to="/">Home</SidebarItem>
                    </SidebarGroup>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.getByText('Navigation')).toBeInTheDocument();
        expect(screen.getByText('Home')).toBeInTheDocument();
    });

    it('is open by default', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarGroup label="Navigation">
                        <SidebarItem to="/">Home</SidebarItem>
                    </SidebarGroup>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.getByText('Home')).toBeVisible();
    });

    it('collapses when trigger is clicked', async () => {
        const user = userEvent.setup();

        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarGroup label="Navigation">
                        <SidebarItem to="/">Home</SidebarItem>
                    </SidebarGroup>
                </SidebarContent>
            </Sidebar>,
        );

        const trigger = screen.getByRole('button', { name: 'Navigation' });
        expect(trigger).toHaveAttribute('aria-expanded', 'true');

        await user.click(trigger);

        expect(trigger).toHaveAttribute('aria-expanded', 'false');
        expect(trigger).toHaveAttribute('data-state', 'closed');
    });

    it('expands when trigger is clicked again', async () => {
        const user = userEvent.setup();

        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarGroup label="Navigation" defaultOpen={false}>
                        <SidebarItem to="/">Home</SidebarItem>
                    </SidebarGroup>
                </SidebarContent>
            </Sidebar>,
        );

        const trigger = screen.getByRole('button', { name: 'Navigation' });
        expect(trigger).toHaveAttribute('aria-expanded', 'false');

        await user.click(trigger);

        expect(trigger).toHaveAttribute('aria-expanded', 'true');
        expect(trigger).toHaveAttribute('data-state', 'open');
    });

    it('renders without label when not provided', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarGroup>
                        <SidebarItem to="/">Home</SidebarItem>
                    </SidebarGroup>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.queryByRole('button', { name: /navigation/i })).not.toBeInTheDocument();
        expect(screen.getByText('Home')).toBeInTheDocument();
    });

    it('preserves collapsible when sidebar is collapsed', () => {
        renderWithRouter(
            <Sidebar collapsed>
                <SidebarContent>
                    <SidebarGroup label="Navigation">
                        <SidebarItem to="/" icon={<HouseIcon />}>
                            Home
                        </SidebarItem>
                    </SidebarGroup>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.queryByRole('button', { name: 'Navigation' })).toBeInTheDocument();
    });
});

describe('SidebarItem', () => {
    it('renders item with label', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarItem to="/dashboard">Dashboard</SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        const item = screen.getByRole('link', { name: 'Dashboard' });
        expect(item).toBeInTheDocument();
        expect(item).toHaveAttribute('href', '/dashboard');
    });

    it('renders item with icon', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarItem to="/" icon={<HouseIcon data-testid="icon" />}>
                        Home
                    </SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.getByTestId('icon')).toBeInTheDocument();
    });

    it('applies active state', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarItem to="/" active>
                        Home
                    </SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        const item = screen.getByRole('link', { name: 'Home' });
        expect(item).toHaveAttribute('data-active', 'true');
        expect(item).toHaveAttribute('data-active', 'true');
        expect(item).toHaveAttribute('aria-current', 'page');
    });

    it('hides label when sidebar is collapsed but preserves accessible name', () => {
        renderWithRouter(
            <Sidebar collapsed>
                <SidebarContent>
                    <SidebarItem to="/" icon={<HouseIcon data-testid="icon" />}>
                        Home
                    </SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.queryByText('Home', { selector: '.sidebar-item-label' })).not.toBeInTheDocument();
        expect(screen.getByTestId('icon')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Home' })).toBeInTheDocument();
    });

    it('applies custom className', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarItem to="/" className="custom-item">
                        Home
                    </SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        const item = screen.getByRole('link', { name: 'Home' });
        expect(item).toHaveClass('sidebar-item');
        expect(item).toHaveClass('custom-item');
    });
});

describe('SidebarSubMenu', () => {
    it('renders submenu with label and children', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarSubMenu label="Settings" icon={<GearIcon />}>
                        <SidebarItem to="/settings/general">General</SidebarItem>
                        <SidebarItem to="/settings/users">Users</SidebarItem>
                    </SidebarSubMenu>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.getByRole('button', { name: 'Settings' })).toBeInTheDocument();
    });

    it('expands when clicked', async () => {
        const user = userEvent.setup();

        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarSubMenu label="Settings" icon={<GearIcon />}>
                        <SidebarItem to="/settings/general">General</SidebarItem>
                    </SidebarSubMenu>
                </SidebarContent>
            </Sidebar>,
        );

        const trigger = screen.getByRole('button', { name: 'Settings' });
        expect(trigger).toHaveAttribute('aria-expanded', 'false');

        await user.click(trigger);

        expect(trigger).toHaveAttribute('aria-expanded', 'true');
        expect(trigger).toHaveAttribute('data-state', 'open');
    });

    it('collapses when clicked again', async () => {
        const user = userEvent.setup();

        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarSubMenu label="Settings" icon={<GearIcon />} defaultOpen>
                        <SidebarItem to="/settings/general">General</SidebarItem>
                    </SidebarSubMenu>
                </SidebarContent>
            </Sidebar>,
        );

        const trigger = screen.getByRole('button', { name: 'Settings' });
        expect(trigger).toHaveAttribute('aria-expanded', 'true');

        await user.click(trigger);

        expect(trigger).toHaveAttribute('aria-expanded', 'false');
        expect(trigger).toHaveAttribute('data-state', 'closed');
    });

    it('auto-expands when active prop is true', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarSubMenu label="Settings" icon={<GearIcon />} active>
                        <SidebarItem to="/settings/general">General</SidebarItem>
                    </SidebarSubMenu>
                </SidebarContent>
            </Sidebar>,
        );

        const trigger = screen.getByRole('button', { name: 'Settings' });
        expect(trigger).toHaveAttribute('data-active', 'true');
        expect(trigger).toHaveAttribute('aria-expanded', 'true');
    });

    it('opens a collapsed submenu without a destination from the keyboard', async () => {
        const user = userEvent.setup();
        renderWithRouter(
            <Sidebar collapsed>
                <SidebarContent>
                    <SidebarSubMenu label="Settings">
                        <SidebarItem to="/settings/general">General</SidebarItem>
                    </SidebarSubMenu>
                </SidebarContent>
            </Sidebar>,
        );

        const trigger = screen.getByRole('button', { name: 'Settings' });
        expect(trigger).toHaveTextContent('Settings');
        trigger.focus();
        await user.keyboard('{Enter}');
        expect(trigger).toHaveAttribute('aria-expanded', 'true');
        expect(screen.getByRole('link', { name: 'General' })).toHaveAttribute('href', '/settings/general');
    });

    it('renders collapsed submenu as a link when collapsedTo is provided', () => {
        renderWithRouter(
            <Sidebar collapsed>
                <SidebarContent>
                    <SidebarSubMenu
                        label="Library"
                        icon={<FolderIcon data-testid="folder-icon" />}
                        collapsedTo="/library"
                    >
                        <SidebarItem to="/library/all">All</SidebarItem>
                    </SidebarSubMenu>
                </SidebarContent>
            </Sidebar>,
        );

        const trigger = screen.getByRole('link', { name: 'Library' });
        expect(trigger).toHaveAttribute('href', '/library');
        expect(screen.getByTestId('folder-icon')).toBeInTheDocument();
    });
});

describe('SidebarSeparator', () => {
    it('renders separator', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarItem to="/">Home</SidebarItem>
                    <SidebarSeparator />
                    <SidebarItem to="/settings">Settings</SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        expect(screen.getByRole('separator')).toBeInTheDocument();
    });

    it('applies custom className', () => {
        render(
            <Sidebar>
                <SidebarContent>
                    <SidebarSeparator className="custom-separator" />
                </SidebarContent>
            </Sidebar>,
        );

        const separator = screen.getByRole('separator');
        expect(separator).toHaveClass('sidebar-separator');
        expect(separator).toHaveClass('custom-separator');
    });
});

describe('Sidebar Integration', () => {
    it('renders a complete sidebar layout', () => {
        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarGroup label="Main">
                        <SidebarItem to="/" icon={<HouseIcon />} active>
                            Dashboard
                        </SidebarItem>
                        <SidebarItem to="/library" icon={<FolderIcon />}>
                            Library
                        </SidebarItem>
                    </SidebarGroup>
                    <SidebarSeparator />
                    <SidebarGroup label="Admin">
                        <SidebarSubMenu label="Settings" icon={<GearIcon />}>
                            <SidebarItem to="/settings/general">General</SidebarItem>
                            <SidebarItem to="/settings/users" icon={<UsersIcon />}>
                                Users
                            </SidebarItem>
                        </SidebarSubMenu>
                    </SidebarGroup>
                </SidebarContent>
                <SidebarFooter>
                    <span>v1.0.0</span>
                </SidebarFooter>
            </Sidebar>,
        );

        expect(screen.getByText('Main')).toBeInTheDocument();
        expect(screen.getByText('Admin')).toBeInTheDocument();

        expect(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Library' })).toBeInTheDocument();

        expect(screen.getByRole('button', { name: 'Settings' })).toBeInTheDocument();

        expect(screen.getByRole('separator')).toBeInTheDocument();

        expect(screen.getByText('v1.0.0')).toBeInTheDocument();
    });

    it('supports keyboard navigation', async () => {
        const user = userEvent.setup();

        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarItem to="/">Home</SidebarItem>
                    <SidebarItem to="/about">About</SidebarItem>
                    <SidebarItem to="/contact">Contact</SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        const homeLink = screen.getByRole('link', { name: 'Home' });
        homeLink.focus();

        expect(homeLink).toHaveFocus();

        await user.tab();
        expect(screen.getByRole('link', { name: 'About' })).toHaveFocus();

        await user.tab();
        expect(screen.getByRole('link', { name: 'Contact' })).toHaveFocus();
    });

    it('forwards ref to Sidebar root', () => {
        const ref = vi.fn();

        render(
            <Sidebar ref={ref}>
                <SidebarContent>
                    <span>Content</span>
                </SidebarContent>
            </Sidebar>,
        );

        expect(ref).toHaveBeenCalled();
        expect(ref.mock.calls[0][0]).toBeInstanceOf(HTMLElement);
    });

    it('forwards ref to SidebarItem', () => {
        const ref = vi.fn();

        renderWithRouter(
            <Sidebar>
                <SidebarContent>
                    <SidebarItem to="/" ref={ref}>
                        Home
                    </SidebarItem>
                </SidebarContent>
            </Sidebar>,
        );

        expect(ref).toHaveBeenCalled();
        expect(ref.mock.calls[0][0]).toBeInstanceOf(HTMLAnchorElement);
    });
});

it('does not change internal group state while controlled', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const { rerender } = render(
        <SidebarGroup label="Group" defaultOpen={false} open={false} onOpenChange={onChange}>
            Child
        </SidebarGroup>,
    );
    await user.click(screen.getByRole('button', { name: 'Group' }));
    expect(onChange).toHaveBeenCalledWith(true);
    rerender(
        <SidebarGroup label="Group" defaultOpen={false}>
            Child
        </SidebarGroup>,
    );
    expect(screen.getByRole('button', { name: 'Group' })).toHaveAttribute('aria-expanded', 'false');
});
