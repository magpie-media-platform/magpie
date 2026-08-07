import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { HouseIcon, MagnifyingGlassIcon, DownloadSimpleIcon, GearIcon, ListIcon } from '@phosphor-icons/react';

import { BottomNav, BottomNavItem, BottomNavButton } from './BottomNav';

function renderWithRouter(ui: React.ReactElement) {
    return render(<MemoryRouter>{ui}</MemoryRouter>);
}

describe('BottomNav', () => {
    it('renders as a nav element', () => {
        render(<BottomNav data-testid="nav" />);
        const nav = screen.getByTestId('nav');
        expect(nav.tagName).toBe('NAV');
    });

    it('applies bottom-nav class', () => {
        render(<BottomNav data-testid="nav" />);
        expect(screen.getByTestId('nav')).toHaveClass('bottom-nav');
    });

    it('applies custom className', () => {
        render(<BottomNav className="custom-class" data-testid="nav" />);
        expect(screen.getByTestId('nav')).toHaveClass('bottom-nav', 'custom-class');
    });

    it('has aria-label for accessibility', () => {
        render(<BottomNav />);
        expect(screen.getByRole('navigation')).toHaveAttribute('aria-label', 'Primary navigation');
    });

    it('renders children', () => {
        renderWithRouter(
            <BottomNav>
                <BottomNavItem icon={<HouseIcon />} label="Home" to="/" />
            </BottomNav>,
        );
        expect(screen.getByText('Home')).toBeInTheDocument();
    });

    it('renders slot content', () => {
        render(<BottomNav slotContent={<div data-testid="slot">Extension</div>} />);
        expect(screen.getByTestId('slot')).toBeInTheDocument();
    });

    it('renders children and slot content together', () => {
        renderWithRouter(
            <BottomNav slotContent={<div data-testid="slot">Extension</div>}>
                <BottomNavItem icon={<HouseIcon />} label="Home" to="/" />
            </BottomNav>,
        );
        expect(screen.getByText('Home')).toBeInTheDocument();
        expect(screen.getByTestId('slot')).toBeInTheDocument();
    });

    it('has container div with correct class', () => {
        render(<BottomNav data-testid="nav" />);
        const nav = screen.getByTestId('nav');
        const container = nav.querySelector('.bottom-nav__container');
        expect(container).toBeInTheDocument();
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<BottomNav ref={ref} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLElement));
    });

    it('passes through additional props', () => {
        render(<BottomNav data-testid="nav" id="main-nav" />);
        expect(screen.getByTestId('nav')).toHaveAttribute('id', 'main-nav');
    });
});

describe('BottomNavItem', () => {
    it('renders as an anchor element', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/" />);
        const link = screen.getByRole('link');
        expect(link.tagName).toBe('A');
    });

    it('applies bottom-nav-item class', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/" />);
        expect(screen.getByRole('link')).toHaveClass('bottom-nav-item');
    });

    it('applies custom className', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/" className="custom-item" />);
        expect(screen.getByRole('link')).toHaveClass('bottom-nav-item', 'custom-item');
    });

    it('renders icon', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon data-testid="icon" />} label="Home" to="/" />);
        expect(screen.getByTestId('icon')).toBeInTheDocument();
    });

    it('icon container has aria-hidden', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" data-testid="item" to="/" />);
        const item = screen.getByTestId('item');
        const iconContainer = item.querySelector('.bottom-nav-item__icon');
        expect(iconContainer).toHaveAttribute('aria-hidden', 'true');
    });

    it('renders label for screen readers by default', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/" />);
        expect(screen.getByText('Home')).toBeInTheDocument();
    });

    it('shows label visually by default', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/" />);
        const label = screen.getByText('Home');
        expect(label).toHaveClass('bottom-nav-item__label');
        expect(screen.getByRole('link')).toHaveAttribute('data-show-label', 'true');
    });

    it('hides label visually when showLabel is false', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/" showLabel={false} />);
        screen.getByText('Home');
        expect(screen.getByRole('link')).toHaveAttribute('data-show-label', 'false');
    });

    it('applies active class when active', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/" active />);
        expect(screen.getByRole('link')).toHaveAttribute('data-active', 'true');
    });

    it('does not apply active class when not active', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/" />);
        expect(screen.getByRole('link')).not.toHaveAttribute('data-active', 'true');
    });

    it('has aria-current="page" when active', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/" active />);
        expect(screen.getByRole('link')).toHaveAttribute('aria-current', 'page');
    });

    it('does not have aria-current when not active', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="About" to="/about" />);
        expect(screen.getByRole('link')).not.toHaveAttribute('aria-current');
    });

    it('passes to prop as href to anchor', () => {
        renderWithRouter(<BottomNavItem icon={<HouseIcon />} label="Home" to="/home" />);
        expect(screen.getByRole('link')).toHaveAttribute('href', '/home');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        renderWithRouter(<BottomNavItem ref={ref} icon={<HouseIcon />} label="Home" to="/" />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLAnchorElement));
    });

    it('passes through additional props', () => {
        renderWithRouter(
            <BottomNavItem icon={<HouseIcon />} label="Home" to="/" data-testid="nav-item" title="Go to home" />,
        );
        expect(screen.getByTestId('nav-item')).toHaveAttribute('title', 'Go to home');
    });
});

