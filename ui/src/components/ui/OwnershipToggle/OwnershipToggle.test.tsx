import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { OwnershipToggle } from './OwnershipToggle';

function renderToggle(ui: React.ReactElement) {
    return render(ui);
}

describe('OwnershipToggle', () => {
    it('renders an All and a Mine radio with accessible labels', () => {
        renderToggle(<OwnershipToggle value="all" onValueChange={vi.fn()} />);

        expect(screen.getByRole('radio', { name: 'All' })).toBeInTheDocument();
        expect(screen.getByRole('radio', { name: 'Mine' })).toBeInTheDocument();
    });

    it('exposes the group accessible label on the radiogroup', () => {
        renderToggle(<OwnershipToggle value="all" onValueChange={vi.fn()} aria-label="Filter by ownership" />);

        expect(screen.getByRole('radiogroup', { name: 'Filter by ownership' })).toBeInTheDocument();
    });

    it('marks the active scope as checked when value is all', () => {
        renderToggle(<OwnershipToggle value="all" onValueChange={vi.fn()} />);

        expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByRole('radio', { name: 'Mine' })).toHaveAttribute('aria-checked', 'false');
    });

    it('marks the active scope as checked when value is mine', () => {
        renderToggle(<OwnershipToggle value="mine" onValueChange={vi.fn()} />);

        expect(screen.getByRole('radio', { name: 'Mine' })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('aria-checked', 'false');
    });

    it('calls onValueChange with mine when Mine is clicked', async () => {
        const user = userEvent.setup();
        const handleChange = vi.fn();
        renderToggle(<OwnershipToggle value="all" onValueChange={handleChange} />);

        await user.click(screen.getByRole('radio', { name: 'Mine' }));

        expect(handleChange).toHaveBeenCalledWith('mine');
    });

    it('calls onValueChange with all when All is clicked', async () => {
        const user = userEvent.setup();
        const handleChange = vi.fn();
        renderToggle(<OwnershipToggle value="mine" onValueChange={handleChange} />);

        await user.click(screen.getByRole('radio', { name: 'All' }));

        expect(handleChange).toHaveBeenCalledWith('all');
    });

    it('renders a visible text label beside the icon on each segment', () => {
        renderToggle(<OwnershipToggle value="all" onValueChange={vi.fn()} />);

        expect(screen.getByRole('radio', { name: 'All' })).toHaveTextContent('All');
        expect(screen.getByRole('radio', { name: 'Mine' })).toHaveTextContent('Mine');
    });

    it('applies the filter hook to each segment', () => {
        renderToggle(<OwnershipToggle value="all" onValueChange={vi.fn()} data-filter="scope" />);

        expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('data-filter', 'scope');
        expect(screen.getByRole('radio', { name: 'Mine' })).toHaveAttribute('data-filter', 'scope');
    });

    it('uses the shared tab list and trigger treatment', () => {
        renderToggle(<OwnershipToggle value="all" onValueChange={vi.fn()} />);

        expect(screen.getByRole('radiogroup')).toHaveClass('ownership-toggle', 'tabs-list');
        expect(screen.getByRole('radio', { name: 'All' })).toHaveClass('ownership-toggle__option', 'tabs-trigger');
        expect(screen.getByRole('radio', { name: 'Mine' })).toHaveClass('ownership-toggle__option', 'tabs-trigger');
    });
    it('enters and leaves the group with one tab stop on the selected option', async () => {
        const user = userEvent.setup();
        render(
            <>
                <button type="button">Before</button>
                <OwnershipToggle value="mine" onValueChange={vi.fn()} />
                <button type="button">After</button>
            </>,
        );

        screen.getByRole('button', { name: 'Before' }).focus();
        await user.tab();
        expect(screen.getByRole('radio', { name: 'Mine' })).toHaveFocus();
        await user.tab();
        expect(screen.getByRole('button', { name: 'After' })).toHaveFocus();
    });

    it.each([
        ['{ArrowRight}', 'all', 'mine'],
        ['{ArrowDown}', 'all', 'mine'],
        ['{ArrowLeft}', 'all', 'mine'],
        ['{ArrowUp}', 'mine', 'all'],
        ['{ArrowRight}', 'mine', 'all'],
    ] as const)('moves the selection with %s from %s to %s', async (key, from, to) => {
        const user = userEvent.setup();
        const onValueChange = vi.fn();
        render(<OwnershipToggle value={from} onValueChange={onValueChange} />);

        screen.getByRole('radio', { checked: true }).focus();
        await user.keyboard(key);

        expect(onValueChange).toHaveBeenCalledWith(to);
        expect(screen.getByRole('radio', { name: to === 'all' ? 'All' : 'Mine' })).toHaveFocus();
    });
});
