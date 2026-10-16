import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    buildUrl: (path: string) => path,
}));

import { apiGet } from '@/lib/api';
import type { ApiJobExecution } from '@/hooks/useJobs';

import { ExecutionRow } from './ExecutionRow';

describe('ExecutionRow', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue({ root: null, items: [], totalCount: 0 });
    });

    it('renders the humanized job type name', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: null,
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        expect(screen.getByText('Import Media')).toBeInTheDocument();
    });

    it('displays the correct status badge', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: null,
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        expect(screen.getByText('Completed')).toBeInTheDocument();
    });

    it('shows failed status badge for failed execution', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Failed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: 'Timeout',
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        expect(screen.getByText('Failed')).toBeInTheDocument();
    });

    it('shows status message when present', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: 'Processing items',
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        expect(screen.getByText('Processing items')).toBeInTheDocument();
    });

    it('marks the status message as live for a running execution', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Running',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: null,
                            statusMessage: 'Importing',
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        const message = screen.getByText('Importing').closest('.execution-row__message');
        expect(message).toHaveAttribute('data-message-kind', 'live');
        expect(message).toHaveTextContent('Now');
    });

    it('marks the status message as a result for a finished execution', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: 'Imported 9 files',
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        const message = screen.getByText('Imported 9 files').closest('.execution-row__message');
        expect(message).toHaveAttribute('data-message-kind', 'result');
        expect(message).toHaveTextContent('Result');
    });

    it('always renders an expandable details panel (collapsed by default)', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: null,
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        const logs = screen.getByTestId('execution-row__logs');
        expect(logs).toBeInTheDocument();
        expect(logs).not.toBeVisible();
    });

    it.each([
        [42.4, 42, '42%'],
        [-1, 0, ''],
        [101, 99, '99%'],
        [99.6, 99, '99%'],
        [Number.NaN, 0, ''],
    ])('shows whole, clamped progress for %s', (progress, displayProgress, fillWidth) => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Running',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: null,
                            statusMessage: null,
                            progress,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        const progressBar = screen.getByTestId('execution-row__progress-bar');
        expect(progressBar).toHaveAttribute('aria-valuenow', `${displayProgress}`);
        expect(screen.getByText(`${displayProgress}%`)).toBeInTheDocument();
        expect(screen.getByTestId('execution-row__progress-fill').style.width).toBe(fillWidth);
    });

    it('does not show progress bar for completed executions', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: null,
                            progress: 100,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        expect(screen.queryByTestId('execution-row__progress-bar')).not.toBeInTheDocument();
    });

    it('shows duration when completedAt is present', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:00Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: null,
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        const trigger = screen.getByTestId('execution-row__trigger');
        expect(trigger.textContent).toContain('5m');
    });

    it('expands details when trigger is clicked', async () => {
        const user = userEvent.setup();
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: 'Details here',
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );

        expect(screen.queryByTestId('execution-row__logs')).not.toBeVisible();

        await user.click(screen.getByTestId('execution-row__trigger'));
        expect(screen.getByTestId('execution-row__logs')).toBeVisible();
    });

    it('sets correct data attributes on the root element', () => {
        const queryClient = new QueryClient({
            defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
        });
        const { container } = render(
            <ExecutionRow
                execution={{
                    id: 'exec-1',
                    jobType: 'ImportMedia',
                    scope: 'System',
                    status: 'Completed',
                    createdAt: '2024-01-15T12:00:00Z',
                    startedAt: '2024-01-15T12:00:01Z',
                    completedAt: '2024-01-15T12:05:00Z',
                    statusMessage: null,
                    progress: null,
                }}
            />,
            {
                wrapper: ({ children }: { children: React.ReactNode }) => (
                    <QueryClientProvider client={queryClient}>
                        <MemoryRouter>{children}</MemoryRouter>
                    </QueryClientProvider>
                ),
            },
        );
        const root = container.firstElementChild!;
        expect(root).toHaveAttribute('data-execution-id', 'exec-1');
        expect(root).toHaveAttribute('data-status', 'completed');
    });

    it('shows indeterminate progress when progress is 0 and running', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Running',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: null,
                            statusMessage: null,
                            progress: 0,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        const progressBar = screen.getByTestId('execution-row__progress-bar');
        expect(progressBar).toHaveAttribute('aria-valuenow', '0');
        expect(screen.getByTestId('execution-row__progress-fill')).toHaveClass(
            'execution-row__progress-fill--indeterminate',
        );
    });

    it('does not animate the bar for a queued (pending) job at 0%', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Queued',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: null,
                            completedAt: null,
                            statusMessage: null,
                            progress: 0,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        const fill = screen.getByTestId('execution-row__progress-fill');
        expect(fill).not.toHaveClass('execution-row__progress-fill--indeterminate');
        expect(fill).toHaveClass('execution-row__progress-fill--idle');
    });

    it('has accessible trigger with aria-expanded', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: 'test',
                            progress: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        const trigger = screen.getByRole('button', {
            name: 'Import Media - Completed. Click to show details.',
        });
        expect(trigger).toHaveAttribute('aria-expanded', 'false');
    });

    it('keeps the details panel open across re-renders that clear the status message', async () => {
        const user = userEvent.setup();
        const running: ApiJobExecution = {
            id: 'exec-1',
            jobType: 'ImportMedia',
            scope: 'System',
            status: 'Running',
            createdAt: '2024-01-15T12:00:00Z',
            startedAt: '2024-01-15T12:00:01Z',
            completedAt: null,
            statusMessage: 'Processing items',
            progress: null,
        };
        const queryClient = new QueryClient({
            defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
        });
        const { rerender } = render(<ExecutionRow execution={running} />, {
            wrapper: ({ children }: { children: React.ReactNode }) => (
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter>{children}</MemoryRouter>
                </QueryClientProvider>
            ),
        });

        await user.click(screen.getByTestId('execution-row__trigger'));
        expect(screen.getByTestId('execution-row__logs')).toHaveAttribute('data-state', 'open');

        const completed: ApiJobExecution = {
            id: 'exec-1',
            jobType: 'ImportMedia',
            scope: 'System',
            status: 'Completed',
            createdAt: '2024-01-15T12:00:00Z',
            startedAt: '2024-01-15T12:00:01Z',
            completedAt: '2024-01-15T12:05:00Z',
            statusMessage: null,
            progress: null,
        };
        rerender(<ExecutionRow execution={completed} />);

        expect(screen.getByTestId('execution-row__logs')).toHaveAttribute('data-state', 'open');
        expect(screen.getByTestId('execution-row__trigger')).toHaveAttribute('aria-expanded', 'true');
    });

    it('preserves the row identity (one row per execution.id) across status updates', () => {
        const running: ApiJobExecution = {
            id: 'exec-42',
            jobType: 'ImportMedia',
            scope: 'System',
            status: 'Running',
            createdAt: '2024-01-15T12:00:00Z',
            startedAt: '2024-01-15T12:00:01Z',
            completedAt: null,
            statusMessage: 'Working',
            progress: null,
        };
        const queryClient = new QueryClient({
            defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
        });
        const { container, rerender } = render(<ExecutionRow execution={running} />, {
            wrapper: ({ children }: { children: React.ReactNode }) => (
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter>{children}</MemoryRouter>
                </QueryClientProvider>
            ),
        });
        const initialRoot = container.querySelector('[data-execution-id="exec-42"]');
        expect(initialRoot).toBeInTheDocument();

        const completed: ApiJobExecution = {
            id: 'exec-42',
            jobType: 'ImportMedia',
            scope: 'System',
            status: 'Completed',
            createdAt: '2024-01-15T12:00:00Z',
            startedAt: '2024-01-15T12:00:01Z',
            completedAt: '2024-01-15T12:05:00Z',
            statusMessage: null,
            progress: null,
        };
        rerender(<ExecutionRow execution={completed} />);

        const updatedRoot = container.querySelector('[data-execution-id="exec-42"]');
        expect(updatedRoot).toBe(initialRoot);
        expect(updatedRoot).toHaveAttribute('data-status', 'completed');
    });

    it('renders the execution-detail panel as one untabbed surface when expanded', async () => {
        const user = userEvent.setup();
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Completed',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: 'Processing',
                            progress: null,
                            correlationId: null,
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        await user.click(screen.getByTestId('execution-row__trigger'));

        expect(screen.getByTestId('execution-detail')).toBeInTheDocument();
        expect(screen.queryAllByRole('tab')).toHaveLength(0);
        expect(screen.getByTestId('execution-detail__details')).toBeInTheDocument();
        expect(screen.getByTestId('execution-detail__ledger')).toBeInTheDocument();
    });
});

