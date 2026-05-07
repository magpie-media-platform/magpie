import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import type { ApiJobExecution } from '@/hooks/useJobs';

import { ExecutionRow } from './ExecutionRow';

function makeExecution(overrides: Partial<ApiJobExecution> = {}): ApiJobExecution {
    return {
        id: 'exec-1',
        jobType: 'ImportMedia',
        scope: 0,
        status: 3, // Completed
        createdAt: '2024-01-15T12:00:00Z',
        startedAt: '2024-01-15T12:00:01Z',
        completedAt: '2024-01-15T12:05:00Z',
        statusMessage: null,
        progress: null,
        ...overrides,
    };
}

describe('ExecutionRow', () => {
    it('renders the job type name', () => {
        render(<ExecutionRow execution={makeExecution()} />);
        expect(screen.getByText('ImportMedia')).toBeInTheDocument();
    });

    it('displays the correct status badge', () => {
        render(<ExecutionRow execution={makeExecution({ status: 3 })} />);
        expect(screen.getByText('Completed')).toBeInTheDocument();
    });

    it('shows failed status badge for failed execution', () => {
        render(<ExecutionRow execution={makeExecution({ status: 4, statusMessage: 'Timeout' })} />);
        expect(screen.getByText('Failed')).toBeInTheDocument();
    });

    it('shows status message when present', () => {
        render(<ExecutionRow execution={makeExecution({ statusMessage: 'Processing items' })} />);
        expect(screen.getByText('Processing items')).toBeInTheDocument();
    });

    it('does not show expand caret when there is no status message', () => {
        render(<ExecutionRow execution={makeExecution({ statusMessage: null })} />);
        expect(screen.queryByTestId('execution-row__logs')).not.toBeInTheDocument();
    });

    it('shows progress bar for running executions', () => {
        render(
            <ExecutionRow
                execution={makeExecution({
                    status: 1, // Running
                    progress: 45,
                    completedAt: null,
                })}
            />,
        );
        const progressBar = screen.getByTestId('execution-row__progress-bar');
        expect(progressBar).toHaveAttribute('aria-valuenow', '45');
    });

    it('does not show progress bar for completed executions', () => {
        render(<ExecutionRow execution={makeExecution({ status: 3, progress: 100 })} />);
        expect(screen.queryByTestId('execution-row__progress-bar')).not.toBeInTheDocument();
    });

    it('shows duration when completedAt is present', () => {
        render(
            <ExecutionRow
                execution={makeExecution({
                    startedAt: '2024-01-15T12:00:00Z',
                    completedAt: '2024-01-15T12:05:00Z',
                })}
            />,
        );
        // Duration element should exist (exact format varies)
        const trigger = screen.getByTestId('execution-row__trigger');
        expect(trigger.textContent).toContain('5m');
    });

    it('expands details when trigger is clicked', async () => {
        const user = userEvent.setup();
        render(<ExecutionRow execution={makeExecution({ statusMessage: 'Details here' })} />);

        expect(screen.queryByTestId('execution-row__logs')).not.toBeVisible();

        await user.click(screen.getByTestId('execution-row__trigger'));
        expect(screen.getByTestId('execution-row__logs')).toBeVisible();
    });

    it('sets correct data attributes on the root element', () => {
        const { container } = render(<ExecutionRow execution={makeExecution()} />);
        const root = container.firstElementChild!;
        expect(root).toHaveAttribute('data-execution-id', 'exec-1');
        expect(root).toHaveAttribute('data-status', 'completed');
    });

    it('shows indeterminate progress when progress is 0 and running', () => {
        render(
            <ExecutionRow
                execution={makeExecution({
                    status: 1,
                    progress: 0,
                    completedAt: null,
                })}
            />,
        );
        const progressBar = screen.getByTestId('execution-row__progress-bar');
        expect(progressBar).toHaveAttribute('aria-valuenow', '0');
    });

    it('has accessible trigger with aria-expanded', () => {
        render(<ExecutionRow execution={makeExecution({ statusMessage: 'test' })} />);
        const trigger = screen.getByTestId('execution-row__trigger');
        expect(trigger).toHaveAttribute('aria-expanded', 'false');
    });
});
