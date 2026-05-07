import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { AuthProvider, Role } from '@/auth';
import { UserMenu } from './UserMenu';

const mockUser = {
    id: 'user-1',
    username: 'testuser',
    role: Role.Admin,
};

function renderWithProviders(ui: React.ReactElement, options?: { user?: typeof mockUser | null }) {
    const user = options?.user === undefined ? mockUser : options.user;
    return render(
        <MemoryRouter>
            <AuthProvider initialUser={user} skipSessionInit>
                {ui}
            </AuthProvider>
        </MemoryRouter>,
    );
}

describe('UserMenu', () => {
    it('renders nothing when user is not logged in', () => {
        renderWithProviders(<UserMenu />, { user: null });
        expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });

    it('renders user menu trigger when logged in', () => {
        renderWithProviders(<UserMenu />);
        expect(screen.getByRole('button', { name: /user menu for testuser/i })).toBeInTheDocument();
    });

    it('shows avatar fallback with user initials', () => {
        renderWithProviders(<UserMenu />);
        expect(screen.getByText('T')).toBeInTheDocument();
    });

    it('shows username on trigger by default', () => {
        renderWithProviders(<UserMenu />);
        expect(screen.getByText('testuser')).toBeInTheDocument();
    });

    it('opens dropdown menu when clicked', async () => {
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);

        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));

        await waitFor(() => {
            expect(screen.getByRole('menu')).toBeInTheDocument();
        });
    });

    it('shows logout option in dropdown', async () => {
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);

        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));

        await waitFor(() => {
            expect(screen.getByRole('menuitem', { name: /log out/i })).toBeInTheDocument();
        });
    });

    it('shows username label in dropdown', async () => {
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);

        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));

        await waitFor(() => {
            expect(screen.getAllByText('testuser').length).toBeGreaterThan(1);
        });
    });

    it('applies custom className', () => {
        renderWithProviders(<UserMenu className="custom-class" />);
        const menuContainer = screen.getByRole('button', { name: /user menu for testuser/i }).closest('.user-menu');
        expect(menuContainer).toHaveClass('custom-class');
    });

    it('has data-component attribute', () => {
        renderWithProviders(<UserMenu />);
        const menuContainer = screen
            .getByRole('button', { name: /user menu for testuser/i })
            .closest('[data-component="user-menu"]');
        expect(menuContainer).toBeInTheDocument();
    });

    it('handles user with multi-word username', () => {
        const multiWordUser = {
            id: 'user-2',
            username: 'John Doe',
            role: Role.Admin,
        };
        renderWithProviders(<UserMenu />, { user: multiWordUser });

        expect(screen.getByText('JD')).toBeInTheDocument();
    });
});
