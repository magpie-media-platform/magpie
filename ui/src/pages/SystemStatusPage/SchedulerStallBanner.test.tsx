import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { SchedulerStallBanner } from './SchedulerStallBanner';
import type { SchedulerStatus } from '@/hooks/useSchedulerStatus';

const mockUseSchedulerStatus = vi.fn();

vi.mock('@/hooks/useSchedulerStatus', async () => {
    const actual = await vi.importActual<typeof import('@/hooks/useSchedulerStatus')>('@/hooks/useSchedulerStatus');
    return {
        ...actual,
        useSchedulerStatus: () => mockUseSchedulerStatus(),
    };
});

function Wrap({ children }: { children: ReactNode }) {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

function setSnapshot(snapshot: SchedulerStatus | undefined) {
    mockUseSchedulerStatus.mockReturnValue({ data: snapshot });
}

describe('SchedulerStallBanner', () => {
    beforeEach(() => {
        mockUseSchedulerStatus.mockReset();
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('renders nothing when the snapshot has not loaded', () => {
        setSnapshot(undefined);
        const { container } = render(<SchedulerStallBanner />, { wrapper: Wrap });
        expect(container).toBeEmptyDOMElement();
    });

    it('renders nothing when the cluster is healthy and no recent recovery', () => {
        setSnapshot({
            isStalled: false,
            stalledSinceUtc: null,
            zeroEligibleSinceUtc: null,
            recoveredAtUtc: null,
            eligibleNodeCount: 2,
        });
        const { container } = render(<SchedulerStallBanner />, { wrapper: Wrap });
        expect(container).toBeEmptyDOMElement();
    });

    it('renders the warning banner when isStalled is true', () => {
        setSnapshot({
            isStalled: true,
            stalledSinceUtc: '2026-05-18T10:00:00.000Z',
            zeroEligibleSinceUtc: '2026-05-18T10:00:00.000Z',
            recoveredAtUtc: null,
            eligibleNodeCount: 0,
        });
        render(<SchedulerStallBanner />, { wrapper: Wrap });

        expect(screen.getByTestId('scheduler-stall-banner')).toBeInTheDocument();
        expect(screen.getByText(/Scheduler stalled/i)).toBeInTheDocument();
    });

    it('renders the success banner immediately after a recovery', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-05-18T10:00:30.000Z'));

        setSnapshot({
            isStalled: false,
            stalledSinceUtc: null,
            zeroEligibleSinceUtc: null,
            recoveredAtUtc: '2026-05-18T10:00:00.000Z',
            eligibleNodeCount: 1,
        });
        render(<SchedulerStallBanner />, { wrapper: Wrap });

        expect(screen.getByTestId('scheduler-recovered-banner')).toBeInTheDocument();
    });

    it('dismisses recovery without another scheduler event and resets for a later recovery', async () => {
        // Arrange
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-05-18T10:00:30.000Z'));
        mockUseSchedulerStatus.mockReturnValue({
            data: { isStalled: false, recoveredAtUtc: '2026-05-18T10:00:00.000Z' },
        });
        const { rerender } = render(<SchedulerStallBanner />);
        expect(screen.getByTestId('scheduler-recovered-banner')).toBeInTheDocument();

        // Act
        await act(() => vi.advanceTimersByTime(30_000));

        // Assert
        expect(screen.queryByTestId('scheduler-recovered-banner')).not.toBeInTheDocument();
        mockUseSchedulerStatus.mockReturnValue({
            data: { isStalled: false, recoveredAtUtc: '2026-05-18T10:01:00.000Z' },
        });
        rerender(<SchedulerStallBanner />);
        expect(screen.getByTestId('scheduler-recovered-banner')).toBeInTheDocument();
        await act(() => vi.advanceTimersByTime(60_000));
        expect(screen.queryByTestId('scheduler-recovered-banner')).not.toBeInTheDocument();
    });

    it('suppresses the success banner after the 60s window', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-05-18T10:05:00.000Z'));

        setSnapshot({
            isStalled: false,
            stalledSinceUtc: null,
            zeroEligibleSinceUtc: null,
            recoveredAtUtc: '2026-05-18T10:00:00.000Z',
            eligibleNodeCount: 1,
        });
        const { container } = render(<SchedulerStallBanner />, { wrapper: Wrap });

        expect(container).toBeEmptyDOMElement();
    });
});
