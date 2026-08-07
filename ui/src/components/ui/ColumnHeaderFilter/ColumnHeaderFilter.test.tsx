import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ColumnHeaderFilter } from './ColumnHeaderFilter';

describe('ColumnHeaderFilter', () => {
    const baseFacet = {
        type: 'facet' as const,
        label: 'Status',
        options: [{ value: 'Missing', label: 'Missing' }],
        value: '',
        onChange: vi.fn(),
    };

    it('labels the trigger as a filter affordance when inactive', () => {
        render(<ColumnHeaderFilter {...baseFacet} active={false} />);
        const trigger = screen.getByRole('button', { name: 'Filter by Status' });
        expect(trigger).toHaveAttribute('aria-pressed', 'false');
        expect(trigger).not.toHaveAttribute('data-active');
    });

    it('exposes the filtered state through aria when active so it is discoverable', () => {
        render(<ColumnHeaderFilter {...baseFacet} active value="Missing" />);
        const trigger = screen.getByRole('button', { name: 'Status filtered, edit filter' });
        expect(trigger).toHaveAttribute('aria-pressed', 'true');
        expect(trigger).toHaveAttribute('data-active', 'true');
        expect(trigger).toHaveAttribute('title', 'Status filtered, edit filter');
    });

    it('renders a filled funnel when active and an outline funnel when inactive', () => {
        const inactive = render(<ColumnHeaderFilter {...baseFacet} active={false} />);
        const inactiveIcon = inactive.container.querySelector('.column-header-filter__trigger svg')?.innerHTML ?? '';
        inactive.unmount();

        const active = render(<ColumnHeaderFilter {...baseFacet} active value="Missing" />);
        const activeIcon = active.container.querySelector('.column-header-filter__trigger svg')?.innerHTML ?? '';

        expect(activeIcon).not.toBe('');
        expect(activeIcon).not.toBe(inactiveIcon);
    });

    it('opens facet options from the trigger', async () => {
        const user = userEvent.setup();

        render(<ColumnHeaderFilter {...baseFacet} active={false} />);

        await user.click(screen.getByRole('button', { name: 'Filter by Status' }));

        expect(await screen.findByRole('menuitemradio', { name: 'Missing' })).toBeInTheDocument();
    });

    it('omits the all option when hideAll is set', async () => {
        const user = userEvent.setup();

        render(<ColumnHeaderFilter {...baseFacet} active={false} hideAll />);

        await user.click(screen.getByRole('button', { name: 'Filter by Status' }));

        expect(await screen.findByRole('menuitemradio', { name: 'Missing' })).toBeInTheDocument();
        expect(screen.queryByRole('menuitemradio', { name: 'Any' })).not.toBeInTheDocument();
    });

    it('locks the facet trigger when disabled and keeps a readable accessible label', () => {
        render(<ColumnHeaderFilter {...baseFacet} active value="Missing" disabled />);

        const trigger = screen.getByRole('button', { name: 'Status filter locked' });
        expect(trigger).toBeDisabled();
    });
});
