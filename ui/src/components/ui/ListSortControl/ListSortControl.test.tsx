import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ListSortControl } from './ListSortControl';

const sortOptions = [
    { id: 'title', label: 'Title' },
    { id: 'published', label: 'Published', initialDirection: 'desc' as const },
];

describe('ListSortControl', () => {
    it('marks only the active direction as pressed', () => {
        render(
            <ListSortControl
                sorting={[{ id: 'title', desc: true }]}
                sortOptions={sortOptions}
                onSortingChange={vi.fn()}
            />,
        );

        expect(screen.getByRole('button', { name: 'Sort ascending', pressed: false })).toBeEnabled();
        expect(screen.getByRole('button', { name: 'Sort descending', pressed: true })).toBeEnabled();
    });

    it.each([
        { name: 'Sort ascending', desc: false, sorting: [{ id: 'title', desc: true }] },
        { name: 'Sort descending', desc: true, sorting: [{ id: 'title', desc: false }] },
    ])('sets the direction when $name is clicked', async ({ name, desc, sorting }) => {
        const user = userEvent.setup();
        const onSortingChange = vi.fn();
        render(<ListSortControl sorting={sorting} sortOptions={sortOptions} onSortingChange={onSortingChange} />);

        await user.click(screen.getByRole('button', { name }));

        expect(onSortingChange).toHaveBeenCalledWith([{ id: 'title', desc }]);
    });

    it('keeps the direction when the active direction button is clicked again', async () => {
        const user = userEvent.setup();
        const onSortingChange = vi.fn();
        render(
            <ListSortControl
                sorting={[{ id: 'title', desc: false }]}
                sortOptions={sortOptions}
                onSortingChange={onSortingChange}
            />,
        );

        await user.click(screen.getByRole('button', { name: 'Sort ascending' }));

        expect(onSortingChange).toHaveBeenCalledWith([{ id: 'title', desc: false }]);
    });

    it('uses the initial direction of the selected field', async () => {
        const user = userEvent.setup();
        const onSortingChange = vi.fn();
        render(
            <ListSortControl
                sorting={[{ id: 'title', desc: false }]}
                sortOptions={sortOptions}
                onSortingChange={onSortingChange}
            />,
        );

        await user.click(screen.getByRole('combobox', { name: 'Sort by field' }));
        await user.click(screen.getByRole('option', { name: 'Published' }));

        expect(onSortingChange).toHaveBeenCalledWith([{ id: 'published', desc: true }]);
    });

    it('shows no direction as pressed and disables both directions while unsorted', () => {
        render(<ListSortControl sorting={[]} sortOptions={sortOptions} onSortingChange={vi.fn()} allowUnsorted />);

        expect(screen.getByRole('combobox', { name: 'Sort by field' })).toHaveTextContent('Sort by');
        expect(screen.getByRole('button', { name: 'Sort ascending', pressed: false })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Sort descending', pressed: false })).toBeDisabled();
    });

    it('keeps the full label in the field when the label is long', () => {
        render(
            <ListSortControl
                sorting={[{ id: 'long', desc: false }]}
                sortOptions={[{ id: 'long', label: 'A Very Long Label That Should Be Truncated' }]}
                onSortingChange={vi.fn()}
            />,
        );

        expect(screen.getByRole('combobox', { name: 'Sort by field' })).toHaveTextContent(
            'A Very Long Label That Should Be Truncated',
        );
    });

    it('disables the field and both directions when disabled', () => {
        render(
            <ListSortControl
                sorting={[{ id: 'title', desc: false }]}
                sortOptions={sortOptions}
                onSortingChange={vi.fn()}
                disabled
            />,
        );

        expect(screen.getByRole('combobox', { name: 'Sort by field' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Sort ascending' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Sort descending' })).toBeDisabled();
    });

    it('renders nothing when there are no sort options', () => {
        const { container } = render(<ListSortControl sorting={[]} sortOptions={[]} onSortingChange={vi.fn()} />);

        expect(container).toBeEmptyDOMElement();
    });
});