describe('BottomNavButton', () => {
    it('renders as a button element', () => {
        render(<BottomNavButton icon={<ListIcon />} label="More" />);
        const btn = screen.getByRole('button');
        expect(btn.tagName).toBe('BUTTON');
    });

    it('applies bottom-nav-item class', () => {
        render(<BottomNavButton icon={<ListIcon />} label="More" />);
        expect(screen.getByRole('button')).toHaveClass('bottom-nav-item');
    });

    it('applies active class when active', () => {
        render(<BottomNavButton icon={<ListIcon />} label="More" active />);
        expect(screen.getByRole('button')).toHaveClass('bottom-nav-item--active');
        expect(screen.getByRole('button')).toHaveAttribute('data-active', 'true');
    });

    it('sets aria-pressed when active', () => {
        render(<BottomNavButton icon={<ListIcon />} label="More" active />);
        expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true');
    });

    it('renders label visually by default', () => {
        render(<BottomNavButton icon={<ListIcon />} label="More" />);
        expect(screen.getByText('More')).toHaveClass('bottom-nav-item__label');
    });

    it('calls onClick when clicked', async () => {
        const user = userEvent.setup();
        const onClick = vi.fn();
        render(<BottomNavButton icon={<ListIcon />} label="More" onClick={onClick} />);
        await user.click(screen.getByRole('button'));
        expect(onClick).toHaveBeenCalledTimes(1);
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<BottomNavButton ref={ref} icon={<ListIcon />} label="More" />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLButtonElement));
    });

    it('passes through additional props', () => {
        render(<BottomNavButton icon={<ListIcon />} label="More" aria-label="Open menu" />);
        expect(screen.getByRole('button')).toHaveAttribute('aria-label', 'Open menu');
    });
});

describe('BottomNav with multiple items', () => {
    it('renders multiple navigation items', () => {
        renderWithRouter(
            <BottomNav>
                <BottomNavItem icon={<HouseIcon />} label="Home" to="/" active />
                <BottomNavItem icon={<MagnifyingGlassIcon />} label="Search" to="/search" />
                <BottomNavItem icon={<DownloadSimpleIcon />} label="Downloads" to="/downloads" />
                <BottomNavItem icon={<GearIcon />} label="Settings" to="/settings" />
            </BottomNav>,
        );

        expect(screen.getByText('Home')).toBeInTheDocument();
        expect(screen.getByText('Search')).toBeInTheDocument();
        expect(screen.getByText('Downloads')).toBeInTheDocument();
        expect(screen.getByText('Settings')).toBeInTheDocument();
    });

    it('only one item is active at a time', () => {
        renderWithRouter(
            <BottomNav>
                <BottomNavItem icon={<HouseIcon />} label="Home" to="/" active />
                <BottomNavItem icon={<MagnifyingGlassIcon />} label="Search" to="/search" />
                <BottomNavItem icon={<DownloadSimpleIcon />} label="Downloads" to="/downloads" />
            </BottomNav>,
        );

        const links = screen.getAllByRole('link');
        const activeLinks = links.filter((link) => link.getAttribute('data-active') === 'true');

        expect(activeLinks).toHaveLength(1);
        expect(screen.getByText('Home').closest('a')).toHaveAttribute('data-active', 'true');
    });

    it('renders all items with visible labels', () => {
        renderWithRouter(
            <BottomNav>
                <BottomNavItem icon={<HouseIcon />} label="Home" to="/" />
                <BottomNavItem icon={<MagnifyingGlassIcon />} label="Search" to="/search" />
            </BottomNav>,
        );

        expect(screen.getByText('Home')).toHaveClass('bottom-nav-item__label');
        expect(screen.getByText('Search')).toHaveClass('bottom-nav-item__label');
    });
});
