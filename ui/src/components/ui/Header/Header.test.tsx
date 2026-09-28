import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Header } from './Header';

describe('Header', () => {
    it('fits the phone header actions without horizontal overflow', () => {
        // Arrange
        const css = readFileSync(resolve(process.cwd(), 'src/components/ui/Header/Header.css'), 'utf8');

        // Act
        const phone = css.slice(css.indexOf('@media (max-width: 767px)'));

        // Assert
        expect(phone).toMatch(/\.header__start,\s*\.header__end \{\s*gap: var\(--space-xs\);/);
        expect(phone).toMatch(
            /\.header__left-slot:has\(> \.slot:empty\),\s*\.header__right-slot:has\(> \.slot:empty\) \{\s*display: none;/,
        );
        expect(css).toMatch(
            /@media \(max-width: 389px\) \{\s*\.header__brand svg,\s*\.header__brand \.logo \{\s*display: none;/,
        );
        expect(css).toMatch(/@media \(max-width: 359px\) \{\s*\.header__brand \{\s*display: none;/);
    });

    it('renders as a header element', () => {
        render(<Header data-testid="header" />);
        const header = screen.getByTestId('header');
        expect(header.tagName).toBe('HEADER');
    });

    it('applies header class', () => {
        render(<Header data-testid="header" />);
        expect(screen.getByTestId('header')).toHaveClass('header');
    });

    it('applies custom className', () => {
        render(<Header className="custom-class" data-testid="header" />);
        expect(screen.getByTestId('header')).toHaveClass('custom-class');
    });

    it('renders brand content', () => {
        render(<Header brand="Magpie" />);
        expect(screen.getByText('Magpie')).toBeInTheDocument();
    });

    it('renders brand as ReactNode', () => {
        render(<Header brand={<span data-testid="logo">Logo</span>} />);
        expect(screen.getByTestId('logo')).toBeInTheDocument();
    });

    it('brand has header__brand class', () => {
        render(<Header brand="App" data-testid="header" />);
        const header = screen.getByTestId('header');
        const brand = header.querySelector('.header__brand');
        expect(brand).toBeInTheDocument();
        expect(brand).toHaveTextContent('App');
    });

    it('renders status content', () => {
        render(<Header status={<span data-testid="status">Healthy</span>} />);
        expect(screen.getByTestId('status')).toBeInTheDocument();
    });

    it('status has header__status class', () => {
        render(<Header status="OK" data-testid="header" />);
        const header = screen.getByTestId('header');
        const status = header.querySelector('.header__status');
        expect(status).toBeInTheDocument();
    });

    it('renders actions content', () => {
        render(<Header actions={<button>Settings</button>} />);
        expect(screen.getByRole('button', { name: 'Settings' })).toBeInTheDocument();
    });

    it('actions has header__end class', () => {
        render(<Header actions={<button>Action</button>} data-testid="header" />);
        const header = screen.getByTestId('header');
        const end = header.querySelector('.header__end');
        expect(end).toBeInTheDocument();
        expect(end).toContainElement(screen.getByRole('button', { name: 'Action' }));
    });

    it('renders slot content', () => {
        render(<Header slotContent={<div data-testid="slot">Extension</div>} />);
        expect(screen.getByTestId('slot')).toBeInTheDocument();
    });

    it('slot has header__slot class', () => {
        render(<Header slotContent="Slot" data-testid="header" />);
        const header = screen.getByTestId('header');
        const slot = header.querySelector('.header__slot');
        expect(slot).toBeInTheDocument();
    });

    it('renders sidebar toggle button when onSidebarToggle provided', () => {
        render(<Header onSidebarToggle={() => {}} />);
        expect(screen.getByRole('button', { name: /sidebar/i })).toBeInTheDocument();
    });

    it('does not render sidebar toggle button without onSidebarToggle', () => {
        render(<Header brand="App" />);
        expect(screen.queryByRole('button', { name: /sidebar/i })).not.toBeInTheDocument();
    });

    it('calls onSidebarToggle when toggle button clicked', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();

        render(<Header onSidebarToggle={handleClick} />);
        await user.click(screen.getByRole('button', { name: /sidebar/i }));

        expect(handleClick).toHaveBeenCalledTimes(1);
    });

    it('toggle button has correct aria-expanded when sidebar closed', () => {
        render(<Header onSidebarToggle={() => {}} sidebarOpen={false} />);
        expect(screen.getByRole('button', { name: 'Expand sidebar' })).toHaveAttribute('aria-expanded', 'false');
    });

    it('toggle button has correct aria-expanded when sidebar open', () => {
        render(<Header onSidebarToggle={() => {}} sidebarOpen={true} />);
        expect(screen.getByRole('button', { name: 'Collapse sidebar' })).toHaveAttribute('aria-expanded', 'true');
    });

    it('toggle button has aria-controls', () => {
        render(<Header onSidebarToggle={() => {}} />);
        expect(screen.getByRole('button', { name: /sidebar/i })).toHaveAttribute('aria-controls', 'navigation-menu');
    });

    it('toggle button has header__sidebar-toggle class', () => {
        render(<Header onSidebarToggle={() => {}} />);
        const button = screen.getByRole('button', { name: /sidebar/i });
        expect(button).toHaveClass('header__sidebar-toggle');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<Header ref={ref} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLElement));
    });

    it('passes through additional props', () => {
        render(<Header data-testid="header" aria-label="Main header" />);
        expect(screen.getByTestId('header')).toHaveAttribute('aria-label', 'Main header');
    });

    it('renders all sections together', () => {
        render(
            <Header
                brand="Magpie"
                status={<span>Healthy</span>}
                actions={<button>Profile</button>}
                slotContent={<div>Extension</div>}
                onSidebarToggle={() => {}}
                data-testid="header"
            />,
        );

        expect(screen.getByText('Magpie')).toBeInTheDocument();
        expect(screen.getByText('Healthy')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Profile' })).toBeInTheDocument();
        expect(screen.getByText('Extension')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /sidebar/i })).toBeInTheDocument();
    });

    it('does not render status section when no status provided', () => {
        render(<Header brand="App" data-testid="header" />);
        const header = screen.getByTestId('header');
        expect(header.querySelector('.header__status')).not.toBeInTheDocument();
    });

    it('does not render slot section when no slotContent provided', () => {
        render(<Header brand="App" data-testid="header" />);
        const header = screen.getByTestId('header');
        expect(header.querySelector('.header__slot')).not.toBeInTheDocument();
    });

    it('does not render actions section when no actions provided', () => {
        render(<Header brand="App" data-testid="header" />);
        const header = screen.getByTestId('header');
        expect(header.querySelector('.header__end')).not.toBeInTheDocument();
    });
});
