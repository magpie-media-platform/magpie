import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    buildUrl: (path: string) => path,
}));

import { apiGet } from '@/lib/api';
import type { ApiJobExecution } from '@/hooks/useJobs';

import { ExecutionRow } from './ExecutionRow';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

function renderWithClient(ui: React.ReactElement) {
    return render(ui, { wrapper: createWrapper() });
}

function makeExecution(overrides: Partial<ApiJobExecution> = {}): ApiJobExecution {
    return {
        id: 'exec-1',
        jobType: 'ImportMedia',
        scope: 'System',
        status: 'Completed',
        createdAt: '2024-01-15T12:00:00Z',
        startedAt: '2024-01-15T12:00:01Z',
        completedAt: '2024-01-15T12:05:00Z',
        statusMessage: null,
        progress: null,
        ...overrides,
    };
}

describe('ExecutionRow', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue({ root: null, totalCount: 0 });
    });

    it('renders the humanized job type name', () => {
        renderWithClient(<ExecutionRow execution={makeExecution()} />);
        expect(screen.getByText('Import Media')).toBeInTheDocument();
    });

    it('displays the correct status badge', () => {
        renderWithClient(<ExecutionRow execution={makeExecution({ status: 'Completed' })} />);
        expect(screen.getByText('Completed')).toBeInTheDocument();
    });

    it('shows failed status badge for failed execution', () => {
        renderWithClient(<ExecutionRow execution={makeExecution({ status: 'Failed', statusMessage: 'Timeout' })} />);
        expect(screen.getByText('Failed')).toBeInTheDocument();
    });

    it('shows status message when present', () => {
        renderWithClient(<ExecutionRow execution={makeExecution({ statusMessage: 'Processing items' })} />);
        expect(screen.getByText('Processing items')).toBeInTheDocument();
    });

    it('always renders an expandable details panel (collapsed by default)', () => {
        renderWithClient(<ExecutionRow execution={makeExecution({ statusMessage: null })} />);
        const logs = screen.getByTestId('execution-row__logs');
        expect(logs).toBeInTheDocument();
        expect(logs).not.toBeVisible();
    });

    it.each([
        [42.4, 42],
        [-1, 0],
        [101, 99],
        [99.6, 99],
        [Number.NaN, 0],
    ])('shows whole, clamped progress for %s', (progress, displayProgress) => {
        renderWithClient(
            <ExecutionRow
                execution={makeExecution({
                    status: 'Running',
                    progress,
                    completedAt: null,
                })}
            />,
        );
        const progressBar = screen.getByTestId('execution-row__progress-bar');
        expect(progressBar).toHaveAttribute('aria-valuenow', `${displayProgress}`);
        expect(screen.getByText(`${displayProgress}%`)).toBeInTheDocument();
        expect(screen.getByTestId('execution-row__progress-fill').style.width).toBe(
            displayProgress > 0 ? `${displayProgress}%` : '',
        );
    });

    it('does not show progress bar for completed executions', () => {
        renderWithClient(<ExecutionRow execution={makeExecution({ status: 'Completed', progress: 100 })} />);
        expect(screen.queryByTestId('execution-row__progress-bar')).not.toBeInTheDocument();
    });

    it('shows duration when completedAt is present', () => {
        renderWithClient(
            <ExecutionRow
                execution={makeExecution({
                    startedAt: '2024-01-15T12:00:00Z',
                    completedAt: '2024-01-15T12:05:00Z',
                })}
            />,
        );
        const trigger = screen.getByTestId('execution-row__trigger');
        expect(trigger.textContent).toContain('5m');
    });

    it('expands details when trigger is clicked', async () => {
        const user = userEvent.setup();
        renderWithClient(<ExecutionRow execution={makeExecution({ statusMessage: 'Details here' })} />);

        expect(screen.queryByTestId('execution-row__logs')).not.toBeVisible();

        await user.click(screen.getByTestId('execution-row__trigger'));
        expect(screen.getByTestId('execution-row__logs')).toBeVisible();
    });

    it('sets correct data attributes on the root element', () => {
        const { container } = renderWithClient(<ExecutionRow execution={makeExecution()} />);
        const root = container.firstElementChild!;
        expect(root).toHaveAttribute('data-execution-id', 'exec-1');
        expect(root).toHaveAttribute('data-status', 'completed');
    });

    it('shows indeterminate progress when progress is 0 and running', () => {
        renderWithClient(
            <ExecutionRow
                execution={makeExecution({
                    status: 'Running',
                    progress: 0,
                    completedAt: null,
                })}
            />,
        );
        const progressBar = screen.getByTestId('execution-row__progress-bar');
        expect(progressBar).toHaveAttribute('aria-valuenow', '0');
        expect(screen.getByTestId('execution-row__progress-fill')).toHaveClass(
            'execution-row__progress-fill--indeterminate',
        );
    });

    it('does not animate the bar for a queued (pending) job at 0%', () => {
        renderWithClient(
            <ExecutionRow
                execution={makeExecution({
                    status: 'Queued',
                    progress: 0,
                    startedAt: null,
                    completedAt: null,
                })}
            />,
        );
        const fill = screen.getByTestId('execution-row__progress-fill');
        expect(fill).not.toHaveClass('execution-row__progress-fill--indeterminate');
        expect(fill).toHaveClass('execution-row__progress-fill--idle');
    });

    it('has accessible trigger with aria-expanded', () => {
        renderWithClient(<ExecutionRow execution={makeExecution({ statusMessage: 'test' })} />);
        const trigger = screen.getByRole('button', {
            name: 'Import Media - Completed. Click to show details.',
        });
        expect(trigger).toHaveAttribute('aria-expanded', 'false');
    });

    it('keeps the details panel open across re-renders that clear the status message', async () => {
        const user = userEvent.setup();
        const running = makeExecution({
            status: 'Running',
            statusMessage: 'Processing items',
            startedAt: '2024-01-15T12:00:01Z',
            completedAt: null,
        });
        const { rerender } = renderWithClient(<ExecutionRow execution={running} />);

        await user.click(screen.getByTestId('execution-row__trigger'));
        expect(screen.getByTestId('execution-row__logs')).toHaveAttribute('data-state', 'open');

        const completed = makeExecution({
            status: 'Completed',
            statusMessage: null,
            startedAt: '2024-01-15T12:00:01Z',
            completedAt: '2024-01-15T12:05:00Z',
        });
        rerender(<ExecutionRow execution={completed} />);

        expect(screen.getByTestId('execution-row__logs')).toHaveAttribute('data-state', 'open');
        expect(screen.getByTestId('execution-row__trigger')).toHaveAttribute('aria-expanded', 'true');
    });

    it('preserves the row identity (one row per execution.id) across status updates', () => {
        const running = makeExecution({
            id: 'exec-42',
            status: 'Running',
            statusMessage: 'Working',
            completedAt: null,
        });
        const { container, rerender } = renderWithClient(<ExecutionRow execution={running} />);
        const initialRoot = container.querySelector('[data-execution-id="exec-42"]');
        expect(initialRoot).toBeInTheDocument();

        const completed = makeExecution({
            id: 'exec-42',
            status: 'Completed',
            statusMessage: null,
            completedAt: '2024-01-15T12:05:00Z',
        });
        rerender(<ExecutionRow execution={completed} />);

        const updatedRoot = container.querySelector('[data-execution-id="exec-42"]');
        expect(updatedRoot).toBe(initialRoot);
        expect(updatedRoot).toHaveAttribute('data-status', 'completed');
    });

    it('renders the execution-detail panel when expanded', async () => {
        const user = userEvent.setup();
        renderWithClient(
            <ExecutionRow execution={makeExecution({ correlationId: null, statusMessage: 'Processing' })} />,
        );
        await user.click(screen.getByTestId('execution-row__trigger'));

        expect(screen.getByRole('tab', { name: /execution details/i })).toHaveAttribute('aria-selected', 'true');
        expect(screen.getByTestId('execution-row__logs-list')).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: /execution trace/i })).toBeInTheDocument();
    });
});

