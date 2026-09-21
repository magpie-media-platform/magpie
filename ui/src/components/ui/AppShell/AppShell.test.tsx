import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

import { AppShell, AppShellHeader, AppShellSidebar, AppShellMain, AppShellBottomNav, useAppShell } from './AppShell';

describe('AppShell', () => {
    it('renders children', () => {
        render(
            <AppShell>
                <div data-testid="content">Content</div>
            </AppShell>,
        );

        expect(screen.getByTestId('content')).toBeInTheDocument();
    });

    it('applies custom className', () => {
        render(<AppShell className="custom-class" data-testid="shell" />);

        expect(screen.getByTestId('shell')).toHaveClass('app-shell', 'custom-class');
    });

    it('sets data-sidebar-collapsed based on prop', () => {
        render(<AppShell sidebarCollapsed data-testid="shell" />);

        expect(screen.getByTestId('shell')).toHaveAttribute('data-sidebar-collapsed', 'true');
    });

    it('sets data-sidebar-collapsed to false by default', () => {
        render(<AppShell data-testid="shell" />);

        expect(screen.getByTestId('shell')).toHaveAttribute('data-sidebar-collapsed', 'false');
    });

    it('calls onSidebarCollapsedChange when collapsed state changes', () => {
        const onCollapsedChange = vi.fn();

        function TestComponent() {
            const { toggleSidebarCollapsed } = useAppShell();
            return <button onClick={toggleSidebarCollapsed}>Toggle</button>;
        }

        render(
            <AppShell onSidebarCollapsedChange={onCollapsedChange}>
                <TestComponent />
            </AppShell>,
        );

        fireEvent.click(screen.getByText('Toggle'));
        expect(onCollapsedChange).toHaveBeenCalledWith(true);
    });

    it('respects defaultSidebarCollapsed prop', () => {
        render(<AppShell defaultSidebarCollapsed data-testid="shell" />);

        expect(screen.getByTestId('shell')).toHaveAttribute('data-sidebar-collapsed', 'true');
    });

    it('sets data-sidebar-open to false by default', () => {
        render(<AppShell data-testid="shell" />);

        expect(screen.getByTestId('shell')).toHaveAttribute('data-sidebar-open', 'false');
    });
});

describe('AppShellHeader', () => {
    it('renders children', () => {
        render(
            <AppShell>
                <AppShellHeader>
                    <span data-testid="header-content">Header</span>
                </AppShellHeader>
            </AppShell>,
        );

        expect(screen.getByTestId('header-content')).toBeInTheDocument();
    });

    it('applies custom className', () => {
        render(
            <AppShell>
                <AppShellHeader className="custom-header" data-testid="header" />
            </AppShell>,
        );

        expect(screen.getByTestId('header')).toHaveClass('app-shell__header', 'custom-header');
    });
});

describe('AppShellSidebar', () => {
    it('renders children', () => {
        render(
            <AppShell>
                <AppShellSidebar>
                    <span data-testid="sidebar-content">Sidebar</span>
                </AppShellSidebar>
            </AppShell>,
            { wrapper: MemoryRouter },
        );

        expect(screen.getByTestId('sidebar-content')).toBeInTheDocument();
    });

    it('has navigation role and aria-label', () => {
        render(
            <AppShell>
                <AppShellSidebar ariaLabel="Test navigation" />
            </AppShell>,
            { wrapper: MemoryRouter },
        );

        const sidebar = screen.getByRole('navigation', { name: 'Test navigation' });
        expect(sidebar).toBeInTheDocument();
    });

    it('sets data-open based on sidebar state', () => {
        function TestComponent() {
            const { setSidebarOpen } = useAppShell();
            return <button onClick={() => setSidebarOpen(true)}>Open</button>;
        }

        render(
            <AppShell>
                <AppShellSidebar data-testid="sidebar" />
                <TestComponent />
            </AppShell>,
            { wrapper: MemoryRouter },
        );

        expect(screen.getByTestId('sidebar')).toHaveAttribute('data-open', 'false');

        fireEvent.click(screen.getByText('Open'));

        expect(screen.getByTestId('sidebar')).toHaveAttribute('data-open', 'true');
    });

    it('renders backdrop element', () => {
        render(
            <AppShell>
                <AppShellSidebar />
            </AppShell>,
            { wrapper: MemoryRouter },
        );

        const backdrop = document.querySelector('.app-shell__sidebar-backdrop');
        expect(backdrop).toBeInTheDocument();
    });

    it('closes sidebar when backdrop is clicked', () => {
        function TestComponent() {
            const { setSidebarOpen, sidebarOpen } = useAppShell();
            return (
                <>
                    <button onClick={() => setSidebarOpen(true)}>Open</button>
                    <span data-testid="state">{sidebarOpen.toString()}</span>
                </>
            );
        }

        render(
            <AppShell>
                <AppShellSidebar data-testid="sidebar" />
                <TestComponent />
            </AppShell>,
            { wrapper: MemoryRouter },
        );

        fireEvent.click(screen.getByText('Open'));
        expect(screen.getByTestId('state')).toHaveTextContent('true');

        const backdrop = document.querySelector('.app-shell__sidebar-backdrop');
        fireEvent.click(backdrop!);

        expect(screen.getByTestId('state')).toHaveTextContent('false');
    });
});

