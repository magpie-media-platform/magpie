import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { Role } from '@/auth';
import { AuthTestProvider as AuthProvider } from '@/test/AuthTestProvider';

import { CommandPalette } from './CommandPalette';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual('react-router-dom');
    return {
        ...actual,
        useNavigate: () => mockNavigate,
    };
});

function renderPalette(role: Role = Role.Viewer) {
    const user = role !== Role.Viewer ? { id: 'user-1', username: 'testuser', role } : null;

    return render(
        <MemoryRouter>
            <AuthProvider initialUser={user ?? undefined} skipSessionInit>
                <CommandPalette />
            </AuthProvider>
        </MemoryRouter>,
    );
}

beforeEach(() => {
    mockNavigate.mockReset();
});

describe('CommandPalette', () => {
    it('is not visible by default', () => {
        renderPalette();
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('opens when Ctrl+K is pressed', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
    });

    it('opens when uppercase Ctrl+K is pressed', async () => {
        renderPalette();
        fireEvent.keyDown(window, { key: 'K', ctrlKey: true });
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
    });

    it('does not open for K without a shortcut modifier', () => {
        renderPalette();
        fireEvent.keyDown(window, { key: 'K' });
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('opens when Meta+K is pressed', async () => {
        renderPalette();
        await userEvent.keyboard('{Meta>}k{/Meta}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
    });

    it('closes when Escape is pressed', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
        await userEvent.keyboard('{Escape}');
        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });

    it('returns focus to the element that invoked it after Escape', async () => {
        renderPalette();
        const trigger = document.createElement('button');
        trigger.textContent = 'Palette trigger';
        document.body.appendChild(trigger);
        trigger.focus();

        await userEvent.keyboard('{Control>}k{/Control}');
        expect(await screen.findByRole('dialog')).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: 'Search commands' })).toHaveFocus();

        await userEvent.keyboard('{Escape}');
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(trigger).toHaveFocus();

        trigger.remove();
    });

    it('renders the search input when open', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
        expect(screen.getByRole('combobox', { name: 'Search commands' })).toBeInTheDocument();
    });

    it('shows navigation commands', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByText('Dashboard')).toBeInTheDocument();
        });
        expect(screen.getByText('Library')).toBeInTheDocument();
        expect(screen.getByText('Downloads')).toBeInTheDocument();
    });

    it('hides admin commands from non-admin users', async () => {
        renderPalette(Role.Viewer);
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
        expect(screen.queryByText('Advanced Settings')).not.toBeInTheDocument();
        expect(screen.queryByText('Users')).not.toBeInTheDocument();
    });

    it('shows admin commands to admin users', async () => {
        renderPalette(Role.Admin);
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByText('Advanced Settings')).toBeInTheDocument();
        });
        expect(screen.getByText('Users')).toBeInTheDocument();
    });

    it('filters commands by query', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
        const input = screen.getByRole('combobox', { name: 'Search commands' });
        await userEvent.type(input, 'cal');
        await waitFor(() => {
            expect(screen.getByText('Calendar')).toBeInTheDocument();
        });
        expect(screen.queryByText('Dashboard')).not.toBeInTheDocument();
    });

    it('shows empty state when no results match', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
        const input = screen.getByRole('combobox', { name: 'Search commands' });
        await userEvent.type(input, 'zzz-no-match');
        await waitFor(() => {
            expect(screen.getByText(/No results for/)).toBeInTheDocument();
        });
    });

    it('navigates to a route when a command is clicked', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByText('Downloads')).toBeInTheDocument();
        });
        await userEvent.click(screen.getByText('Downloads'));
        expect(mockNavigate).toHaveBeenCalledWith('/downloads');
    });

    it('closes the palette after navigating', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByText('Downloads')).toBeInTheDocument();
        });
        await userEvent.click(screen.getByText('Downloads'));
        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });

    it('navigates with Enter on the highlighted command', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const input = screen.getByRole('combobox', { name: 'Search commands' });
        await userEvent.type(input, 'Dashboard');
        await waitFor(() => {
            expect(screen.getByText('Dashboard')).toBeInTheDocument();
        });
        await userEvent.keyboard('{Enter}');
        expect(mockNavigate).toHaveBeenCalledWith('/');
    });

    it('moves highlight down with ArrowDown', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const input = screen.getByRole('combobox', { name: 'Search commands' });
        await userEvent.type(input, 'Cal');
        await waitFor(() => {
            expect(screen.getByText('Calendar')).toBeInTheDocument();
        });

        await userEvent.keyboard('{ArrowDown}');
        await userEvent.keyboard('{Enter}');
        expect(mockNavigate).toHaveBeenCalledWith('/calendar');
    });

    it('renders input with combobox role and aria attributes', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const input = screen.getByRole('combobox', { name: 'Search commands' });
        expect(input).toHaveAttribute('aria-controls', 'command-palette-listbox');
        expect(input).toHaveAttribute('aria-expanded', 'true');
        expect(input).toHaveAttribute('aria-activedescendant', 'command-palette-option-dashboard');
    });

    it('renders results container with listbox role', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByRole('listbox')).toHaveAttribute('id', 'command-palette-listbox');
    });

    it('renders items with option role', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const options = screen.getAllByRole('option');
        expect(options.length).toBeGreaterThan(0);
        expect(options[0]).toHaveAttribute('id', 'command-palette-option-dashboard');
    });

    it('updates aria-activedescendant on arrow navigation', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const input = screen.getByRole('combobox', { name: 'Search commands' });
        expect(input).toHaveAttribute('aria-activedescendant', 'command-palette-option-dashboard');

        await userEvent.keyboard('{ArrowDown}');
        expect(input).toHaveAttribute('aria-activedescendant', 'command-palette-option-library');
    });

    it('sets aria-expanded to false when no results match', async () => {
        renderPalette();
        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const input = screen.getByRole('combobox', { name: 'Search commands' });
        await userEvent.type(input, 'zzz-no-match');
        await waitFor(() => {
            expect(input).toHaveAttribute('aria-expanded', 'false');
        });
    });

    it('resets query when reopened', async () => {
        renderPalette();

        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
        const input = screen.getByRole('combobox', { name: 'Search commands' });
        await userEvent.type(input, 'down');
        await waitFor(() => {
            expect(screen.queryByText('Dashboard')).not.toBeInTheDocument();
        });

        await userEvent.keyboard('{Escape}');
        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });

        await userEvent.keyboard('{Control>}k{/Control}');
        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Dashboard')).toBeInTheDocument();
    });
});
