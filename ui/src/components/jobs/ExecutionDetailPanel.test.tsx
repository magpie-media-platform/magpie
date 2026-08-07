import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    buildUrl: (path: string) => path,
}));

import { apiGet } from '@/lib/api';
import type { ApiJobExecution } from '@/hooks/useJobs';

import { ExecutionDetailPanel } from './ExecutionDetailPanel';

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

describe('ExecutionDetailPanel', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue({ root: null, totalCount: 0 });
    });

    it('renders details for only the selected execution', () => {
        renderWithClient(
            <ExecutionDetailPanel execution={makeExecution({ correlationId: null, statusMessage: 'Processing' })} />,
        );

        expect(screen.getByText('Created')).toBeInTheDocument();
        expect(screen.getByText('Started')).toBeInTheDocument();
        expect(screen.getByText('Processing')).toBeInTheDocument();
        expect(screen.getByText('Completed successfully')).toBeInTheDocument();
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('does not render related executions in execution details', () => {
        mockApiGet.mockResolvedValue({
            root: {
                id: 'exec-1',
                correlationId: 'corr-1',
                jobType: 'RootFolderContentDiscovery',
                status: 'Completed',
                createdAt: '2024-01-15T12:00:00Z',
                startedAt: '2024-01-15T12:00:01Z',
                completedAt: '2024-01-15T12:05:00Z',
                children: [
                    {
                        id: 'child-1',
                        correlationId: 'corr-1',
                        jobType: 'ReleaseSearchItem',
                        status: 'Completed',
                        createdAt: '2024-01-15T12:02:00Z',
                        children: [],
                    },
                ],
            },
            totalCount: 2,
        });

        renderWithClient(
            <ExecutionDetailPanel
                execution={makeExecution({ correlationId: 'corr-1', jobType: 'RootFolderContentDiscovery' })}
            />,
        );

        expect(screen.queryByText('ReleaseSearchItem (Completed)')).not.toBeInTheDocument();
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('does not fetch the execution tree while inactive', () => {
        renderWithClient(
            <ExecutionDetailPanel execution={makeExecution({ correlationId: 'corr-1' })} active={false} />,
        );
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('shows an Execution trace tab that renders the execution tree when selected', async () => {
        const user = userEvent.setup();
        mockApiGet.mockResolvedValue({
            root: {
                id: 'exec-1',
                correlationId: 'corr-rel',
                jobType: 'ImportMedia',
                status: 'Completed',
                createdAt: '2024-01-15T12:00:00Z',
                children: [
                    {
                        id: 'job-1',
                        correlationId: 'corr-rel',
                        jobType: 'ReleaseSearchItem',
                        status: 'Completed',
                        createdAt: '2024-01-15T12:01:00Z',
                        children: [],
                    },
                ],
            },
            totalCount: 2,
        });

        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ correlationId: 'corr-rel' })} />);

        await user.click(screen.getByRole('tab', { name: /execution trace/i }));

        const chain = await screen.findByTestId('related-events__chain');
        expect(chain.textContent).toContain('Release Search Item');
        expect(mockApiGet).toHaveBeenCalledWith('/jobs/exec-1/execution-tree');
    });

    it('shows an empty execution trace when the execution has no related jobs', async () => {
        const user = userEvent.setup();
        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ correlationId: null })} />);

        await user.click(screen.getByRole('tab', { name: /execution trace/i }));

        expect(await screen.findByText('No related events for this chain.')).toBeInTheDocument();
    });
});