describe('ExecutionRow deferred status', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-15T12:00:00Z'));
        mockApiGet.mockResolvedValue({ root: null, totalCount: 0 });
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('renders a deferred Queued row as "Pending" and surfaces the deferred-until hint', () => {
        renderWithClient(
            <ExecutionRow
                execution={makeExecution({
                    status: 'Queued',
                    notBeforeAt: '2024-01-15T13:00:00Z',
                    startedAt: null,
                    completedAt: null,
                })}
            />,
        );

        const badge = screen.getByTestId('execution-row__status-badge');
        expect(badge).toHaveTextContent('Pending');
        expect(badge).toHaveAttribute('title', expect.stringContaining('Deferred until'));
        expect(document.querySelector('[data-status="pending"]')).toBeInTheDocument();
    });

    it('renders an eligible-now Queued row as "Queued" with no deferred hint', () => {
        renderWithClient(
            <ExecutionRow
                execution={makeExecution({
                    status: 'Queued',
                    notBeforeAt: '2024-01-15T12:00:00Z',
                    startedAt: null,
                    completedAt: null,
                })}
            />,
        );

        const badge = screen.getByTestId('execution-row__status-badge');
        expect(badge).toHaveTextContent('Queued');
        expect(badge).not.toHaveAttribute('title');
        expect(document.querySelector('[data-status="queued"]')).toBeInTheDocument();
    });

    it('renders a past-deferred Queued row as "Queued"', () => {
        renderWithClient(
            <ExecutionRow execution={makeExecution({ status: 'Queued', notBeforeAt: '2024-01-15T11:59:59Z' })} />,
        );
        expect(screen.getByTestId('execution-row__status-badge')).toHaveTextContent('Queued');
    });
});
