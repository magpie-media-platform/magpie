import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
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
        createElement(QueryClientProvider, { client: queryClient }, createElement(MemoryRouter, null, children));
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

const emptyTree = { root: null, totalCount: 0 };
const emptyAudits = { items: [], pageNumber: 1, pageSize: 100, totalItems: 0 };
const scheduleTrigger = {
    id: 'schedule-1',
    jobType: 'ImportMedia',
    scope: 'System',
    name: 'Nightly import',
    schedule: '0 0 * * *',
    executionMode: 'Queued',
    enabled: true,
};

function relatedTree(count: number, correlationId: string) {
    return {
        root: {
            id: 'exec-1',
            correlationId,
            jobType: 'ImportMedia',
            status: 'Completed',
            createdAt: '2024-01-15T12:00:00Z',
            completedAt: '2024-01-15T12:00:00Z',
            children: Array.from({ length: count }, (_, index) => ({
                id: `child-${index}`,
                correlationId,
                jobType: 'ReleaseSearchItem',
                status: 'Completed',
                createdAt: '2024-01-15T12:01:00Z',
                completedAt: `2024-01-15T12:${String(10 + index).padStart(2, '0')}:00Z`,
                children: [],
            })),
        },
        totalCount: count + 1,
    };
}

describe('ExecutionDetailPanel', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) =>
            Promise.resolve(
                url.startsWith('/audit-history')
                    ? emptyAudits
                    : url.startsWith('/schedule-triggers')
                      ? scheduleTrigger
                      : emptyTree,
            ),
        );
    });

    it('shows the current activity with no timestamp while the execution runs', () => {
        renderWithClient(
            <ExecutionDetailPanel
                execution={makeExecution({ status: 'Running', completedAt: null, statusMessage: 'Importing 3 of 9' })}
            />,
        );

        const live = screen.getByTestId('execution-detail__live');
        expect(live).toHaveTextContent('Importing 3 of 9');
        expect(live).toHaveTextContent('now');
        expect(live.querySelector('time')).toBeNull();
        expect(live).not.toHaveTextContent(/\d{1,2}:\d{2}:\d{2}/);
    });

    it('falls back to "Working" for a running execution with no status message', () => {
        renderWithClient(
            <ExecutionDetailPanel
                execution={makeExecution({ status: 'Running', completedAt: null, statusMessage: null })}
            />,
        );

        expect(screen.getByTestId('execution-detail__live')).toHaveTextContent('Working');
    });

    it('shows the result and no live row once the execution finished', () => {
        renderWithClient(
            <ExecutionDetailPanel
                execution={makeExecution({ statusMessage: 'Imported 9 files', nodeId: 'node-a', attempt: 2 })}
            />,
        );

        expect(screen.queryByTestId('execution-detail__live')).not.toBeInTheDocument();
        const ledger = screen.getByTestId('execution-detail__ledger');
        expect(ledger.querySelector('[data-tone="done"]')).toHaveTextContent('Completed successfully');

        const details = screen.getByTestId('execution-detail__details');
        expect(details).toHaveTextContent('Duration');
        expect(details).toHaveTextContent('4m 59s');
        expect(details).toHaveTextContent('node-a');
        expect(screen.getByText('Attempt').closest('.execution-details__row')).toHaveTextContent('2');
    });

    it('shows the failure prominently when the execution failed', () => {
        renderWithClient(
            <ExecutionDetailPanel execution={makeExecution({ status: 'Failed', statusMessage: 'Disk full' })} />,
        );

        const ledger = screen.getByTestId('execution-detail__ledger');
        expect(ledger.querySelector('[data-tone="error"]')).toHaveTextContent('Disk full');
    });

    it('renders the attempt row as a plain count when the run has no original execution', () => {
        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ attempt: 2 })} />);

        const row = screen.getByText('Attempt').closest('.execution-details__row') as HTMLElement;
        expect(row).toHaveTextContent('2');
        expect(within(row).queryByRole('link')).not.toBeInTheDocument();
    });

    it('renders the attempt count with a link to every attempt when the run has an original execution', () => {
        renderWithClient(
            <ExecutionDetailPanel
                execution={makeExecution({ attempt: 3, originalExecutionId: 'exec-original', correlationId: 'corr-9' })}
            />,
        );

        const row = screen.getByText('Attempt').closest('.execution-details__row') as HTMLElement;
        expect(row).toHaveTextContent('3');
        expect(within(row).getByRole('link', { name: 'View all attempts' })).toHaveAttribute(
            'href',
            '/?correlationId=corr-9&rootsOnly=0',
        );
    });

    it('shows an em dash for the attempt count when a requeued run records none', () => {
        renderWithClient(
            <ExecutionDetailPanel execution={makeExecution({ attempt: null, originalExecutionId: 'exec-original' })} />,
        );

        const row = screen.getByText('Attempt').closest('.execution-details__row') as HTMLElement;
        expect(row).toHaveTextContent('—');
        expect(within(row).getByRole('link', { name: 'View all attempts' })).toBeInTheDocument();
    });

    it('names Manual as the trigger for an on-demand run', () => {
        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ createdByUserId: 'user-1' })} />);

        expect(screen.getByText('Trigger').closest('.execution-details__row')).toHaveTextContent('Manual');
    });

    it('names the event and links the parent job for an event-triggered run', () => {
        renderWithClient(
            <ExecutionDetailPanel
                execution={makeExecution({
                    eventTriggerId: 'trigger-1',
                    triggerContext: { 'event.name': 'download.completed', 'event.jobExecutionId': 'exec-parent' },
                })}
            />,
        );

        const row = screen.getByText('Trigger').closest('.execution-details__row') as HTMLElement;
        expect(row).toHaveTextContent('Event');
        expect(row).toHaveTextContent('download.completed');
        expect(within(row).getByRole('link', { name: 'Parent job' })).toHaveAttribute('href', '/?job=exec-parent');
    });

    it('names the schedule as the trigger once it resolves', async () => {
        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ scheduleTriggerId: 'schedule-1' })} />);

        const row = screen.getByText('Trigger').closest('.execution-details__row') as HTMLElement;
        expect(row).toHaveTextContent('Schedule');
        expect(await within(row).findByText('Nightly import')).toBeInTheDocument();
        expect(mockApiGet).toHaveBeenCalledWith('/schedule-triggers/schedule-1');
    });

    it('keeps the bare Schedule label when the schedule cannot be read', async () => {
        mockApiGet.mockImplementation((url: string) =>
            url.startsWith('/schedule-triggers')
                ? Promise.reject(new Error('Forbidden'))
                : Promise.resolve(url.startsWith('/audit-history') ? emptyAudits : emptyTree),
        );

        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ scheduleTriggerId: 'schedule-1' })} />);

        await waitFor(() => expect(mockApiGet).toHaveBeenCalledWith('/schedule-triggers/schedule-1'));
        expect(screen.getByText('Trigger').closest('.execution-details__row')).toHaveTextContent('Schedule');
        expect(screen.queryByText('Nightly import')).not.toBeInTheDocument();
    });

    it('renders the parameters of the run', () => {
        renderWithClient(
            <ExecutionDetailPanel execution={makeExecution({ parameters: { contentItemId: 'abc-123' } })} />,
        );

        const details = screen.getByTestId('execution-detail__details');
        expect(details).toHaveTextContent('Content Item Id');
        expect(details).toHaveTextContent('abc-123');
    });

    it('renders the audit records the execution wrote as ledger rows', async () => {
        mockApiGet.mockImplementation((url: string) =>
            Promise.resolve(
                url.startsWith('/audit-history')
                    ? {
                          items: [
                              {
                                  id: 'audit-1',
                                  kind: 'AuditRecord',
                                  timestamp: '2024-01-15T12:04:00Z',
                                  label: 'content.updated',
                                  category: 'Content',
                                  detail: 'Set the monitored flag',
                                  jobExecutionId: 'exec-1',
                              },
                          ],
                          pageNumber: 1,
                          pageSize: 100,
                          totalItems: 1,
                      }
                    : emptyTree,
            ),
        );

        renderWithClient(<ExecutionDetailPanel execution={makeExecution()} />);

        const text = await screen.findByText('Set the monitored flag');
        expect(text.closest('.execution-ledger__row')).toHaveAttribute('data-tone', 'info');
    });

    it('loads another audit page only when requested', async () => {
        let auditPage = 0;
        mockApiGet.mockImplementation((url: string) => {
            if (!url.startsWith('/audit-history')) return Promise.resolve(emptyTree);
            auditPage += 1;
            const first = auditPage === 1;
            return Promise.resolve({
                items: Array.from({ length: first ? 100 : 1 }, (_, index) => ({
                    id: `audit-${first ? index : 100}`,
                    kind: 'AuditRecord',
                    timestamp: '2024-01-15T12:04:00Z',
                    label: 'content.updated',
                    category: 'Content',
                    detail: first ? `Audit ${index}` : 'Audit 100',
                    jobExecutionId: 'exec-1',
                })),
                pageNumber: auditPage,
                pageSize: 100,
                totalItems: 101,
            });
        });

        renderWithClient(<ExecutionDetailPanel execution={makeExecution()} />);

        const user = userEvent.setup();
        expect(await screen.findByRole('button', { name: 'Load more audit records' })).toBeInTheDocument();
        expect(screen.queryByText('Audit 100')).not.toBeInTheDocument();
        expect(auditPage).toBe(1);

        await user.click(screen.getByRole('button', { name: 'Load more audit records' }));

        expect(await screen.findByText('Audit 100')).toBeInTheDocument();
        expect(auditPage).toBe(2);
        expect(screen.queryByRole('button', { name: /more audit/ })).not.toBeInTheDocument();
    });

    it('lets the user return to audit records evicted from the retained window', async () => {
        // Arrange
        const pages = [1, 2, 3, 4, 5, 6].map((pageNumber) => ({
            items: Array.from({ length: 100 }, (_, index) => ({
                id: `${pageNumber}-${index}`,
                timestamp: '2024-01-15T12:04:00Z',
                label: `Page ${pageNumber} record ${index}`,
            })),
            pageNumber,
            pageSize: 100,
            totalItems: 600,
        }));
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
        queryClient.setQueryData(['magpie-activity', 'execution-audits', 'exec-1'], {
            pages: pages.slice(0, 5),
            pageParams: [1, 2, 3, 4, 5],
        });
        mockApiGet.mockImplementation((url: string) =>
            Promise.resolve(url.startsWith('/audit-history') ? pages[5] : emptyTree),
        );
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={queryClient}>
                <MemoryRouter>
                    <ExecutionDetailPanel execution={makeExecution()} />
                </MemoryRouter>
            </QueryClientProvider>,
        );

        // Act
        expect(screen.getByText('Page 1 Record 0')).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Load more audit records' }));

        // Assert
        expect(await screen.findByRole('button', { name: 'Load previous audit records' })).toBeInTheDocument();
        expect(screen.queryByText('Page 1 Record 0')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Load more audit/ })).not.toBeInTheDocument();
        mockApiGet.mockResolvedValueOnce(pages[0]);
        await user.click(screen.getByRole('button', { name: 'Load previous audit records' }));
        expect(await screen.findByText('Page 1 Record 0')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Load previous audit records' })).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Load more audit records' })).toBeInTheDocument();
    });

    it('shows an empty ledger when the audit response has no body', async () => {
        mockApiGet.mockImplementation((url: string) =>
            Promise.resolve(url.startsWith('/audit-history') ? undefined : emptyTree),
        );

        renderWithClient(<ExecutionDetailPanel execution={makeExecution()} />);

        await waitFor(() => expect(mockApiGet).toHaveBeenCalledWith(expect.stringContaining('/audit-history')));
        expect(screen.queryByText(/Failed to load the full record/)).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /more audit/ })).not.toBeInTheDocument();
    });

    it('stops offering more audit records when the server returns an empty page', async () => {
        let auditPage = 0;
        mockApiGet.mockImplementation((url: string) => {
            if (!url.startsWith('/audit-history')) return Promise.resolve(emptyTree);
            auditPage += 1;
            return Promise.resolve({
                items:
                    auditPage === 1
                        ? Array.from({ length: 100 }, (_, index) => ({
                              id: `audit-${index}`,
                              kind: 'AuditRecord',
                              timestamp: '2024-01-15T12:04:00Z',
                              label: 'content.updated',
                              category: 'Content',
                              detail: `Audit ${index}`,
                              jobExecutionId: 'exec-1',
                          }))
                        : [],
                pageNumber: auditPage,
                pageSize: 100,
                totalItems: 101,
            });
        });
        const user = userEvent.setup();
        renderWithClient(<ExecutionDetailPanel execution={makeExecution()} />);

        await user.click(await screen.findByRole('button', { name: 'Load more audit records' }));

        await waitFor(() => expect(screen.queryByRole('button', { name: /more audit/ })).not.toBeInTheDocument());
        expect(auditPage).toBe(2);
    });

    it('lists related jobs as ledger rows and excludes the current execution', async () => {
        mockApiGet.mockImplementation((url: string) =>
            Promise.resolve(url.startsWith('/audit-history') ? emptyAudits : relatedTree(1, 'corr-rel')),
        );

        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ correlationId: 'corr-rel' })} />);

        const ledger = screen.getByTestId('execution-detail__ledger');
        const link = await within(ledger).findByRole('link', { name: 'Release Search Item' });
        expect(link).toHaveAttribute('href', '/?job=child-0');
        expect(within(ledger).queryByRole('link', { name: 'Import Media' })).not.toBeInTheDocument();
    });

    it('caps related jobs in the ledger and links the rest, pluralized', async () => {
        mockApiGet.mockImplementation((url: string) =>
            Promise.resolve(url.startsWith('/audit-history') ? emptyAudits : relatedTree(9, 'corr-many')),
        );

        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ correlationId: 'corr-many' })} />);

        const ledger = screen.getByTestId('execution-detail__ledger');
        const jobLinks = await within(ledger).findAllByRole('link', { name: 'Release Search Item' });
        expect(jobLinks).toHaveLength(6);
        expect(within(ledger).getByRole('link', { name: '3 more related jobs' })).toHaveAttribute(
            'href',
            '/?correlationId=corr-many&rootsOnly=0',
        );
    });

    it('uses the singular "job" when exactly one related job overflows the cap', async () => {
        mockApiGet.mockImplementation((url: string) =>
            Promise.resolve(url.startsWith('/audit-history') ? emptyAudits : relatedTree(7, 'corr-seven')),
        );

        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ correlationId: 'corr-seven' })} />);

        const ledger = screen.getByTestId('execution-detail__ledger');
        await within(ledger).findAllByRole('link', { name: 'Release Search Item' });
        expect(within(ledger).getByRole('link', { name: '1 more related job' })).toBeInTheDocument();
    });

    it('renders one untabbed surface with the details grid and the ledger', () => {
        renderWithClient(<ExecutionDetailPanel execution={makeExecution({ correlationId: 'corr-1' })} />);

        expect(screen.queryAllByRole('tab')).toHaveLength(0);
        expect(screen.getByTestId('execution-detail__details')).toBeInTheDocument();
        expect(screen.getByTestId('execution-detail__ledger')).toBeInTheDocument();
    });

    it.each([
        ['2024-01-15T12:00:00.500Z', '<1s'],
        ['2024-01-15T12:00:12Z', '12s'],
        ['2024-01-15T12:00:42Z', '42s'],
        ['2024-01-15T12:01:35Z', '1m 35s'],
    ])('shows second-granular duration for %s', (completedAt, expected) => {
        renderWithClient(
            <ExecutionDetailPanel
                execution={{
                    id: 'duration-job',
                    jobType: 'Scan',
                    scope: 'System',
                    status: 'Completed',
                    createdAt: '2024-01-15T12:00:00Z',
                    startedAt: '2024-01-15T12:00:00Z',
                    completedAt,
                }}
                active={false}
            />,
        );
        expect(screen.getByText('Duration').nextElementSibling).toHaveTextContent(expected);
    });

    it('does not fetch while inactive', () => {
        renderWithClient(
            <ExecutionDetailPanel
                execution={makeExecution({ correlationId: 'corr-1', scheduleTriggerId: 'schedule-1' })}
                active={false}
            />,
        );
        expect(mockApiGet).not.toHaveBeenCalled();
    });
});
