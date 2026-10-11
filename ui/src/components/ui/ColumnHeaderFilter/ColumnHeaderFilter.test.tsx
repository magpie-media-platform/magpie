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

    it('Range_AppliesOnce_WhenEnterIsPressedInnumberRange', async () => {
        // Arrange
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ColumnHeaderFilter type="numberRange" label="Size" active={false} min="" max="" onChange={onChange} />);
        await user.click(screen.getByRole('button', { name: 'Filter by Size' }));
        await user.type(screen.getByRole('spinbutton', { name: 'Size minimum' }), '2');

        // Act
        await user.keyboard('{Enter}');

        // Assert
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenCalledWith({ min: '2', max: '' });
    });

    it('Range_AppliesOnce_WhenEnterIsPressedIndateRange', async () => {
        // Arrange
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(<ColumnHeaderFilter type="dateRange" label="Size" active={false} from="" to="" onChange={onChange} />);
        await user.click(screen.getByRole('button', { name: 'Filter by Size' }));
        await user.type(screen.getByLabelText('Size from'), '2020-01-01');

        // Act
        await user.keyboard('{Enter}');

        // Assert
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenCalledWith({ preset: '', from: '2020-01-01', to: '' });
    });

    it('DateRange_CommitsTheDates_WhenBetweenDatesIsChosenAfterAPreset', async () => {
        // Arrange
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(
            <ColumnHeaderFilter
                type="dateRange"
                label="Time"
                active
                preset="7d"
                presets={[{ value: '7d', label: 'Last 7 days' }]}
                from=""
                to=""
                onChange={onChange}
            />,
        );
        await user.click(screen.getByRole('button', { name: 'Time filtered, edit filter' }));

        // Act
        await user.click(screen.getByRole('menuitemradio', { name: 'Between dates' }));
        await user.type(screen.getByLabelText('Time from'), '2026-09-01');
        await user.type(screen.getByLabelText('Time to'), '2026-09-30');
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        // Assert
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenCalledWith({ preset: 'between', from: '2026-09-01', to: '2026-09-30' });
    });

    it('NumberRange_CommitsFractionalMinimum_WhenAppliedFromHeader', async () => {
        // Arrange
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(
            <ColumnHeaderFilter
                type="numberRange"
                label="Size"
                active={false}
                min=""
                max=""
                unit="GB"
                onChange={onChange}
            />,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Filter by Size' }));
        const minimum = screen.getByRole('spinbutton', { name: 'Size minimum (GB)' }) as HTMLInputElement;
        await user.type(minimum, '1.5');

        // Assert
        expect(minimum).toHaveFocus();
        expect(minimum.validity.stepMismatch).toBe(false);

        // Act
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        // Assert
        expect(onChange).toHaveBeenCalledWith({ min: '1.5', max: '' });
    });
});

it.each(['text', 'dateRange', 'numberRange'] as const)('uses different IDs for separate %s filters', async (type) => {
    const user = userEvent.setup();
    render(
        <>
            <ColumnHeaderFilter
                type={type}
                label="First"
                active={false}
                value=""
                from=""
                to=""
                min=""
                max=""
                onChange={vi.fn()}
            />
            <ColumnHeaderFilter
                type={type}
                label="Second"
                active={false}
                value=""
                from=""
                to=""
                min=""
                max=""
                onChange={vi.fn()}
            />
        </>,
    );
    await user.click(screen.getByRole('button', { name: 'Filter by First' }));
    const first = document.querySelector<HTMLInputElement>('.column-header-filter__form input')!;
    expect(first.labels?.[0]).toHaveTextContent('First');
    const firstId = first.id;
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: 'Filter by Second' }));
    const second = document.querySelector<HTMLInputElement>('.column-header-filter__form input')!;
    expect(second.labels?.[0]).toHaveTextContent('Second');
    expect(second.id).not.toBe(firstId);
});
