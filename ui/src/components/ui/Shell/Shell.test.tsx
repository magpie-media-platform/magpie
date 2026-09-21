import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Link, MemoryRouter } from 'react-router-dom';

import { Shell, ShellHeader, ShellSidebar, ShellMain, ShellBottomNav, useShell } from './Shell';

function TestShell({ onSidebarCollapsedChange }: { onSidebarCollapsedChange?: (c: boolean) => void } = {}) {
    return (
        <MemoryRouter>
            <Shell onSidebarCollapsedChange={onSidebarCollapsedChange}>
                <ShellHeader>Header</ShellHeader>
                <ShellSidebar>Sidebar</ShellSidebar>
                <ShellMain>Main content</ShellMain>
                <ShellBottomNav>Bottom nav</ShellBottomNav>
            </Shell>
        </MemoryRouter>
    );
}

function ShellConsumer() {
    const { sidebarOpen, sidebarCollapsed, toggleSidebar } = useShell();
    return (
        <div>
            <span data-testid="sidebar-open">{String(sidebarOpen)}</span>
            <span data-testid="sidebar-collapsed">{String(sidebarCollapsed)}</span>
            <button type="button" onClick={toggleSidebar}>
                Toggle
            </button>
        </div>
    );
}

describe('Shell', () => {
    it('renders all sub-components', () => {
        render(<TestShell />);
        expect(screen.getByText('Header')).toBeInTheDocument();
        expect(screen.getByText('Sidebar')).toBeInTheDocument();
        expect(screen.getByText('Main content')).toBeInTheDocument();
        expect(screen.getByText('Bottom nav')).toBeInTheDocument();
    });

    it('sets data-component attributes on sub-components', () => {
        render(<TestShell />);
        expect(screen.getByText('Header').closest('[data-component="shell-header"]')).toBeInTheDocument();
        expect(screen.getByRole('navigation')).toHaveAttribute('data-component', 'shell-sidebar');
        expect(screen.getByRole('main')).toHaveAttribute('data-component', 'shell-main');
    });

    it('defaults sidebar to closed and not collapsed', () => {
        const { container } = render(<TestShell />);
        const root = container.firstElementChild!;
        expect(root).toHaveAttribute('data-sidebar-open', 'false');
        expect(root).toHaveAttribute('data-sidebar-collapsed', 'false');
    });

    it('supports defaultSidebarCollapsed prop', () => {
        const { container } = render(
            <Shell defaultSidebarCollapsed>
                <ShellMain>Content</ShellMain>
            </Shell>,
        );
        expect(container.firstElementChild).toHaveAttribute('data-sidebar-collapsed', 'true');
    });

    it('supports controlled sidebarCollapsed prop', () => {
        const { container, rerender } = render(
            <Shell sidebarCollapsed={false}>
                <ShellMain>Content</ShellMain>
            </Shell>,
        );
        expect(container.firstElementChild).toHaveAttribute('data-sidebar-collapsed', 'false');

        rerender(
            <Shell sidebarCollapsed={true}>
                <ShellMain>Content</ShellMain>
            </Shell>,
        );
        expect(container.firstElementChild).toHaveAttribute('data-sidebar-collapsed', 'true');
    });

    it('applies custom classPrefix', () => {
        render(
            <Shell classPrefix="admin">
                <ShellMain classPrefix="admin">Content</ShellMain>
            </Shell>,
        );
        expect(screen.getByRole('main')).toHaveAttribute('data-component', 'admin-main');
        expect(screen.getByRole('main').firstElementChild).toHaveAttribute('data-component', 'admin-main-wrapper');
    });

    it('ShellSidebar renders a nav element with aria-label', () => {
        render(<TestShell />);
        const nav = screen.getByRole('navigation');
        expect(nav).toHaveAttribute('aria-label', 'Main navigation');
        expect(nav).toHaveAttribute('id', 'navigation-menu');
    });

    it('ShellMain renders a main element with tabIndex', () => {
        render(<TestShell />);
        const main = screen.getByRole('main');
        expect(main).toHaveAttribute('id', 'main-content');
        expect(main).toHaveAttribute('tabindex', '-1');
    });

    it('calls onSidebarCollapsedChange when sidebar collapsed state changes', async () => {
        const onChange = vi.fn();

        function TestWithConsumer() {
            return (
                <Shell onSidebarCollapsedChange={onChange}>
                    <ShellConsumer />
                </Shell>
            );
        }

        Object.defineProperty(window, 'matchMedia', {
            writable: true,
            value: vi.fn().mockReturnValue({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() }),
        });

        const user = userEvent.setup();
        render(<TestWithConsumer />);
        await user.click(screen.getByText('Toggle'));
        expect(onChange).toHaveBeenCalledWith(true);
    });
});

