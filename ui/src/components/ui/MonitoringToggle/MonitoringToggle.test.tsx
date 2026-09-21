import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { MonitoringToggle } from './MonitoringToggle';

function renderToggle(value: boolean | null, onChange = vi.fn()) {
    render(
        <TooltipProvider>
            <MonitoringToggle value={value} onChange={onChange} />
        </TooltipProvider>,
    );
    return onChange;
}

describe('MonitoringToggle', () => {
    it('renders labels for every option, never the raw enum value', () => {
        renderToggle(null);

        const options = screen.getAllByRole('radio');
        expect(options.map((option) => option.textContent)).toEqual(['Monitor', 'Do not monitor', 'Inherit']);
    });

    it('marks the matching option checked', () => {
        renderToggle(false);

        expect(screen.getByRole('radio', { name: /^Do not monitor:/ })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByRole('radio', { name: /^Monitor:/ })).toHaveAttribute('aria-checked', 'false');
    });

    it('calls onChange with the option value when clicked', async () => {
        const user = userEvent.setup();
        const onChange = renderToggle(null);

        await user.click(screen.getByRole('radio', { name: /^Monitor:/ }));

        expect(onChange).toHaveBeenCalledWith(true);
    });

    it('disables every option when disabled', () => {
        render(
            <TooltipProvider>
                <MonitoringToggle value={null} onChange={vi.fn()} disabled />
            </TooltipProvider>,
        );

        for (const option of screen.getAllByRole('radio')) {
            expect(option).toBeDisabled();
        }
    });
});

it.each([
    ['{ArrowRight}', true, 'Monitor'],
    ['{ArrowDown}', true, 'Monitor'],
    ['{ArrowLeft}', false, 'Do not monitor'],
    ['{ArrowUp}', false, 'Do not monitor'],
] as const)('selects and focuses the next radio with %s', async (key, expected, label) => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
        <TooltipProvider>
            <MonitoringToggle value={null} onChange={onChange} />
        </TooltipProvider>,
    );
    await user.tab();
    expect(screen.getByRole('radio', { name: /^Inherit:/ })).toHaveFocus();
    await user.keyboard(key);
    expect(onChange).toHaveBeenCalledWith(expected);
    expect(screen.getByRole('radio', { name: new RegExp(`^${label}:`) })).toHaveFocus();
});
