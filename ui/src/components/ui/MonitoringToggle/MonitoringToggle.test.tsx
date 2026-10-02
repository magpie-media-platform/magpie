import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { TooltipProvider } from '@/components/ui/Tooltip';
import type { MonitoringMode } from '@/types';

import { MonitoringToggle } from './MonitoringToggle';

describe('MonitoringToggle', () => {
    it('renders labels for every option, never the raw enum value', () => {
        render(
            <TooltipProvider>
                <MonitoringToggle value={null} onChange={vi.fn()} />
            </TooltipProvider>,
        );

        const options = screen.getAllByRole('radio');
        expect(options.map((option) => option.textContent)).toEqual(['Monitor', 'Do not monitor', 'Inherit']);
    });

    it('marks the matching option checked', () => {
        render(
            <TooltipProvider>
                <MonitoringToggle value={false} onChange={vi.fn()} />
            </TooltipProvider>,
        );

        expect(screen.getByRole('radio', { name: /^Do not monitor:/ })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByRole('radio', { name: /^Monitor:/ })).toHaveAttribute('aria-checked', 'false');
    });

    it('calls onChange with the option value when clicked', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(
            <TooltipProvider>
                <MonitoringToggle value={null} onChange={onChange} />
            </TooltipProvider>,
        );

        await user.click(screen.getByRole('radio', { name: /^Monitor:/ }));

        expect(onChange).toHaveBeenCalledWith(true);
    });

    it('offers Inherit, Unmonitored, Monitored and Shotgun in order in Shotgun mode', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        render(
            <TooltipProvider>
                <MonitoringToggle<MonitoringMode | null> value="Shotgun" onChange={onChange} shotgun />
            </TooltipProvider>,
        );

        const options = screen.getAllByRole('radio');
        await user.click(screen.getByRole('radio', { name: /^Unmonitored:/ }));

        expect(options.map((option) => option.textContent)).toEqual(['Inherit', 'Unmonitored', 'Monitored', 'Shotgun']);
        expect(screen.getByRole('radio', { name: /^Shotgun:/ })).toHaveAttribute('aria-checked', 'true');
        expect(onChange).toHaveBeenCalledWith('Unmonitored');
    });

    it('offers Unmonitored, Monitored and Shotgun without Inherit when inherit is off', () => {
        render(
            <TooltipProvider>
                <MonitoringToggle<MonitoringMode> value="Monitored" onChange={vi.fn()} shotgun inherit={false} />
            </TooltipProvider>,
        );

        const options = screen.getAllByRole('radio');

        expect(options.map((option) => option.textContent)).toEqual(['Unmonitored', 'Monitored', 'Shotgun']);
    });

    it('takes its name from the labelling element when aria-labelledby is set', () => {
        render(
            <TooltipProvider>
                <span id="mode-heading">Default monitoring</span>
                <MonitoringToggle<MonitoringMode>
                    value="Monitored"
                    onChange={vi.fn()}
                    aria-labelledby="mode-heading"
                    shotgun
                    inherit={false}
                />
            </TooltipProvider>,
        );

        const group = screen.getByRole('radiogroup');

        expect(group).toHaveAccessibleName('Default monitoring');
        expect(group).not.toHaveAttribute('aria-label');
    });

    it('disables every option when disabled', () => {
        render(
            <TooltipProvider>
                <MonitoringToggle value={null} onChange={vi.fn()} disabled />
            </TooltipProvider>,
        );

        expect(screen.getAllByRole('radio').map((option) => (option as HTMLButtonElement).disabled)).toEqual([
            true,
            true,
            true,
        ]);
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
