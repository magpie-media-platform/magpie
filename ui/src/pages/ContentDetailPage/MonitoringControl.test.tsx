import { FlagCheckeredIcon } from '@phosphor-icons/react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { TooltipProvider } from '@/components/ui/Tooltip';
import type { MonitoringMode } from '@/types';

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

function renderControl(monitoring: MonitoringMode | null, effectiveMonitoring: MonitoringMode) {
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
        renderControl(null, 'Monitored');

        const row = screen.getByTestId('monitoring-control');
        expect(within(row).getByText('Monitoring')).toBeInTheDocument();
        expect(within(row).getByTestId('monitoring-control__effective')).toHaveTextContent(/^Monitored$/);
        expect(within(row).getByTestId('monitoring-control__toggle')).toHaveAttribute(
            'data-component',
            'monitoring-toggle',
        );
    });

    it('renders four icon segments with labels in choice order', () => {
        renderControl(null, 'Monitored');

        const options = screen.getAllByRole('radio');
        expect(options.map((option) => option.textContent)).toEqual(['Inherit', 'Unmonitored', 'Monitored', 'Shotgun']);
        expect(options.map((option) => option.querySelector('svg') !== null)).toEqual([true, true, true, true]);
    });

    it('marks the inherited override active and keeps the effective value clean', () => {
        renderControl(null, 'Monitored');

        expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent(/^Monitored$/);
        expect(screen.queryByText('Inherited')).not.toBeInTheDocument();
        expect(screen.getByTestId('monitoring-control__option-inherit')).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByTestId('monitoring-control__option-true')).toHaveAttribute('aria-checked', 'false');
    });

    it('marks the explicit disabled override active', () => {
        renderControl('Unmonitored', 'Unmonitored');

        expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent('Unmonitored');
        expect(screen.getByTestId('monitoring-control__option-false')).toHaveAttribute('aria-checked', 'true');
    });

    it('hides the toggle while preserving the effective value for read-only users', () => {
        render(
            <TooltipProvider>
                <MonitoringControl contentId="c-1" monitoring={null} effectiveMonitoring="Monitored" canEdit={false} />
            </TooltipProvider>,
        );

        expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent('Monitored');
        expect(screen.queryByTestId('monitoring-control__toggle')).not.toBeInTheDocument();
    });

    it('disables every segment while a mutation is pending', () => {
        mockIsPending = true;
        renderControl('Monitored', 'Monitored');

        expect(screen.getAllByRole('radio').map((option) => (option as HTMLButtonElement).disabled)).toEqual([
            true,
            true,
            true,
            true,
        ]);
    });

    it.each([
        ['monitoring-control__option-true', 'Monitored', 'Unmonitored'],
        ['monitoring-control__option-false', 'Unmonitored', 'Monitored'],
        ['monitoring-control__option-inherit', null, 'Monitored'],
    ] as const)('patches monitoring via %s', async (testId, monitoring, initialMonitoring) => {
        const user = userEvent.setup();
        renderControl(initialMonitoring, 'Monitored');

        await user.click(screen.getByTestId(testId));

        await waitFor(() => {
            expect(mockMutate).toHaveBeenCalledWith({ id: 'c-1', monitoring }, expect.anything());
        });
    });

    it('uses unmonitored wording in the disabled success toast', async () => {
        const user = userEvent.setup();
        renderControl('Monitored', 'Monitored');
        await user.click(screen.getByTestId('monitoring-control__option-false'));

        await waitFor(() => expect(mockMutate).toHaveBeenCalled());
        const mutationOptions = mockMutate.mock.calls[0][1] as { onSuccess: () => void };
        mutationOptions.onSuccess();

        expect(mockToast).toHaveBeenCalledWith(
            expect.objectContaining({ description: 'Monitoring set to unmonitored.' }),
        );
    });

    it('asks for confirmation with the Shotgun copy and patches nothing when cancelled', async () => {
        const user = userEvent.setup();
        renderControl('Monitored', 'Monitored');

        await user.click(screen.getByTestId('monitoring-control__option-shotgun'));

        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).getByText('Use Shotgun?')).toBeInTheDocument();
        expect(
            within(dialog).getByText(
                'Downloads every good release at once and keeps the first one that finishes. Faster, but uses more bandwidth.',
            ),
        ).toBeInTheDocument();
        expect(within(dialog).queryByText('Also applies to everything inside it.')).not.toBeInTheDocument();
        expect(within(dialog).getByRole('button', { name: 'Use Shotgun' })).toBeInTheDocument();
        await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));

        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(mockMutate).not.toHaveBeenCalled();
    });

    it('ShotgunConfirmation_SaysItAppliesInside_WhenTheItemHasChildren', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TooltipProvider>
                <MonitoringControl contentId="c-1" monitoring="Monitored" effectiveMonitoring="Monitored" hasChildren />
            </TooltipProvider>,
        );

        // Act
        await user.click(screen.getByTestId('monitoring-control__option-shotgun'));

        // Assert
        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).getByText('Also applies to everything inside it.')).toBeInTheDocument();
    });

    it('shows the flag icon on the Shotgun confirmation action', async () => {
        // Arrange
        const user = userEvent.setup();
        const reference = render(<FlagCheckeredIcon weight="bold" aria-hidden="true" />).container.querySelector('svg');
        renderControl('Monitored', 'Monitored');

        // Act
        await user.click(screen.getByTestId('monitoring-control__option-shotgun'));

        // Assert
        expect((await screen.findByRole('button', { name: 'Use Shotgun' })).querySelector('svg')?.outerHTML).toBe(
            reference?.outerHTML,
        );
    });

    it('patches Shotgun monitoring when Shotgun is confirmed', async () => {
        const user = userEvent.setup();
        renderControl(null, 'Unmonitored');

        await user.click(screen.getByTestId('monitoring-control__option-shotgun'));
        await user.click(await screen.findByRole('button', { name: 'Use Shotgun' }));

        expect(mockMutate).toHaveBeenCalledWith({ id: 'c-1', monitoring: 'Shotgun' }, expect.anything());
    });

    it('patches plain monitoring without confirmation when Shotgun changes to Monitored', async () => {
        const user = userEvent.setup();
        render(
            <TooltipProvider>
                <MonitoringControl contentId="c-1" monitoring="Shotgun" effectiveMonitoring="Shotgun" />
            </TooltipProvider>,
        );

        await user.click(screen.getByTestId('monitoring-control__option-true'));

        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        expect(mockMutate).toHaveBeenCalledWith({ id: 'c-1', monitoring: 'Monitored' }, expect.anything());
    });

    it.each([
        ['Shotgun', 'monitoring-control__option-shotgun'],
        [null, 'monitoring-control__option-inherit'],
    ] as const)('shows the effective Shotgun mode when monitoring is %s', (monitoring, activeTestId) => {
        render(
            <TooltipProvider>
                <MonitoringControl contentId="c-1" monitoring={monitoring} effectiveMonitoring="Shotgun" />
            </TooltipProvider>,
        );

        expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent(/^Shotgun$/);
        expect(screen.getByTestId(activeTestId)).toHaveAttribute('aria-checked', 'true');
    });
});