describe('ShellSidebar on mobile', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('keeps the header usable so its toggle can close the open sidebar', async () => {
        vi.stubGlobal(
            'matchMedia',
            vi.fn((query: string) => ({
                matches: query === '(max-width: 767px)',
                media: query,
                addEventListener: () => {},
                removeEventListener: () => {},
            })),
        );

        const user = userEvent.setup();
        render(
            <MemoryRouter>
                <Shell>
                    <ShellHeader>
                        <ShellConsumer />
                    </ShellHeader>
                    <ShellSidebar>
                        <a href="/first">First</a>
                    </ShellSidebar>
                    <ShellMain>Main content</ShellMain>
                </Shell>
            </MemoryRouter>,
        );

        await user.click(screen.getByText('Toggle'));

        const sidebar = screen.getByRole('navigation', { name: 'Main navigation' });
        expect(sidebar).toHaveAttribute('data-open', 'true');
        expect(sidebar).not.toHaveAttribute('aria-modal');
        expect(screen.getByRole('link', { name: 'First' })).toHaveFocus();
        expect(screen.getByText('Toggle').closest('[data-component="shell-header"]')).not.toHaveProperty('inert', true);
        expect(screen.getByRole('main')).toHaveProperty('inert', true);

        await user.tab();
        expect(screen.getByRole('link', { name: 'First' })).not.toHaveFocus();

        await user.click(screen.getByText('Toggle'));

        expect(screen.getByTestId('sidebar-open')).toHaveTextContent('false');
        expect(sidebar).toHaveAttribute('data-open', 'false');
    });

    it('closes the open sidebar and unlocks scrolling when the route changes', async () => {
        vi.stubGlobal(
            'matchMedia',
            vi.fn((query: string) => ({
                matches: query === '(max-width: 767px)',
                media: query,
                addEventListener: () => {},
                removeEventListener: () => {},
            })),
        );

        const user = userEvent.setup();
        render(
            <MemoryRouter initialEntries={['/']}>
                <Shell>
                    <ShellHeader>
                        <ShellConsumer />
                        <Link to="/elsewhere">Elsewhere</Link>
                    </ShellHeader>
                    <ShellSidebar>
                        <a href="/first">First</a>
                    </ShellSidebar>
                    <ShellMain>Main content</ShellMain>
                </Shell>
            </MemoryRouter>,
        );

        await user.click(screen.getByText('Toggle'));
        expect(document.body.style.overflow).toBe('hidden');

        await user.click(screen.getByRole('link', { name: 'Elsewhere' }));

        expect(screen.getByTestId('sidebar-open')).toHaveTextContent('false');
        expect(screen.getByRole('navigation', { name: 'Main navigation' })).toHaveAttribute('data-open', 'false');
        expect(document.body.style.overflow).toBe('');
        expect(screen.getByRole('main')).toHaveProperty('inert', false);
        expect(document.activeElement).not.toBe(document.body);
    });
});

describe('useShell', () => {
    it('throws when used outside Shell', () => {
        const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
        expect(() => render(<ShellConsumer />)).toThrow('Shell components must be used within a Shell');
        spy.mockRestore();
    });

    it('provides sidebar state', () => {
        render(
            <Shell>
                <ShellConsumer />
            </Shell>,
        );
        expect(screen.getByTestId('sidebar-open')).toHaveTextContent('false');
        expect(screen.getByTestId('sidebar-collapsed')).toHaveTextContent('false');
    });
});

it('works when matchMedia is unavailable', async () => {
    const original = window.matchMedia;
    Object.defineProperty(window, 'matchMedia', { configurable: true, value: undefined });
    try {
        const user = userEvent.setup();
        render(
            <MemoryRouter>
                <Shell>
                    <ShellSidebar>Navigation</ShellSidebar>
                    <ShellConsumer />
                </Shell>
            </MemoryRouter>,
        );
        await user.click(screen.getByRole('button', { name: 'Toggle' }));
        expect(screen.getByTestId('sidebar-open')).toHaveTextContent('true');
    } finally {
        Object.defineProperty(window, 'matchMedia', { configurable: true, writable: true, value: original });
    }
});

it('does not change internal collapse state while controlled', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const { rerender, container } = render(
        <Shell sidebarCollapsed={false} onSidebarCollapsedChange={onChange}>
            <ShellConsumer />
        </Shell>,
    );
    await user.click(screen.getByRole('button', { name: 'Toggle' }));
    expect(onChange).toHaveBeenCalledWith(true);
    rerender(
        <Shell>
            <ShellConsumer />
        </Shell>,
    );
    expect(container.firstElementChild).toHaveAttribute('data-sidebar-collapsed', 'false');
});
