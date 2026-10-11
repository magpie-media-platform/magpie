import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { Role } from '@/auth';
import { builtInThemes, initialThemeSettings, setThemeCatalog, setThemePreference } from '@/lib/theme';
import { AuthTestProvider as AuthProvider } from '@/test/AuthTestProvider';
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

describe('UserMenu theme', () => {
    beforeEach(() => {
        setThemeCatalog({ themes: builtInThemes, defaults: initialThemeSettings });
        setThemePreference(null);
    });

    afterEach(() => {
        vi.restoreAllMocks();
        localStorage.clear();
        document.querySelectorAll('[data-theme-stylesheet]').forEach((element) => element.remove());
        delete document.documentElement.dataset.theme;
        delete document.documentElement.dataset.colorScheme;
    });

    it('UserMenu_OpensTheThemeDialogOnTheInstanceDefault_WhenThemeIsSelected', async () => {
        // Arrange
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);
        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Theme…' }));

        // Assert
        expect(screen.getByRole('dialog', { name: 'Theme' })).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: 'Mode' })).toHaveTextContent('Default');
        expect(screen.getByRole('combobox', { name: 'Dark theme' })).toBeDisabled();
        expect(screen.getByRole('combobox', { name: 'Light theme' })).toBeDisabled();
    });

    it('UserMenu_AppliesAndStoresTheMode_WhenAModeIsSelectedInTheThemeDialog', async () => {
        // Arrange
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);
        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));
        await user.click(screen.getByRole('menuitem', { name: 'Theme…' }));
        await user.click(screen.getByRole('combobox', { name: 'Mode' }));

        // Act
        await user.click(screen.getByRole('option', { name: 'Light' }));

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Frost/Light');
        expect(JSON.parse(localStorage.getItem('magpie.theme')!)).toEqual({
            mode: 'Light',
            darkTheme: 'Magpie.Api/Tokyo Night/Dark',
            lightTheme: 'Magpie.Api/Frost/Light',
        });
    });

    it('UserMenu_ClearsTheBrowserOverride_WhenDefaultIsSelectedInTheThemeDialog', async () => {
        // Arrange
        setThemePreference({ ...initialThemeSettings, mode: 'Light' });
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);
        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));
        await user.click(screen.getByRole('menuitem', { name: 'Theme…' }));
        await user.click(screen.getByRole('combobox', { name: 'Mode' }));

        // Act
        await user.click(screen.getByRole('option', { name: 'Default' }));

        // Assert
        expect(localStorage.getItem('magpie.theme')).toBeNull();
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Tokyo Night/Dark');
    });

    it('UserMenu_ClosesTheThemeDialog_WhenDoneIsClicked', async () => {
        // Arrange
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);
        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));
        await user.click(screen.getByRole('menuitem', { name: 'Theme…' }));

        // Act
        await user.click(screen.getByRole('button', { name: 'Done' }));

        // Assert
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it.each([
        ['Dark', 'Noir', 'Tokyo Night', 'Slate', 'Ember', 'Evergreen'],
        ['Light', 'Frost', 'Parchment', 'Seafoam', 'Banana Stand', 'Bubblegum'],
    ])('UserMenu_OffersOnlyThemesOfTheVariant_WhenThe%sThemeListIsOpened', async (variant, ...names) => {
        // Arrange
        setThemePreference({ ...initialThemeSettings, mode: 'System' });
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);
        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));
        await user.click(screen.getByRole('menuitem', { name: 'Theme…' }));

        // Act
        await user.click(screen.getByRole('combobox', { name: `${variant} theme` }));

        // Assert
        expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual(names);
    });

    it.each([
        ['Noir', 'Dark', 'Magpie.Api/Noir/Dark'],
        ['Tokyo Night', 'Dark', 'Magpie.Api/Tokyo Night/Dark'],
        ['Slate', 'Dark', 'Magpie.Api/Slate/Dark'],
        ['Ember', 'Dark', 'Magpie.Api/Ember/Dark'],
        ['Evergreen', 'Dark', 'Magpie.Api/Evergreen/Dark'],
        ['Frost', 'Light', 'Magpie.Api/Frost/Light'],
        ['Parchment', 'Light', 'Magpie.Api/Parchment/Light'],
        ['Seafoam', 'Light', 'Magpie.Api/Seafoam/Light'],
        ['Banana Stand', 'Light', 'Magpie.Api/Banana Stand/Light'],
        ['Bubblegum', 'Light', 'Magpie.Api/Bubblegum/Light'],
    ])('UserMenu_AppliesTheTheme_When%sIsSelectedInTheThemeDialog', async (name, mode, id) => {
        // Arrange
        setThemePreference({ ...initialThemeSettings, mode: mode as 'Dark' | 'Light' });
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);
        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));
        await user.click(screen.getByRole('menuitem', { name: 'Theme…' }));
        await user.click(screen.getByRole('combobox', { name: `${mode} theme` }));

        // Act
        await user.click(screen.getByRole('option', { name }));

        // Assert
        expect(document.documentElement.dataset.theme).toBe(id);
    });

    it('UserMenu_ShowsAnUnavailableTheme_WhenTheStoredThemeIsMissing', async () => {
        // Arrange
        setThemePreference({ ...initialThemeSettings, mode: 'Light', lightTheme: 'Removed/Ocean/Light' });
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);
        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));

        // Act
        await user.click(screen.getByRole('menuitem', { name: 'Theme…' }));

        // Assert
        expect(screen.getByRole('combobox', { name: 'Light theme' })).toHaveTextContent('Unavailable theme');
        expect(screen.getByText('Selected theme is unavailable. Using Tokyo Night.')).toBeInTheDocument();
    });

    it('UserMenu_RemovesTheUnavailableThemeError_WhenAnAvailableThemeIsSelected', async () => {
        // Arrange
        setThemePreference({ ...initialThemeSettings, mode: 'Light', lightTheme: 'Removed/Ocean/Light' });
        const user = userEvent.setup();
        renderWithProviders(<UserMenu />);
        await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));
        await user.click(screen.getByRole('menuitem', { name: 'Theme…' }));
        await user.click(screen.getByRole('combobox', { name: 'Light theme' }));

        // Act
        await user.click(screen.getByRole('option', { name: 'Bubblegum' }));

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Bubblegum/Light');
        expect(screen.queryByText('Selected theme is unavailable. Using Tokyo Night.')).not.toBeInTheDocument();
    });

    it.each([
        ['Dark', false, 'Light mode', 'Light', 'Magpie.Api/Seafoam/Light'],
        ['Light', false, 'Dark mode', 'Dark', 'Magpie.Api/Noir/Dark'],
        ['System', false, 'Light mode', 'Light', 'Magpie.Api/Seafoam/Light'],
        ['System', true, 'Dark mode', 'Dark', 'Magpie.Api/Noir/Dark'],
    ] as const)(
        'UserMenu_SwitchesToTheOtherMode_WhenTheQuickSwitchIsSelectedFrom%sWithSystemLight%s',
        async (mode, systemLight, item, targetMode, id) => {
            // Arrange
            vi.spyOn(window, 'matchMedia').mockReturnValue({ matches: systemLight } as MediaQueryList);
            setThemePreference({ mode, darkTheme: 'Magpie.Api/Noir/Dark', lightTheme: 'Magpie.Api/Seafoam/Light' });
            const user = userEvent.setup();
            renderWithProviders(<UserMenu />);
            await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));

            // Act
            await user.click(screen.getByRole('menuitem', { name: item }));

            // Assert
            expect(document.documentElement.dataset.theme).toBe(id);
            expect(JSON.parse(localStorage.getItem('magpie.theme')!)).toEqual({
                mode: targetMode,
                darkTheme: 'Magpie.Api/Noir/Dark',
                lightTheme: 'Magpie.Api/Seafoam/Light',
            });
        },
    );

    it.each(['Dark', 'Light', 'System'] as const)(
        'UserMenu_ShowsNoQuickSwitch_WhenTheBrowserInheritsThe%sDefault',
        async (mode) => {
            // Arrange
            setThemeCatalog({
                themes: builtInThemes,
                defaults: { mode, darkTheme: 'Magpie.Api/Ember/Dark', lightTheme: 'Magpie.Api/Bubblegum/Light' },
            });
            const user = userEvent.setup();
            renderWithProviders(<UserMenu />);

            // Act
            await user.click(screen.getByRole('button', { name: /user menu for testuser/i }));

            // Assert
            expect(screen.getAllByRole('menuitem').map((menuItem) => menuItem.textContent)).toEqual([
                'Theme…',
                'Log out',
            ]);
        },
    );
});
