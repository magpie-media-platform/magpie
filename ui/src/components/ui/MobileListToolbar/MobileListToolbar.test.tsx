import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { MobileListToolbar, type MobileListSortOption } from './MobileListToolbar';

const sortOptions: MobileListSortOption[] = [
    { id: 'title', label: 'Title' },
    { id: 'published', label: 'Published', initialDirection: 'desc' },
];

describe('MobileListToolbar', () => {
    it('SortDirection_DescribesCurrentState_WhenDirectionCanToggle', () => {
        // Arrange
        render(
            <MobileListToolbar
                sorting={[{ id: 'title', desc: true }]}
                sortOptions={sortOptions}
                onSortingChange={vi.fn()}
            />,
        );

        // Act
        const direction = screen.getByRole('button', { name: 'Sort ascending' });

        // Assert
        expect(direction).toHaveAttribute('data-direction', 'desc');
        expect(direction).toHaveAttribute('aria-description', 'Current direction: descending');
    });

    it('keeps the selection control in place while its state changes', async () => {
        const user = userEvent.setup();
        const onToggle = vi.fn();
        const { rerender } = render(
            <MobileListToolbar
                sorting={[{ id: 'title', desc: false }]}
                sortOptions={sortOptions}
                onSortingChange={vi.fn()}
                selection={{ active: false, onToggle }}
            />,
        );

        const toggle = screen.getByRole('button', { name: 'Select', pressed: false });
        await user.click(toggle);
        expect(onToggle).toHaveBeenCalledOnce();

        rerender(
            <MobileListToolbar
                sorting={[{ id: 'title', desc: false }]}
                sortOptions={sortOptions}
                onSortingChange={vi.fn()}
                selection={{ active: true, onToggle }}
            />,
        );

        expect(screen.getByRole('button', { name: 'Select', pressed: true })).toBe(toggle);
        expect(toggle).toHaveAttribute('data-action', 'exit-select-mode');
        expect(toggle.closest('[data-component="mobile-list-toolbar"]')).toBe(
            screen.getByRole('group', { name: 'Sort list' }).closest('[data-component="mobile-list-toolbar"]'),
        );
    });

    it('toggles the active sort direction', async () => {
        const user = userEvent.setup();
        const onSortingChange = vi.fn();
        render(
            <MobileListToolbar
                sorting={[{ id: 'title', desc: false }]}
                sortOptions={sortOptions}
                onSortingChange={onSortingChange}
            />,
        );

        await user.click(screen.getByRole('button', { name: 'Sort descending' }));

        expect(onSortingChange).toHaveBeenCalledWith([{ id: 'title', desc: true }]);
    });

    it('uses the selected field natural initial direction', async () => {
        const user = userEvent.setup();
        const onSortingChange = vi.fn();
        render(
            <MobileListToolbar
                sorting={[{ id: 'title', desc: false }]}
                sortOptions={sortOptions}
                onSortingChange={onSortingChange}
            />,
        );

        await user.click(screen.getByRole('combobox', { name: 'Sort by field' }));
        await user.click(screen.getByRole('option', { name: 'Published' }));

        expect(onSortingChange).toHaveBeenCalledWith([{ id: 'published', desc: true }]);
    });

    it('renders filter actions inside the toolbar', () => {
        render(
            <MobileListToolbar
                sorting={[{ id: 'title', desc: false }]}
                sortOptions={sortOptions}
                onSortingChange={vi.fn()}
                filterActions={<button type="button">Clear filters</button>}
            />,
        );

        const toolbar = document.querySelector('[data-component="mobile-list-toolbar"]') as HTMLElement;
        expect(within(toolbar).getByRole('button', { name: 'Clear filters' })).toBeInTheDocument();
    });

    it('right aligns sorting without rendering an empty selection affordance', () => {
        render(
            <MobileListToolbar
                sorting={[{ id: 'title', desc: false }]}
                sortOptions={sortOptions}
                onSortingChange={vi.fn()}
            />,
        );

        expect(screen.queryByRole('button', { name: 'Select' })).not.toBeInTheDocument();
        expect(screen.getByRole('group', { name: 'Sort list' })).toBeInTheDocument();
    });
});

it('keeps selection and filters when there are no sort options', async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn();
    render(
        <MobileListToolbar
            sorting={[]}
            sortOptions={[]}
            onSortingChange={vi.fn()}
            selection={{ active: false, onToggle }}
            filterActions={<button>Filters</button>}
        />,
    );
    await user.click(screen.getByRole('button', { name: 'Select' }));
    expect(onToggle).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Filters' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Sort list' })).not.toBeInTheDocument();
});
