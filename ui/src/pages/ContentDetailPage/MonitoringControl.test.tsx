import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { MonitoringControl } from './MonitoringControl';

const mockMutate = vi.fn();
const mockToast = vi.fn();
let mockIsPending = false;

vi.mock('@/hooks/useContent', () => ({
    useUpdateContent: () => ({ mutate: mockMutate, isPending: mockIsPending }),
}));

vi.mock('@/components/ui/Toast', async () => {
    const actual = await vi.importActual<typeof import('@/components/ui/Toast')>('@/components/ui/Toast');
    return { ...actual, useToast: () => ({ toast: mockToast }) };
});

function renderControl(monitoring: boolean | null, effectiveMonitoring: boolean) {
    return render(
        <TooltipProvider>
            <MonitoringControl contentId="c-1" monitoring={monitoring} effectiveMonitoring={effectiveMonitoring} />
        </TooltipProvider>,
    );
}

describe('MonitoringControl', () => {
    beforeEach(() => {
        mockMutate.mockReset();
        mockToast.mockReset();
        mockIsPending = false;
    });

    it('renders the effective value and toggle within the aligned policy row', () => {
        renderControl(null, true);

        const row = screen.getByTestId('monitoring-control');
        expect(within(row).getByText('Monitoring')).toBeInTheDocument();
        expect(within(row).getByTestId('monitoring-control__effective')).toHaveTextContent(/^Monitored$/);
        expect(within(row).getByTestId('monitoring-control__toggle')).toHaveAttribute(
            'data-component',
            'monitoring-toggle',
        );
    });

    it('renders three icon segments with labels in persistence order', () => {
        renderControl(null, true);

        const options = screen.getAllByRole('radio');
        expect(options.map((option) => option.textContent)).toEqual(['Monitor', 'Do not monitor', 'Inherit']);
        for (const option of options) {
            expect(option.querySelector('svg')).toBeInTheDocument();
        }
    });

    it('marks the inherited override active and keeps the effective value clean', () => {
        renderControl(null, true);

        expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent(/^Monitored$/);
        expect(screen.queryByText('Inherited')).not.toBeInTheDocument();
        expect(screen.getByTestId('monitoring-control__option-inherit')).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByTestId('monitoring-control__option-true')).toHaveAttribute('aria-checked', 'false');
    });

    it('marks the explicit disabled override active', () => {
        renderControl(false, false);

        expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent('Not monitored');
        expect(screen.getByTestId('monitoring-control__option-false')).toHaveAttribute('aria-checked', 'true');
    });

    it('hides the toggle while preserving the effective value for read-only users', () => {
        render(
            <TooltipProvider>
                <MonitoringControl contentId="c-1" monitoring={null} effectiveMonitoring canEdit={false} />
            </TooltipProvider>,
        );

        expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent('Monitored');
        expect(screen.queryByTestId('monitoring-control__toggle')).not.toBeInTheDocument();
    });

    it('disables every segment while a mutation is pending', () => {
        mockIsPending = true;
        renderControl(true, true);

        for (const option of screen.getAllByRole('radio')) {
            expect(option).toBeDisabled();
        }
    });

    it.each([
        ['monitoring-control__option-true', true],
        ['monitoring-control__option-false', false],
        ['monitoring-control__option-inherit', null],
    ] as const)('patches monitoring via %s', async (testId, monitoring) => {
        const user = userEvent.setup();
        renderControl(monitoring === true ? false : true, true);

        await user.click(screen.getByTestId(testId));

        await waitFor(() => {
            expect(mockMutate).toHaveBeenCalledWith({ id: 'c-1', monitoring }, expect.anything());
        });
    });

    it('uses not monitored wording in the disabled success toast', async () => {
        const user = userEvent.setup();
        renderControl(true, true);
        await user.click(screen.getByTestId('monitoring-control__option-false'));

        await waitFor(() => expect(mockMutate).toHaveBeenCalled());
        const mutationOptions = mockMutate.mock.calls[0][1] as { onSuccess: () => void };
        mutationOptions.onSuccess();

        expect(mockToast).toHaveBeenCalledWith(
            expect.objectContaining({ description: 'Monitoring set to not monitored.' }),
        );
    });
});