describe('ExecutionRow deferred status', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-15T12:00:00Z'));
        mockApiGet.mockResolvedValue({ root: null, items: [], totalCount: 0 });
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('shows the pending-until date without opening the row or hovering', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Queued',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: null,
                            completedAt: null,
                            statusMessage: null,
                            progress: null,
                            notBeforeAt: '2024-01-15T13:00:00Z',
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );

        const badge = screen.getByTestId('execution-row__status-badge');
        expect(badge).toHaveTextContent('Pending');
        const pendingTime = screen.getByText('Pending until').querySelector('time');
        expect(pendingTime).toBeVisible();
        expect(pendingTime).toHaveAttribute('datetime', '2024-01-15T13:00:00Z');
        expect(pendingTime).toHaveTextContent(
            new Date('2024-01-15T13:00:00Z').toLocaleDateString(undefined, {
                month: 'short',
                day: 'numeric',
                hour: '2-digit',
                minute: '2-digit',
            }),
        );
        expect(screen.getByTestId('execution-row__trigger')).toHaveAccessibleName(/Pending until/);
        expect(document.querySelector('[data-status="pending"]')).toBeInTheDocument();
    });

    it('renders an eligible-now Queued row as "Queued" with no deferred hint', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Queued',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: null,
                            completedAt: null,
                            statusMessage: null,
                            progress: null,
                            notBeforeAt: '2024-01-15T12:00:00Z',
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );

        const badge = screen.getByTestId('execution-row__status-badge');
        expect(badge).toHaveTextContent('Queued');
        expect(badge).not.toHaveAttribute('title');
        expect(screen.queryByText('Pending until')).not.toBeInTheDocument();
        expect(document.querySelector('[data-status="queued"]')).toBeInTheDocument();
    });

    it('renders a past-deferred Queued row as "Queued"', () => {
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
                    })
                }
            >
                <MemoryRouter>
                    <ExecutionRow
                        execution={{
                            id: 'exec-1',
                            jobType: 'ImportMedia',
                            scope: 'System',
                            status: 'Queued',
                            createdAt: '2024-01-15T12:00:00Z',
                            startedAt: '2024-01-15T12:00:01Z',
                            completedAt: '2024-01-15T12:05:00Z',
                            statusMessage: null,
                            progress: null,
                            notBeforeAt: '2024-01-15T11:59:59Z',
                        }}
                    />
                </MemoryRouter>
            </QueryClientProvider>,
        );
        expect(screen.getByTestId('execution-row__status-badge')).toHaveTextContent('Queued');
    });
});