describe('AppShellMain', () => {
    it('renders as main element', () => {
        render(
            <AppShell>
                <AppShellMain>Content</AppShellMain>
            </AppShell>,
        );

        expect(screen.getByRole('main')).toBeInTheDocument();
    });

    it('renders children', () => {
        render(
            <AppShell>
                <AppShellMain>
                    <span data-testid="main-content">Main</span>
                </AppShellMain>
            </AppShell>,
        );

        expect(screen.getByTestId('main-content')).toBeInTheDocument();
    });

    it('applies custom className', () => {
        render(
            <AppShell>
                <AppShellMain className="custom-main" data-testid="main" />
            </AppShell>,
        );

        expect(screen.getByTestId('main')).toHaveClass('app-shell__main', 'custom-main');
    });
});

describe('AppShellBottomNav', () => {
    it('renders children', () => {
        render(
            <AppShell>
                <AppShellBottomNav>
                    <span data-testid="bottom-nav-content">Nav</span>
                </AppShellBottomNav>
            </AppShell>,
        );

        expect(screen.getByTestId('bottom-nav-content')).toBeInTheDocument();
    });

    it('applies custom className', () => {
        render(
            <AppShell>
                <AppShellBottomNav className="custom-bottom-nav" data-testid="bottom-nav" />
            </AppShell>,
        );

        expect(screen.getByTestId('bottom-nav')).toHaveClass('app-shell__bottom-nav', 'custom-bottom-nav');
    });
});

describe('useAppShell', () => {
    it('throws error when used outside AppShell', () => {
        function TestComponent() {
            useAppShell();
            return null;
        }

        expect(() => render(<TestComponent />)).toThrow('Shell components must be used within a Shell');
    });

    it('provides toggleSidebar function', () => {
        function TestComponent() {
            const { sidebarCollapsed, toggleSidebar } = useAppShell();
            return (
                <>
                    <button onClick={toggleSidebar}>Toggle</button>
                    <span data-testid="state">{sidebarCollapsed.toString()}</span>
                </>
            );
        }

        render(
            <AppShell>
                <TestComponent />
            </AppShell>,
        );

        expect(screen.getByTestId('state')).toHaveTextContent('false');

        fireEvent.click(screen.getByText('Toggle'));

        expect(screen.getByTestId('state')).toHaveTextContent('true');
    });

    it('provides closeSidebar function', () => {
        function TestComponent() {
            const { sidebarOpen, setSidebarOpen, closeSidebar } = useAppShell();
            return (
                <>
                    <button onClick={() => setSidebarOpen(true)}>Open</button>
                    <button onClick={closeSidebar}>Close</button>
                    <span data-testid="state">{sidebarOpen.toString()}</span>
                </>
            );
        }

        render(
            <AppShell>
                <TestComponent />
            </AppShell>,
        );

        fireEvent.click(screen.getByText('Open'));
        expect(screen.getByTestId('state')).toHaveTextContent('true');

        fireEvent.click(screen.getByText('Close'));
        expect(screen.getByTestId('state')).toHaveTextContent('false');
    });

    it('provides toggleSidebarCollapsed function', () => {
        function TestComponent() {
            const { sidebarCollapsed, toggleSidebarCollapsed } = useAppShell();
            return (
                <>
                    <button onClick={toggleSidebarCollapsed}>Toggle Collapsed</button>
                    <span data-testid="state">{sidebarCollapsed.toString()}</span>
                </>
            );
        }

        render(
            <AppShell>
                <TestComponent />
            </AppShell>,
        );

        expect(screen.getByTestId('state')).toHaveTextContent('false');

        fireEvent.click(screen.getByText('Toggle Collapsed'));

        expect(screen.getByTestId('state')).toHaveTextContent('true');
    });

    it('provides setSidebarOpen function', () => {
        function TestComponent() {
            const { sidebarOpen, setSidebarOpen } = useAppShell();
            return (
                <>
                    <button onClick={() => setSidebarOpen(true)}>Open</button>
                    <span data-testid="state">{sidebarOpen.toString()}</span>
                </>
            );
        }

        render(
            <AppShell>
                <TestComponent />
            </AppShell>,
        );

        expect(screen.getByTestId('state')).toHaveTextContent('false');

        fireEvent.click(screen.getByText('Open'));

        expect(screen.getByTestId('state')).toHaveTextContent('true');
    });

    it('provides setSidebarCollapsed function', () => {
        function TestComponent() {
            const { sidebarCollapsed, setSidebarCollapsed } = useAppShell();
            return (
                <>
                    <button onClick={() => setSidebarCollapsed(true)}>Collapse</button>
                    <span data-testid="state">{sidebarCollapsed.toString()}</span>
                </>
            );
        }

        render(
            <AppShell>
                <TestComponent />
            </AppShell>,
        );

        expect(screen.getByTestId('state')).toHaveTextContent('false');

        fireEvent.click(screen.getByText('Collapse'));

        expect(screen.getByTestId('state')).toHaveTextContent('true');
    });
});
